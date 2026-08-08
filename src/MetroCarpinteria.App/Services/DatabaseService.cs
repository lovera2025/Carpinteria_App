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
    /// Se invoca solo si hay migraciones pendientes sobre una base preexistente, para
    /// dejar un respaldo antes de tocar el esquema. No corre en una instalación nueva.
    /// </param>
    public void Initialize(Action? beforeMigration = null)
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

        if (existedBefore && beforeMigration is not null && migrator.HasPendingMigrations())
        {
            try
            {
                beforeMigration();
            }
            catch
            {
                // Un respaldo que falla no puede impedir que la app abra; la migración
                // en sí es transaccional y se revierte sola si algo sale mal.
            }
        }

        LastMigration = migrator.MigrateToLatest();

        // Recién ahora el esquema coincide con el modelo y se puede usar EF.
        using (var context = CreateContext())
        {
            MigrateProductUnits(context);
        }

        EnableWalMode();
    }

    private static void EnsureStockMovementsTable(AppDbContext context)
    {
        context.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS StockMovements (
                Id INTEGER NOT NULL CONSTRAINT PK_StockMovements PRIMARY KEY AUTOINCREMENT,
                ProductId INTEGER NOT NULL,
                Type INTEGER NOT NULL,
                Quantity REAL NOT NULL,
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

        context.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS CashMovements (
                Id INTEGER NOT NULL CONSTRAINT PK_CashMovements PRIMARY KEY AUTOINCREMENT,
                CashSessionId INTEGER NOT NULL,
                Type INTEGER NOT NULL,
                Amount REAL NOT NULL,
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
                Budget REAL NULL,
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
                Quantity REAL NOT NULL,
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
