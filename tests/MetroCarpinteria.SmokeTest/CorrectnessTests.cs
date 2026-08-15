using System.IO;
using MetroCarpinteria.App.Data.Entities;
using MetroCarpinteria.App.Models;
using MetroCarpinteria.App.Services;
using Microsoft.Data.Sqlite;

namespace MetroCarpinteria.SmokeTest;

/// <summary>
/// Los diez agujeros de correctitud de F5. Cada uno describe la situación real que lo
/// destapó, porque el síntoma casi nunca aparece donde está la causa: el stock quedaba
/// mal en Inventario y el motivo estaba en la pantalla de Proyectos.
/// </summary>
internal static class CorrectnessTests
{
    public static void Run(Action<string, Action> run)
    {
        var root = Path.Combine(Path.GetTempPath(), $"MetroCarpinteriaCorrectitud_{Guid.NewGuid():N}");

        try
        {
            var paths = new AppPaths(root);
            paths.EnsureDirectories();

            var database = new DatabaseService(paths);
            database.Initialize();

            var settings = new SettingsService(paths);
            var inventory = new InventoryService(database);
            var projects = new ProjectService(database);
            var quotes = new QuoteService(database, settings);
            var employees = new EmployeeService(database);

            RunApprovalTests(run, inventory, quotes);
            RunStockWarningTests(run, inventory, quotes);
            RunFinalPriceTests(run, inventory, quotes);
            RunAssignmentPayTests(run, projects, employees);
            RunStatusPolicyTests(run, inventory, quotes, projects);
            RunBackupTests(run, paths, settings, database);
            RunSettingsTests(run);
            RunClockTests(run);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    // --- Aprobación -----------------------------------------------------------

    private static void RunApprovalTests(
        Action<string, Action> run,
        InventoryService inventory,
        QuoteService quotes)
    {
        run("Correctitud: no se aprueba un presupuesto sin precio", () =>
        {
            // Aprobar descuenta stock y ya no se puede editar. Sin precio, el trabajo
            // arrancaba con el inventario movido y nada que cobrar.
            var productId = inventory.CreateProduct("Fenólico sin precio", 20m, 0m, "Metro cuadrado", 900m).Id;
            var id = quotes.CreateQuote("Mueble sin cotizar", "Cliente apurado", null).Id;
            quotes.AddInventoryLine(id, productId, 3m);

            Assert.Throws(() => quotes.ApproveQuote(id), "precio");

            // Y el inventario quedó intacto.
            Assert.Equal(SingleProduct(inventory, "Fenólico sin precio").CurrentStock, 20m, "stock tras el rechazo");
        });

        run("Correctitud: no se aprueba un presupuesto sin materiales", () =>
        {
            var id = quotes.CreateQuote("Trabajo vacío", "Cliente sin lista", null).Id;
            quotes.SaveCalculation(id, 0m, 2m, 30000m, BudgetRates.Defaults());

            Assert.Throws(() => quotes.ApproveQuote(id), "materiales");
        });
    }

    // --- Aviso de faltante ----------------------------------------------------

    private static void RunStockWarningTests(
        Action<string, Action> run,
        InventoryService inventory,
        QuoteService quotes)
    {
        run("Correctitud: el aviso de faltante suma las líneas del mismo producto", () =>
        {
            // Dos líneas de 6 sobre un stock de 10: cada una alcanza por separado, pero
            // al aprobar se descuentan las dos. Evaluado línea por línea, el presupuesto
            // se veía cubierto y el faltante recién aparecía después de aprobar.
            var productId = inventory.CreateProduct("Melamina repetida", 10m, 0m, "Metro cuadrado", 700m).Id;
            var id = quotes.CreateQuote("Vestidor", "Cliente repetidor", null).Id;
            quotes.AddInventoryLine(id, productId, 6m);
            quotes.AddInventoryLine(id, productId, 6m);

            var lines = RequireQuote(quotes, id).Lines;
            Assert.Equal(lines.Count, 2, "líneas del presupuesto");
            Assert.True(lines.All(l => l.HasStockWarning), "las dos líneas tendrían que avisar el faltante.");
        });

        run("Correctitud: con stock de sobra no se avisa nada", () =>
        {
            var productId = inventory.CreateProduct("Melamina sobrada", 10m, 0m, "Metro cuadrado", 700m).Id;
            var id = quotes.CreateQuote("Repisa", "Cliente tranquilo", null).Id;
            quotes.AddInventoryLine(id, productId, 4m);
            quotes.AddInventoryLine(id, productId, 4m);

            var lines = RequireQuote(quotes, id).Lines;
            Assert.True(lines.All(l => !l.HasStockWarning), "8 de 10 alcanzan: no correspondía avisar.");
        });
    }

    // --- Precio final ajustado ------------------------------------------------

    private static void RunFinalPriceTests(
        Action<string, Action> run,
        InventoryService inventory,
        QuoteService quotes)
    {
        run("Correctitud: el precio final ajustado no toca el cálculo", () =>
        {
            // Redondear para cerrar la venta es lo normal en el taller. Lo que no puede
            // pasar es que el redondeo se coma el desglose: la hoja de costos tiene que
            // seguir mostrando lo que el trabajo cuesta de verdad.
            var productId = inventory.CreateProduct("Roble ajustado", 30m, 0m, "Metro", 1000m).Id;
            var id = quotes.CreateQuote("Mesa redondeada", "Cliente negociador", null).Id;
            quotes.AddInventoryLine(id, productId, 5m);

            var calculated = quotes.SaveCalculation(id, 5000m, 3m, 30000m, BudgetRates.Defaults());
            quotes.SetFinalPrice(id, 150000m);

            var detail = RequireQuote(quotes, id);
            Assert.Equal(detail.Budget ?? 0m, 150000m, "precio guardado");
            Assert.Equal(detail.Breakdown!.FinalPrice, calculated.FinalPrice, "desglose tras ajustar");
            Assert.True(detail.BudgetAdjustedManually, "tendría que quedar marcado como ajustado a mano.");

            // Volver al calculado usa el mismo camino, y el cartel se apaga.
            quotes.SetFinalPrice(id, calculated.FinalPrice);
            Assert.False(
                RequireQuote(quotes, id).BudgetAdjustedManually,
                "al volver al calculado ya no habría que avisar de un ajuste.");
        });

        run("Correctitud: recortar de ganancia baja esa línea y el desglose suma el nuevo total", () =>
        {
            var productId = inventory.CreateProduct("Pino recortado", 20m, 0m, "Metro", 800m).Id;
            var id = quotes.CreateQuote("Mesa de pino", "Cliente que pide descuento", null).Id;
            quotes.AddInventoryLine(id, productId, 4m);

            var calculated = quotes.SaveCalculation(id, 4000m, 2m, 20000m, BudgetRates.Defaults());
            var newPrice = calculated.FinalPrice - 3000m;

            quotes.SetFinalPrice(id, newPrice, [BudgetLineKind.Profit]);

            var detail = RequireQuote(quotes, id);
            Assert.Equal(detail.Budget ?? 0m, newPrice, "precio guardado");
            Assert.Equal(detail.Breakdown!.FinalPrice, newPrice, "desglose tras recortar");
            Assert.Equal(detail.Breakdown.Profit, calculated.Profit - 3000m, "ganancia recortada");
            Assert.Equal(detail.Breakdown.MaterialsCost, calculated.MaterialsCost, "materiales intactos");
            Assert.Equal(detail.Breakdown.Labor, calculated.Labor, "mano de obra intacta");
            Assert.Equal(detail.UnadjustedBreakdown!.Profit, calculated.Profit, "el cálculo original sigue ahí");
            Assert.True(detail.PriceAdjustmentTargets.Contains(BudgetLineKind.Profit), "quedó marcada ganancia");
            Assert.True(detail.BudgetAdjustedManually, "tiene que avisar el recorte a mano");
        });

        run("Correctitud: si lo marcado no cubre la diferencia, no se guarda", () =>
        {
            var productId = inventory.CreateProduct("Guatambú corto", 15m, 0m, "Metro", 900m).Id;
            var id = quotes.CreateQuote("Estante", "Cliente imposible", null).Id;
            quotes.AddInventoryLine(id, productId, 3m);

            var calculated = quotes.SaveCalculation(id, 3000m, 2m, 20000m, BudgetRates.Defaults());

            Assert.Throws(
                () => quotes.SetFinalPrice(id, 1000m, [BudgetLineKind.Profit]),
                "faltan");

            var detail = RequireQuote(quotes, id);
            Assert.Equal(detail.Budget ?? 0m, calculated.FinalPrice, "precio no se movió");
            Assert.Equal(detail.Breakdown!.Profit, calculated.Profit, "ganancia no se movió");
            Assert.Equal(detail.PriceAdjustmentTargets.Count, 0, "no quedó recorte a medias");
        });

        run("Correctitud: recalcular limpia el recorte del desglose", () =>
        {
            var productId = inventory.CreateProduct("Cedro recálculo", 12m, 0m, "Metro", 1100m).Id;
            var id = quotes.CreateQuote("Mesa de cedro", "Cliente indeciso", null).Id;
            quotes.AddInventoryLine(id, productId, 2m);

            var calculated = quotes.SaveCalculation(id, 2500m, 1m, 15000m, BudgetRates.Defaults());
            quotes.SetFinalPrice(id, calculated.FinalPrice - 1000m, [BudgetLineKind.Profit]);

            var after = quotes.SaveCalculation(id, 2500m, 1m, 15000m, BudgetRates.Defaults());
            var detail = RequireQuote(quotes, id);

            Assert.Equal(detail.PriceAdjustmentTargets.Count, 0, "el recálculo tiene que borrar las marcas");
            Assert.Equal(detail.Breakdown!.Profit, after.Profit, "ganancia vuelta al cálculo");
            Assert.Equal(detail.Budget ?? 0m, after.FinalPrice, "precio vuelto al calculado");
        });

        run("Correctitud: no se puede recortar de materiales ni de mano de obra", () =>
        {
            var breakdown = BudgetCalculatorService.Calculate(new BudgetInput
            {
                MaterialsCost = 5000m,
                Days = 1m,
                DailyRate = 10000m,
                Rates = BudgetRates.Defaults()
            });

            Assert.Throws(
                () => BudgetCalculatorService.ApplyPriceAdjustment(
                    breakdown, [BudgetLineKind.Materials], 1000m),
                "Materiales");

            Assert.Throws(
                () => BudgetCalculatorService.ApplyPriceAdjustment(
                    breakdown, [BudgetLineKind.Labor], 1000m),
                "Mano de obra");
        });
    }

    private static void RunAssignmentPayTests(
        Action<string, Action> run,
        ProjectService projects,
        EmployeeService employees)
    {
        run("Correctitud: el jornal arranca pendiente y se puede marcar pagado", () =>
        {
            var employee = employees.Create("Operario a cobrar", null, "Ayudante", 18000m);
            var project = projects.Create("Placard en curso", "Cliente del barrio", null, 80000m, ProjectStatus.InProgress);

            projects.AssignEmployee(project.Id, employee.Id, null);

            var assigned = projects.GetProjectAssignments(project.Id).Single();
            Assert.False(assigned.IsPaid, "al asignar tiene que arrancar pendiente");
            Assert.Equal(assigned.PaymentStatusLabel, "Pendiente", "etiqueta inicial");

            projects.SetAssignmentPaid(assigned.Id, true);
            var paid = projects.GetProjectAssignments(project.Id).Single();
            Assert.True(paid.IsPaid, "tenía que quedar pagado");
            Assert.Equal(paid.PaymentStatusLabel, "Pagado", "etiqueta pagado");

            var staff = employees.GetEmployees(false, "Operario a cobrar").Single();
            Assert.Equal(staff.UnpaidAssignmentCount, 0, "ya no le queda nada a cobrar");

            projects.SetAssignmentPaid(paid.Id, false);
            Assert.Equal(
                employees.GetEmployees(false, "Operario a cobrar").Single().UnpaidAssignmentCount,
                1,
                "volvió a pendiente");
        });
    }

    // --- Ciclo de estados -----------------------------------------------------

    private static void RunStatusPolicyTests(
        Action<string, Action> run,
        InventoryService inventory,
        QuoteService quotes,
        ProjectService projects)
    {
        run("Correctitud: de Presupuesto no se salta a En curso desde Proyectos", () =>
        {
            // Es el atajo que daba por aprobado un presupuesto sin descontar un solo
            // material: el trabajo arrancaba y el inventario decía que estaba todo.
            var id = quotes.CreateQuote("Escritorio salteado", "Cliente impaciente", null).Id;

            Assert.Throws(
                () => projects.Update(id, "Escritorio salteado", "Cliente impaciente", null, 100000m, ProjectStatus.InProgress),
                "Aprobar");
        });

        run("Correctitud: un trabajo en curso no vuelve a Presupuesto a mano", () =>
        {
            var project = projects.Create("Trabajo arrancado", "Cliente dudoso", null, 50000m, ProjectStatus.InProgress);

            Assert.Throws(
                () => projects.Update(project.Id, project.Title, project.ClientName, null, 50000m, ProjectStatus.Quote),
                "Cancelar");
        });

        run("Correctitud: el avance normal del trabajo sigue permitido", () =>
        {
            var project = projects.Create("Trabajo que avanza", "Cliente paciente", null, 50000m, ProjectStatus.InProgress);

            projects.Update(project.Id, project.Title, project.ClientName, null, 50000m, ProjectStatus.Completed);
            projects.Update(project.Id, project.Title, project.ClientName, null, 50000m, ProjectStatus.Delivered);

            var reloaded = projects.GetProjects(false, null, "Trabajo que avanza").Single();
            Assert.Equal(reloaded.Status, ProjectStatus.Delivered, "estado tras el avance normal");

            // Y la marcha atrás de un paso, para corregir un clic equivocado.
            projects.Update(project.Id, project.Title, project.ClientName, null, 50000m, ProjectStatus.Completed);
            Assert.Equal(
                projects.GetProjects(false, null, "Trabajo que avanza").Single().Status,
                ProjectStatus.Completed,
                "estado tras corregir");
        });

        run("Correctitud: cancelar el trabajo devuelve los materiales al inventario", () =>
        {
            var productId = inventory.CreateProduct("Guatambú cancelado", 12m, 0m, "Metro", 1500m).Id;
            var id = quotes.CreateQuote("Puerta cancelada", "Cliente arrepentido", null).Id;
            quotes.AddInventoryLine(id, productId, 5m);
            quotes.SaveCalculation(id, 7500m, 2m, 25000m, BudgetRates.Defaults());

            quotes.ApproveQuote(id);
            Assert.Equal(SingleProduct(inventory, "Guatambú cancelado").CurrentStock, 7m, "stock tras aprobar");

            quotes.CancelApproval(id);

            Assert.Equal(SingleProduct(inventory, "Guatambú cancelado").CurrentStock, 12m, "stock tras cancelar");
            Assert.Equal(projects.GetProjectMaterials(id).Count, 0, "materiales entregados tras cancelar");

            var detail = RequireQuote(quotes, id);
            Assert.Equal(detail.Status, ProjectStatus.Quote, "estado tras cancelar");
            Assert.True(detail.IsEditable, "un trabajo cancelado tendría que volver a ser editable.");

            // Y se puede volver a aprobar descontando todo otra vez, no la mitad.
            quotes.ApproveQuote(id);
            Assert.Equal(SingleProduct(inventory, "Guatambú cancelado").CurrentStock, 7m, "stock tras reaprobar");
        });

        run("Correctitud: solo se cancela un trabajo en curso", () =>
        {
            // Si el trabajo ya está terminado, el material se usó: devolverlo al
            // inventario sería inventar existencias que no están en el galpón.
            var project = projects.Create("Trabajo terminado", "Cliente conforme", null, 80000m, ProjectStatus.InProgress);
            projects.Update(project.Id, project.Title, project.ClientName, null, 80000m, ProjectStatus.Completed);

            Assert.Throws(() => quotes.CancelApproval(project.Id), "en curso");
        });
    }

    // --- Respaldos ------------------------------------------------------------

    private static void RunBackupTests(
        Action<string, Action> run,
        AppPaths paths,
        SettingsService settings,
        DatabaseService database)
    {
        var backups = new BackupService(paths, settings);

        run("Correctitud: la lista de respaldos no mezcla las copias previas a restaurar", () =>
        {
            // Restaurar guarda una copia de lo que había, para poder deshacer. Listada
            // junto a los respaldos buenos, la siguiente restauración ofrecía como opción
            // justo la base que se acababa de descartar.
            var backup = backups.CreateBackup();
            backups.RestoreBackup(backup.FullPath);

            var listed = backups.GetRecentBackups();
            var safety = backups.GetSafetyCopies();

            Assert.True(safety.Count > 0, "tendría que haber quedado una copia previa a la restauración.");
            Assert.True(
                listed.All(b => !b.FileName.Contains("pre_restore", StringComparison.OrdinalIgnoreCase)),
                "las copias pre_restore no van en la lista para elegir.");
            Assert.True(
                listed.Any(b => b.FullPath == backup.FullPath),
                "el respaldo bueno tendría que seguir en la lista.");
        });

        run("Correctitud: no se restaura un archivo que no es una base sana", () =>
        {
            // Un .db truncado por un pendrive que se sacó a mitad de copia. Antes se
            // copiaba igual sobre la base buena y el error recién saltaba al reabrir.
            var broken = Path.Combine(paths.BackupsDirectory, "carpinteria_roto.db");
            File.WriteAllText(broken, "esto no es una base de datos");

            Assert.Throws(() => backups.RestoreBackup(broken), "dañado");
            File.Delete(broken);
        });

        run("Correctitud: no se restaura una base de una versión más nueva", () =>
        {
            var newer = Path.Combine(paths.BackupsDirectory, "carpinteria_futura.db");
            File.Copy(paths.DatabasePath, newer, overwrite: true);
            SetUserVersion(newer, SchemaMigrator.LatestVersion + 1);

            Assert.Throws<SchemaTooNewException>(
                () => backups.RestoreBackup(newer), "versión más nueva");

            File.Delete(newer);

            // La base sigue en la versión que corresponde: el intento no la tocó.
            Assert.Equal(database.GetLowStockCount() >= 0, true, "la base siguió consultable");
        });
    }

    // --- Configuración --------------------------------------------------------

    private static void RunSettingsTests(Action<string, Action> run)
    {
        run("Correctitud: una configuración ilegible se aparta y no se pierde", () =>
        {
            // Adentro pueden estar los porcentajes con los que se venía cotizando. Antes
            // se pisaba con los valores por defecto sin decir nada, y el primer
            // presupuesto del día salía a otro precio sin que nadie supiera por qué.
            var root = Path.Combine(Path.GetTempPath(), $"MetroCarpinteriaSettings_{Guid.NewGuid():N}");

            try
            {
                var paths = new AppPaths(root);
                paths.EnsureDirectories();

                const string original = "{ \"BudgetRates\": { \"ProfitPercent\": 45 ";
                File.WriteAllText(paths.SettingsPath, original);

                var service = new SettingsService(paths);

                Assert.NotNull(service.CorruptFileName, "tendría que avisar que la configuración era ilegible");
                Assert.Equal(service.Current.MaxBackupFiles, 30, "valor por defecto tras el descarte");

                var quarantined = Path.Combine(root, service.CorruptFileName!);
                Assert.True(File.Exists(quarantined), "el archivo original tendría que haberse conservado.");
                Assert.Equal(File.ReadAllText(quarantined), original, "contenido conservado");

                // Y el próximo arranque ya encuentra una configuración válida, así que
                // el aviso no se repite para siempre.
                Assert.True(File.Exists(paths.SettingsPath), "tendría que haber quedado una configuración nueva.");
                Assert.True(new SettingsService(paths).CorruptFileName is null, "el aviso no tendría que repetirse.");
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        });

        run("Correctitud: guardar la configuración no deja archivos a medio escribir", () =>
        {
            var root = Path.Combine(Path.GetTempPath(), $"MetroCarpinteriaSettings_{Guid.NewGuid():N}");

            try
            {
                var paths = new AppPaths(root);
                paths.EnsureDirectories();

                var service = new SettingsService(paths);
                service.Update(s => s.DefaultQuoteValidityDays = 21);
                service.Update(s => s.MaxBackupFiles = 40);

                Assert.False(File.Exists(paths.SettingsPath + ".tmp"), "quedó el temporal de la escritura atómica.");
                Assert.Equal(new SettingsService(paths).Current.DefaultQuoteValidityDays, 21, "valor releído");
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        });
    }

    // --- Vigencia y reloj -----------------------------------------------------

    private static void RunClockTests(Action<string, Action> run)
    {
        run("Correctitud: la vigencia del listado se deriva de la fecha", () =>
        {
            // Guardada al armar la lista quedaba congelada: con la app abierta de un día
            // para el otro, un presupuesto vencido ayer seguía figurando como vigente.
            var expired = new QuoteListItem { ValidUntilLocal = DateTime.Today.AddDays(-1) };
            var dueSoon = new QuoteListItem { ValidUntilLocal = DateTime.Today.AddDays(1) };
            var current = new QuoteListItem { ValidUntilLocal = DateTime.Today.AddDays(30) };
            var noExpiry = new QuoteListItem();

            Assert.Equal(expired.Freshness, QuoteFreshness.Expired, "vencido");
            Assert.Equal(dueSoon.Freshness, QuoteFreshness.DueSoon, "por vencer");
            Assert.Equal(current.Freshness, QuoteFreshness.Current, "vigente");
            Assert.Equal(noExpiry.Freshness, QuoteFreshness.NoExpiry, "sin vencimiento");
        });

        run("Correctitud: el reloj avisa una sola vez por cambio de día", () =>
        {
            var now = new DateTime(2026, 8, 10, 23, 59, 0);
            var clock = new ClockService(() => now);
            var changes = 0;
            clock.DayChanged += (_, _) => changes++;

            Assert.False(clock.CheckForDayChange(), "sin cambio de fecha no tendría que avisar.");

            now = new DateTime(2026, 8, 11, 0, 1, 0);
            Assert.True(clock.CheckForDayChange(), "tendría que detectar el cambio de día.");
            Assert.False(clock.CheckForDayChange(), "el mismo día no se avisa dos veces.");

            Assert.Equal(changes, 1, "avisos de cambio de día");
            Assert.Equal(clock.Today, new DateTime(2026, 8, 11), "día vigente");
        });
    }

    // --- Utilidades -----------------------------------------------------------

    private static QuoteDetail RequireQuote(QuoteService quotes, int id) =>
        quotes.GetDetail(id) ?? throw new InvalidOperationException($"No se encontró el presupuesto {id}.");

    private static ProductListItem SingleProduct(InventoryService inventory, string name) =>
        inventory.GetProducts(true, false, name).SingleOrDefault()
        ?? throw new InvalidOperationException($"No se encontró el producto «{name}».");

    /// <summary>
    /// Sin pool: el test borra el archivo enseguida, y una conexión devuelta al pool lo
    /// deja tomado por el propio proceso.
    /// </summary>
    private static void SetUserVersion(string databasePath, int version)
    {
        using var connection = new SqliteConnection(
            $"Data Source={databasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA user_version = {version};";
        command.ExecuteNonQuery();
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Una carpeta temporal que quedó tomada por SQLite no hace fallar la suite.
        }
    }
}
