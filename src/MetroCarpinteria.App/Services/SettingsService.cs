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

    public AppSettings Load()
    {
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
        catch
        {
            _settings = new AppSettings();
        }

        return _settings;
    }

    public void Save(AppSettings settings)
    {
        _settings = settings;
        var json = JsonSerializer.Serialize(_settings, JsonOptions);
        File.WriteAllText(_paths.SettingsPath, json);
    }

    public void Update(Action<AppSettings> configure)
    {
        configure(_settings);
        Save(_settings);
    }
}
