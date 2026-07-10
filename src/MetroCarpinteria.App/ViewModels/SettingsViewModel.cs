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
            return local.ToString("dd/MM/yyyy HH:mm", new System.Globalization.CultureInfo("es-AR"));
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
