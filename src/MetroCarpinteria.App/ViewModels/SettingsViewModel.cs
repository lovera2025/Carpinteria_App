using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using MetroCarpinteria.App.Helpers;
using MetroCarpinteria.App.Models;
using MetroCarpinteria.App.Services;

namespace MetroCarpinteria.App.ViewModels;

/// <remarks>
/// Hereda de <see cref="ViewModelBase"/> por el estado de "ocupado": respaldar y restaurar
/// copian la base entera, que es lo único de esta pantalla que tarda lo suficiente como
/// para que valga la pena avisarlo.
/// </remarks>
public class SettingsViewModel : ViewModelBase
{
    private bool _backupOnExit;
    private int _maxBackupFiles;
    private string _statusMessage = string.Empty;
    private bool _isStatusError;
    private BackupInfo? _selectedBackup;

    private bool _checkUpdatesOnStartup;
    private string _updateStatus = string.Empty;
    private bool _isCheckingUpdates;
    private bool _isDownloadingUpdate;
    private int _updateProgress;
    private bool _updateReady;

    public SettingsViewModel()
    {
        LoadFromSettings();
        InitializeAppearanceCommands();
        RecentBackups = new ObservableCollection<BackupInfo>(AppHost.BackupService.GetRecentBackups());

        SaveSettingsCommand = new RelayCommand(SaveSettings);
        BackupNowCommand = new AsyncRelayCommand(BackupNowAsync, () => !IsBusy);
        RestoreBackupCommand = new AsyncRelayCommand(
            RestoreSelectedBackupAsync, () => SelectedBackup is not null && !IsBusy);
        OpenDataFolderCommand = new RelayCommand(_ => OpenFolder(AppHost.Paths.DataDirectory));
        OpenBackupsFolderCommand = new RelayCommand(_ => OpenFolder(AppHost.Paths.BackupsDirectory));
        RefreshBackupsCommand = new RelayCommand(_ => RefreshBackups());

        CheckUpdatesCommand = new AsyncRelayCommand(CheckUpdatesAsync, () => !IsCheckingUpdates);
        _updateStatus = AppHost.UpdateService.IsSupported
            ? "Presioná «Buscar actualizaciones» para consultar."
            : "Estás usando una copia portable: las actualizaciones automáticas no aplican.";
    }

    // --- Apariencia -----------------------------------------------------------

    /// <summary>
    /// Opción de tema con su etiqueta. Se expone como lista para que la vista sea un
    /// grupo de botones y no un desplegable: son tres opciones y conviene verlas todas.
    /// </summary>
    public sealed record ThemeOption(AppTheme Value, string Label, string Description);

    public IReadOnlyList<ThemeOption> ThemeOptions { get; } =
    [
        new(AppTheme.System, "Automático", "Sigue lo que tenga configurado Windows"),
        new(AppTheme.Light, "Claro", "El de siempre, para el taller de día"),
        new(AppTheme.Dark, "Oscuro", "Menos brillo para trabajar de noche")
    ];

    public sealed record ScaleOption(FontScale Value, string Label);

    public IReadOnlyList<ScaleOption> ScaleOptions { get; } =
    [
        new(FontScale.Small, "Chica"),
        new(FontScale.Normal, "Normal"),
        new(FontScale.Large, "Grande")
    ];

    public ThemeOption SelectedTheme => ThemeOptions.First(o => o.Value == AppHost.ThemeService.Theme);

    public ScaleOption SelectedScale => ScaleOptions.First(o => o.Value == AppHost.ThemeService.Scale);

    public ICommand SelectThemeCommand { get; private set; } = null!;
    public ICommand SelectScaleCommand { get; private set; } = null!;

    private void InitializeAppearanceCommands()
    {
        // Se aplica en el momento y no con un botón de guardar: un tema que hay que
        // confirmar sin verlo obliga a adivinar cómo va a quedar antes de elegirlo.
        SelectThemeCommand = new RelayCommand(parameter =>
        {
            if (parameter is not ThemeOption option || option.Value == AppHost.ThemeService.Theme)
            {
                return;
            }

            AppHost.ThemeService.SetTheme(option.Value);
            OnPropertyChanged(nameof(SelectedTheme));
        });

        SelectScaleCommand = new RelayCommand(parameter =>
        {
            if (parameter is not ScaleOption option || option.Value == AppHost.ThemeService.Scale)
            {
                return;
            }

            AppHost.ThemeService.SetScale(option.Value);
            OnPropertyChanged(nameof(SelectedScale));
        });
    }

    // --- Actualizaciones ------------------------------------------------------

    public string CurrentVersionDisplay => AppHost.UpdateService.CurrentVersion;

    public bool UpdatesSupported => AppHost.UpdateService.IsSupported;

