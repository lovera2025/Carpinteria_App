namespace MetroCarpinteria.App.Models;

public class AppSettings
{
    public bool BackupOnExit { get; set; } = true;
    public int MaxBackupFiles { get; set; } = 30;
    public DateTime? LastBackupUtc { get; set; }
}
