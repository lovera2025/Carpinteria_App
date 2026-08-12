using MetroCarpinteria.App.Data;
using MetroCarpinteria.App.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MetroCarpinteria.App.Services;

public sealed class DatabaseService
{
    private readonly AppPaths _paths;

    public DatabaseService(AppPaths paths)
    {
        _paths = paths;
    }

    /// <summary>Última migración aplicada al arrancar. Vacía si no había nada pendiente.</summary>
    public SchemaMigrationResult? LastMigration { get; private set; }

    /// <param name="beforeMigration">
    /// Deja un respaldo antes de tocar el esquema y devuelve dónde quedó. Se invoca solo
    /// si hay migraciones pendientes sobre una base preexistente: no corre en una
    /// instalación nueva. Devolver la ruta permite volver atrás sola si la migración
    /// termina dejando la base inconsistente.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Si no se pudo respaldar antes de una migración que reescribe datos, o si la base
    /// quedó dañada después de migrar.
    /// </exception>
    public void Initialize(Func<string?>? beforeMigration = null)
    {
        var existedBefore = File.Exists(_paths.DatabasePath);

        // Esquema base. Solo SQL crudo: todavía no se puede consultar por EF, porque el
        // modelo ya conoce columnas que en una base vieja no existen hasta migrar.
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            EnsureStockMovementsTable(context);
            EnsureCashTables(context);
            EnsureProjectTables(context);
        }

        var migrator = new SchemaMigrator(_paths.DatabasePath);
        string? backupPath = null;

        if (existedBefore && beforeMigration is not null && migrator.HasPendingMigrations())
        {
            var reescribeDatos = migrator.HasPendingDataTransform();

            try
            {
                backupPath = beforeMigration();
            }
            catch (Exception ex)
            {
                // Agregar una columna es reversible en la práctica: queda en null y nadie
                // la lee, así que un respaldo fallido no justifica dejar al taller sin app.
                if (!reescribeDatos)
                {
                    LogService.Warning(
                        "DatabaseService",
                        $"No se pudo respaldar antes de migrar, pero el cambio no toca datos: {ex.Message}");
                }
                else
                {
                    // Reescribir filas sin copia previa sí es un camino de ida.
                    throw new InvalidOperationException(
                        "Esta actualización reescribe datos de la base y no se pudo hacer la copia de " +
                        "seguridad previa, así que no se aplicó nada.\n\n" +
                        "Revisá que haya espacio en disco y que la carpeta de datos no esté bloqueada " +
                        "por otro programa (un antivirus o una carpeta sincronizada).",
                        ex);
                }
            }
        }

        LastMigration = migrator.MigrateToLatest();
        VerifyIntegrityAfterMigration(backupPath);

        // Recién ahora el esquema coincide con el modelo y se puede usar EF.
        using (var context = CreateContext())
        {
            MigrateProductUnits(context);
        }

