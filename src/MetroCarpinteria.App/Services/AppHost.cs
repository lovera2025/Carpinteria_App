using MetroCarpinteria.App.Models;

namespace MetroCarpinteria.App.Services;

public static class AppHost
{
    private static bool _initialized;

    public static AppPaths Paths { get; private set; } = null!;
    public static SettingsService SettingsService { get; private set; } = null!;
    public static DatabaseService DatabaseService { get; private set; } = null!;
    public static BackupService BackupService { get; private set; } = null!;
    public static InventoryService InventoryService { get; private set; } = null!;
    public static CashRegisterService CashRegisterService { get; private set; } = null!;
    public static ProjectService ProjectService { get; private set; } = null!;
    public static EmployeeService EmployeeService { get; private set; } = null!;
    public static ReportService ReportService { get; private set; } = null!;

    public static AppSettings Settings => SettingsService.Current;

    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        Paths = new AppPaths();
        Paths.EnsureDirectories();

        SettingsService = new SettingsService(Paths);
        DatabaseService = new DatabaseService(Paths);
        DatabaseService.Initialize();
        BackupService = new BackupService(Paths, SettingsService);
        InventoryService = new InventoryService(DatabaseService);
        CashRegisterService = new CashRegisterService(DatabaseService);
        ProjectService = new ProjectService(DatabaseService);
        EmployeeService = new EmployeeService(DatabaseService);
        ReportService = new ReportService(DatabaseService);

        _initialized = true;
    }

    public static void RunBackupOnExitIfEnabled()
    {
        if (!Settings.BackupOnExit)
        {
            return;
        }

        try
        {
            BackupService.CreateBackup();
        }
        catch
        {
            // Do not block app exit if backup fails.
        }
    }
}
