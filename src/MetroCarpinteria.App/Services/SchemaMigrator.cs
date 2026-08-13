using MetroCarpinteria.App.Models;
using Microsoft.Data.Sqlite;

namespace MetroCarpinteria.App.Services;

public sealed record SchemaMigrationResult(int FromVersion, int ToVersion, IReadOnlyList<string> AppliedSteps)
{
    public bool AnyApplied => AppliedSteps.Count > 0;
}

/// <summary>
/// La base la escribió una versión más nueva de la app.
/// <para>
/// Pasa cuando dos máquinas comparten un respaldo y una está desactualizada. Antes esto
/// se ignoraba en silencio y la app abría igual contra un esquema que no entiende: leía
/// de menos, escribía de menos, y el daño recién se notaba más tarde.
/// </para>
/// </summary>
public sealed class SchemaTooNewException(int fileVersion, int supportedVersion)
    : InvalidOperationException(
        $"Esta base de datos fue creada por una versión más nueva de la aplicación " +
        $"(esquema v{fileVersion}; esta versión maneja hasta v{supportedVersion}).\n\n" +
        "Actualizá Metro Carpintería antes de abrirla. Si abrís esta base con la versión " +
        "vieja podrías perder datos.")
{
    public int FileVersion { get; } = fileVersion;
    public int SupportedVersion { get; } = supportedVersion;
}

/// <summary>
/// Migraciones de esquema versionadas, apoyadas en <c>PRAGMA user_version</c>
/// (un entero que vive en el header del archivo SQLite, sin tabla extra).
/// </summary>
/// <remarks>
/// <para>
/// El esquema base lo sigue garantizando <see cref="DatabaseService.Initialize"/> con
/// <c>EnsureCreated()</c> más los <c>CREATE TABLE IF NOT EXISTS</c>. Eso alcanza para
/// agregar tablas nuevas pero no columnas, que es lo que resuelve esta clase.
/// </para>
/// <para>
/// Cada paso comprueba el estado real de la base antes de tocar nada. Todas las
/// instalaciones existentes arrancan en <c>user_version = 0</c> aunque ya tengan las
/// tablas, y una base recién creada por <c>EnsureCreated()</c> ya viene con las columnas
/// del modelo actual: en los dos casos los pasos se saltean solos.
/// </para>
/// <para>
/// Las columnas <c>decimal</c> nuevas se declaran <c>TEXT</c> a propósito. EF Core
/// serializa los decimales como texto; si la columna tuviera afinidad REAL, SQLite los
/// convertiría a punto flotante y se perdería exactitud.
/// </para>
/// </remarks>
public sealed class SchemaMigrator
{
    public const int LatestVersion = 9;

    /// <param name="TransformsData">
    /// El paso no solo agrega estructura: reescribe filas que ya existen.
    /// <para>
    /// Agregar una columna es reversible en la práctica —queda en null y nadie la lee—,
    /// así que si el respaldo previo falla se puede seguir igual. Reescribir datos no:
    /// si algo sale mal a mitad de camino y no hay copia, no hay vuelta atrás. El arranque
    /// consulta esto para decidir si un respaldo fallido es motivo de abortar.
    /// </para>
    /// </param>
    private sealed record Step(
        int Version,
        string Name,
        Action<SqliteConnection, SqliteTransaction> Apply,
        bool TransformsData = false);

    private static readonly IReadOnlyList<Step> Steps =
    [
        new(1, "Precio de costo en productos", ApplyProductCostPrice),
        new(2, "Datos de presupuesto en proyectos", ApplyProjectQuoteFields),
        new(3, "Tabla de líneas de presupuesto", ApplyBudgetLines),
        new(4, "IVA y descuento comercial", ApplyCommercialTerms),
        new(5, "Ficha de clientes", ApplyClients, TransformsData: true),
        new(6, "Señas y pagos a cuenta", ApplyProjectPayments),
        new(7, "Afinidad de las columnas de dinero", ApplyMoneyColumnAffinity, TransformsData: true),
        new(8, "Fotos de referencia en presupuestos", ApplyQuoteImages),
        new(9, "Mano de obra por operario", ApplyLaborLines)
    ];

