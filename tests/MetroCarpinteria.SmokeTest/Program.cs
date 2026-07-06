using System.IO;
using MetroCarpinteria.App.Data.Entities;
using MetroCarpinteria.App.Services;
using Microsoft.Data.Sqlite;

namespace MetroCarpinteria.SmokeTest;

internal static class Program
{
    private static readonly List<TestResult> Results = [];

    public static int Main()
    {
        Console.WriteLine("=== Metro Carpintería — Smoke Test ===");
        Console.WriteLine();

        Run("Build (solution compiles)", () =>
        {
            // Verified by running this executable after dotnet build.
        });

        RunProductionHealthChecks();
        RunIsolatedIntegrationFlow();
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

            var inventory = new InventoryService(database);
            var cash = new CashRegisterService(database);
            var projects = new ProjectService(database);
            var employees = new EmployeeService(database);
            var reports = new ReportService(database);

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

                var product = inventory.GetProducts(false, false, "Tornillo test").First();
                if (product.CurrentStock != 75m)
                {
                    throw new InvalidOperationException($"Stock tras asignar material: esperado 75, actual {product.CurrentStock}.");
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
            "Inventario: crear producto, movimiento, buscar y filtrar",
            "Caja: abrir sesión, registrar ingreso/egreso, cerrar",
            "Proyectos: crear, asignar material y empleado",
            "Personal: alta de empleado y asignación",
            "Reportes: revisar que los números coinciden con los módulos",
            "Configuración: abrir carpetas de datos y probar backup manual"
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
