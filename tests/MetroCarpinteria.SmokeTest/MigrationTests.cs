using System.Globalization;
using System.IO;
using MetroCarpinteria.App.Models;
using MetroCarpinteria.App.Services;
using Microsoft.Data.Sqlite;

namespace MetroCarpinteria.SmokeTest;

/// <summary>
/// Las migraciones v4 a v7 contra una base <b>vieja de verdad</b>, armada acá con el DDL
/// que usaban las instalaciones anteriores.
/// </summary>
/// <remarks>
/// Una base nueva no sirve para probar esto: la crea EF, que ya declara los decimales como
/// TEXT, así que el paso que endereza la afinidad no tendría nada que hacer y el test
/// pasaría sin haber ejecutado una sola línea de lo que importa.
/// </remarks>
internal static class MigrationTests
{
    /// <summary>El importe que destapa el problema: en punto flotante no cierra exacto.</summary>
    private const decimal AwkwardAmount = 1234567.89m;

    public static void Run(Action<string, Action> run)
    {
        RunAffinityTests(run);
        RunCommercialTermsTests(run);
        RunClientTests(run);
        RunPaymentTests(run);
        RunBackupGuardTests(run);
        RunNormalizationTests(run);
        RunQuoteImageMigrationTests(run);
        RunCommitmentAndAttachmentMigrationTests(run);
        RunPriceAdjustmentMigrationTests(run);
        RunWorkshopCycleMigrationTests(run);
        RunManualPriceMigrationTests(run);
        RunCashSafeMigrationTests(run);
        RunFrozenStockFactsMigrationTests(run);
    }

    // --- v15: unidad y costo congelados en el stock -----------------------------

    private static void RunFrozenStockFactsMigrationTests(Action<string, Action> run)
    {
        run("Migración v15: los movimientos viejos se quedan con la unidad que tenían", () =>
        {
            using var legacy = LegacyDatabase.Create();

            legacy.Execute("""
                INSERT INTO Products (Id, Name, CurrentStock, MinimumStock, Unit, IsArchived, CreatedAtUtc, UpdatedAtUtc)
                VALUES (71, 'Melamina cargada mal', 40, 5, 'Metro', 0,
                        '2026-06-01T10:00:00Z', '2026-06-01T10:00:00Z');

                INSERT INTO StockMovements (Id, ProductId, Type, Quantity, Reason, CreatedAtUtc)
                VALUES (81, 71, 0, 40, 'Stock inicial', '2026-06-01T10:00:00Z'),
                       (82, 71, 1, 6, 'Asignado a proyecto: Placard', '2026-06-10T10:00:00Z');
                """);

            var movements = legacy.Count("StockMovements");

            new SchemaMigrator(legacy.Path).MigrateToLatest();

            Assert.Equal(legacy.ReadUserVersion(), SchemaMigrator.LatestVersion, "versión del esquema");
            Assert.Equal(legacy.Count("StockMovements"), movements, "movimientos preservados");
            Assert.Equal(
                legacy.ReadText("SELECT Unit FROM StockMovements WHERE Id = 81;"),
                "Metro",
                "la unidad se rellena con la del producto");

            // Y acá está el punto: corregir la unidad del producto —que es exactamente lo
            // que uno hace al notar que la cargó mal— ya no reescribe el pasado.
            legacy.Execute("UPDATE Products SET Unit = 'Metro cuadrado' WHERE Id = 71;");

            Assert.Equal(
                legacy.ReadText("SELECT Unit FROM StockMovements WHERE Id = 82;"),
                "Metro",
                "el movimiento viejo conserva la unidad que tenía cuando pasó");

            legacy.AssertIntegrity();
        });

        run("Migración v15: el material ya asignado se queda con lo que costaba", () =>
        {
            using var legacy = LegacyDatabase.Create();

            legacy.Execute("""
                INSERT INTO Products (Id, Name, CurrentStock, MinimumStock, Unit, IsArchived, CreatedAtUtc, UpdatedAtUtc)
                VALUES (72, 'Melamina con precio', 30, 5, 'Metro cuadrado', 0,
                        '2026-06-01T10:00:00Z', '2026-06-01T10:00:00Z'),
                       (73, 'Tabla sin precio', 10, 2, 'Unidad', 0,
                        '2026-06-01T10:00:00Z', '2026-06-01T10:00:00Z');

                INSERT INTO Projects (Id, Title, ClientName, Budget, Status, IsArchived, CreatedAtUtc, UpdatedAtUtc)
                VALUES (74, 'Mueble con material', 'Cliente', 200000, 1, 0,
                        '2026-06-05T10:00:00Z', '2026-06-05T10:00:00Z');

                INSERT INTO ProjectMaterials (Id, ProjectId, ProductId, Quantity, AssignedAtUtc)
                VALUES (75, 74, 72, 4, '2026-06-05T11:00:00Z'),
                       (76, 74, 73, 2, '2026-06-05T11:00:00Z');
                """);

            new SchemaMigrator(legacy.Path).MigrateToLatest();

            // El precio de costo lo crea la v1, así que el caso solo se puede sembrar
            // después de migrar. Se vuelve la versión a 14 para reejecutar el paso v15 solo,
            // que es la misma técnica que usa el test de la v13.
            legacy.Execute("""
                UPDATE Products SET CostPrice = 8500 WHERE Id = 72;
                UPDATE ProjectMaterials SET UnitCost = NULL;
                PRAGMA user_version = 14;
                """);

            new SchemaMigrator(legacy.Path).MigrateToLatest();

            Assert.Equal(legacy.ReadUserVersion(), SchemaMigrator.LatestVersion, "versión del esquema");
            Assert.Equal(
                legacy.ReadDecimal("SELECT UnitCost FROM ProjectMaterials WHERE Id = 75;"),
                8500m,
                "el costo se rellena con el del producto");

            // Y después sube la melamina: lo que costó ese trabajo no se mueve.
            legacy.Execute("UPDATE Products SET CostPrice = 12000 WHERE Id = 72;");

            Assert.Equal(
                legacy.ReadDecimal("SELECT UnitCost FROM ProjectMaterials WHERE Id = 75;"),
                8500m,
                "subir el precio del producto no cambia lo que costó el trabajo");

            // Un producto sin precio cargado queda en null: es «no sé cuánto costaba», que
            // no es lo mismo que cero.
            Assert.Equal(
                legacy.ReadInt("SELECT COUNT(*) FROM ProjectMaterials WHERE Id = 76 AND UnitCost IS NULL;"),
                1,
                "sin precio de costo, el material queda sin valuar");

            legacy.AssertIntegrity();
        });
    }

    // --- v7: afinidad de las columnas de dinero -------------------------------

