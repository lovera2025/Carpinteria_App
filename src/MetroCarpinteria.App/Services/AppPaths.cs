namespace MetroCarpinteria.App.Services;

public sealed class AppPaths
{
    public AppPaths(string? rootDirectory = null)
    {
        RootDirectory = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "MetroCarpinteria");
    }

    public string RootDirectory { get; }

    public string DataDirectory => Path.Combine(RootDirectory, "data");
    public string BackupsDirectory => Path.Combine(RootDirectory, "backups");
    public string DatabasePath => Path.Combine(DataDirectory, "carpinteria.db");
    public string SettingsPath => Path.Combine(RootDirectory, "settings.json");

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(BackupsDirectory);
    }
}
