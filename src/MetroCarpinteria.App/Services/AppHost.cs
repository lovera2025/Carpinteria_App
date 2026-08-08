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
    public static QuoteService QuoteService { get; private set; } = null!;
    public static QuoteDocumentService QuoteDocumentService { get; private set; } = null!;
    public static EmployeeService EmployeeService { get; private set; } = null!;
    public static ReportService ReportService { get; private set; } = null!;
    public static UpdateService UpdateService { get; private set; } = null!;

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
        BackupService = new BackupService(Paths, SettingsService);

        // El respaldo va antes de cualquier cambio de esquema sobre una base con datos.
        DatabaseService.Initialize(beforeMigration: () => BackupService.CreateBackup());

        InventoryService = new InventoryService(DatabaseService);
        CashRegisterService = new CashRegisterService(DatabaseService);
        ProjectService = new ProjectService(DatabaseService);
        QuoteService = new QuoteService(DatabaseService, SettingsService);
        QuoteDocumentService = new QuoteDocumentService();
        EmployeeService = new EmployeeService(DatabaseService);
        ReportService = new ReportService(DatabaseService);
        UpdateService = new UpdateService(SettingsService);

        _initialized = true;
    }

    /// <summary>
    /// Deja aplicada la actualización que se haya descargado durante la sesión.
    /// Va al final del cierre: el respaldo tiene que terminar antes, porque el
    /// actualizador solo espera un rato a que el proceso muera.
    /// </summary>
    public static void ApplyPendingUpdateOnExit()
    {
        try
        {
            UpdateService?.ApplyPendingOnExit();
        }
        catch
        {
            // Nunca bloquear el cierre por una actualización.
        }
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
