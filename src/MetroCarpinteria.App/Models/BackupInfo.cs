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
}