    /// <summary>
    /// Columnas que guardan <c>decimal</c> y que en algunas instalaciones nacieron
    /// <c>REAL</c>. Las declara así el DDL crudo de <see cref="DatabaseService"/>; en
    /// cambio, cuando la tabla la crea EF, quedan <c>TEXT</c>. Cuál de los dos ganó depende
    /// de <b>cuándo se instaló la app</b>.
    /// </summary>
    private static readonly (string Table, string[] Columns)[] MoneyColumns =
    [
        ("CashSessions", ["OpeningAmount", "ClosingExpectedAmount", "ClosingCountedAmount", "Difference"]),
        ("CashMovements", ["Amount"]),
        ("StockMovements", ["Quantity"]),
        ("Projects", ["Budget"]),
        ("ProjectMaterials", ["Quantity"])
    ];

    private readonly string _databasePath;

    public SchemaMigrator(string databasePath)
    {
        _databasePath = databasePath;
    }

    public bool HasPendingMigrations()
    {
        using var connection = Open();
        return ReadUserVersion(connection) < LatestVersion;
    }

    /// <summary>
    /// Alguno de los pasos pendientes reescribe datos existentes. El arranque lo consulta
    /// para saber si puede seguir con un respaldo fallido o tiene que cortar.
    /// </summary>
    public bool HasPendingDataTransform()
    {
        using var connection = Open();
        var from = ReadUserVersion(connection);
        return Steps.Any(s => s.Version > from && s.TransformsData);
    }

    /// <exception cref="SchemaTooNewException">
    /// Si la base viene de una versión posterior de la app. Se corta el arranque en vez de
    /// seguir contra un esquema desconocido.
    /// </exception>
    public SchemaMigrationResult MigrateToLatest()
    {
        using var connection = Open();
        var from = ReadUserVersion(connection);

        if (from > LatestVersion)
        {
            throw new SchemaTooNewException(from, LatestVersion);
        }

        if (from == LatestVersion)
        {
            return new SchemaMigrationResult(from, from, []);
        }

        var applied = new List<string>();

        foreach (var step in Steps.Where(s => s.Version > from).OrderBy(s => s.Version))
        {
            using var transaction = connection.BeginTransaction();
            step.Apply(connection, transaction);
            SetUserVersion(connection, transaction, step.Version);
            transaction.Commit();
            applied.Add($"v{step.Version} · {step.Name}");
        }

        return new SchemaMigrationResult(from, LatestVersion, applied);
    }

    // --- Pasos ---------------------------------------------------------------

    private static void ApplyProductCostPrice(SqliteConnection connection, SqliteTransaction transaction)
    {
        AddColumnIfMissing(connection, transaction, "Products", "CostPrice", "TEXT NULL");
    }

    private static void ApplyProjectQuoteFields(SqliteConnection connection, SqliteTransaction transaction)
    {
        // Entradas congeladas del cálculo: si mañana cambia el precio de un material o
        // el margen del taller, un presupuesto ya entregado tiene que seguir dando lo mismo.
        string[] columns =
        [
            "QuotedMaterialsCost",
            "EstimatedDays",
            "DailyRate",
            "WastePercent",
            "ToolWearPercent",
            "OverheadPercent",
            "ProfitPercent",
            "QuotedAtUtc",
            "QuoteValidUntilUtc"
        ];

        foreach (var column in columns)
        {
            AddColumnIfMissing(connection, transaction, "Projects", column, "TEXT NULL");
        }
    }

