namespace MetroCarpinteria.App.Models;

public sealed class BackupInfo
{
    public required string FileName { get; init; }
    public required string FullPath { get; init; }
    public required DateTime CreatedAtLocal { get; init; }
    public required long SizeBytes { get; init; }

    public string SizeDisplay => SizeBytes < 1024 * 1024
        ? $"{SizeBytes / 1024.0:F1} KB"
        : $"{SizeBytes / (1024.0 * 1024.0):F1} MB";

    /// <summary>
    /// Cuándo se hizo, escrito para leer de corrido: "hoy a las 14:32", "ayer a las 09:10".
    /// Elegir qué respaldo restaurar es más fácil por el momento que por el nombre del archivo.
    /// </summary>
    public string CreatedAtDisplay
    {
        get
        {
            var day = CreatedAtLocal.Date;
            var today = DateTime.Today;

            var prefix = day == today ? "hoy"
                : day == today.AddDays(-1) ? "ayer"
                : Helpers.AppCulture.ShortDate(CreatedAtLocal);

            return $"{prefix} a las {CreatedAtLocal:HH:mm}";
        }
    }
}
