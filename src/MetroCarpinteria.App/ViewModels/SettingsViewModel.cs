using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using MetroCarpinteria.App.Helpers;
using MetroCarpinteria.App.Models;
using MetroCarpinteria.App.Services;

namespace MetroCarpinteria.App.ViewModels;

public class SettingsViewModel : ObservableObject
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
        RecentBackups = new ObservableCollection<BackupInfo>(AppHost.BackupService.GetRecentBackups());

        SaveSettingsCommand = new RelayCommand(SaveSettings);
        BackupNowCommand = new RelayCommand(BackupNow);
        RestoreBackupCommand = new RelayCommand(_ => RestoreSelectedBackup(), _ => SelectedBackup is not null);
        OpenDataFolderCommand = new RelayCommand(_ => OpenFolder(AppHost.Paths.DataDirectory));
        OpenBackupsFolderCommand = new RelayCommand(_ => OpenFolder(AppHost.Paths.BackupsDirectory));
        RefreshBackupsCommand = new RelayCommand(_ => RefreshBackups());

        CheckUpdatesCommand = new AsyncRelayCommand(CheckUpdatesAsync, () => !IsCheckingUpdates);
        _updateStatus = AppHost.UpdateService.IsSupported
            ? "Presioná «Buscar actualizaciones» para consultar."
            : "Estás usando una copia portable: las actualizaciones automáticas no aplican.";
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

    private void BackupNow()
    {
        try
        {
            var backup = AppHost.BackupService.CreateBackup();
            RefreshBackups();
            OnPropertyChanged(nameof(LastBackupDisplay));
            SetStatus($"Respaldo creado: {backup.FileName}", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus($"Error al crear respaldo: {ex.Message}", isError: true);
        }
    }

    private void RestoreSelectedBackup()
    {
        if (SelectedBackup is null)
        {
            return;
        }

        var result = MessageBox.Show(
            $"¿Restaurar el respaldo «{SelectedBackup.FileName}»?\n\n" +
            "Se reemplazará la base de datos actual. Antes se creará una copia de seguridad automática.\n" +
            "Reiniciá la app después de restaurar para ver los datos recuperados.",
            "Confirmar restauración",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            AppHost.BackupService.RestoreBackup(SelectedBackup.FullPath);
            RefreshBackups();
            SetStatus(
                $"Respaldo restaurado: {SelectedBackup.FileName}. Reiniciá la aplicación para aplicar los cambios.",
                isError: false);
        }
        catch (Exception ex)
        {
            SetStatus($"Error al restaurar: {ex.Message}", isError: true);
        }
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

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        IsStatusError = isError;
    }

    private void ClearStatus()
    {
        StatusMessage = string.Empty;
        IsStatusError = false;
    }
}
