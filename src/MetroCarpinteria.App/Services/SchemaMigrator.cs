using Microsoft.Data.Sqlite;

namespace MetroCarpinteria.App.Services;

public sealed record SchemaMigrationResult(int FromVersion, int ToVersion, IReadOnlyList<string> AppliedSteps)
{
    public bool AnyApplied => AppliedSteps.Count > 0;
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
    public const int LatestVersion = 3;

    private sealed record Step(int Version, string Name, Action<SqliteConnection, SqliteTransaction> Apply);

    private static readonly IReadOnlyList<Step> Steps =
    [
        new(1, "Precio de costo en productos", ApplyProductCostPrice),
        new(2, "Datos de presupuesto en proyectos", ApplyProjectQuoteFields),
        new(3, "Tabla de líneas de presupuesto", ApplyBudgetLines)
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

    public SchemaMigrationResult MigrateToLatest()
    {
        using var connection = Open();
        var from = ReadUserVersion(connection);

        if (from >= LatestVersion)
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

    // --- Utilidades ----------------------------------------------------------

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
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
