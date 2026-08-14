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
    public string LogsDirectory => Path.Combine(RootDirectory, "logs");
    public string QuoteImagesDirectory => Path.Combine(DataDirectory, "quote-images");
    public string ReceiptsDirectory => Path.Combine(RootDirectory, "recibos");
    public string DatabasePath => Path.Combine(DataDirectory, "carpinteria.db");
    public string SettingsPath => Path.Combine(RootDirectory, "settings.json");

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(BackupsDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(QuoteImagesDirectory);
        Directory.CreateDirectory(ReceiptsDirectory);
    }

    /// <summary>Carpeta de las fotos de un presupuesto. El id va en el nombre, no en una subruta libre.</summary>
    public string QuoteImageFolder(int projectId) =>
        Path.Combine(QuoteImagesDirectory, projectId.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>
    /// Ruta absoluta de un archivo de foto. Rechaza nombres con separadores: un valor
    /// venido de la base no puede salir a recorrer el disco.
    /// </summary>
    public string QuoteImagePath(int projectId, string fileName)
    {
        if (!IsSafeImageFileName(fileName))
        {
            throw new InvalidOperationException("El nombre del archivo de la foto no es válido.");
        }

        return Path.Combine(QuoteImageFolder(projectId), fileName);
    }

    public static bool IsSafeImageFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var name = fileName.Trim();
        if (name != Path.GetFileName(name))
        {
            return false;
        }

        if (name.Contains("..", StringComparison.Ordinal)
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return false;
        }

        return name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase);
    }
}