    private static void ApplyBudgetLines(SqliteConnection connection, SqliteTransaction transaction)
    {
        Execute(connection, transaction, """
            CREATE TABLE IF NOT EXISTS ProjectBudgetLines (
                Id INTEGER NOT NULL CONSTRAINT PK_ProjectBudgetLines PRIMARY KEY AUTOINCREMENT,
                ProjectId INTEGER NOT NULL,
                ProductId INTEGER NULL,
                Description TEXT NOT NULL,
                Unit TEXT NOT NULL,
                Quantity TEXT NOT NULL,
                UnitCost TEXT NOT NULL,
                SortOrder INTEGER NOT NULL,
                AppliedQuantity TEXT NOT NULL DEFAULT '0',
                AppliedToStockAtUtc TEXT NULL,
                CreatedAtUtc TEXT NOT NULL,
                CONSTRAINT FK_ProjectBudgetLines_Projects_ProjectId FOREIGN KEY (ProjectId) REFERENCES Projects (Id) ON DELETE CASCADE,
                CONSTRAINT FK_ProjectBudgetLines_Products_ProductId FOREIGN KEY (ProductId) REFERENCES Products (Id) ON DELETE RESTRICT
            );
            """);

        Execute(connection, transaction,
            "CREATE INDEX IF NOT EXISTS IX_ProjectBudgetLines_ProjectId ON ProjectBudgetLines (ProjectId);");
        Execute(connection, transaction,
            "CREATE INDEX IF NOT EXISTS IX_ProjectBudgetLines_ProductId ON ProjectBudgetLines (ProductId);");
    }

    /// <summary>
    /// IVA y descuento comercial. Se guardan las <b>entradas</b> —la alícuota y el
    /// descuento pactado—, no los importes: igual que el resto del cálculo, el desglose se
    /// reconstruye. Las tres quedan en null en lo que ya existe, así que ningún presupuesto
    /// histórico cambia de total.
    /// </summary>
    private static void ApplyCommercialTerms(SqliteConnection connection, SqliteTransaction transaction)
    {
        AddColumnIfMissing(connection, transaction, "Projects", "VatPercent", "TEXT NULL");
        AddColumnIfMissing(connection, transaction, "Projects", "DiscountMode", "INTEGER NULL");
        AddColumnIfMissing(connection, transaction, "Projects", "DiscountValue", "TEXT NULL");
    }

