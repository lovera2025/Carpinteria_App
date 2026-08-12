using System.Text.Json;
using MetroCarpinteria.App.Models;

namespace MetroCarpinteria.App.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly AppPaths _paths;
    private AppSettings _settings;

    public SettingsService(AppPaths paths)
    {
        _paths = paths;
        _settings = Load();
    }

    public AppSettings Current => _settings;

    /// <summary>
    /// Nombre del archivo al que se movió una configuración ilegible, o <c>null</c> si
    /// todo estaba bien. El arranque lo consulta para avisarlo: perder los porcentajes
    /// del taller y las preferencias sin que nadie diga nada es peor que el archivo roto.
    /// </summary>
    public string? CorruptFileName { get; private set; }

    public AppSettings Load()
    {
        CorruptFileName = null;

        if (!File.Exists(_paths.SettingsPath))
        {
            _settings = new AppSettings();
            Save(_settings);
            return _settings;
        }

        try
        {
            var json = File.ReadAllText(_paths.SettingsPath);
            _settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            // Se conserva el original en vez de sobrescribirlo: adentro pueden estar los
            // porcentajes con los que se venía cotizando, y de un JSON truncado casi
            // siempre se rescatan a mano.
            _settings = new AppSettings();
            CorruptFileName = QuarantineCorruptFile(ex);
        }

        return _settings;
    }

    public void Save(AppSettings settings)
    {
        _settings = settings;
        var json = JsonSerializer.Serialize(_settings, JsonOptions);

        Directory.CreateDirectory(Path.GetDirectoryName(_paths.SettingsPath)!);

        // Escritura atómica: WriteAllText trunca y después escribe, así que un corte de
        // luz en el medio dejaba un settings.json vacío o partido. Con el temporal, o
        // queda el archivo viejo entero o el nuevo entero.
        var tempPath = _paths.SettingsPath + ".tmp";
        File.WriteAllText(tempPath, json);

        if (File.Exists(_paths.SettingsPath))
        {
            File.Replace(tempPath, _paths.SettingsPath, destinationBackupFileName: null);
        }
        else
        {
            File.Move(tempPath, _paths.SettingsPath);
        }
    }

    public void Update(Action<AppSettings> configure)
    {
        configure(_settings);
        Save(_settings);
    }

    private string? QuarantineCorruptFile(Exception cause)
    {
        var name = $"settings.corrupto-{DateTime.Now:yyyyMMdd_HHmmss}.json";
        var target = Path.Combine(Path.GetDirectoryName(_paths.SettingsPath)!, name);

        try
        {
            File.Move(_paths.SettingsPath, target, overwrite: true);
            LogService.Warning(
                "SettingsService",
                $"La configuración era ilegible ({cause.Message}). Se guardó como {name} y se " +
                "arrancó con los valores por defecto.");

            // Deja la configuración por defecto ya escrita: si no, el próximo arranque
            // volvería a encontrarse sin archivo y el aviso se repetiría sin motivo.
            Save(_settings);
            return name;
        }
        catch (Exception ex)
        {
            // Sin permisos para mover el archivo se sigue igual con los valores por
            // defecto en memoria, pero sin escribirlos: pisar el original sería lo único
            // peor que no poder leerlo.
            LogService.Error("SettingsService", "No se pudo apartar la configuración ilegible", ex);
            return null;
        }
    }
}
