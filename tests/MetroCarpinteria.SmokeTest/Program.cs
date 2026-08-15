using System.IO;
using MetroCarpinteria.App.Data.Entities;
using MetroCarpinteria.App.Helpers;
using MetroCarpinteria.App.Models;
using MetroCarpinteria.App.Services;
using Microsoft.Data.Sqlite;

namespace MetroCarpinteria.SmokeTest;

internal static class Program
{
    private static readonly List<TestResult> Results = [];

    /// <param name="args">
    /// <c>--with-production-db</c> agrega los chequeos de salud contra la base real del
    /// taller (<c>Documentos/MetroCarpinteria</c>). Son de solo lectura, pero requieren
    /// que esa base exista, así que en CI no corren: por eso el modo aislado es el default.
    /// Ese requisito era el motivo real por el que el workflow de release nunca ejecutó
    /// la suite — en un runner limpio el primer test fallaba y cortaba la publicación.
    /// </param>
    public static int Main(string[] args)
    {
        // Modo aparte: dibuja las pantallas a PNG para poder revisar el diseño sin
        // tener que abrir la app ni depender de qué ventana esté en primer plano.
        var shotsIndex = Array.FindIndex(
            args, a => string.Equals(a, "--screenshots", StringComparison.OrdinalIgnoreCase));

        if (shotsIndex >= 0 && shotsIndex + 1 < args.Length)
        {
            Debouncer.DefaultDelay = TimeSpan.Zero;
            return ScreenshotRunner.Run(args[shotsIndex + 1]);
        }

        // Y los papeles que salen impresos, hoja por hoja: es lo único de la app que
        // termina en la mano de otra persona.
        var docsIndex = Array.FindIndex(
            args, a => string.Equals(a, "--documents", StringComparison.OrdinalIgnoreCase));

        if (docsIndex >= 0 && docsIndex + 1 < args.Length)
        {
            Debouncer.DefaultDelay = TimeSpan.Zero;
            return DocumentRunner.Run(args[docsIndex + 1]);
        }

        // Las búsquedas corren en el acto: la suite no bombea el bucle de mensajes, así que
        // un temporizador de WPF no llegaría a disparar y toda aserción sobre una lista
        // filtrada quedaría mirando la lista sin filtrar.
        Debouncer.DefaultDelay = TimeSpan.Zero;

        var withProductionDb = args.Contains("--with-production-db", StringComparer.OrdinalIgnoreCase);

        Console.WriteLine("=== Metro Carpintería — Smoke Test ===");
        Console.WriteLine(withProductionDb
            ? "Modo: aislado + chequeos de la base de producción (solo lectura)"
            : "Modo: aislado (no toca la base del taller)");
        Console.WriteLine();

        Run("Build (solution compiles)", () =>
        {
            // Verified by running this executable after dotnet build.
        });

        if (withProductionDb)
        {
            RunProductionHealthChecks();
        }

        RunMigrationTests();
        MigrationTests.Run(Run);
        RunUpdateCheckTests();
        RunNumberFormatTests();
        NumberInputTests.Run(Run);
        StartupTests.Run(Run);
        ComponentTests.Run(Run);
        CorrectnessTests.Run(Run);
        CommercialTests.Run(Run);
        RunIsolatedIntegrationFlow();
        QuoteImageTests.Run(Run);
        UiSmokeTests.Run(Run);
        PrintManualUiChecklist();

        PrintSummary();
        return Results.All(r => r.Passed) ? 0 : 1;
    }

    private static void RunProductionHealthChecks()
    {
        var productionPaths = new AppPaths();

        if (!File.Exists(productionPaths.DatabasePath))
        {
            Run("Production DB exists", () =>
                throw new InvalidOperationException(
                    $"No se encontró la base de datos en: {productionPaths.DatabasePath}"));
            return;
        }

        Run("Production DB integrity_check", () =>
        {
            using var connection = new SqliteConnection($"Data Source={productionPaths.DatabasePath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";
            var result = command.ExecuteScalar()?.ToString();
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"integrity_check devolvió: {result}");
            }
        });

        Run("Production DB tables", () =>
        {
            var expected = new[]
            {
                "Products", "StockMovements", "CashSessions", "CashMovements",
                "Projects", "Employees", "ProjectMaterials", "ProjectAssignments"
            };

            using var connection = new SqliteConnection($"Data Source={productionPaths.DatabasePath}");
            connection.Open();

            foreach (var table in expected)
            {
                using var command = connection.CreateCommand();
                command.CommandText =
                    "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name LIMIT 1;";
                command.Parameters.AddWithValue("$name", table);
                if (command.ExecuteScalar() is null)
                {
                    throw new InvalidOperationException($"Falta la tabla: {table}");
                }
            }
        });

        var productionDb = new DatabaseService(productionPaths);
        var inventory = new InventoryService(productionDb);
        var cash = new CashRegisterService(productionDb);
        var projects = new ProjectService(productionDb);
        var employees = new EmployeeService(productionDb);
        var reports = new ReportService(productionDb);