    private static void RunAffinityTests(Action<string, Action> run)
    {
        run("Migración v7: las columnas de dinero pasan de REAL a TEXT sin perder centavos", () =>
        {
            using var legacy = LegacyDatabase.Create();

            // Tal como estaba antes: los importes en punto flotante.
            Assert.Equal(legacy.ReadAffinity("CashMovements", "Amount"), "REAL", "afinidad previa");
            Assert.Equal(legacy.ReadAffinity("Projects", "Budget"), "REAL", "afinidad previa de Budget");

            new SchemaMigrator(legacy.Path).MigrateToLatest();

            foreach (var (table, column) in new[]
            {
                ("CashSessions", "OpeningAmount"),
                ("CashSessions", "ClosingExpectedAmount"),
                ("CashSessions", "ClosingCountedAmount"),
                ("CashSessions", "Difference"),
                ("CashMovements", "Amount"),
                ("StockMovements", "Quantity"),
                ("Projects", "Budget"),
                ("ProjectMaterials", "Quantity")
            })
            {
                Assert.Equal(legacy.ReadAffinity(table, column), "TEXT", $"afinidad de {table}.{column}");
            }

            // Y el importe sobrevivió al centavo.
            Assert.Equal(
                legacy.ReadDecimal("SELECT Amount FROM CashMovements WHERE Id = 1;"),
                AwkwardAmount,
                "importe del movimiento de caja");

            Assert.Equal(
                legacy.ReadDecimal("SELECT Budget FROM Projects WHERE Id = 1;"),
                AwkwardAmount,
                "presupuesto del proyecto");

            Assert.Equal(
                legacy.ReadDecimal("SELECT Quantity FROM StockMovements WHERE Id = 1;"),
                2.125m,
                "cantidad del movimiento de stock");

            legacy.AssertIntegrity();
        });

        run("Migración v7: no se pierden filas, índices ni claves foráneas", () =>
        {
            using var legacy = LegacyDatabase.Create();

            var indexesBefore = legacy.ReadIndexNames("CashMovements");
            Assert.True(indexesBefore.Count > 0, "la prueba necesita índices para tener sentido.");

            new SchemaMigrator(legacy.Path).MigrateToLatest();

            // Los dos movimientos que había, más el que la v14 crea a partir de la apertura
            // de la sesión: sin sesiones, esa plata necesita su propio renglón o el saldo
            // arrancaría corto.
            Assert.Equal(legacy.Count("CashMovements"), 3, "movimientos de caja");
            Assert.Equal(legacy.Count("CashSessions"), 1, "la tabla de sesiones se conserva");
            Assert.Equal(legacy.Count("Projects"), 4, "proyectos");
            Assert.Equal(legacy.Count("ProjectMaterials"), 1, "materiales entregados");

            foreach (var index in indexesBefore)
            {
                Assert.True(
                    legacy.ReadIndexNames("CashMovements").Contains(index),
                    $"se perdió el índice «{index}» al reconstruir la tabla.");
            }

            // El AUTOINCREMENT tiene que seguir vivo: sin él, SQLite reusa ids borrados.
            Assert.True(
                legacy.ReadTableSql("CashMovements").Contains("AUTOINCREMENT", StringComparison.OrdinalIgnoreCase),
                "se perdió el AUTOINCREMENT de la clave primaria.");

            Assert.True(
                legacy.ReadTableSql("CashMovements").Contains("REFERENCES", StringComparison.OrdinalIgnoreCase),
                "se perdió la clave foránea hacia CashSessions.");

            Assert.Equal(legacy.ReadForeignKeyViolations(), 0, "referencias rotas tras migrar");
        });

        run("Migración v7: la app abre y opera contra la base ya reconstruida", () =>
        {
            // Es la prueba que de verdad importa: reconstruir las tablas puede dejarlas
            // sintácticamente válidas pero incompatibles con el modelo de EF, y eso recién
            // se vería al abrir la app en el taller.
            using var legacy = LegacyDatabase.Create();

            var paths = new AppPaths(legacy.Root);
            var database = new DatabaseService(paths);
            database.Initialize();

            Assert.Equal(legacy.ReadUserVersion(), SchemaMigrator.LatestVersion, "versión del esquema");

            var inventory = new InventoryService(database);
            var cash = new CashRegisterService(database);
            var projects = new ProjectService(database);

            // Leer lo que ya estaba, con los importes intactos.
            var product = inventory.GetProducts(false, false, "Tabla de roble").Single();
            Assert.Equal(product.CurrentStock, 40m, "stock leído por la app");

            var quote = projects.GetProjects(false, null, "Mesada").Single();
            Assert.Equal(quote.Budget ?? 0m, AwkwardAmount, "presupuesto leído por la app");

            // Y escribir encima sin romper nada.
            inventory.RegisterMovement(product.Id, MetroCarpinteria.App.Data.Entities.StockMovementType.In, 1.5m, "Compra");
            Assert.Equal(
                inventory.GetProducts(false, false, "Tabla de roble").Single().CurrentStock,
                41.5m,
                "stock tras el movimiento");

            var before = cash.GetBalance().Balance;
            cash.RegisterMovement(MetroCarpinteria.App.Data.Entities.CashMovementType.Income, 2500.75m, "Venta");

            // Los centavos son lo que se está probando: la columna es TEXT justamente para
            // que 2500,75 no se convierta en 2500,749999… al pasar por punto flotante.
            Assert.Equal(cash.GetBalance().Balance, before + 2500.75m, "saldo tras migrar");

            legacy.AssertIntegrity();
        });

        run("Migración v7: correrla dos veces no vuelve a tocar nada", () =>
        {
            using var legacy = LegacyDatabase.Create();

            var migrator = new SchemaMigrator(legacy.Path);
            migrator.MigrateToLatest();

            var sqlAfterFirst = legacy.ReadTableSql("Projects");

            // Un segundo arranque: ya está en la última versión y no hay nada pendiente.
            Assert.False(new SchemaMigrator(legacy.Path).HasPendingMigrations(), "no debía quedar nada pendiente.");
            var second = new SchemaMigrator(legacy.Path).MigrateToLatest();
            Assert.False(second.AnyApplied, "no debía aplicar ningún paso la segunda vez.");

            Assert.Equal(legacy.ReadTableSql("Projects"), sqlAfterFirst, "definición de Projects");
            Assert.Equal(
                legacy.ReadDecimal("SELECT Budget FROM Projects WHERE Id = 1;"),
                AwkwardAmount,
                "presupuesto tras el segundo arranque");
        });
    }

    // --- v4: IVA y descuento --------------------------------------------------

    private static void RunCommercialTermsTests(Action<string, Action> run)
    {
        run("Migración v4: ningún presupuesto histórico cambia de importe", () =>
        {
            // Es la decisión de más riesgo del plan: Budget pasa a significar «total con
            // descuento e IVA». Para que eso no mueva nada de lo que ya está guardado, las
            // tres columnas nuevas tienen que quedar en null.
            using var legacy = LegacyDatabase.Create();

            var before = legacy.ReadAllBudgets();
            new SchemaMigrator(legacy.Path).MigrateToLatest();
            var after = legacy.ReadAllBudgets();

            Assert.Equal(after.Count, before.Count, "cantidad de proyectos");

            for (var i = 0; i < before.Count; i++)
            {
                Assert.Equal(after[i], before[i], $"presupuesto de la fila {i + 1}");
            }

            Assert.Equal(
                legacy.CountWhere("Projects", "VatPercent IS NOT NULL OR DiscountMode IS NOT NULL"),
                0,
                "proyectos con condiciones comerciales cargadas");
        });
    }

    // --- v5: clientes ---------------------------------------------------------

