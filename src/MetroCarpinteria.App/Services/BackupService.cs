using MetroCarpinteria.App.Models;
using Microsoft.Data.Sqlite;

namespace MetroCarpinteria.App.Services;

public sealed class BackupService
{
    /// <summary>
    /// Prefijo de las copias que se guardan justo antes de restaurar. Son una red de
    /// seguridad, no respaldos que el usuario haya pedido: van aparte para que no
    /// aparezcan en la lista para elegir ni le coman lugar a los buenos en la rotación.
    /// </summary>
    private const string SafetyPrefix = "carpinteria_pre_restore_";

    /// <summary>Sirven para deshacer la última restauración, no para armar un historial.</summary>
    private const int MaxSafetyCopies = 5;

    /// <summary>Lo mínimo que tiene que traer un archivo para que valga la pena restaurarlo.</summary>
    private static readonly string[] RequiredTables =
    [
        "Products", "StockMovements", "CashSessions", "CashMovements",
        "Projects", "Employees", "ProjectMaterials", "ProjectAssignments"
    ];

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

        // Validar antes de pisar nada: una vez copiado el archivo sobre la base, el
        // único camino de vuelta es la copia de seguridad, y el usuario ya cerró la app.
        ValidateRestoreCandidate(backupPath);

        _paths.EnsureDirectories();

        if (File.Exists(_paths.DatabasePath))
        {
            CheckpointWal();
            var safetyName = $"{SafetyPrefix}{DateTime.Now:yyyyMMdd_HHmmss}.db";
            var safetyPath = Path.Combine(_paths.BackupsDirectory, safetyName);
            File.Copy(_paths.DatabasePath, safetyPath, overwrite: false);
        }

        DeleteSidecarFiles(_paths.DatabasePath);
        File.Copy(backupPath, _paths.DatabasePath, overwrite: true);
        DeleteSidecarFiles(_paths.DatabasePath);
        SqliteConnection.ClearAllPools();
    }

    /// <summary>
    /// Comprueba que el archivo sea una base sana, completa y que esta versión de la app
    /// entienda. Los tres casos que cubre pasan de verdad: un archivo truncado por un
    /// pendrive que se sacó a mitad de copia, un .db de otro programa, y el respaldo de
    /// una máquina con una versión más nueva.
    /// </summary>
    /// <exception cref="SchemaTooNewException">Si el esquema es posterior al que maneja esta versión.</exception>
    public static void ValidateRestoreCandidate(string backupPath)
    {
        if (!File.Exists(backupPath))
        {
            throw new InvalidOperationException("No se encontró el archivo de respaldo.");
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            // Solo lectura: sobre un archivo dañado, abrirlo para escribir puede
            // empeorarlo, y además le dejaría un -wal al lado al usuario.
            DataSource = backupPath,
            Mode = SqliteOpenMode.ReadOnly,

            // Sin pool: apenas termina esta validación el archivo se copia sobre la base,
            // y una conexión devuelta al pool lo deja tomado por el propio proceso.
            Pooling = false
        }.ToString();

        using var connection = new SqliteConnection(connectionString);

        try
        {
            connection.Open();
            Inspect(connection);
        }
        catch (SqliteException ex)
        {
            // SQLite abre sin mirar el header: un archivo que no es una base recién se
            // delata en la primera consulta, no en Open().
            throw new InvalidOperationException(
                "El archivo no es una base de datos de Metro Carpintería o está dañado.", ex);
        }
    }

    /// <exception cref="SchemaTooNewException">Si el esquema es posterior al que maneja esta versión.</exception>
    private static void Inspect(SqliteConnection connection)
    {
        using (var integrity = connection.CreateCommand())
        {
            integrity.CommandText = "PRAGMA integrity_check;";
            var result = integrity.ExecuteScalar()?.ToString();

            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"El respaldo está dañado y no se puede restaurar (integrity_check: {result}).");
            }
        }

        using (var version = connection.CreateCommand())
        {
            version.CommandText = "PRAGMA user_version;";
            var value = version.ExecuteScalar();
            var fileVersion = value is null or DBNull ? 0 : Convert.ToInt32(value);

            if (fileVersion > SchemaMigrator.LatestVersion)
            {
                throw new SchemaTooNewException(fileVersion, SchemaMigrator.LatestVersion);
            }
        }

        foreach (var table in RequiredTables)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name LIMIT 1;";
            command.Parameters.AddWithValue("$name", table);

            if (command.ExecuteScalar() is null)
            {
                throw new InvalidOperationException(
                    $"El respaldo no parece de Metro Carpintería: le falta la tabla «{table}».");
            }
        }
    }

    /// <summary>
    /// Los respaldos que el usuario puede elegir para restaurar. Deja fuera las copias
    /// <c>pre_restore</c>: mezcladas en la misma lista, restaurar dos veces seguidas
    /// terminaba ofreciendo como "respaldo" la base que se acababa de descartar.
    /// </summary>
    public IReadOnlyList<BackupInfo> GetRecentBackups() => ReadBackups(includeSafetyCopies: false);

    /// <summary>Las copias previas a una restauración, por si hay que deshacerla.</summary>
    public IReadOnlyList<BackupInfo> GetSafetyCopies() =>
        ReadBackups(includeSafetyCopies: true)
            .Where(b => IsSafetyCopy(b.FileName))
            .ToList();

    private IReadOnlyList<BackupInfo> ReadBackups(bool includeSafetyCopies)
    {
        if (!Directory.Exists(_paths.BackupsDirectory))
        {
            return [];
        }

        return Directory.GetFiles(_paths.BackupsDirectory, "carpinteria_*.db")
            .Select(path => new FileInfo(path))
            .Where(file => includeSafetyCopies || !IsSafetyCopy(file.Name))
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

    private static bool IsSafetyCopy(string fileName) =>
        fileName.StartsWith(SafetyPrefix, StringComparison.OrdinalIgnoreCase);

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
        // El límite configurado cuenta solo los respaldos buenos. Con las copias
        // pre_restore mezcladas, restaurar tres veces borraba tres respaldos reales.
        var maxFiles = Math.Max(1, _settingsService.Current.MaxBackupFiles);
        Rotate(GetRecentBackups(), maxFiles);

        // Las copias de seguridad tienen su propio tope, chico: sirven para deshacer la
        // última restauración, no para armar un historial.
        Rotate(GetSafetyCopies(), MaxSafetyCopies);
    }

    private static void Rotate(IReadOnlyList<BackupInfo> backups, int keep)
    {
        foreach (var backup in backups.Skip(keep))
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