        Run("Production read: inventory", () => _ = inventory.GetProducts(false, false, null));
        Run("Production read: cash history", () => _ = cash.GetSessionHistory(10));
        Run("Production read: projects", () => _ = projects.GetProjects(false, null, null));
        Run("Production read: employees", () => _ = employees.GetEmployees(false, null));
        Run("Production read: reports", () =>
        {
            var sections = reports.BuildSummary();
            if (sections.Count == 0)
            {
                throw new InvalidOperationException("BuildSummary no devolvió secciones.");
            }
        });
    }

    private static void RunIsolatedIntegrationFlow()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"MetroCarpinteriaSmoke_{Guid.NewGuid():N}");
        try
        {
            var paths = new AppPaths(tempRoot);
            paths.EnsureDirectories();

            var database = new DatabaseService(paths);
            database.Initialize();

            var settingsService = new SettingsService(paths);
            var inventory = new InventoryService(database);
            var cash = new CashRegisterService(database);
            var projects = new ProjectService(database);
            var employees = new EmployeeService(database);
            var reports = new ReportService(database);
            var quotes = new QuoteService(database, settingsService);

            Run("Temp DB initialize", () =>
            {
                if (!File.Exists(paths.DatabasePath))
                {
                    throw new InvalidOperationException("No se creó la base temporal.");
                }
            });

            int productId = 0;
            int projectId = 0;
            int employeeId = 0;

            Run("Inventory: create product", () =>
            {
                var product = inventory.CreateProduct("Tornillo test", 100m, 10m, "Pieza");
                productId = product.Id;
                if (product.CurrentStock != 100m)
                {
                    throw new InvalidOperationException("Stock inicial incorrecto.");
                }
            });

            Run("Inventory: register movement", () =>
            {
                inventory.RegisterMovement(productId, StockMovementType.Out, 20m, "Prueba smoke");
                var items = inventory.GetProducts(false, false, "Tornillo test");
                var item = items.FirstOrDefault()
                    ?? throw new InvalidOperationException("Producto no encontrado tras movimiento.");
                if (item.CurrentStock != 80m)
                {
                    throw new InvalidOperationException($"Stock esperado 80, actual {item.CurrentStock}.");
                }
            });

            Run("Inventory: validation (insufficient stock)", () =>
            {
                try
                {
                    inventory.RegisterMovement(productId, StockMovementType.Out, 999m, "Debe fallar");
                    throw new InvalidOperationException("Debía fallar por stock insuficiente.");
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("insuficiente", StringComparison.OrdinalIgnoreCase))
                {
                    // expected
                }
            });

            Run("Cash: open session", () =>
            {
                var session = cash.OpenSession(500m, "Apertura test");
                if (session.OpeningAmount != 500m)
                {
                    throw new InvalidOperationException("Monto de apertura incorrecto.");
                }
            });

            Run("Cash: register movements", () =>
            {
                cash.RegisterMovement(CashMovementType.Income, 150m, "Venta test");
                cash.RegisterMovement(CashMovementType.Expense, 50m, "Gasto test");

                var state = cash.GetOpenSessionState()
                    ?? throw new InvalidOperationException("No hay sesión abierta.");
                if (state.ExpectedBalance != 600m)
                {
                    throw new InvalidOperationException($"Saldo esperado 600, actual {state.ExpectedBalance}.");
                }
            });

            Run("Cash: validation (double open)", () =>
            {
                try
                {
                    cash.OpenSession(100m, null);
                    throw new InvalidOperationException("Debía fallar al abrir segunda caja.");
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("Ya hay una caja abierta", StringComparison.OrdinalIgnoreCase))
                {
                    // expected
                }
            });

            Run("Cash: close session", () =>
            {
                var closed = cash.CloseSession(600m, "Cierre test");
                if (closed.Difference != 0m)
                {
                    throw new InvalidOperationException($"Diferencia esperada 0, actual {closed.Difference}.");
                }
            });

            Run("Employee: create", () =>
            {
                var employee = employees.Create("Juan Test", "3777-000000", "Carpintero");
                employeeId = employee.Id;
            });

            Run("Project: create and assign", () =>
            {
                var project = projects.Create(
                    "Mueble test",
                    "Cliente test",
                    "Descripción smoke",
                    10000m,
                    ProjectStatus.InProgress);
                projectId = project.Id;

                projects.AssignMaterial(projectId, productId, 5m);
                projects.AssignEmployee(projectId, employeeId, "Asignación test");

                var materials = projects.GetProjectMaterials(projectId);
                if (materials.Count != 1)
                {
                    throw new InvalidOperationException("Material no asignado.");
                }

                var assignments = projects.GetProjectAssignments(projectId);
                if (assignments.Count != 1)
                {
                    throw new InvalidOperationException("Empleado no asignado.");
                }

                if (assignments[0].IsPaid)
                {
                    throw new InvalidOperationException("El jornal tiene que arrancar pendiente.");
                }

                projects.SetAssignmentPaid(assignments[0].Id, true);
                if (!projects.GetProjectAssignments(projectId)[0].IsPaid)
                {
                    throw new InvalidOperationException("No se pudo marcar el jornal como pagado.");
                }

                var product = inventory.GetProducts(false, false, "Tornillo test").First();
                if (product.CurrentStock != 75m)
                {
                    throw new InvalidOperationException($"Stock tras asignar material: esperado 75, actual {product.CurrentStock}.");
                }
            });

            Run("Project: remove material restores stock", () =>
            {
                var material = projects.GetProjectMaterials(projectId).First();
                projects.RemoveMaterial(material.Id);

                if (projects.GetProjectMaterials(projectId).Count != 0)
                {
                    throw new InvalidOperationException("El material debía eliminarse del proyecto.");
                }

                var product = inventory.GetProducts(false, false, "Tornillo test").First();
                if (product.CurrentStock != 80m)
                {
                    throw new InvalidOperationException($"Stock tras devolver material: esperado 80, actual {product.CurrentStock}.");
                }

                // Re-assign for remaining project dependency checks.
                projects.AssignMaterial(projectId, productId, 5m);
            });

            Run("Project: validation (negative budget)", () =>
            {
                try
                {
                    projects.Create("Proyecto inválido", "Cliente", null, -10m, ProjectStatus.Quote);
                    throw new InvalidOperationException("Debía fallar por presupuesto negativo.");
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("presupuesto", StringComparison.OrdinalIgnoreCase))
                {
                    // expected
                }
            });

            Run("Project: validation (duplicate assignment)", () =>
            {
                try
                {
                    projects.AssignEmployee(projectId, employeeId, null);
                    throw new InvalidOperationException("Debía fallar por asignación duplicada.");
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("ya está asignado", StringComparison.OrdinalIgnoreCase))
                {
                    // expected
                }
            });

            Run("Cash: close with difference", () =>
            {
                cash.OpenSession(100m, "Segunda sesión");
                cash.RegisterMovement(CashMovementType.Income, 50m, "Ingreso extra");
                var closed = cash.CloseSession(140m, "Cierre con diferencia");
                if (closed.Difference != -10m)
                {
                    throw new InvalidOperationException($"Diferencia esperada -10, actual {closed.Difference}.");
                }
            });

            Run("Inventory: archive and restore", () =>
            {
                inventory.ArchiveProduct(productId);
                var archived = inventory.GetProducts(true, false, "Tornillo test").First();
                if (!archived.IsArchived)
                {
                    throw new InvalidOperationException("El producto debía quedar archivado.");
                }

                inventory.RestoreProduct(productId);
                var restored = inventory.GetProducts(false, false, "Tornillo test").First();
                if (restored.IsArchived)
                {
                    throw new InvalidOperationException("El producto debía restaurarse.");
                }
            });

            Run("Inventory: delete only without movements", () =>
            {
                var empty = inventory.CreateProduct("Producto vacío", 0m, 0m, "Pieza");
                if (inventory.HasMovements(empty.Id))
                {
                    throw new InvalidOperationException("Producto sin stock inicial no debía tener movimientos.");
                }

                inventory.DeleteProduct(empty.Id);
                if (inventory.GetProducts(true, false, "Producto vacío").Count != 0)
                {
                    throw new InvalidOperationException("El producto vacío debía eliminarse.");
                }
            });

            Run("Reports: low stock matches home counter", () =>
            {
                var lowFromDb = database.GetLowStockCount();
                var sections = reports.BuildSummary();
                var inventorySection = sections.First(s => s.Title == "Inventario");
                var lowFromReport = int.Parse(
                    inventorySection.Metrics.First(m => m.Label == "Stock bajo").Value);

                if (lowFromDb != lowFromReport)
                {
                    throw new InvalidOperationException(
                        $"Stock bajo inconsistente: DB={lowFromDb}, Reportes={lowFromReport}.");
                }
            });

            RunBudgetCalculatorTests();
            RunQuoteFreshnessTests();
            RunStockCounterTests(inventory, database, reports);
            RunQuoteTests(inventory, quotes, projects);
            CommercialTests.RunIntegration(Run, quotes, new PaymentService(database), cash, inventory);
            ClientTests.Run(Run, new ClientService(database), quotes, inventory);

            Run("Backup: create and restore", () =>
            {
                var backupService = new BackupService(paths, settingsService);
                var backup = backupService.CreateBackup();
                if (!File.Exists(backup.FullPath) || backup.SizeBytes <= 0)
                {
                    throw new InvalidOperationException("El respaldo no se creó correctamente.");
                }

                inventory.CreateProduct("Producto post-backup", 1m, 0m, "Pieza");
                backupService.RestoreBackup(backup.FullPath);

                var afterRestore = inventory.GetProducts(true, false, "Producto post-backup");
                if (afterRestore.Count != 0)
                {
                    throw new InvalidOperationException("Tras restaurar, el producto post-backup no debía existir.");
                }

                if (inventory.GetProducts(false, false, "Tornillo test").Count != 1)
                {
                    throw new InvalidOperationException("Tras restaurar, debía existir Tornillo test.");
                }
            });

            Run("Reports: build summary", () =>
            {
                var sections = reports.BuildSummary();
                if (sections.Count < 3)
                {
                    throw new InvalidOperationException($"Se esperaban al menos 3 secciones, hay {sections.Count}.");
                }
            });

            Run("Database counters", () =>
            {
                if (database.GetLowStockCount() < 0
                    || database.GetActiveProjectCount() < 0
                    || database.GetEmployeeCount() < 0)
                {
                    throw new InvalidOperationException("Contadores inválidos.");
                }
            });
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    // --- Calculadora de presupuestos -----------------------------------------

    private static void RunBudgetCalculatorTests()
    {
        Run("Cálculo: ejemplo de referencia ($ 287.000)", () =>
        {
            var breakdown = Calculate(100000m, 3m, 30000m);

            Expect(breakdown.MaterialsCost, 100000m, "materiales");
            Expect(breakdown.Waste, 16000m, "desperdicio 16%");
            Expect(breakdown.ToolWear, 9000m, "desgaste 9%");
            Expect(breakdown.Labor, 90000m, "mano de obra");
            Expect(breakdown.Overhead, 45000m, "gastos adicionales 50%");
            Expect(breakdown.Profit, 27000m, "ganancia 30%");
            Expect(breakdown.FinalPrice, 287000m, "precio final");
        });

        Run("Cálculo: el desglose suma exactamente el precio final", () =>
        {
            var breakdown = Calculate(33333.33m, 2.5m, 27777.77m);

            var shown = breakdown.Lines.Where(l => !l.IsTotal).Sum(l => l.Amount);
            Expect(shown, breakdown.FinalPrice, "suma de las líneas mostradas");
        });

        Run("Cálculo: caso de redondeo que la fórmula simplificada erra", () =>
        {
            var breakdown = Calculate(33333.33m, 2.5m, 27777.77m);
            Expect(breakdown.FinalPrice, 166666.64m, "precio final");

            // Si alguien "optimiza" usando (materiales × 1,25) + (mano de obra × 1,80),
            // el total deja de cerrar con el desglose que ve el cliente.
            var simplified = Math.Round(
                (33333.33m * 1.25m) + (27777.77m * 2.5m * 1.80m), 2, MidpointRounding.AwayFromZero);

            if (simplified == breakdown.FinalPrice)
            {
                throw new InvalidOperationException(
                    "Este caso debía diferir de la fórmula simplificada; el test perdió sentido.");
            }
        });

        Run("Cálculo: cambiar un porcentaje recalcula", () =>
        {
            var rates = BudgetRates.Defaults();
            rates.ProfitPercent = 40m;

            var breakdown = BudgetCalculatorService.Calculate(new BudgetInput
            {
                MaterialsCost = 100000m,
                Days = 3m,
                DailyRate = 30000m,
                Rates = rates
            });

            Expect(breakdown.Profit, 36000m, "ganancia al 40%");
            Expect(breakdown.FinalPrice, 296000m, "precio final al 40%");
        });

        Run("Cálculo: acepta días con decimales", () =>
        {
            Expect(Calculate(0m, 2.5m, 10000m).Labor, 25000m, "mano de obra de 2,5 jornadas");
        });

        Run("Cálculo: validación (valores negativos)", () =>
        {
            ExpectThrows(() => Calculate(-1m, 1m, 1m), "materiales");
            ExpectThrows(() => Calculate(1m, -1m, 1m), "días");
            ExpectThrows(() => Calculate(1m, 1m, -1m), "jornal");

            var rates = BudgetRates.Defaults();
            rates.WastePercent = -5m;
            ExpectThrows(
                () => BudgetCalculatorService.Calculate(new BudgetInput { Rates = rates }),
                "desperdicio");
        });

        // --- Mano de obra con operarios ---------------------------------------

        Run("Mano de obra: el jefe más dos operarios ($ 676.000)", () =>
        {
            var breakdown = CalculateWithWorkers();

            Expect(breakdown.Labor, 391000m, "mano de obra: 200.000 + 125.000 + 66.000");
            Expect(breakdown.Overhead, 100000m, "gastos 50% sobre el jornal del jefe");
            Expect(breakdown.Profit, 60000m, "ganancia 30% sobre el jornal del jefe");
            Expect(breakdown.FinalPrice, 676000m, "precio final");

            var overheadLine = breakdown.Lines.Single(l => l.Label == "Gastos adicionales");
            if (overheadLine.Detail?.Contains("jornal del jefe", StringComparison.Ordinal) != true)
            {
                throw new InvalidOperationException(
                    $"El detalle de gastos tenía que decir que es del jefe: «{overheadLine.Detail}».");
            }
        });

        Run("Mano de obra: sin operarios el resultado no se mueve", () =>
        {
            // Es la garantía de que ningún presupuesto ya entregado cambia de precio.
            var before = Calculate(100000m, 3m, 30000m);

            var after = BudgetCalculatorService.Calculate(new BudgetInput
            {
                MaterialsCost = 100000m,
                Days = 3m,
                DailyRate = 30000m,
                LaborLines = [],
                Rates = BudgetRates.Defaults()
            });

            Expect(after.FinalPrice, before.FinalPrice, "precio final sin operarios");
            Expect(after.FinalPrice, 287000m, "el ejemplo de referencia de siempre");

            if (after.HasWorkers)
            {
                throw new InvalidOperationException("Sin operarios no debía haber desglose por persona.");
            }

            // Y el renglón del desglose sigue diciendo lo mismo que antes.
            var labor = after.Lines.Single(l => l.Label == "Mano de obra");
            if (labor.Detail?.Contains("por día", StringComparison.Ordinal) != true)
            {
                throw new InvalidOperationException($"El detalle cambió de forma: «{labor.Detail}».");
            }
        });

        Run("Mano de obra: lo que pesa cada persona suma exactamente el total cargado", () =>
        {
            // Números feos a propósito: el jefe se lleva gastos y ganancia redondeados;
            // cada operario pesa exactamente su jornal. La columna tiene que cerrar igual.
            var breakdown = BudgetCalculatorService.Calculate(new BudgetInput
            {
                MaterialsCost = 33333.33m,
                Days = 2.5m,
                DailyRate = 27777.77m,
                LaborLines =
                [
                    new LaborLineInput { Description = "Uno", Days = 1.5m, DailyRate = 19999.99m },
                    new LaborLineInput { Description = "Otro", Days = 3.5m, DailyRate = 11111.11m }
                ],
                Rates = BudgetRates.Defaults()
            });

            Expect(
                breakdown.LaborShares.Sum(s => s.Amount),
                breakdown.Labor,
                "suma de los jornales por persona");

            Expect(
                breakdown.LaborShares.Sum(s => s.Loaded),
                breakdown.Labor + breakdown.Overhead + breakdown.Profit,
                "suma de la columna «pesa en el precio»");
        });

        Run("Mano de obra: el jefe siempre encabeza el desglose por persona", () =>
        {
            var breakdown = CalculateWithWorkers();

            Assert.Equal(breakdown.LaborShares.Count, 3, "el jefe más los dos operarios");

            if (!breakdown.LaborShares[0].IsForeman)
            {
                throw new InvalidOperationException("El jefe tenía que ir primero.");
            }

            Expect(breakdown.LaborShares[0].Amount, 200000m, "jornal del jefe");
            Expect(breakdown.LaborShares[0].Loaded, 360000m, "lo que pesa el jefe");

            // El operario se suma al costo: Diego cobra 66.000 y pesa 66.000.
            var diego = breakdown.LaborShares.Single(s => s.Description == "Diego Ruiz");
            Expect(diego.Amount, 66000m, "jornal de Diego");
            Expect(diego.Loaded, 66000m, "lo que Diego le suma al precio");
        });

        Run("Mano de obra: con operarios el desglose lista a cada uno", () =>
        {
            var breakdown = CalculateWithWorkers();
            var labels = breakdown.Lines.Select(l => l.Label).ToList();

            foreach (var expected in new[] { "Jefe", "Cristian Gómez", "Diego Ruiz" })
            {
                if (!labels.Contains(expected))
                {
                    throw new InvalidOperationException($"Faltaba «{expected}» en el desglose.");
                }
            }

            if (labels.Contains("Mano de obra"))
            {
                throw new InvalidOperationException(
                    "Con operarios no debía quedar el renglón unificado de mano de obra.");
            }

            var compact = breakdown.CompactLines.Single(l => l.Label == "Mano de obra");
            if (compact.Detail?.Contains("jefe +", StringComparison.Ordinal) != true)
            {
                throw new InvalidOperationException($"El compacto tenía que resumir: «{compact.Detail}».");
            }
        });

        Run("Mano de obra: validación (un operario con valores negativos)", () =>
        {
            ExpectThrows(
                () => BudgetCalculatorService.Calculate(new BudgetInput
                {
                    Days = 1m,
                    DailyRate = 1m,
                    LaborLines = [new LaborLineInput { Description = "Diego Ruiz", Days = -1m, DailyRate = 1m }],
                    Rates = BudgetRates.Defaults()
                }),
                // El mensaje nombra a la persona: con tres cargados hay que saber cuál.
                "Diego Ruiz");
        });
    }

    /// <summary>El ejemplo del jefe con dos operarios que se usa en varios tests.</summary>
    private static BudgetBreakdown CalculateWithWorkers() =>
        BudgetCalculatorService.Calculate(new BudgetInput
        {
            MaterialsCost = 100000m,
            Days = 5m,
            DailyRate = 40000m,
            LaborLines =
            [
                new LaborLineInput { Description = "Cristian Gómez", Days = 5m, DailyRate = 25000m },
                new LaborLineInput { Description = "Diego Ruiz", Days = 3m, DailyRate = 22000m }
            ],
            Rates = BudgetRates.Defaults()
        });

    private static BudgetBreakdown Calculate(decimal materials, decimal days, decimal dailyRate) =>
        BudgetCalculatorService.Calculate(new BudgetInput
        {
            MaterialsCost = materials,
            Days = days,
            DailyRate = dailyRate,
            Rates = BudgetRates.Defaults()
        });

    // --- Vigencia -------------------------------------------------------------

    private static void RunQuoteFreshnessTests()
    {
        var today = new DateTime(2026, 8, 7);

        Run("Vigencia: vigente, por vencer y vencido", () =>
        {
            ExpectFreshness(today.AddDays(15), today, QuoteFreshness.Current);
            ExpectFreshness(today.AddDays(4), today, QuoteFreshness.Current);
            ExpectFreshness(today.AddDays(3), today, QuoteFreshness.DueSoon);
            ExpectFreshness(today, today, QuoteFreshness.DueSoon);
            ExpectFreshness(today.AddDays(-1), today, QuoteFreshness.Expired);
            ExpectFreshness(null, today, QuoteFreshness.NoExpiry);
        });

        Run("Vigencia: antigüedad en palabras", () =>
        {
            ExpectText(QuoteRules.DescribeAge(today, today), "hoy");
            ExpectText(QuoteRules.DescribeAge(today.AddDays(-1), today), "ayer");
            ExpectText(QuoteRules.DescribeAge(today.AddDays(-6), today), "hace 6 días");
        });
    }

    // --- Contadores de stock --------------------------------------------------

    private static void RunStockCounterTests(
        InventoryService inventory,
        DatabaseService database,
        ReportService reports)
    {
        Run("Stock: 9 con mínimo 15 cuenta como stock bajo", () =>
        {
            var before = database.GetLowStockCount();
            inventory.CreateProduct("Bisagra baja", 9m, 15m, "Unidad");
            Expect(database.GetLowStockCount(), before + 1, "contador de stock bajo");
        });

        Run("Stock: 100 con mínimo 20 NO cuenta como stock bajo", () =>
        {
            // Comparado como texto, '100.0' < '20.0' y este producto se marcaba en falso.
            var before = database.GetLowStockCount();
            inventory.CreateProduct("Tabla sobrada", 100m, 20m, "Metro");
            Expect(database.GetLowStockCount(), before, "contador de stock bajo");
        });

        Run("Stock: producto en cero cuenta como sin stock", () =>
        {
            // Comparado como texto, '0.0' <= 0 era falso y el contador daba siempre cero.
            var before = ReadMetric(reports, "Inventario", "Sin stock");
            inventory.CreateProduct("Cola agotada", 0m, 5m, "Litro");
            Expect(ReadMetric(reports, "Inventario", "Sin stock"), before + 1, "contador de sin stock");
        });

        Run("Stock: el contador coincide con la lista de Inventario", () =>
        {
            var fromList = inventory.GetProducts(false, false, null)
                .Count(p => p.Status != ProductStockStatus.Ok);

            Expect(database.GetLowStockCount(), fromList, "contador contra la lista");
        });
    }

    // --- Presupuestos ---------------------------------------------------------

    private static void RunQuoteTests(
        InventoryService inventory,
        QuoteService quotes,
        ProjectService projects)
    {
        var boardId = 0;
        var quoteId = 0;
        var approvedId = 0;

        Run("Presupuesto: crear autocompleta la vigencia", () =>
        {
            boardId = inventory.CreateProduct("Tabla presupuesto", 10m, 2m, "Metro", 1200m).Id;
            quoteId = quotes.CreateQuote("Mueble de cocina", "Cliente presupuesto", "A medida").Id;

            var detail = RequireQuote(quotes, quoteId);

            if (detail.Status != ProjectStatus.Quote)
            {
                throw new InvalidOperationException($"Estado esperado Presupuesto, actual {detail.Status}.");
            }

            if (detail.ValidUntilLocal is null)
            {
                throw new InvalidOperationException("Debía autocompletar la fecha de validez.");
            }

            if (detail.Freshness != QuoteFreshness.Current)
            {
                throw new InvalidOperationException($"Un presupuesto nuevo debía quedar vigente, quedó {detail.Freshness}.");
            }
        });

        Run("Presupuesto: agregar materiales NO mueve el stock", () =>
        {
            quotes.AddInventoryLine(quoteId, boardId, 4m, 1500m);
            quotes.AddLooseLine(quoteId, "Bisagra importada", "Unidad", 8m, 250m, saveToCatalog: false);

            var stock = SingleProduct(inventory, "Tabla presupuesto").CurrentStock;
            if (stock != 10m)
            {
                throw new InvalidOperationException($"El presupuesto no debía tocar el stock; quedó en {stock}.");
            }

            var detail = RequireQuote(quotes, quoteId);
            if (detail.Lines.Count != 2)
            {
                throw new InvalidOperationException($"Se esperaban 2 líneas, hay {detail.Lines.Count}.");
            }

            Expect(detail.MaterialsTotal, 8000m, "total de materiales");
        });

        Run("Presupuesto: ítem suelto guardado en catálogo entra en cero", () =>
        {
            quotes.AddLooseLine(quoteId, "Cola vinílica premium", "Litro", 2m, 900m, saveToCatalog: true);

            var created = SingleProduct(inventory, "Cola vinílica premium");
            Expect(created.CurrentStock, 0m, "stock del producto creado");
            Expect(created.CostPrice ?? 0m, 900m, "precio de costo del producto creado");

            if (created.Status != ProductStockStatus.Out)
            {
                throw new InvalidOperationException("Debía quedar marcado como sin stock.");
            }
        });

        Run("Presupuesto: guardar el cálculo congela las entradas", () =>
        {
            var detail = RequireQuote(quotes, quoteId);
            var breakdown = quotes.SaveCalculation(quoteId, detail.MaterialsTotal, 3m, 30000m, BudgetRates.Defaults());

            var reloaded = RequireQuote(quotes, quoteId);
            if (reloaded.Breakdown is null)
            {
                throw new InvalidOperationException("El desglose debía reconstruirse desde lo guardado.");
            }

            Expect(reloaded.Breakdown.FinalPrice, breakdown.FinalPrice, "precio final reconstruido");
            Expect(reloaded.Budget ?? 0m, breakdown.FinalPrice, "precio guardado en el proyecto");
            Expect(reloaded.Breakdown.MaterialsCost, 9800m, "materiales congelados");

            if (reloaded.Rates is null || reloaded.Rates.ProfitPercent != 30m)
            {
                throw new InvalidOperationException("No se congelaron los porcentajes.");
            }
        });

        Run("Presupuesto: el precio del material queda congelado", () =>
        {
            // Sube el precio de catálogo: lo ya cotizado no se puede mover solo.
            inventory.UpdateProduct(boardId, "Tabla presupuesto", 2m, "Metro", costPrice: 9999m);

            var line = RequireQuote(quotes, quoteId).Lines.First(l => l.ProductId == boardId);
            Expect(line.UnitCost, 1500m, "precio unitario congelado");
        });

        Run("Presupuesto: no se borra un producto usado en un presupuesto", () =>
        {
            var loose = SingleProduct(inventory, "Cola vinílica premium");
            ExpectThrows(() => inventory.DeleteProduct(loose.Id), "presupuesto");
        });

        Run("Presupuesto: aprobar descuenta stock y genera los materiales", () =>
        {
            var productId = inventory.CreateProduct("Melamina aprobación", 20m, 2m, "Metro cuadrado", 800m).Id;
            approvedId = quotes.CreateQuote("Placard", "Cliente aprobación", null).Id;
            quotes.AddInventoryLine(approvedId, productId, 5m);
            quotes.SaveCalculation(approvedId, 4000m, 2m, 25000m, BudgetRates.Defaults());

            var result = quotes.ApproveQuote(approvedId);
            if (result.HasShortfalls)
            {
                throw new InvalidOperationException("No debía faltar stock.");
            }

            Expect(SingleProduct(inventory, "Melamina aprobación").CurrentStock, 15m, "stock tras aprobar");

            var detail = RequireQuote(quotes, approvedId);
            if (detail.Status != ProjectStatus.InProgress)
            {
                throw new InvalidOperationException($"Debía pasar a En curso, quedó en {detail.Status}.");
            }

            var materials = projects.GetProjectMaterials(approvedId);
            if (materials.Count != 1 || materials[0].Quantity != 5m)
            {
                throw new InvalidOperationException("Debía quedar un material entregado de 5 unidades.");
            }
        });

        Run("Presupuesto: no se puede aprobar dos veces", () =>
        {
            ExpectThrows(() => quotes.ApproveQuote(approvedId), "ya fue aprobado");
        });

        Run("Presupuesto: no se editan líneas de un presupuesto aprobado", () =>
        {
            ExpectThrows(() => quotes.AddInventoryLine(approvedId, boardId, 1m), "aprobado");
        });

        Run("Presupuesto: aprobar sin stock descuenta lo que hay y avisa el faltante", () =>
        {
            var productId = inventory.CreateProduct("Herraje faltante", 3m, 0m, "Unidad", 500m).Id;
            var id = quotes.CreateQuote("Vitrina", "Cliente faltante", null).Id;
            quotes.AddInventoryLine(id, productId, 10m);
            quotes.SaveCalculation(id, 5000m, 1m, 20000m, BudgetRates.Defaults());

            var result = quotes.ApproveQuote(id);
            if (!result.HasShortfalls)
            {
                throw new InvalidOperationException("Debía reportar el faltante.");
            }

            Expect(result.Shortfalls.Single().Missing, 7m, "cantidad faltante");
            Expect(SingleProduct(inventory, "Herraje faltante").CurrentStock, 0m, "stock tras aprobar");

            // Reintentar sin reponer no puede descontar de más.
            var retry = quotes.ApplyPendingStock(id);
            Expect(retry.Shortfalls.Single().Missing, 7m, "faltante sin reponer");
            Expect(SingleProduct(inventory, "Herraje faltante").CurrentStock, 0m, "stock sin reponer");

            // Con el material comprado, el pendiente se salda.
            inventory.RegisterMovement(productId, StockMovementType.In, 10m, "Compra de reposición");
            var settled = quotes.ApplyPendingStock(id);

            if (settled.HasShortfalls)
            {
                throw new InvalidOperationException("Ya no debía faltar nada.");
            }

            Expect(SingleProduct(inventory, "Herraje faltante").CurrentStock, 3m, "stock tras completar");
        });

        Run("Presupuesto: rechazar y reabrir", () =>
        {
            var id = quotes.CreateQuote("Mesa rechazada", "Cliente que dijo que no", null).Id;

            quotes.RejectQuote(id);
            var rejected = RequireQuote(quotes, id);

            if (rejected.Status != ProjectStatus.Rejected)
            {
                throw new InvalidOperationException("Debía quedar rechazado.");
            }

            if (rejected.IsEditable)
            {
                throw new InvalidOperationException("Un presupuesto rechazado no debería ser editable.");
            }

            quotes.ReopenQuote(id);
            if (RequireQuote(quotes, id).Status != ProjectStatus.Quote)
            {
                throw new InvalidOperationException("Debía volver a Presupuesto.");
            }
        });

        Run("Presupuesto: vencido no cambia nada solo y todavía se puede aprobar", () =>
        {
            var productId = inventory.CreateProduct("Pino vencido", 5m, 0m, "Metro", 100m).Id;
            var id = quotes.CreateQuote("Banco", "Cliente tardío", null, DateTime.Today.AddDays(-1)).Id;
            quotes.AddInventoryLine(id, productId, 2m);
            quotes.SaveCalculation(id, 200m, 1m, 15000m, BudgetRates.Defaults());

            var detail = RequireQuote(quotes, id);

            if (detail.Freshness != QuoteFreshness.Expired)
            {
                throw new InvalidOperationException("Debía figurar como vencido.");
            }

            if (detail.Status != ProjectStatus.Quote || detail.IsArchived)
            {
                throw new InvalidOperationException("Vencer no puede cambiar el estado ni archivar.");
            }

            // La app avisa, no decide: un vencido se sigue pudiendo aprobar.
            quotes.ApproveQuote(id);
            if (RequireQuote(quotes, id).Status != ProjectStatus.InProgress)
            {
                throw new InvalidOperationException("Un vencido debía poder aprobarse igual.");
            }
        });

        Run("Presupuesto: duplicar recotiza con los precios de hoy", () =>
        {
            var productId = inventory.CreateProduct("Roble duplicado", 50m, 0m, "Metro", 1000m).Id;
            var id = quotes.CreateQuote("Biblioteca", "Cliente repetido", null).Id;
            quotes.AddInventoryLine(id, productId, 3m);
            quotes.SaveCalculation(id, 3000m, 2m, 20000m, BudgetRates.Defaults());

            inventory.UpdateProduct(productId, "Roble duplicado", 0m, "Metro", costPrice: 2000m);

            var copyId = quotes.DuplicateQuote(id);
            var copy = RequireQuote(quotes, copyId);

            if (copy.Status != ProjectStatus.Quote)
            {
                throw new InvalidOperationException("La copia debía nacer como presupuesto.");
            }

            Expect(copy.Lines.Single().UnitCost, 2000m, "precio recotizado en la copia");
            Expect(copy.MaterialsTotal, 6000m, "materiales de la copia");

            // El original no se toca.
            Expect(RequireQuote(quotes, id).Lines.Single().UnitCost, 1000m, "precio del original");
        });

        Run("Presupuesto: adjuntar solo deja pasar al mismo cliente", () =>
        {
            var mesa = quotes.CreateQuote("Mesa adjunto", "Marcos Adjunto", "Comedor").Id;
            var placard = quotes.CreateQuote("Placard adjunto", "Marcos Adjunto", "Dormitorio").Id;
            var silla = quotes.CreateQuote("Silla ajena", "Juan Otro", null).Id;

            quotes.AttachQuote(mesa, placard);

            var detail = RequireQuote(quotes, mesa);
            Assert.Equal(detail.Attachments.Count, 1, "un adjunto");
            Assert.Equal(detail.Attachments[0].Title, "Placard adjunto", "título del anexo");

            ExpectThrows(() => quotes.AttachQuote(mesa, mesa), "sí mismo");
            ExpectThrows(() => quotes.AttachQuote(mesa, silla), "mismo cliente");
            ExpectThrows(() => quotes.AttachQuote(mesa, placard), "ya está adjunto");
        });

        Run("Presupuesto: otro trabajo del mismo cliente nace vacío y enganchado", () =>
        {
            var parent = quotes.CreateQuote("Cocina principal", "Cliente hermano", null);
            var siblingId = quotes.CreateSiblingQuote(parent.Id, "Alacena");

            var sibling = RequireQuote(quotes, siblingId);
            Assert.Equal(sibling.ClientName, "Cliente hermano", "mismo cliente");
            Assert.Equal(sibling.Lines.Count, 0, "sin materiales");
            Assert.Equal(sibling.Status, ProjectStatus.Quote, "presupuesto nuevo");

            var fromParent = RequireQuote(quotes, parent.Id);
            Assert.Equal(fromParent.Attachments.Single().ProjectId, siblingId, "enganchado al principal");
        });

        Run("Presupuesto: el aviso de seña es opt-in", () =>
        {
            var id = quotes.CreateQuote("Mesa seña", "Cliente seña", null).Id;
            quotes.AddLooseLine(id, "Tabla", "Metro", 1m, 1000m, saveToCatalog: false);
            quotes.SaveCalculation(id, 1000m, 1m, 40000m, BudgetRates.Defaults());

            var before = RequireQuote(quotes, id);
            if (before.HasCommitmentNote)
            {
                throw new InvalidOperationException("Sin marcar el aviso no debía aparecer.");
            }

            quotes.SaveCommitmentNote(id, true, 100000m, null);
            var after = RequireQuote(quotes, id);

            if (!after.HasCommitmentNote)
            {
                throw new InvalidOperationException("Con tilde e importe el aviso tenía que prenderse.");
            }

            if (!after.CommitmentNoteDisplay.Contains("100.000", StringComparison.Ordinal)
                && !after.CommitmentNoteDisplay.Contains("100000", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"El aviso no nombraba el importe: «{after.CommitmentNoteDisplay}».");
            }
        });

        Run("Presupuesto: contador de pendientes", () =>
        {
            var summary = quotes.GetPendingSummary();
            var listed = quotes.GetQuotes(QuoteFilter.All, null).Count(q => q.Status == ProjectStatus.Quote);

            if (summary.Pending + summary.Expired != listed)
            {
                throw new InvalidOperationException(
                    $"Pendientes {summary.Pending} + vencidos {summary.Expired} debía dar {listed}.");
            }
        });

        Run("Presupuesto: el filtro de vencidos deja fuera a los rechazados", () =>
        {
            foreach (var item in quotes.GetQuotes(QuoteFilter.Expired, null))
            {
                if (item.Status == ProjectStatus.Rejected)
                {
                    throw new InvalidOperationException("Un rechazado no debía aparecer entre los vencidos.");
                }
            }
        });
    }

    // --- Migraciones ----------------------------------------------------------

    private static void RunMigrationTests()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "MetroCarpinteriaMigracion_" + Guid.NewGuid().ToString("N"));

        try
        {
            var paths = new AppPaths(root);
            paths.EnsureDirectories();

            var database = new DatabaseService(paths);
            database.Initialize();

            Run("Migraciones: una base nueva queda en la última versión", () =>
            {
                Expect(ReadUserVersion(paths.DatabasePath), SchemaMigrator.LatestVersion, "user_version");
            });

            Run("Migraciones: inicializar dos veces es idempotente", () =>
            {
                database.Initialize();
                database.Initialize();

                Expect(ReadUserVersion(paths.DatabasePath), SchemaMigrator.LatestVersion, "user_version");
                AssertIntegrity(paths.DatabasePath);
            });

            Run("Migraciones: una base en v0 con datos migra sin perder nada", () =>
            {
                var inventory = new InventoryService(database);
                inventory.CreateProduct("Producto previo", 7m, 1m, "Unidad", 123m);

                // Simula una instalación vieja: el esquema ya existe pero el marcador
                // de versión nunca se escribió.
                SetUserVersion(paths.DatabasePath, 0);

                var migrator = new SchemaMigrator(paths.DatabasePath);
                if (!migrator.HasPendingMigrations())
                {
                    throw new InvalidOperationException("Debía detectar migraciones pendientes.");
                }

                var result = migrator.MigrateToLatest();
                if (!result.AnyApplied || result.ToVersion != SchemaMigrator.LatestVersion)
                {
                    throw new InvalidOperationException("La migración no llegó a la última versión.");
                }

                var kept = inventory.GetProducts(false, false, "Producto previo").SingleOrDefault()
                    ?? throw new InvalidOperationException("Se perdió el producto al migrar.");

                Expect(kept.CurrentStock, 7m, "stock preservado");
                Expect(kept.CostPrice ?? 0m, 123m, "precio de costo preservado");
                AssertIntegrity(paths.DatabasePath);
            });
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    // --- Utilidades de test ---------------------------------------------------

    private static QuoteDetail RequireQuote(QuoteService quotes, int id) =>
        quotes.GetDetail(id) ?? throw new InvalidOperationException($"No se encontró el presupuesto {id}.");

    private static ProductListItem SingleProduct(InventoryService inventory, string name) =>
        inventory.GetProducts(true, false, name).SingleOrDefault()
        ?? throw new InvalidOperationException($"No se encontró el producto «{name}».");

    /// <summary>
    /// Las cantidades se mostraban con "N2", que rellena decimales: 5 tornillos salían
    /// como "5,00 Unidad". Acá se fija que los decimales aparezcan solo si existen.
    /// </summary>
    // --- Actualizaciones ------------------------------------------------------

    /// <summary>
    /// Que «no pude preguntar» nunca se confunda con «estás al día».
    /// </summary>
    /// <remarks>
    /// Se publicó la 1.4.0 y la máquina del taller, con la 1.3.0 instalada, no se enteró.
    /// El chequeo devolvía el mismo null cuando fallaba que cuando no había novedades, así
    /// que la pantalla decía «ya tenés la última versión» y nadie podía notar la diferencia.
    /// Los tests corren sin instalar, así que acá solo se puede fijar el caso NotSupported;
    /// lo que importa es que ninguno de los dos caiga en UpToDate por descarte.
    /// </remarks>
    private static void RunUpdateCheckTests()
    {
        Run("Actualizaciones: una copia sin instalar no dice que está al día", () =>
        {
            var service = new UpdateService(new SettingsService(new AppPaths(
                Path.Combine(Path.GetTempPath(), $"MetroUpdate_{Guid.NewGuid():N}"))));

            // Corriendo desde los tests nunca es una copia instalada.
            if (service.IsSupported)
            {
                throw new InvalidOperationException(
                    "Los tests no corren desde una instalación, así que esto tenía que ser falso.");
            }

            var check = service.CheckAsync().GetAwaiter().GetResult();

            Assert.Equal(
                check.Outcome,
                UpdateCheckOutcome.NotSupported,
                "una copia suelta tiene que decir que no aplica, no que está al día");

            if (check.Update is not null)
            {
                throw new InvalidOperationException("Sin actualizador no puede venir una versión.");
            }
        });

        Run("Actualizaciones: los cuatro resultados son distintos entre sí", () =>
        {
            // Si alguien colapsa dos de estos en el mismo valor, volvemos al problema
            // original: un fallo indistinguible de estar actualizado.
            var outcomes = Enum.GetValues<UpdateCheckOutcome>();

            Assert.Equal(outcomes.Length, 4, "esperaba NotSupported, UpToDate, Available y Failed");
            Assert.Equal(outcomes.Distinct().Count(), 4, "los resultados no pueden repetirse");
        });

        Run("Actualizaciones: el feed se busca en la URL estable de GitHub, no en la API", () =>
        {
            // GithubSource pega a api.github.com (60 pedidos/hora) y baja el json de
            // cada release viejo: un solo 404 tumba el chequeo. Esta URL es un archivo.
            Assert.Equal(
                GitHubLatestUpdateSource.LatestDownloadUrl,
                "https://github.com/lovera2025/Carpinteria_App/releases/latest/download/",
                "si cambia esta URL, las copias instaladas dejan de encontrar versiones nuevas");
        });
    }

    private static void RunNumberFormatTests()
    {
        Run("Cantidad entera sin decimales de relleno", () =>
        {
            ExpectText(AppCulture.Quantity(5m), "5");
            ExpectText(AppCulture.Quantity(100m), "100");
            ExpectText(AppCulture.Quantity(0m), "0");
        });

        Run("Cantidad fraccionaria conserva decimales", () =>
        {
            ExpectText(AppCulture.Quantity(2.5m), "2,5");
            ExpectText(AppCulture.Quantity(0.25m), "0,25");
            ExpectText(AppCulture.Quantity(1.75m), "1,75");
        });

        Run("Unidades abreviadas", () =>
        {
            ExpectText(ProductUnits.Abbreviate(ProductUnits.Unit), "u.");
            ExpectText(ProductUnits.Abbreviate(ProductUnits.SquareMeter), "m²");
            ExpectText(ProductUnits.Abbreviate(ProductUnits.Kilogram), "kg");

            // Las abreviaturas viejas guardadas en la base se normalizan antes de acortar.
            ExpectText(ProductUnits.Abbreviate("m2"), "m²");

            // Una unidad tipeada a mano se respeta tal cual.
            ExpectText(ProductUnits.Abbreviate("Chapa"), "Chapa");
        });

        Run("Cantidad con unidad, formato completo", () =>
        {
            ExpectText(AppCulture.QuantityWithUnit(5m, ProductUnits.Unit), "5 u.");
            ExpectText(AppCulture.QuantityWithUnit(2.5m, ProductUnits.SquareMeter), "2,5 m²");
            ExpectText(AppCulture.QuantityWithUnit(12m, ProductUnits.Meter), "12 m");
        });

        Run("Los modelos usan el formato corregido", () =>
        {
            var line = new QuoteLineItem
            {
                Description = "Tornillo",
                Unit = ProductUnits.Unit,
                Quantity = 5m,
                UnitCost = 100m,
                AvailableStock = 20m
            };

            ExpectText(line.QuantityDisplay, "5 u.");
            ExpectText(line.StockDisplay, "Stock: 20 u.");

            var product = new ProductListItem
            {
                Name = "Placa",
                Unit = ProductUnits.SquareMeter,
                CurrentStock = 2.5m,
                MinimumStock = 1m
            };

            ExpectText(product.StockDisplay, "2,5 m²");
            ExpectText(product.MinimumDisplay, "1 m²");
        });
    }

    private static void Expect(decimal actual, decimal expected, string label)
    {
        if (actual != expected)
        {
            throw new InvalidOperationException($"{label}: esperado {expected}, actual {actual}.");
        }
    }

    private static void Expect(int actual, int expected, string label)
    {
        if (actual != expected)
        {
            throw new InvalidOperationException($"{label}: esperado {expected}, actual {actual}.");
        }
    }

    private static void ExpectText(string actual, string expected)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Esperado «{expected}», actual «{actual}».");
        }
    }

    private static void ExpectFreshness(DateTime? validUntil, DateTime today, QuoteFreshness expected)
    {
        var actual = QuoteRules.GetFreshness(validUntil, today);
        if (actual != expected)
        {
            var label = validUntil?.ToString("dd/MM/yyyy") ?? "sin fecha";
            throw new InvalidOperationException($"{label}: esperado {expected}, actual {actual}.");
        }
    }

    private static void ExpectThrows(Action action, string expectedFragment)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException ex)
            when (ex.Message.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new InvalidOperationException($"Debía fallar con un mensaje sobre «{expectedFragment}».");
    }

    private static int ReadMetric(ReportService reports, string section, string metric)
    {
        var value = reports.BuildSummary()
            .First(s => s.Title == section)
            .Metrics.First(m => m.Label == metric)
            .Value;

        return int.Parse(value);
    }

    private static int ReadUserVersion(string databasePath)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void SetUserVersion(string databasePath, int version)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA user_version = {version};";
        command.ExecuteNonQuery();
    }

    private static void AssertIntegrity(string databasePath)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        var result = command.ExecuteScalar()?.ToString();

        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"integrity_check devolvió: {result}");
        }
    }

    private static void Run(string name, Action action)
    {
        try
        {
            action();
            Results.Add(new TestResult(name, true, null));
            Console.WriteLine($"  OK  {name}");
        }
        catch (Exception ex)
        {
            Results.Add(new TestResult(name, false, ex.Message));
            Console.WriteLine($"  FAIL  {name}");
            Console.WriteLine($"        {ex.Message}");
        }
    }

    private static void PrintManualUiChecklist()
    {
        Console.WriteLine();
        Console.WriteLine("--- Checklist manual de UI (opcional, ~5 min) ---");
        string[] items =
        [
            "Abrir la app y verificar que el logo y colores se ven bien",
            "Inventario: crear producto con precio de costo, movimiento, buscar y filtrar",
            "Caja: abrir sesión, registrar ingreso/egreso, cerrar",
            "Presupuestos: armar uno con ítems del inventario y uno suelto",
            "Presupuestos: verificar en Inventario que el stock NO se movió",
            "Presupuestos: calcular con 100000 / 3 días / 30000 y ver $ 287.000,00",
            "Presupuestos: agregar dos operarios y ver que el precio final sube",
            "Presupuestos: imprimir para el cliente y revisar que solo muestre el TOTAL",
            "Presupuestos: «Guardar PDF» — elegir carpeta, y que el archivo se abra solo",
            "Presupuestos: imprimir la hoja de costos y revisar que SÍ traiga el desglose",
            "Presupuestos: en la hoja de costos, que «Pesa en el precio» sume mano de obra + gastos + ganancia",
            "Presupuestos: abrir uno viejo (sin operarios) y confirmar que el precio no se movió",
            "Presupuestos: aprobar y confirmar que descuenta stock y aparece en Proyectos",
            "Proyectos: que los operarios cotizados hayan quedado asignados solos",
            "Proyectos: crear, asignar material y empleado",
            "Personal: alta de empleado con jornal, y que prellene la mano de obra al cotizarlo",
            "Reportes: revisar que los números coinciden con los módulos",
            "Configuración: respaldar y restaurar un backup de prueba",
            "Proyectos: quitar material y verificar que vuelve el stock"
        ];

        foreach (var item in items)
        {
            Console.WriteLine($"  [ ] {item}");
        }
    }

    private static void PrintSummary()
    {
        var passed = Results.Count(r => r.Passed);
        var total = Results.Count;

        Console.WriteLine();
        Console.WriteLine("--- Resumen ---");
        Console.WriteLine($"{passed}/{total} pruebas OK");

        if (passed == total)
        {
            Console.WriteLine();
            Console.WriteLine("RESULTADO: TODO OK — la app está funcionando correctamente.");
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine("RESULTADO: HAY FALLOS — revisá los errores arriba.");
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private sealed record TestResult(string Name, bool Passed, string? Error);
}