    /// <summary>
    /// Da de alta la ficha de clientes y le engancha los presupuestos que ya existen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// La fusión automática es <b>solo exacta tras normalizar</b>: «  juan  pérez », «Juan
    /// Perez» y «JUAN PÉREZ» colapsan en uno porque son sin lugar a dudas la misma persona
    /// escrita de tres formas. Nada aproximado se junta solo — «Juan Pérez» y «Juan Pérez
    /// h.» pueden ser padre e hijo, y fusionarlos mezcla dos historiales comerciales sin
    /// forma de deshacerlo.
    /// </para>
    /// <para>
    /// Como nombre visible se toma la variante que más veces se escribió: es la que el
    /// taller reconoce.
    /// </para>
    /// </remarks>
    private static void ApplyClients(SqliteConnection connection, SqliteTransaction transaction)
    {
        Execute(connection, transaction, """
            CREATE TABLE IF NOT EXISTS Clients (
                Id INTEGER NOT NULL CONSTRAINT PK_Clients PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                NormalizedName TEXT NOT NULL,
                Phone TEXT NULL,
                Email TEXT NULL,
                TaxId TEXT NULL,
                Address TEXT NULL,
                Notes TEXT NULL,
                IsArchived INTEGER NOT NULL DEFAULT 0,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );
            """);

        Execute(connection, transaction,
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_Clients_NormalizedName ON Clients (NormalizedName);");
        Execute(connection, transaction,
            "CREATE INDEX IF NOT EXISTS IX_Clients_IsArchived ON Clients (IsArchived);");

        AddColumnIfMissing(connection, transaction, "Projects", "ClientId", "INTEGER NULL");
        Execute(connection, transaction,
            "CREATE INDEX IF NOT EXISTS IX_Projects_ClientId ON Projects (ClientId);");

        SeedClientsFromProjects(connection, transaction);
    }

    /// <summary>
    /// Cobros a cuenta de un trabajo. <c>Amount</c> va TEXT como todo el dinero, y el
    /// vínculo con Caja es opcional porque solo lo tienen los cobros en efectivo.
    /// </summary>
    private static void ApplyProjectPayments(SqliteConnection connection, SqliteTransaction transaction)
    {
        Execute(connection, transaction, """
            CREATE TABLE IF NOT EXISTS ProjectPayments (
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

        Execute(connection, transaction,
            "CREATE INDEX IF NOT EXISTS IX_ProjectPayments_ProjectId ON ProjectPayments (ProjectId);");
        Execute(connection, transaction,
            "CREATE INDEX IF NOT EXISTS IX_ProjectPayments_CreatedAtUtc ON ProjectPayments (CreatedAtUtc);");
    }

    /// <summary>
    /// Fotos de referencia del presupuesto. Solo metadatos: el archivo vive en
    /// <c>data/quote-images</c>. Agregar la tabla no toca filas existentes.
    /// </summary>
    private static void ApplyQuoteImages(SqliteConnection connection, SqliteTransaction transaction)
    {
        Execute(connection, transaction, """
            CREATE TABLE IF NOT EXISTS ProjectQuoteImages (
                Id INTEGER NOT NULL CONSTRAINT PK_ProjectQuoteImages PRIMARY KEY AUTOINCREMENT,
                ProjectId INTEGER NOT NULL,
                FileName TEXT NOT NULL,
                Caption TEXT NULL,
                SortOrder INTEGER NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                CONSTRAINT FK_ProjectQuoteImages_Projects_ProjectId FOREIGN KEY (ProjectId) REFERENCES Projects (Id) ON DELETE CASCADE
            );
            """);

        Execute(connection, transaction,
            "CREATE INDEX IF NOT EXISTS IX_ProjectQuoteImages_ProjectId ON ProjectQuoteImages (ProjectId);");
    }

    /// <summary>
    /// Mano de obra de los operarios, uno por fila, y el jornal en la ficha de Personal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// El jefe no se toca: sigue en <c>Projects.EstimatedDays</c> y <c>Projects.DailyRate</c>,
    /// que ya significaban eso. Por eso este paso no reescribe ni una fila y todo presupuesto
    /// existente se sigue leyendo como «el jefe, sin operarios», dando el mismo precio.
    /// </para>
    /// <para>
    /// <c>Days</c>, <c>DailyRate</c> y el jornal del empleado van <c>TEXT</c> por lo que
    /// explica la nota de la clase: con afinidad REAL, SQLite pasa los importes a punto
    /// flotante y las sumas empiezan a diferir en centavos.
    /// </para>
    /// </remarks>
    private static void ApplyLaborLines(SqliteConnection connection, SqliteTransaction transaction)
    {
        Execute(connection, transaction, """
            CREATE TABLE IF NOT EXISTS ProjectLaborLines (
                Id INTEGER NOT NULL CONSTRAINT PK_ProjectLaborLines PRIMARY KEY AUTOINCREMENT,
                ProjectId INTEGER NOT NULL,
                EmployeeId INTEGER NULL,
                Description TEXT NOT NULL,
                Days TEXT NOT NULL,
                DailyRate TEXT NOT NULL,
                SortOrder INTEGER NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                CONSTRAINT FK_ProjectLaborLines_Projects_ProjectId FOREIGN KEY (ProjectId) REFERENCES Projects (Id) ON DELETE CASCADE,
                CONSTRAINT FK_ProjectLaborLines_Employees_EmployeeId FOREIGN KEY (EmployeeId) REFERENCES Employees (Id) ON DELETE SET NULL
            );
            """);

        Execute(connection, transaction,
            "CREATE INDEX IF NOT EXISTS IX_ProjectLaborLines_ProjectId ON ProjectLaborLines (ProjectId);");
        Execute(connection, transaction,
            "CREATE INDEX IF NOT EXISTS IX_ProjectLaborLines_EmployeeId ON ProjectLaborLines (EmployeeId);");

        AddColumnIfMissing(connection, transaction, "Employees", "DailyRate", "TEXT NULL");
    }

    /// <summary>
    /// Pasa a <c>TEXT</c> las columnas de dinero que hayan quedado <c>REAL</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Con afinidad REAL, SQLite convierte cada importe a punto flotante al guardarlo: un
    /// precio de $ 1.234.567,89 vuelve como 1234567.8899999999 y las sumas empiezan a
    /// diferir en centavos. Es el mismo motivo por el que EF serializa los decimales como
    /// texto. Antes de que la seña escriba en estas tablas hay que dejarlas parejas.
    /// </para>
    /// <para>
    /// Cada tabla se comprueba antes de tocarla, así que en una instalación que ya nació
    /// bien esto no hace nada.
    /// </para>
    /// </remarks>
    private static void ApplyMoneyColumnAffinity(SqliteConnection connection, SqliteTransaction transaction)
    {
        foreach (var (table, columns) in MoneyColumns)
        {
            NormalizeAffinity(connection, transaction, table, columns);
        }

        // Tras reconstruir tablas referenciadas por otras conviene verificar que no haya
        // quedado ninguna fila apuntando a algo que no existe.
        using var check = connection.CreateCommand();
        check.Transaction = transaction;
        check.CommandText = "PRAGMA foreign_key_check;";

        using var reader = check.ExecuteReader();
        if (reader.Read())
        {
            throw new InvalidOperationException(
                $"Tras normalizar las columnas de dinero quedaron referencias rotas en «{reader.GetValue(0)}».");
        }
    }

    /// <summary>
    /// Reconstruye la tabla con el procedimiento estándar de SQLite —tabla nueva, copia
    /// con <c>CAST</c>, borrado y renombre— cambiando solo la afinidad de
    /// <paramref name="targets"/>.
    /// </summary>
    /// <remarks>
    /// El DDL nuevo se arma leyendo la tabla real con <c>PRAGMA</c> y no de una copia
    /// escrita a mano: las columnas de <c>Projects</c> dependen de qué migraciones pasaron
    /// antes, y una lista fija se desactualizaría en la siguiente.
    /// </remarks>
    private static void NormalizeAffinity(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        IReadOnlyList<string> targets)
    {
        var columns = ReadColumns(connection, transaction, table);

        if (columns.Count == 0)
        {
            return;
        }

        var pending = columns
            .Where(c => targets.Contains(c.Name, StringComparer.OrdinalIgnoreCase)
                && c.Type.Equals("REAL", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (pending.Count == 0)
        {
            return;
        }

        var indexes = ReadIndexDefinitions(connection, transaction, table);
        var hasAutoIncrement = ReadTableSql(connection, transaction, table)
            .Contains("AUTOINCREMENT", StringComparison.OrdinalIgnoreCase);

        var temporary = $"{table}__afinidad";
        var definitions = new List<string>();

        foreach (var column in columns)
        {
            var type = pending.Contains(column.Name) ? "TEXT" : column.Type;
            var definition = $"\"{column.Name}\" {type}";

            if (column.IsPrimaryKey && hasAutoIncrement)
            {
                definition += " PRIMARY KEY AUTOINCREMENT";
            }
            else if (column.NotNull)
            {
                definition += " NOT NULL";
            }

            if (column.Default is not null)
            {
                definition += $" DEFAULT {column.Default}";
            }

            definitions.Add(definition);
        }

        if (!hasAutoIncrement && columns.Any(c => c.IsPrimaryKey))
        {
            var keys = columns.Where(c => c.IsPrimaryKey).Select(c => $"\"{c.Name}\"");
            definitions.Add($"PRIMARY KEY ({string.Join(", ", keys)})");
        }

        definitions.AddRange(ReadForeignKeyClauses(connection, transaction, table));

        Execute(connection, transaction,
            $"CREATE TABLE \"{temporary}\" (\n    {string.Join(",\n    ", definitions)}\n);");

        var names = string.Join(", ", columns.Select(c => $"\"{c.Name}\""));
        var values = string.Join(", ", columns.Select(c =>
            pending.Contains(c.Name) ? $"CAST(\"{c.Name}\" AS TEXT)" : $"\"{c.Name}\""));

        Execute(connection, transaction,
            $"INSERT INTO \"{temporary}\" ({names}) SELECT {values} FROM \"{table}\";");

        Execute(connection, transaction, $"DROP TABLE \"{table}\";");
        Execute(connection, transaction, $"ALTER TABLE \"{temporary}\" RENAME TO \"{table}\";");

        // Los índices se van con la tabla vieja: se recrean con su definición original.
        foreach (var index in indexes)
        {
            Execute(connection, transaction, index);
        }
    }

    private sealed record ColumnInfo(string Name, string Type, bool NotNull, string? Default, bool IsPrimaryKey);

    private static List<ColumnInfo> ReadColumns(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table)
    {
        var columns = new List<ColumnInfo>();

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info(\"{table}\");";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            columns.Add(new ColumnInfo(
                Name: reader.GetString(1),
                Type: reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                NotNull: reader.GetInt32(3) != 0,
                Default: reader.IsDBNull(4) ? null : reader.GetString(4),
                IsPrimaryKey: reader.GetInt32(5) != 0));
        }

        return columns;
    }

    /// <summary>
    /// Las claves foráneas salen de <c>PRAGMA foreign_key_list</c> y no de recortar el SQL
    /// original: leer la estructura es exacto, parsear texto SQL a mano no.
    /// </summary>
    private static List<string> ReadForeignKeyClauses(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table)
    {
        var rows = new List<(int Id, string From, string Target, string To, string OnDelete)>();

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = $"PRAGMA foreign_key_list(\"{table}\");";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add((
                    reader.GetInt32(0),
                    reader.GetString(3),
                    reader.GetString(2),
                    reader.IsDBNull(4) ? "Id" : reader.GetString(4),
                    reader.IsDBNull(6) ? "NO ACTION" : reader.GetString(6)));
            }
        }

        return rows
            .GroupBy(r => r.Id)
            .Select(group =>
            {
                var first = group.First();
                var from = string.Join(", ", group.Select(r => $"\"{r.From}\""));
                var to = string.Join(", ", group.Select(r => $"\"{r.To}\""));
                return $"FOREIGN KEY ({from}) REFERENCES \"{first.Target}\" ({to}) ON DELETE {first.OnDelete}";
            })
            .ToList();
    }

    private static List<string> ReadIndexDefinitions(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table)
    {
        var definitions = new List<string>();

        using var command = connection.CreateCommand();
        command.Transaction = transaction;

        // sql viene null en los índices que SQLite crea solo (UNIQUE, PK): ésos vuelven
        // con la tabla y recrearlos a mano fallaría.
        command.CommandText =
            "SELECT sql FROM sqlite_master WHERE type='index' AND tbl_name=$table AND sql IS NOT NULL;";
        command.Parameters.AddWithValue("$table", table);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            definitions.Add(reader.GetString(0) + ";");
        }

        return definitions;
    }

    private static string ReadTableSql(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name=$table;";
        command.Parameters.AddWithValue("$table", table);
        return command.ExecuteScalar()?.ToString() ?? string.Empty;
    }

    private static void SeedClientsFromProjects(SqliteConnection connection, SqliteTransaction transaction)
    {
        var names = new List<(int ProjectId, string ClientName)>();

        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT Id, ClientName FROM Projects WHERE ClientId IS NULL;";

            using var reader = read.ExecuteReader();
            while (reader.Read())
            {
                names.Add((reader.GetInt32(0), reader.IsDBNull(1) ? string.Empty : reader.GetString(1)));
            }
        }

        var groups = names
            .Where(n => !string.IsNullOrWhiteSpace(n.ClientName))
            .GroupBy(n => ClientRules.Normalize(n.ClientName))
            .Where(g => g.Key.Length > 0);

        var now = DateTime.UtcNow.ToString("o");

        foreach (var group in groups)
        {
            var displayName = ClientRules.PickDisplayName(group.Select(n => n.ClientName));
            var clientId = InsertOrGetClient(connection, transaction, displayName, group.Key, now);

            foreach (var (projectId, _) in group)
            {
                using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = "UPDATE Projects SET ClientId = $client WHERE Id = $project;";
                update.Parameters.AddWithValue("$client", clientId);
                update.Parameters.AddWithValue("$project", projectId);
                update.ExecuteNonQuery();
            }
        }
    }

    /// <summary>
    /// Inserta la ficha, o devuelve la que ya estaba. El <c>ON CONFLICT DO NOTHING</c>
    /// cubre volver a correr el paso sobre una base que ya tiene clientes cargados.
    /// </summary>
    private static int InsertOrGetClient(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string displayName,
        string normalizedName,
        string timestamp)
    {
        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO Clients (Name, NormalizedName, IsArchived, CreatedAtUtc, UpdatedAtUtc)
                VALUES ($name, $normalized, 0, $now, $now)
                ON CONFLICT (NormalizedName) DO NOTHING;
                """;
            insert.Parameters.AddWithValue("$name", displayName);
            insert.Parameters.AddWithValue("$normalized", normalizedName);
            insert.Parameters.AddWithValue("$now", timestamp);
            insert.ExecuteNonQuery();
        }

        using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = "SELECT Id FROM Clients WHERE NormalizedName = $normalized;";
        select.Parameters.AddWithValue("$normalized", normalizedName);
        return Convert.ToInt32(select.ExecuteScalar());
    }

    // --- Utilidades ----------------------------------------------------------

    /// <remarks>
    /// Las claves foráneas van apagadas: reconstruir una tabla referenciada por otra pasa
    /// por borrar la original, y con la verificación encendida eso falla o —peor— arrastra
    /// las filas dependientes. Es la única parte de la app que reestructura tablas; el
    /// resto abre por EF, con las claves activas. Al final de cada paso que reconstruye se
    /// corre <c>foreign_key_check</c> para no quedarse sin red.
    /// <para>
    /// El pragma se manda fuera de transacción porque dentro de una no tiene efecto.
    /// </para>
    /// </remarks>
    private SqliteConnection Open()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();

        using var pragma = connection.CreateCommand();

        // legacy_alter_table: sin esto, renombrar la tabla nueva a su nombre definitivo
        // falla porque, en ese instante, las tablas que la referencian apuntan a un nombre
        // que todavía no existe. Es lo que recomienda la documentación de SQLite para el
        // procedimiento de reconstrucción.
        pragma.CommandText = "PRAGMA foreign_keys = OFF; PRAGMA legacy_alter_table = ON;";
        pragma.ExecuteNonQuery();

        return connection;
    }

    private static int ReadUserVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        var value = command.ExecuteScalar();
        return value is null or DBNull ? 0 : Convert.ToInt32(value);
    }

    private static void SetUserVersion(SqliteConnection connection, SqliteTransaction transaction, int version)
    {
        // PRAGMA no acepta parámetros; el valor sale de la lista de pasos, no de entrada del usuario.
        Execute(connection, transaction, $"PRAGMA user_version = {version};");
    }

    private static void AddColumnIfMissing(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string column,
        string definition)
    {
        if (ColumnExists(connection, transaction, table, column))
        {
            return;
        }

        Execute(connection, transaction, $"ALTER TABLE {table} ADD COLUMN {column} {definition};");
    }

    private static bool ColumnExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string column)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info({table});";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