    private static void RunClientTests(Action<string, Action> run)
    {
        run("Migración v5: el mismo cliente escrito de tres formas queda en una sola ficha", () =>
        {
            using var legacy = LegacyDatabase.Create();
            new SchemaMigrator(legacy.Path).MigrateToLatest();

            // La base de prueba tiene «Juan Pérez», «juan perez» y «  JUAN  PÉREZ ».
            Assert.Equal(legacy.CountWhere("Clients", "NormalizedName = 'JUAN PEREZ'"), 1, "fichas de Juan Pérez");

            var clientId = legacy.ReadInt("SELECT Id FROM Clients WHERE NormalizedName = 'JUAN PEREZ';");
            Assert.Equal(
                legacy.CountWhere("Projects", $"ClientId = {clientId}"),
                3,
                "presupuestos enganchados a la ficha");

            // Y el nombre visible es la variante que más veces se escribió.
            Assert.Equal(
                legacy.ReadText("SELECT Name FROM Clients WHERE NormalizedName = 'JUAN PEREZ';"),
                "Juan Pérez",
                "nombre visible de la ficha");
        });

        run("Migración v5: los nombres parecidos NO se fusionan solos", () =>
        {
            // «Juan Pérez» y «Juan Pérez h.» pueden ser padre e hijo. Juntarlos mezcla dos
            // historiales comerciales y no hay forma de deshacerlo: eso se revisa a mano.
            using var legacy = LegacyDatabase.Create();
            new SchemaMigrator(legacy.Path).MigrateToLatest();

            Assert.Equal(legacy.Count("Clients"), 2, "fichas creadas");
            Assert.Equal(legacy.CountWhere("Clients", "NormalizedName = 'JUAN PEREZ H'"), 1, "ficha del hijo");
        });

        run("Migración v5: el nombre escrito en cada presupuesto se conserva", () =>
        {
            // ClientName no se borra: es la instantánea de lo que se entregó.
            using var legacy = LegacyDatabase.Create();
            new SchemaMigrator(legacy.Path).MigrateToLatest();

            Assert.Equal(
                legacy.ReadText("SELECT ClientName FROM Projects WHERE Id = 2;"),
                "juan perez",
                "nombre tal como se tipeó en el presupuesto");
        });
    }

    // --- v6: pagos ------------------------------------------------------------

    private static void RunPaymentTests(Action<string, Action> run)
    {
        run("Migración v6: la tabla de pagos queda lista y con el importe en TEXT", () =>
        {
            using var legacy = LegacyDatabase.Create();
            new SchemaMigrator(legacy.Path).MigrateToLatest();

            Assert.Equal(legacy.ReadAffinity("ProjectPayments", "Amount"), "TEXT", "afinidad del importe");
            Assert.Equal(legacy.Count("ProjectPayments"), 0, "pagos al migrar");

            legacy.Execute("""
                INSERT INTO ProjectPayments (ProjectId, Kind, Amount, Method, CreatedAtUtc)
                VALUES (1, 0, '1234567.89', 0, '2026-08-10T00:00:00Z');
                """);

            Assert.Equal(
                legacy.ReadDecimal("SELECT Amount FROM ProjectPayments WHERE Id = 1;"),
                AwkwardAmount,
                "importe de la seña");
        });
    }

    // --- Respaldo obligatorio -------------------------------------------------

    private static void RunBackupGuardTests(Action<string, Action> run)
    {
        run("Arranque: un respaldo fallido aborta si la migración reescribe datos", () =>
        {
            // Agregar columnas es reversible; reescribir filas sin copia previa no.
            using var legacy = LegacyDatabase.Create();

            var paths = new AppPaths(legacy.Root);
            var database = new DatabaseService(paths);

            var failure = Assert.Throws(
                () => database.Initialize(() => throw new IOException("disco lleno")),
                "copia de seguridad");

            Assert.True(
                failure.Message.Contains("reescribe datos", StringComparison.OrdinalIgnoreCase),
                $"el mensaje tendría que explicar por qué se abortó: «{failure.Message}»");

            // Y no se aplicó nada: la base sigue como estaba.
            Assert.Equal(legacy.ReadUserVersion(), 0, "versión del esquema tras abortar");
        });

        run("Arranque: sin migraciones que reescriban datos, un respaldo fallido no frena la app", () =>
        {
            using var legacy = LegacyDatabase.Create();
            new SchemaMigrator(legacy.Path).MigrateToLatest();

            // Ya está al día: no queda ningún paso pendiente, así que el respaldo ni se pide.
            var paths = new AppPaths(legacy.Root);
            new DatabaseService(paths).Initialize(() => throw new IOException("disco lleno"));

            Assert.Equal(legacy.ReadUserVersion(), SchemaMigrator.LatestVersion, "versión del esquema");
        });
    }

    // --- Normalización de nombres ---------------------------------------------

    private static void RunNormalizationTests(Action<string, Action> run)
    {
        run("Clientes: la clave de comparación ignora acentos, puntos y espacios de más", () =>
        {
            Assert.Equal(ClientRules.Normalize("  juan  pérez "), "JUAN PEREZ", "espacios y acentos");
            Assert.Equal(ClientRules.Normalize("Juan Perez"), "JUAN PEREZ", "sin acentos");
            Assert.Equal(ClientRules.Normalize("JUAN PÉREZ"), "JUAN PEREZ", "mayúsculas");
            Assert.Equal(ClientRules.Normalize("Muebles S.A."), "MUEBLES SA", "puntuación");
            Assert.Equal(ClientRules.Normalize("  "), string.Empty, "solo espacios");
            Assert.Equal(ClientRules.Normalize(null), string.Empty, "sin nombre");

            // Lo que NO tiene que colapsar.
            Assert.True(
                ClientRules.Normalize("Juan Pérez") != ClientRules.Normalize("Juan Pérez h."),
                "padre e hijo no pueden dar la misma clave.");
        });

        run("Clientes: el nombre visible conserva cómo se escribió", () =>
        {
            Assert.Equal(ClientRules.CleanDisplayName("  Juan   Pérez "), "Juan Pérez", "espacios colapsados");
            Assert.Equal(ClientRules.CleanDisplayName("Muebles S.A."), "Muebles S.A.", "puntuación conservada");

            // Gana la variante más repetida.
            Assert.Equal(
                ClientRules.PickDisplayName(["juan perez", "Juan Pérez", "juan perez"]),
                "juan perez",
                "variante más frecuente");

            // Y con empate, la que está capitalizada como un nombre: una ficha que grita
            // en mayúsculas o que parece a medio cargar no ayuda a nadie.
            Assert.Equal(
                ClientRules.PickDisplayName(["JUAN PÉREZ", "Juan Pérez", "juan perez"]),
                "Juan Pérez",
                "desempate por capitalización");
        });
    }

    // --- v8: fotos de referencia ----------------------------------------------

