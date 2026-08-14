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

            Assert.Equal(legacy.Count("CashMovements"), 2, "movimientos de caja");
            Assert.Equal(legacy.Count("CashSessions"), 1, "sesiones de caja");
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

            cash.OpenSession(1000m, "Apertura tras migrar");
            cash.RegisterMovement(MetroCarpinteria.App.Data.Entities.CashMovementType.Income, 2500.75m, "Venta");

            var state = cash.GetOpenSessionState()
                ?? throw new InvalidOperationException("No quedó una sesión abierta.");
            Assert.Equal(state.ExpectedBalance, 3500.75m, "saldo esperado tras migrar");

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