    public bool CheckUpdatesOnStartup
    {
        get => _checkUpdatesOnStartup;
        set
        {
            if (SetProperty(ref _checkUpdatesOnStartup, value))
            {
                AppHost.SettingsService.Update(s => s.CheckUpdatesOnStartup = value);
            }
        }
    }

    public string UpdateStatus
    {
        get => _updateStatus;
        private set => SetProperty(ref _updateStatus, value);
    }

    public bool IsCheckingUpdates
    {
        get => _isCheckingUpdates;
        private set => SetProperty(ref _isCheckingUpdates, value);
    }

    public bool IsDownloadingUpdate
    {
        get => _isDownloadingUpdate;
        private set => SetProperty(ref _isDownloadingUpdate, value);
    }

    public int UpdateProgress
    {
        get => _updateProgress;
        private set => SetProperty(ref _updateProgress, value);
    }

    /// <summary>Ya está descargada y se instala al cerrar la app.</summary>
    public bool UpdateReady
    {
        get => _updateReady;
        private set => SetProperty(ref _updateReady, value);
    }

    public string LastUpdateCheckDisplay
    {
        get
        {
            var last = AppHost.Settings.LastUpdateCheckUtc;
            return last is null
                ? "Todavía no se buscaron actualizaciones"
                : AppCulture.DateTimeShort(last.Value.ToLocalTime());
        }
    }

    public ICommand CheckUpdatesCommand { get; }

    /// <summary>La avisa el chequeo automático de arranque.</summary>
    public void NotifyUpdateReady(string version)
    {
        UpdateReady = true;
        UpdateStatus = $"La versión {version} está lista. Se instala sola cuando cierres la app.";
        OnPropertyChanged(nameof(LastUpdateCheckDisplay));
    }

    private async Task CheckUpdatesAsync()
    {
        if (!AppHost.UpdateService.IsSupported)
        {
            UpdateStatus = "Estás usando una copia portable: las actualizaciones automáticas no aplican.";
            return;
        }

        IsCheckingUpdates = true;
        UpdateStatus = "Buscando actualizaciones…";

        try
        {
            var update = await AppHost.UpdateService.CheckAsync();
            OnPropertyChanged(nameof(LastUpdateCheckDisplay));

            if (update is null)
            {
                UpdateStatus = AppHost.UpdateService.HasPendingUpdate
                    ? UpdateStatus
                    : "Ya tenés la última versión.";
                return;
            }

            var version = update.TargetFullRelease.Version.ToString();
            UpdateStatus = $"Descargando la versión {version}…";
            IsDownloadingUpdate = true;
            UpdateProgress = 0;

            var progress = new Progress<int>(p => UpdateProgress = p);
            var ok = await AppHost.UpdateService.DownloadAsync(update, progress);

            if (ok)
            {
                NotifyUpdateReady(version);
            }
            else
            {
                UpdateStatus = "No se pudo descargar la actualización. Probá más tarde.";
            }
        }
        catch
        {
            UpdateStatus = "No se pudo consultar. Revisá la conexión a internet.";
        }
        finally
        {
            IsCheckingUpdates = false;
            IsDownloadingUpdate = false;
        }
    }

    public string DatabasePath => AppHost.Paths.DatabasePath;
    public string BackupsDirectory => AppHost.Paths.BackupsDirectory;
    public string RootDirectory => AppHost.Paths.RootDirectory;

    public string LastBackupDisplay
    {
        get
        {
            var lastBackup = AppHost.Settings.LastBackupUtc;
            if (lastBackup is null)
            {
                return "Sin respaldos registrados";
            }

            var local = lastBackup.Value.ToLocalTime();
            return AppCulture.DateTimeShort(local);
        }
    }

    public bool BackupOnExit
    {
        get => _backupOnExit;
        set => SetProperty(ref _backupOnExit, value);
    }

    public int MaxBackupFiles
    {
        get => _maxBackupFiles;
        set => SetProperty(ref _maxBackupFiles, Math.Clamp(value, 5, 200));
    }