    private static void RunQuoteImageMigrationTests(Action<string, Action> run)
    {
        run("Migración v8: aparece la tabla de fotos y no se tocan los proyectos", () =>
        {
            using var legacy = LegacyDatabase.Create();
            var projects = legacy.Count("Projects");

            new SchemaMigrator(legacy.Path).MigrateToLatest();

            Assert.Equal(legacy.ReadUserVersion(), SchemaMigrator.LatestVersion, "versión del esquema");
            Assert.Equal(
                legacy.ReadInt(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ProjectQuoteImages';"),
                1,
                "tabla ProjectQuoteImages");
            Assert.Equal(legacy.Count("Projects"), projects, "proyectos preservados");
            Assert.Equal(legacy.Count("ProjectQuoteImages"), 0, "fotos al migrar");
            legacy.AssertIntegrity();
        });

        run("Migración v8: inicializar dos veces es idempotente", () =>
        {
            using var legacy = LegacyDatabase.Create();
            var paths = new AppPaths(legacy.Root);
            var database = new DatabaseService(paths);
            database.Initialize();
            database.Initialize();

            Assert.Equal(legacy.ReadUserVersion(), SchemaMigrator.LatestVersion, "user_version");
            Assert.Equal(
                legacy.ReadInt(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ProjectQuoteImages';"),
                1,
                "tabla presente tras el segundo arranque");
        });
    }

    // --- v10: aviso de seña y presupuestos adjuntos ---------------------------

    private static void RunCommitmentAndAttachmentMigrationTests(Action<string, Action> run)
    {
        run("Migración v10: aparecen el aviso de seña y la tabla de adjuntos", () =>
        {
            using var legacy = LegacyDatabase.Create();
            var projects = legacy.Count("Projects");

            new SchemaMigrator(legacy.Path).MigrateToLatest();

            Assert.Equal(legacy.ReadUserVersion(), SchemaMigrator.LatestVersion, "versión del esquema");
            Assert.Equal(legacy.ReadAffinity("Projects", "ShowCommitmentNote"), "INTEGER", "tipo del tilde");
            Assert.Equal(legacy.ReadAffinity("Projects", "CommitmentAmount"), "TEXT", "afinidad del importe");
            Assert.Equal(
                legacy.ReadInt(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ProjectQuoteAttachments';"),
                1,
                "tabla ProjectQuoteAttachments");
            Assert.Equal(legacy.Count("Projects"), projects, "proyectos preservados");
            Assert.Equal(legacy.Count("ProjectQuoteAttachments"), 0, "adjuntos al migrar");
            legacy.AssertIntegrity();
        });
    }

    // --- v11: recorte de desglose y jornal pagado -----------------------------

    private static void RunPriceAdjustmentMigrationTests(Action<string, Action> run)
    {
        run("Migración v11: aparecen el recorte de desglose y el tilde de jornal pagado", () =>
        {
            using var legacy = LegacyDatabase.Create();
            var projects = legacy.Count("Projects");

            new SchemaMigrator(legacy.Path).MigrateToLatest();

            Assert.Equal(legacy.ReadUserVersion(), SchemaMigrator.LatestVersion, "versión del esquema");
            Assert.Equal(legacy.ReadAffinity("Projects", "PriceAdjustmentTargets"), "TEXT", "afinidad de las claves");
            Assert.Equal(legacy.ReadAffinity("ProjectAssignments", "IsPaid"), "INTEGER", "tipo del tilde");
            Assert.Equal(legacy.Count("Projects"), projects, "proyectos preservados");
            Assert.Equal(
                legacy.ReadInt("SELECT COUNT(*) FROM ProjectAssignments WHERE IsPaid != 0;"),
                0,
                "jornales pendientes al migrar");
            legacy.AssertIntegrity();
        });
    }

    // --- v12: ciclo del taller y adjuntos en el total --------------------------

    private static void RunWorkshopCycleMigrationTests(Action<string, Action> run)
    {
        run("Migración v12: el ciclo del taller reescribe los entregados y fecha los activos", () =>
        {
            using var legacy = LegacyDatabase.Create();

            // Un trabajo «Entregado» (4), que es el caso que la migración reescribe. Se
            // siembra acá y no en la base común para no correrle los números al resto de
            // las migraciones, que cuentan proyectos.
            legacy.Execute("""
                INSERT INTO Projects (Id, Title, ClientName, Budget, Status, IsArchived, CreatedAtUtc, UpdatedAtUtc)
                VALUES (90, 'Ropero entregado', 'Cliente de antes', 120000, 4, 0,
                        '2026-07-01T10:00:00Z', '2026-07-20T10:00:00Z');
                """);

            var projects = legacy.Count("Projects");

            new SchemaMigrator(legacy.Path).MigrateToLatest();

            Assert.Equal(legacy.ReadUserVersion(), SchemaMigrator.LatestVersion, "versión del esquema");
            Assert.Equal(
                legacy.ReadAffinity("Projects", "IncludeAttachmentsInTotal"), "INTEGER", "tipo del tilde de adjuntos");
            Assert.Equal(
                legacy.ReadAffinity("Projects", "ApprovedAtUtc"), "TEXT", "afinidad de la fecha de aprobación");
            Assert.Equal(legacy.Count("Projects"), projects, "proyectos preservados");

            // «Entregado» dejó de existir: esa fila tiene que haber quedado en «Listo».
            Assert.Equal(
                legacy.ReadInt("SELECT COUNT(*) FROM Projects WHERE Status = 4;"),
                0,
                "trabajos que quedaron en Entregado");
            Assert.Equal(
                legacy.ReadInt("SELECT Status FROM Projects WHERE Id = 90;"),
                3,
                "estado del que estaba entregado");

            // Los activos arrancan con fecha de aprobación respaldada, para que el aviso de
            // atraso no los ignore para siempre.
            Assert.Equal(
                legacy.ReadInt("SELECT COUNT(*) FROM Projects WHERE Status IN (2, 3) AND ApprovedAtUtc IS NULL;"),
                0,
                "activos sin fecha de aprobación");

            // Y a un presupuesto no se le inventa una: todavía no lo aprobó nadie.
            Assert.Equal(
                legacy.ReadInt("SELECT COUNT(*) FROM Projects WHERE Status = 1 AND ApprovedAtUtc IS NOT NULL;"),
                0,
                "presupuestos con fecha de aprobación inventada");

            Assert.Equal(
                legacy.ReadInt("SELECT COUNT(*) FROM Projects WHERE IncludeAttachmentsInTotal != 0;"),
                0,
                "el tilde de adjuntos arranca apagado");

            legacy.AssertIntegrity();
        });
    }

    // --- v13: precio pactado a mano --------------------------------------------

    private static void RunManualPriceMigrationTests(Action<string, Action> run)
    {
        run("Migración v13: se marcan como pactados los que tienen recorte repartido", () =>
        {
            using var legacy = LegacyDatabase.Create();

            legacy.Execute("""
                INSERT INTO Projects (Id, Title, ClientName, Budget, Status, IsArchived, CreatedAtUtc, UpdatedAtUtc)
                VALUES (91, 'Con recorte repartido', 'Cliente que negoció', 110000, 1, 0,
                        '2026-07-01T10:00:00Z', '2026-07-01T10:00:00Z'),
                       (92, 'Solo redondeado', 'Cliente que redondeó', 110000, 1, 0,
                        '2026-07-02T10:00:00Z', '2026-07-02T10:00:00Z'),
                       (93, 'Sin precio', 'Cliente sin cotizar', NULL, 1, 0,
                        '2026-07-03T10:00:00Z', '2026-07-03T10:00:00Z');
                """);

            var projects = legacy.Count("Projects");

            new SchemaMigrator(legacy.Path).MigrateToLatest();

            // La columna del recorte la crea la v11, así que el caso solo se puede sembrar
            // después de migrar. Para reejecutar el paso v13 sobre él —y solo ese— se
            // vuelve la versión a 12: es la forma de probar un paso aislado sin rehacer
            // toda la cadena a mano.
            legacy.Execute("""
                UPDATE Projects SET PriceAdjustmentTargets = 'Profit' WHERE Id = 91;
                UPDATE Projects SET IsPriceManual = 0;
                PRAGMA user_version = 12;
                """);

            new SchemaMigrator(legacy.Path).MigrateToLatest();

            Assert.Equal(legacy.ReadUserVersion(), SchemaMigrator.LatestVersion, "versión del esquema");
            Assert.Equal(legacy.ReadAffinity("Projects", "IsPriceManual"), "INTEGER", "tipo de la marca");
            Assert.Equal(legacy.Count("Projects"), projects, "proyectos preservados");

            Assert.Equal(
                legacy.ReadInt("SELECT IsPriceManual FROM Projects WHERE Id = 91;"),
                1,
                "el que repartió el recorte tiene precio pactado");
            Assert.Equal(
                legacy.ReadInt("SELECT IsPriceManual FROM Projects WHERE Id = 92;"),
                0,
                "el que solo redondeó no dejó rastro");
            Assert.Equal(
                legacy.ReadInt("SELECT IsPriceManual FROM Projects WHERE Id = 93;"),
                0,
                "sin precio no hay nada que pactar");

            legacy.AssertIntegrity();
        });

        run("Migración v13: la marca arranca apagada y no se inventa en los que no dejaron rastro", () =>
        {
            using var legacy = LegacyDatabase.Create();

            legacy.Execute("""
                INSERT INTO Projects (Id, Title, ClientName, Budget, Status, IsArchived, CreatedAtUtc, UpdatedAtUtc)
                VALUES (94, 'Solo redondeado', 'Cliente que redondeó', 110000, 1, 0,
                        '2026-07-02T10:00:00Z', '2026-07-02T10:00:00Z');
                """);

            new SchemaMigrator(legacy.Path).MigrateToLatest();

            // Un presupuesto redondeado a mano sin repartir la diferencia no dejó ningún
            // rastro en la base, así que no hay forma honesta de reconocerlo. Marcarlo por
            // las dudas le congelaría el precio a presupuestos que sí tienen que seguir a
            // la fórmula: se prefiere el falso negativo, que se corrige solo la próxima
            // vez que le toquen el precio.
            Assert.Equal(
                legacy.ReadInt("SELECT IsPriceManual FROM Projects WHERE Id = 94;"),
                0,
                "un redondeo sin rastro no se puede reconocer");

            Assert.Equal(
                legacy.ReadInt("""
                    SELECT COUNT(*) FROM Projects
                     WHERE IsPriceManual != 0
                       AND (PriceAdjustmentTargets IS NULL OR TRIM(PriceAdjustmentTargets) = '');
                    """),
                0,
                "marcas puestas sin rastro que las justifique");

            legacy.AssertIntegrity();
        });
    }

    // --- v14: la caja fuerte ----------------------------------------------------

    private static void RunCashSafeMigrationTests(Action<string, Action> run)
    {
        run("Migración v14: el saldo queda igual a lo último que el taller contó", () =>
        {
            // Es la prueba que más importa de toda la tanda: la conversión cambia de dónde
            // sale el número que el taller mira todos los días. Si la apertura y la
            // diferencia de arqueo no se convirtieran en movimientos, el saldo arrancaría
            // corto y nadie sabría por qué.
            using var legacy = LegacyDatabase.Create();

            legacy.Execute("""
                INSERT INTO CashSessions
                    (Id, OpeningAmount, ClosingExpectedAmount, ClosingCountedAmount, Difference,
                     OpenedAtUtc, ClosedAtUtc)
                VALUES (50, 1000, 1300, 1310, 10, '2026-07-10T09:00:00Z', '2026-07-10T18:00:00Z');
                """);

            legacy.Execute("""
                INSERT INTO CashMovements (CashSessionId, Type, Amount, Reason, CreatedAtUtc)
                VALUES (50, 1, 500, 'Venta del día', '2026-07-10T12:00:00Z'),
                       (50, 2, 200, 'Compra de tornillos', '2026-07-10T15:00:00Z');
                """);

            new SchemaMigrator(legacy.Path).MigrateToLatest();

            // 1000 de apertura + 500 − 200 + 10 de ajuste = 1310, que es exactamente lo
            // que se contó al cerrar esa caja. Se mira solo esa sesión: la base de prueba
            // trae movimientos propios y el total incluiría también los suyos.
            Assert.Equal(
                legacy.ReadDecimal("""
                    SELECT COALESCE(SUM(CASE WHEN Type = 1 THEN CAST(Amount AS REAL)
                                             ELSE -CAST(Amount AS REAL) END), 0)
                      FROM CashMovements
                     WHERE CashSessionId = 50;
                    """),
                1310m,
                "lo que aporta la sesión disuelta tiene que ser lo que se contó al cerrarla");

            Assert.Equal(
                legacy.CountWhere("CashMovements", "CashSessionId = 50 AND Reason LIKE 'Apertura de caja%'"),
                1,
                "la apertura tiene que tener su renglón");
            Assert.Equal(
                legacy.CountWhere("CashMovements", "CashSessionId = 50 AND Reason LIKE 'Ajuste de arqueo%'"),
                1,
                "la diferencia de arqueo también");
            Assert.True(legacy.Count("CashSessions") > 0, "la tabla de sesiones se conserva.");

            legacy.AssertIntegrity();
        });

        run("Migración v14: un faltante de arqueo entra como egreso, no como importe negativo", () =>
        {
            using var legacy = LegacyDatabase.Create();

            legacy.Execute("""
                INSERT INTO CashSessions
                    (Id, OpeningAmount, ClosingExpectedAmount, ClosingCountedAmount, Difference,
                     OpenedAtUtc, ClosedAtUtc)
                VALUES (51, 0, 500, 480, -20, '2026-07-11T09:00:00Z', '2026-07-11T18:00:00Z');
                """);

            new SchemaMigrator(legacy.Path).MigrateToLatest();

            // El signo va en el tipo del movimiento. Un importe negativo rompería cualquier
            // suma que asuma que los importes son positivos.
            Assert.Equal(
                legacy.CountWhere("CashMovements", "Reason LIKE 'Ajuste de arqueo%' AND Type = 2"),
                1,
                "el faltante tiene que ser un egreso");
            Assert.Equal(
                legacy.ReadDecimal("SELECT CAST(Amount AS REAL) FROM CashMovements WHERE Reason LIKE 'Ajuste de arqueo%';"),
                20m,
                "el importe va en positivo");
            Assert.Equal(
                legacy.CountWhere("CashMovements", "CAST(Amount AS REAL) < 0"),
                0,
                "ningún movimiento con importe negativo");

            legacy.AssertIntegrity();
        });

        run("Migración v14: los cobros que nunca entraron a Caja se asientan, y los que sí no se duplican", () =>
        {
            using var legacy = LegacyDatabase.Create();

            // La tabla de cobros nace en la v6. Se crea acá con esa forma para poder
            // sembrarla antes de migrar y que la conversión corra una sola vez.
            legacy.Execute("""
                CREATE TABLE ProjectPayments (
                    Id INTEGER NOT NULL CONSTRAINT PK_ProjectPayments PRIMARY KEY AUTOINCREMENT,
                    ProjectId INTEGER NOT NULL,
                    Kind INTEGER NOT NULL,
                    Amount TEXT NOT NULL,
                    Method INTEGER NOT NULL,
                    CashMovementId INTEGER NULL,
                    Notes TEXT NULL,
                    CreatedAtUtc TEXT NOT NULL,
                    CONSTRAINT FK_ProjectPayments_Projects_ProjectId FOREIGN KEY (ProjectId) REFERENCES Projects (Id) ON DELETE CASCADE
                );
                """);

            legacy.Execute("""
                INSERT INTO Projects (Id, Title, ClientName, Budget, Status, IsArchived, CreatedAtUtc, UpdatedAtUtc)
                VALUES (95, 'Mostrador', 'María González', 730, 1, 0,
                        '2026-07-01T10:00:00Z', '2026-07-01T10:00:00Z');
                """);

            // Una seña en efectivo que YA tenía su movimiento, y una por transferencia que
            // nunca dejó rastro: el caso que el taller reclamó como plata desaparecida.
            legacy.Execute("""
                INSERT INTO CashSessions (Id, OpeningAmount, OpenedAtUtc) VALUES (52, 0, '2026-07-05T09:00:00Z');
                INSERT INTO CashMovements (Id, CashSessionId, Type, Amount, Reason, CreatedAtUtc)
                VALUES (900, 52, 1, 365, 'Seña: Mostrador', '2026-07-05T10:00:00Z');
                INSERT INTO ProjectPayments (Id, ProjectId, Kind, Amount, Method, CashMovementId, CreatedAtUtc)
                VALUES (800, 95, 0, 365, 0, 900, '2026-07-05T10:00:00Z'),
                       (801, 95, 1, 150, 1, NULL, '2026-07-20T16:00:00Z');
                """);

            new SchemaMigrator(legacy.Path).MigrateToLatest();

            // El de efectivo no se duplica: contarlo de nuevo sería inventar plata.
            Assert.Equal(
                legacy.CountWhere("CashMovements", "ProjectPaymentId = 800"),
                1,
                "la seña en efectivo tenía que quedar con un solo movimiento");

            // El de transferencia ahora existe, con su fecha original y no la de hoy.
            Assert.Equal(
                legacy.CountWhere("CashMovements", "ProjectPaymentId = 801"),
                1,
                "el cobro por transferencia tenía que asentarse");
            Assert.Equal(
                legacy.ReadText("SELECT CreatedAtUtc FROM CashMovements WHERE ProjectPaymentId = 801;"),
                "2026-07-20T16:00:00Z",
                "la fecha del cobro por transferencia");
            Assert.Equal(
                legacy.ReadInt("SELECT Method FROM CashMovements WHERE ProjectPaymentId = 801;"),
                1,
                "el medio del cobro por transferencia");

            // El movimiento viejo aprende de qué trabajo salió, que es lo que faltaba para
            // poder explicar de dónde vino la plata.
            Assert.Equal(
                legacy.ReadInt("SELECT ProjectId FROM CashMovements WHERE Id = 900;"),
                95,
                "el movimiento viejo tiene que quedar atado a su trabajo");

            // Y el cobro por transferencia queda vinculado del otro lado también.
            Assert.Equal(
                legacy.CountWhere("ProjectPayments", "Id = 801 AND CashMovementId IS NOT NULL"),
                1,
                "el cobro tiene que apuntar a su movimiento");

            // El nombre del cliente entra en el texto: es lo que el taller lee de un vistazo.
            Assert.Equal(
                legacy.CountWhere("CashMovements", "ProjectPaymentId = 801 AND Reason LIKE '%María González%'"),
                1,
                "el motivo tendría que nombrar al cliente");

            legacy.AssertIntegrity();
        });

        run("Migración v14: una apertura igual al cierre anterior queda señalada, no borrada", () =>
        {
            // Las cajas viejas no encadenaban saldo: cada apertura se tipeaba de cero. Si
            // alguna vez se tipeó ahí lo que había quedado del día anterior, esa plata
            // queda contada dos veces. No se puede saber con certeza —por eso la migración
            // convierte todas— pero sí señalarla para que el taller decida.
            using var legacy = LegacyDatabase.Create();

            // Fechas viejas a propósito: la base de prueba trae una caja fechada «ahora», y
            // si se colara entre estas dos, la anterior a la 61 no sería la 60 y la prueba
            // estaría midiendo otra cosa.
            legacy.Execute("""
                INSERT INTO CashSessions
                    (Id, OpeningAmount, ClosingExpectedAmount, ClosingCountedAmount, Difference,
                     OpenedAtUtc, ClosedAtUtc)
                VALUES (60, 0, 16000, 16000, 0, '2020-01-01T09:00:00Z', '2020-01-01T18:00:00Z'),
                       (61, 16000, NULL, NULL, NULL, '2020-01-02T09:00:00Z', NULL);
                """);

            legacy.Execute("""
                INSERT INTO CashMovements (CashSessionId, Type, Amount, Reason, CreatedAtUtc)
                VALUES (60, 1, 16000, 'Cobros del día', '2020-01-01T12:00:00Z');
                """);

            var paths = new AppPaths(legacy.Root);
            var database = new DatabaseService(paths);
            database.Initialize();

            var cash = new CashRegisterService(database);

            // Antes de mirar la sospecha: las aperturas tienen que haberse convertido y
            // haber quedado marcadas como tales. Sin esto, un cero acá abajo no diría si
            // el problema es la detección o la conversión.
            Assert.Equal(
                legacy.CountWhere("CashMovements", "Origin = 3 AND CashSessionId = 61"),
                1,
                "la apertura de la caja 61 tenía que convertirse y quedar marcada");
            Assert.Equal(
                legacy.ReadDecimal("SELECT CAST(OpeningAmount AS REAL) FROM CashSessions WHERE Id = 61;"),
                16000m,
                "apertura de la caja 61");
            Assert.Equal(
                legacy.ReadDecimal("SELECT CAST(ClosingCountedAmount AS REAL) FROM CashSessions WHERE Id = 60;"),
                16000m,
                "contado al cerrar la caja 60");
            Assert.Equal(
                legacy.ReadInt("""
                    SELECT COUNT(*) FROM CashSessions
                     WHERE OpenedAtUtc < (SELECT OpenedAtUtc FROM CashSessions WHERE Id = 61);
                    """),
                1,
                "la caja 60 tiene que ser la única anterior a la 61");

            var review = cash.GetConversionReview();

            Assert.Equal(review.Suspicious.Count, 1, "aperturas señaladas");

            var suspicious = review.Suspicious.Single();
            Assert.Equal(suspicious.Amount, 16000m, "importe de la apertura sospechosa");
            Assert.True(
                suspicious.Explanation.Contains("dos veces", StringComparison.Ordinal),
                "la explicación tiene que decir por qué se sospecha.");

            // La migración NO decide por él: la apertura está convertida y sumando, solo
            // señalada. Esconderla sería inventar un saldo distinto sin avisar.
            var before = cash.GetBalance().Balance;
            Assert.True(
                review.Origins.Any(o => o.Label == "Aperturas de cajas viejas"),
                "las aperturas convertidas tienen que figurar en el desglose.");

            // Y al descontarla se compensa, no se borra: los dos renglones quedan.
            var movementsBefore = cash.GetBalance().MovementCount;
            cash.DiscardDuplicatedOpening(suspicious.MovementId);

            Assert.Equal(cash.GetBalance().Balance, before - 16000m, "saldo tras descontar la apertura");
            Assert.Equal(
                cash.GetBalance().MovementCount,
                movementsBefore + 1,
                "descontar tiene que agregar un renglón, no sacar el viejo");
        });

        run("Migración v14: una apertura que no coincide con nada no se señala", () =>
        {
            // El falso positivo tiene su costo: señalar plata legítima haría que el taller
            // descuente algo que sí tenía, y ahí el saldo pasaría a estar mal de verdad.
            using var legacy = LegacyDatabase.Create();

            legacy.Execute("""
                INSERT INTO CashSessions
                    (Id, OpeningAmount, ClosingExpectedAmount, ClosingCountedAmount, Difference,
                     OpenedAtUtc, ClosedAtUtc)
                VALUES (62, 0, 5000, 5000, 0, '2020-01-01T09:00:00Z', '2020-01-01T18:00:00Z'),
                       (63, 2000, NULL, NULL, NULL, '2020-01-02T09:00:00Z', NULL);
                """);

            var paths = new AppPaths(legacy.Root);
            var database = new DatabaseService(paths);
            database.Initialize();

            var review = new CashRegisterService(database).GetConversionReview();
            Assert.Equal(review.Suspicious.Count, 0, "no tendría que señalar una apertura distinta");
        });

        run("Migración v14: el desglose de la revisión suma el saldo", () =>
        {
            // Si las categorías no cerraran contra el total, el panel estaría explicando
            // un número distinto del que muestra: peor que no explicar nada.
            using var legacy = LegacyDatabase.Create();

            var paths = new AppPaths(legacy.Root);
            var database = new DatabaseService(paths);
            database.Initialize();

            var cash = new CashRegisterService(database);
            cash.RegisterMovement(MetroCarpinteria.App.Data.Entities.CashMovementType.Expense, 750.25m, "Gasto suelto");

            var review = cash.GetConversionReview();
            Assert.Equal(
                review.Origins.Sum(o => o.Amount),
                review.Balance,
                "el desglose por origen tiene que sumar el saldo");
            Assert.Equal(review.Balance, cash.GetBalance().Balance, "y coincidir con el saldo de la caja");
        });

        run("Migración v14: CashSessionId deja de ser obligatorio y los índices sobreviven", () =>
        {
            using var legacy = LegacyDatabase.Create();
            var indexesBefore = legacy.ReadIndexNames("CashMovements");
            Assert.True(indexesBefore.Count > 0, "la prueba necesita índices para tener sentido.");

            new SchemaMigrator(legacy.Path).MigrateToLatest();

            // Sin esto, un cobro por transferencia seguiría necesitando una caja abierta
            // donde asentarse — que es la fricción que hacía perder la plata de vista.
            Assert.True(
                legacy.ReadTableSql("CashMovements").Contains("\"CashSessionId\" INTEGER NULL", StringComparison.Ordinal),
                "CashSessionId tendría que aceptar nulos.");

            Assert.Equal(legacy.ReadAffinity("CashMovements", "Amount"), "TEXT", "afinidad del importe");
            Assert.Equal(legacy.ReadAffinity("CashMovements", "Method"), "INTEGER", "tipo del medio");

            foreach (var index in indexesBefore)
            {
                Assert.True(
                    legacy.ReadIndexNames("CashMovements").Contains(index),
                    $"el índice {index} tendría que sobrevivir al rebuild.");
            }

            Assert.Equal(legacy.ReadForeignKeyViolations(), 0, "claves foráneas rotas");
            legacy.AssertIntegrity();
        });
    }

    /// <summary>
    /// Una base con el esquema y la afinidad que tenían las instalaciones anteriores a la
    /// v7, con datos cargados.
    /// </summary>
    private sealed class LegacyDatabase : IDisposable
    {
        public required string Root { get; init; }
        public required string Path { get; init; }

        public static LegacyDatabase Create()
        {
            var root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"MetroCarpinteriaLegacy_{Guid.NewGuid():N}");

            var paths = new AppPaths(root);
            paths.EnsureDirectories();

            var legacy = new LegacyDatabase { Root = root, Path = paths.DatabasePath };
            legacy.BuildSchema();
            legacy.Seed();
            return legacy;
        }

        /// <summary>El DDL viejo, con los decimales en REAL. Es el que dejó las bases torcidas.</summary>
        private void BuildSchema()
        {
            Execute("""
                CREATE TABLE Products (
                    Id INTEGER NOT NULL CONSTRAINT PK_Products PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    CurrentStock TEXT NOT NULL,
                    MinimumStock TEXT NOT NULL,
                    Unit TEXT NOT NULL,
                    IsArchived INTEGER NOT NULL,
                    CreatedAtUtc TEXT NOT NULL,
                    UpdatedAtUtc TEXT NOT NULL
                );
                """);

            Execute("""
                CREATE TABLE Projects (
                    Id INTEGER NOT NULL CONSTRAINT PK_Projects PRIMARY KEY AUTOINCREMENT,
                    Title TEXT NOT NULL,
                    ClientName TEXT NOT NULL,
                    Description TEXT NULL,
                    Budget REAL NULL,
                    Status INTEGER NOT NULL,
                    IsArchived INTEGER NOT NULL,
                    CreatedAtUtc TEXT NOT NULL,
                    UpdatedAtUtc TEXT NOT NULL
                );
                """);

            Execute("""
                CREATE TABLE Employees (
                    Id INTEGER NOT NULL CONSTRAINT PK_Employees PRIMARY KEY AUTOINCREMENT,
                    FullName TEXT NOT NULL,
                    Phone TEXT NULL,
                    Role TEXT NULL,
                    IsArchived INTEGER NOT NULL,
                    CreatedAtUtc TEXT NOT NULL,
                    UpdatedAtUtc TEXT NOT NULL
                );
                """);

            Execute("""
                CREATE TABLE StockMovements (
                    Id INTEGER NOT NULL CONSTRAINT PK_StockMovements PRIMARY KEY AUTOINCREMENT,
                    ProductId INTEGER NOT NULL,
                    Type INTEGER NOT NULL,
                    Quantity REAL NOT NULL,
                    Reason TEXT NOT NULL,
                    CreatedAtUtc TEXT NOT NULL,
                    CONSTRAINT FK_StockMovements_Products_ProductId FOREIGN KEY (ProductId) REFERENCES Products (Id) ON DELETE RESTRICT
                );
                """);

            Execute("""
                CREATE TABLE CashSessions (
                    Id INTEGER NOT NULL CONSTRAINT PK_CashSessions PRIMARY KEY AUTOINCREMENT,
                    OpeningAmount REAL NOT NULL,
                    ClosingExpectedAmount REAL NULL,
                    ClosingCountedAmount REAL NULL,
                    Difference REAL NULL,
                    OpeningNotes TEXT NULL,
                    ClosingNotes TEXT NULL,
                    OpenedAtUtc TEXT NOT NULL,
                    ClosedAtUtc TEXT NULL
                );
                """);

            Execute("""
                CREATE TABLE CashMovements (
                    Id INTEGER NOT NULL CONSTRAINT PK_CashMovements PRIMARY KEY AUTOINCREMENT,
                    CashSessionId INTEGER NOT NULL,
                    Type INTEGER NOT NULL,
                    Amount REAL NOT NULL,
                    Reason TEXT NOT NULL,
                    CreatedAtUtc TEXT NOT NULL,
                    CONSTRAINT FK_CashMovements_CashSessions_CashSessionId FOREIGN KEY (CashSessionId) REFERENCES CashSessions (Id) ON DELETE CASCADE
                );
                """);

            Execute("""
                CREATE TABLE ProjectMaterials (
                    Id INTEGER NOT NULL CONSTRAINT PK_ProjectMaterials PRIMARY KEY AUTOINCREMENT,
                    ProjectId INTEGER NOT NULL,
                    ProductId INTEGER NOT NULL,
                    Quantity REAL NOT NULL,
                    AssignedAtUtc TEXT NOT NULL,
                    CONSTRAINT FK_ProjectMaterials_Projects_ProjectId FOREIGN KEY (ProjectId) REFERENCES Projects (Id) ON DELETE CASCADE,
                    CONSTRAINT FK_ProjectMaterials_Products_ProductId FOREIGN KEY (ProductId) REFERENCES Products (Id) ON DELETE RESTRICT
                );
                """);

            Execute("CREATE INDEX IX_CashMovements_CashSessionId ON CashMovements (CashSessionId);");
            Execute("CREATE INDEX IX_CashMovements_CreatedAtUtc ON CashMovements (CreatedAtUtc);");
            Execute("CREATE INDEX IX_StockMovements_ProductId ON StockMovements (ProductId);");
            Execute("CREATE INDEX IX_Projects_Status ON Projects (Status);");
        }

        private void Seed()
        {
            const string now = "2026-08-01T10:00:00Z";

            Execute($"""
                INSERT INTO Products (Id, Name, CurrentStock, MinimumStock, Unit, IsArchived, CreatedAtUtc, UpdatedAtUtc)
                VALUES (1, 'Tabla de roble', '40', '5', 'Metro', 0, '{now}', '{now}');
                """);

            // El mismo cliente escrito de tres formas, más uno parecido pero distinto.
            Execute($"""
                INSERT INTO Projects (Id, Title, ClientName, Budget, Status, IsArchived, CreatedAtUtc, UpdatedAtUtc)
                VALUES
                    (1, 'Mesada', 'Juan Pérez', 1234567.89, 1, 0, '{now}', '{now}'),
                    (2, 'Placard', 'juan perez', 250000.5, 2, 0, '{now}', '{now}'),
                    (3, 'Biblioteca', '  JUAN  PÉREZ ', NULL, 1, 0, '{now}', '{now}');
                """);

            Execute($"""
                INSERT INTO Projects (Id, Title, ClientName, Budget, Status, IsArchived, CreatedAtUtc, UpdatedAtUtc)
                VALUES (4, 'Puerta', 'Juan Pérez h.', 99999.99, 1, 0, '{now}', '{now}');
                """);

            Execute($"""
                INSERT INTO StockMovements (Id, ProductId, Type, Quantity, Reason, CreatedAtUtc)
                VALUES (1, 1, 0, 2.125, 'Compra', '{now}');
                """);

            // Cerrada: es una sesión del historial, y así la app puede abrir una nueva.
            Execute($"""
                INSERT INTO CashSessions (Id, OpeningAmount, ClosingExpectedAmount, ClosingCountedAmount, Difference, OpenedAtUtc, ClosedAtUtc)
                VALUES (1, 1234567.89, 1234567.89, 1234567.89, 0, '{now}', '{now}');
                """);

            Execute($"""
                INSERT INTO CashMovements (Id, CashSessionId, Type, Amount, Reason, CreatedAtUtc)
                VALUES
                    (1, 1, 0, 1234567.89, 'Venta', '{now}'),
                    (2, 1, 1, 500.25, 'Gasto', '{now}');
                """);

            Execute($"""
                INSERT INTO ProjectMaterials (Id, ProjectId, ProductId, Quantity, AssignedAtUtc)
                VALUES (1, 1, 1, 3.5, '{now}');
                """);
        }

        // --- Lecturas ---------------------------------------------------------

        public string ReadAffinity(string table, string column)
        {
            using var connection = Connect();
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info(\"{table}\");";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                {
                    return reader.GetString(2).ToUpperInvariant();
                }
            }

            throw new InvalidOperationException($"No existe la columna {table}.{column}.");
        }

        public decimal ReadDecimal(string sql)
        {
            var value = ReadScalar(sql)
                ?? throw new InvalidOperationException($"«{sql}» no devolvió nada.");

            // Como TEXT, el valor se lee tal cual se guardó; como REAL vendría con el ruido
            // del punto flotante, que es justo lo que la migración viene a sacar.
            return value is string text
                ? decimal.Parse(text, CultureInfo.InvariantCulture)
                : Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        }

        public string ReadText(string sql) => ReadScalar(sql)?.ToString() ?? string.Empty;

        public int ReadInt(string sql) => Convert.ToInt32(ReadScalar(sql));

        public int Count(string table) => ReadInt($"SELECT COUNT(*) FROM \"{table}\";");

        public int CountWhere(string table, string condition) =>
            ReadInt($"SELECT COUNT(*) FROM \"{table}\" WHERE {condition};");

        public int ReadUserVersion() => ReadInt("PRAGMA user_version;");

        /// <summary>Los presupuestos como texto, para comparar sin que el tipo influya.</summary>
        public List<string> ReadAllBudgets()
        {
            var values = new List<string>();

            using var connection = Connect();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT CAST(Budget AS TEXT) FROM Projects ORDER BY Id;";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                values.Add(reader.IsDBNull(0) ? "<null>" : reader.GetString(0));
            }

            return values;
        }

        public List<string> ReadIndexNames(string table)
        {
            var names = new List<string>();

            using var connection = Connect();
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT name FROM sqlite_master WHERE type='index' AND tbl_name=$table AND sql IS NOT NULL;";
            command.Parameters.AddWithValue("$table", table);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                names.Add(reader.GetString(0));
            }

            return names;
        }

        public string ReadTableSql(string table) =>
            ReadText($"SELECT sql FROM sqlite_master WHERE type='table' AND name='{table}';");

        public int ReadForeignKeyViolations()
        {
            using var connection = Connect();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_key_check;";

            var violations = 0;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                violations++;
            }

            return violations;
        }

        public void AssertIntegrity()
        {
            var result = ReadText("PRAGMA integrity_check;");
            Assert.Equal(result, "ok", "integrity_check");
        }

        public void Execute(string sql)
        {
            using var connection = Connect();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        private object? ReadScalar(string sql)
        {
            using var connection = Connect();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            var value = command.ExecuteScalar();
            return value is DBNull ? null : value;
        }

        /// <summary>Sin pool: la carpeta temporal se borra al terminar cada prueba.</summary>
        private SqliteConnection Connect()
        {
            var connection = new SqliteConnection($"Data Source={Path};Pooling=False");
            connection.Open();
            return connection;
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();

            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch
            {
                // Una carpeta temporal que quedó tomada no hace fallar la suite.
            }
        }
    }
}
