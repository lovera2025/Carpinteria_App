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
    public static QuoteImageService QuoteImageService { get; private set; } = null!;
    public static QuoteService QuoteService { get; private set; } = null!;
    public static PaymentService PaymentService { get; private set; } = null!;
    public static ClientService ClientService { get; private set; } = null!;
    public static QuoteDocumentService QuoteDocumentService { get; private set; } = null!;
    public static PdfExportService PdfExportService { get; private set; } = null!;
    public static EmployeeService EmployeeService { get; private set; } = null!;
    public static ReportService ReportService { get; private set; } = null!;
    public static UpdateService UpdateService { get; private set; } = null!;
    public static ThemeService ThemeService { get; private set; } = null!;
    public static NotificationService NotificationService { get; private set; } = null!;
    public static DialogService DialogService { get; private set; } = null!;
    public static ClockService ClockService { get; private set; } = null!;

    public static AppSettings Settings => SettingsService.Current;

    /// <summary>
    /// Si el arranque terminó bien. Mientras sea <c>false</c> las propiedades de arriba
    /// pueden estar en null, así que nadie debería tocarlas: el manejador global de
    /// excepciones lo consulta para decidir si vale la pena seguir vivo o morir limpio.
    /// </summary>
    public static bool IsReady { get; private set; }

    /// <param name="paths">
    /// Permite apuntar a una carpeta de datos distinta de <c>Documentos/MetroCarpinteria</c>.
    /// Los tests lo usan para no escribir en la base real del taller.
    /// </param>
    public static void Initialize(AppPaths? paths = null)
    {
        if (_initialized)
        {
            return;
        }

        Paths = paths ?? new AppPaths();
        Paths.EnsureDirectories();

        SettingsService = new SettingsService(Paths);
        DatabaseService = new DatabaseService(Paths);
        BackupService = new BackupService(Paths, SettingsService);

        LogService.Info("AppHost", $"Datos en {Paths.RootDirectory}");

        // El respaldo va antes de cualquier cambio de esquema sobre una base con datos.
        // Devuelve dónde quedó para que la migración pueda volver atrás sola si el
        // archivo termina dañado.
        DatabaseService.Initialize(beforeMigration: () => BackupService.CreateBackup().FullPath);

        if (DatabaseService.LastMigration is { AnyApplied: true } migration)
        {
            LogService.Info(
                "AppHost",
                $"Esquema migrado v{migration.FromVersion} → v{migration.ToVersion}: " +
                string.Join(", ", migration.AppliedSteps));
        }

        InventoryService = new InventoryService(DatabaseService);
        CashRegisterService = new CashRegisterService(DatabaseService);
        QuoteImageService = new QuoteImageService(DatabaseService, Paths);
        ProjectService = new ProjectService(DatabaseService, QuoteImageService);
        QuoteService = new QuoteService(DatabaseService, SettingsService, QuoteImageService);
        PaymentService = new PaymentService(DatabaseService);
        ClientService = new ClientService(DatabaseService);
        QuoteDocumentService = new QuoteDocumentService();
        PdfExportService = new PdfExportService();
        EmployeeService = new EmployeeService(DatabaseService);
        ReportService = new ReportService(DatabaseService);
        UpdateService = new UpdateService(SettingsService);
        ThemeService = new ThemeService(SettingsService);
        NotificationService = new NotificationService();
        DialogService = new DialogService();
        ClockService = new ClockService();

        // Deja aplicado el tema y el tamaño de letra guardados antes de abrir la ventana,
        // para que no se vea un destello con el tema anterior.
        ThemeService.ApplySaved();

        // Las dos banderas se prenden juntas y al final: si algo de arriba tiró, la app
        // queda marcada como no lista en vez de aparentar estar entera con servicios en null.
        _initialized = true;
        IsReady = true;
    }

    /// <summary>
    /// Vuelve el host al estado previo al arranque. Solo para tests, que necesitan
    /// inicializar contra varias carpetas temporales dentro del mismo proceso.
    /// </summary>
    internal static void ResetForTests()
    {
        _initialized = false;
        IsReady = false;
        Paths = null!;
        SettingsService = null!;
        DatabaseService = null!;
        BackupService = null!;
        InventoryService = null!;
        CashRegisterService = null!;
        QuoteImageService = null!;
        ProjectService = null!;
        QuoteService = null!;
        PaymentService = null!;
        ClientService = null!;
        QuoteDocumentService = null!;
        PdfExportService = null!;
        EmployeeService = null!;
        ReportService = null!;
        UpdateService = null!;
        ThemeService = null!;
        NotificationService = null!;
        DialogService = null!;
        ClockService?.Stop();
        ClockService = null!;
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
        // Si el arranque falló, los servicios son null: no hay nada que respaldar.
        if (!IsReady || !Settings.BackupOnExit)
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