    public BackupInfo? SelectedBackup
    {
        get => _selectedBackup;
        set => SetProperty(ref _selectedBackup, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsStatusError
    {
        get => _isStatusError;
        private set => SetProperty(ref _isStatusError, value);
    }

    public ObservableCollection<BackupInfo> RecentBackups { get; }

    public ICommand SaveSettingsCommand { get; }
    public ICommand BackupNowCommand { get; }
    public ICommand RestoreBackupCommand { get; }
    public ICommand OpenDataFolderCommand { get; }
    public ICommand OpenBackupsFolderCommand { get; }
    public ICommand RefreshBackupsCommand { get; }

    public void Refresh()
    {
        LoadFromSettings();
        RefreshBackups();
        OnPropertyChanged(nameof(LastBackupDisplay));
        ClearStatus();
    }

    private void LoadFromSettings()
    {
        BackupOnExit = AppHost.Settings.BackupOnExit;
        MaxBackupFiles = AppHost.Settings.MaxBackupFiles;

        // Directo al campo: el setter público escribe en settings.json y esto es una lectura.
        _checkUpdatesOnStartup = AppHost.Settings.CheckUpdatesOnStartup;
        OnPropertyChanged(nameof(CheckUpdatesOnStartup));
    }

    private void SaveSettings()
    {
        AppHost.SettingsService.Update(settings =>
        {
            settings.BackupOnExit = BackupOnExit;
            settings.MaxBackupFiles = MaxBackupFiles;
        });

        SetStatus("Configuración guardada correctamente.", isError: false);
        OnPropertyChanged(nameof(LastBackupDisplay));
    }

    /// <summary>
    /// Copiar la base es lo que más tarda de toda la app: con años de movimientos son
    /// varios megas más el checkpoint del WAL. En el hilo de la interfaz eso congela la
    /// ventana y Windows la pinta de gris como si se hubiera colgado.
    /// </summary>
    private async Task BackupNowAsync()
    {
        try
        {
            var backup = await RunBusyAsync(
                () => AppHost.BackupService.CreateBackup(),
                "Copiando la base de datos…");

            RefreshBackups();
            OnPropertyChanged(nameof(LastBackupDisplay));
            SetStatus($"Respaldo creado: {backup.FileName}", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus($"Error al crear respaldo: {ex.Message}", isError: true);
        }
    }

    private async Task RestoreSelectedBackupAsync()
    {
        if (SelectedBackup is null)
        {
            return;
        }

        var fileName = SelectedBackup.FileName;

        var confirmed = await AppHost.DialogService.ConfirmAsync(
            "Restaurar un respaldo",
            $"Los datos actuales se reemplazan por los del {SelectedBackup.CreatedAtDisplay}.\n\n" +
            "Antes de tocar nada se guarda una copia de lo que hay ahora, así que se puede volver atrás.\n\n" +
            "La aplicación se cierra al terminar, para volver a abrir con los datos restaurados.",
            confirmText: "Restaurar",
            isDestructive: true);

        if (!confirmed)
        {
            return;
        }

        var backupPath = SelectedBackup.FullPath;

        try
        {
            await RunBusyAsync(
                () => AppHost.BackupService.RestoreBackup(backupPath),
                "Restaurando el respaldo…");

            RefreshBackups();

            await AppHost.DialogService.AlertAsync(
                "Respaldo restaurado",
                $"Se restauró «{fileName}».\n\n" +
                "La aplicación se va a cerrar. Volvela a abrir para ver los datos recuperados.",
                DialogKind.Info);

            // Reinicio en vez de recargar en caliente: hay ocho pantallas con datos ya
            // leídos en memoria, y dejarlas mostrando información de la base anterior es
            // peor que pedir que se vuelva a abrir.
            RestartApplication();
        }
        catch (Exception ex)
        {
            AppHost.NotificationService.Error($"No se pudo restaurar el respaldo: {ex.Message}", ex);
        }
    }

    private static void RestartApplication()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(path))
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            LogService.Warning("SettingsViewModel", $"No se pudo reabrir la app: {ex.Message}");
        }

        Application.Current?.Shutdown();
    }

    private void RefreshBackups()
    {
        var selectedPath = SelectedBackup?.FullPath;
        RecentBackups.Clear();
        foreach (var backup in AppHost.BackupService.GetRecentBackups())
        {
            RecentBackups.Add(backup);
        }

        SelectedBackup = selectedPath is null
            ? RecentBackups.FirstOrDefault()
            : RecentBackups.FirstOrDefault(b => b.FullPath == selectedPath) ?? RecentBackups.FirstOrDefault();
    }

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    /// <summary>
    /// Publica el resultado de una acción.
    /// <para>
    /// Antes esto llenaba una barra fija arriba de la pantalla que <b>nunca se borraba</b>:
    /// un "Producto creado." quedaba ahí para siempre, y al rato no se sabía si
    /// correspondía a lo de recién o a algo de veinte minutos antes. Ahora va al aviso
    /// flotante, que se descarta solo.
    /// </para>
    /// <para>
    /// StatusMessage se sigue actualizando porque los tests lo leen para verificar qué
    /// pasó tras una acción.
    /// </para>
    /// </summary>
    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        IsStatusError = isError;

        if (string.IsNullOrWhiteSpace(message) || !AppHost.IsReady)
        {
            return;
        }

        if (isError)
        {
            AppHost.NotificationService.Warning(message);
        }
        else
        {
            AppHost.NotificationService.Success(message);
        }
    }

    private void ClearStatus()
    {
        StatusMessage = string.Empty;
        IsStatusError = false;
    }
}