        EnableWalMode();
    }

    /// <summary>
    /// Comprueba que la base haya quedado sana después de migrar y, si no, la deja como
    /// estaba antes.
    /// </summary>
    /// <remarks>
    /// Cada paso es transaccional, así que un error <em>durante</em> la migración se
    /// revierte solo. Esto cubre lo otro: que haya terminado bien y aun así el archivo
    /// quede inconsistente, que es el riesgo real de reconstruir tablas.
    /// </remarks>
    private void VerifyIntegrityAfterMigration(string? backupPath)
    {
        if (LastMigration is not { AnyApplied: true })
        {
            return;
        }

        if (ReadIntegrity() is not { } problem)
        {
            return;
        }

        LogService.Error(
            "DatabaseService",
            $"La base quedó inconsistente tras migrar (integrity_check: {problem})");

        var restored = TryRestore(backupPath);

        throw new InvalidOperationException(
            "La actualización de la base de datos no terminó bien y el archivo quedó dañado.\n\n" +
            (restored
                ? "Se restauró automáticamente la copia previa, así que no se perdió nada. " +
                  "Volvé a abrir la aplicación."
                : "No se pudo restaurar la copia previa automáticamente. " +
                  "Buscá el respaldo más reciente en la carpeta «backups» antes de seguir."));
    }

    /// <summary>Devuelve el problema que reporta SQLite, o <c>null</c> si la base está sana.</summary>
    private string? ReadIntegrity()
    {
        SqliteConnection.ClearAllPools();

        using var connection = new SqliteConnection($"Data Source={_paths.DatabasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        var result = command.ExecuteScalar()?.ToString();

        return string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase) ? null : result ?? "sin respuesta";
    }

    private bool TryRestore(string? backupPath)
    {
        if (backupPath is null || !File.Exists(backupPath))
        {
            return false;
        }

        try
        {
            SqliteConnection.ClearAllPools();
            File.Copy(backupPath, _paths.DatabasePath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            LogService.Error("DatabaseService", "No se pudo restaurar el respaldo previo a la migración", ex);
            return false;
        }
    }

    /// <remarks>
    /// Las cantidades y los importes van <c>TEXT</c>, igual que los declara EF. Con
    /// afinidad <c>REAL</c>, SQLite convierte cada valor a punto flotante al guardarlo y
    /// los centavos se corren; este DDL los declaraba así, y por eso qué afinidad tiene
    /// cada instalación dependía de si la tabla la creó EF o este bloque. La migración v7
    /// endereza las bases que ya existen: esto evita que nazcan torcidas.
    /// </remarks>
    private static void EnsureStockMovementsTable(AppDbContext context)
    {
        context.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS StockMovements (
                Id INTEGER NOT NULL CONSTRAINT PK_StockMovements PRIMARY KEY AUTOINCREMENT,
                ProductId INTEGER NOT NULL,
                Type INTEGER NOT NULL,
                Quantity TEXT NOT NULL,
                Reason TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                CONSTRAINT FK_StockMovements_Products_ProductId FOREIGN KEY (ProductId) REFERENCES Products (Id) ON DELETE RESTRICT
            );
            """);

        context.Database.ExecuteSqlRaw(
            "CREATE INDEX IF NOT EXISTS IX_StockMovements_ProductId ON StockMovements (ProductId);");
        context.Database.ExecuteSqlRaw(
            "CREATE INDEX IF NOT EXISTS IX_StockMovements_CreatedAtUtc ON StockMovements (CreatedAtUtc);");
    }

    private static void EnsureCashTables(AppDbContext context)
    {
        context.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS CashSessions (
                Id INTEGER NOT NULL CONSTRAINT PK_CashSessions PRIMARY KEY AUTOINCREMENT,
                OpeningAmount TEXT NOT NULL,
                ClosingExpectedAmount TEXT NULL,
                ClosingCountedAmount TEXT NULL,
                Difference TEXT NULL,
                OpeningNotes TEXT NULL,
                ClosingNotes TEXT NULL,
                OpenedAtUtc TEXT NOT NULL,
                ClosedAtUtc TEXT NULL
            );
            """);

        context.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS CashMovements (
                Id INTEGER NOT NULL CONSTRAINT PK_CashMovements PRIMARY KEY AUTOINCREMENT,
                CashSessionId INTEGER NOT NULL,
                Type INTEGER NOT NULL,
                Amount TEXT NOT NULL,
                Reason TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                CONSTRAINT FK_CashMovements_CashSessions_CashSessionId FOREIGN KEY (CashSessionId) REFERENCES CashSessions (Id) ON DELETE CASCADE
            );
            """);

        context.Database.ExecuteSqlRaw(
            "CREATE INDEX IF NOT EXISTS IX_CashSessions_ClosedAtUtc ON CashSessions (ClosedAtUtc);");
        context.Database.ExecuteSqlRaw(
            "CREATE INDEX IF NOT EXISTS IX_CashSessions_OpenedAtUtc ON CashSessions (OpenedAtUtc);");
        context.Database.ExecuteSqlRaw(
            "CREATE INDEX IF NOT EXISTS IX_CashMovements_CashSessionId ON CashMovements (CashSessionId);");
        context.Database.ExecuteSqlRaw(
            "CREATE INDEX IF NOT EXISTS IX_CashMovements_CreatedAtUtc ON CashMovements (CreatedAtUtc);");
    }

    private static void EnsureProjectTables(AppDbContext context)
    {
        context.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS Projects (
                Id INTEGER NOT NULL CONSTRAINT PK_Projects PRIMARY KEY AUTOINCREMENT,
                Title TEXT NOT NULL,
                ClientName TEXT NOT NULL,
                Description TEXT NULL,
                Budget TEXT NULL,
                Status INTEGER NOT NULL,
                IsArchived INTEGER NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );
            """);

        context.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS Employees (
                Id INTEGER NOT NULL CONSTRAINT PK_Employees PRIMARY KEY AUTOINCREMENT,
                FullName TEXT NOT NULL,
                Phone TEXT NULL,
                Role TEXT NULL,
                IsArchived INTEGER NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );
            """);

        context.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS ProjectMaterials (
                Id INTEGER NOT NULL CONSTRAINT PK_ProjectMaterials PRIMARY KEY AUTOINCREMENT,
                ProjectId INTEGER NOT NULL,
                ProductId INTEGER NOT NULL,
                Quantity TEXT NOT NULL,
                AssignedAtUtc TEXT NOT NULL,
                CONSTRAINT FK_ProjectMaterials_Projects_ProjectId FOREIGN KEY (ProjectId) REFERENCES Projects (Id) ON DELETE CASCADE,
                CONSTRAINT FK_ProjectMaterials_Products_ProductId FOREIGN KEY (ProductId) REFERENCES Products (Id) ON DELETE RESTRICT
            );
            """);

        context.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS ProjectAssignments (
                Id INTEGER NOT NULL CONSTRAINT PK_ProjectAssignments PRIMARY KEY AUTOINCREMENT,
                ProjectId INTEGER NOT NULL,
                EmployeeId INTEGER NOT NULL,
                Notes TEXT NULL,
                AssignedAtUtc TEXT NOT NULL,
                CONSTRAINT FK_ProjectAssignments_Projects_ProjectId FOREIGN KEY (ProjectId) REFERENCES Projects (Id) ON DELETE CASCADE,
                CONSTRAINT FK_ProjectAssignments_Employees_EmployeeId FOREIGN KEY (EmployeeId) REFERENCES Employees (Id) ON DELETE RESTRICT
            );
            """);

        context.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_Projects_Status ON Projects (Status);");
        context.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_Projects_IsArchived ON Projects (IsArchived);");
        context.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_Employees_FullName ON Employees (FullName);");
        context.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_ProjectMaterials_ProjectId ON ProjectMaterials (ProjectId);");
        context.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_ProjectAssignments_ProjectId ON ProjectAssignments (ProjectId);");
        context.Database.ExecuteSqlRaw("CREATE UNIQUE INDEX IF NOT EXISTS IX_ProjectAssignments_ProjectId_EmployeeId ON ProjectAssignments (ProjectId, EmployeeId);");
    }

    public int GetActiveProjectCount()
    {
        using var context = CreateContext();
        return context.Projects.Count(p => !p.IsArchived && p.Status == Data.Entities.ProjectStatus.InProgress);
    }

    public int GetEmployeeCount()
    {
        using var context = CreateContext();
        return context.Employees.Count(e => !e.IsArchived);
    }

    public AppDbContext CreateContext() => new(_paths.DatabasePath);

    public int GetLowStockCount()
    {
        using var context = CreateContext();

        // La comparación se hace en memoria: los decimales viven como TEXT en SQLite
        // y del lado del SQL se ordenan alfabéticamente ('9.0' > '15.0'). Ver StockRules.
        return context.Products
            .AsNoTracking()
            .Where(p => !p.IsArchived)
            .Select(p => new { p.CurrentStock, p.MinimumStock })
            .AsEnumerable()
            .Count(p => StockRules.IsLowOrOut(p.CurrentStock, p.MinimumStock));
    }

    private void EnableWalMode()
    {
        using var connection = new SqliteConnection($"Data Source={_paths.DatabasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL;";
        command.ExecuteNonQuery();
    }

    private static void MigrateProductUnits(AppDbContext context)
    {
        var products = context.Products.ToList();
        var changed = false;

        foreach (var product in products)
        {
            var normalized = ProductUnits.Normalize(product.Unit);
            if (product.Unit != normalized)
            {
                product.Unit = normalized;
                changed = true;
            }
        }

        if (changed)
        {
            context.SaveChanges();
        }
    }
}
