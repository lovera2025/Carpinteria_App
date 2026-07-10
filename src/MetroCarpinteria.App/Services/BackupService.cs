using MetroCarpinteria.App.Models;
using Microsoft.Data.Sqlite;

namespace MetroCarpinteria.App.Services;

public sealed class BackupService
{
    private readonly AppPaths _paths;
    private readonly SettingsService _settingsService;

    public BackupService(AppPaths paths, SettingsService settingsService)
    {
        _paths = paths;
        _settingsService = settingsService;
    }

    public BackupInfo CreateBackup()
    {
        if (!File.Exists(_paths.DatabasePath))
        {
            throw new InvalidOperationException("No existe la base de datos para respaldar.");
        }

        _paths.EnsureDirectories();
        CheckpointWal();

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var backupFileName = $"carpinteria_{timestamp}.db";
        var backupPath = Path.Combine(_paths.BackupsDirectory, backupFileName);

        File.Copy(_paths.DatabasePath, backupPath, overwrite: false);

        var backupInfo = new BackupInfo
        {
            FileName = backupFileName,
            FullPath = backupPath,
            CreatedAtLocal = File.GetCreationTime(backupPath),
            SizeBytes = new FileInfo(backupPath).Length
        };

        _settingsService.Update(settings => settings.LastBackupUtc = DateTime.UtcNow);
        CleanupOldBackups();

        return backupInfo;
    }

    public void RestoreBackup(string backupPath)
    {
        if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath))
        {
            throw new InvalidOperationException("No se encontró el archivo de respaldo.");
        }

        _paths.EnsureDirectories();

        if (File.Exists(_paths.DatabasePath))
        {
            CheckpointWal();
            var safetyName = $"carpinteria_pre_restore_{DateTime.Now:yyyyMMdd_HHmmss}.db";
            var safetyPath = Path.Combine(_paths.BackupsDirectory, safetyName);
            File.Copy(_paths.DatabasePath, safetyPath, overwrite: false);
        }

        DeleteSidecarFiles(_paths.DatabasePath);
        File.Copy(backupPath, _paths.DatabasePath, overwrite: true);
        DeleteSidecarFiles(_paths.DatabasePath);
        SqliteConnection.ClearAllPools();
    }

    public IReadOnlyList<BackupInfo> GetRecentBackups()
    {
        if (!Directory.Exists(_paths.BackupsDirectory))
        {
            return [];
        }

        return Directory.GetFiles(_paths.BackupsDirectory, "carpinteria_*.db")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.CreationTime)
            .Select(file => new BackupInfo
            {
                FileName = file.Name,
                FullPath = file.FullName,
                CreatedAtLocal = file.CreationTime,
                SizeBytes = file.Length
            })
            .ToList();
    }

    private void CheckpointWal()
    {
        using var connection = new SqliteConnection($"Data Source={_paths.DatabasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        command.ExecuteNonQuery();
    }

    private static void DeleteSidecarFiles(string databasePath)
    {
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var sidecar = databasePath + suffix;
            if (File.Exists(sidecar))
            {
                try
                {
                    File.Delete(sidecar);
                }
                catch
                {
                    // Best effort: restore still proceeds with the main db file.
                }
            }
        }
    }

    private void CleanupOldBackups()
    {
        var maxFiles = Math.Max(1, _settingsService.Current.MaxBackupFiles);
        var backups = GetRecentBackups();

        foreach (var backup in backups.Skip(maxFiles))
        {
            try
            {
                File.Delete(backup.FullPath);
            }
            catch
            {
                // Best effort cleanup.
            }
        }
    }
}
