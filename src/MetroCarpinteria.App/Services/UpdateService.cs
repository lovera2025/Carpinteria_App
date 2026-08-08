using Velopack;
using Velopack.Sources;

namespace MetroCarpinteria.App.Services;

/// <summary>
/// Busca versiones nuevas en los releases de GitHub y las deja listas para aplicarse
/// al cerrar la app.
///
/// Regla de oro de este servicio: <b>ningún problema de red puede molestar al usuario</b>.
/// La app es de taller y funciona entera sin internet; si no hay conexión, el chequeo
/// falla en silencio y todo sigue igual.
/// </summary>
public sealed class UpdateService
{
    private const string RepositoryUrl = "https://github.com/lovera2025/Carpinteria_App";

    private readonly SettingsService _settingsService;
    private readonly UpdateManager? _manager;

    public UpdateService(SettingsService settingsService)
    {
        _settingsService = settingsService;

        try
        {
            // El repositorio es público, así que no hace falta token: nada de credenciales
            // embebidas en el ejecutable que se reparte.
            _manager = new UpdateManager(new GithubSource(RepositoryUrl, null, false));
        }
        catch
        {
            // Si Velopack no puede resolver dónde está instalada la app, el updater
            // simplemente no está disponible. No es motivo para impedir que abra.
            _manager = null;
        }
    }

    /// <summary>
    /// Falso mientras se corre desde Visual Studio o <c>dotnet run</c>, y en los tests.
    /// Solo una copia instalada con el instalador puede actualizarse.
    /// </summary>
    public bool IsSupported
    {
        get
        {
            try
            {
                return _manager is { IsInstalled: true };
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Versión que se está ejecutando, para mostrar en pantalla.</summary>
    public string CurrentVersion
    {
        get
        {
            try
            {
                var version = _manager?.CurrentVersion;
                if (version is not null)
                {
                    return version.ToString();
                }
            }
            catch
            {
                // Cae al número del ensamblado.
            }

            var assemblyVersion = typeof(UpdateService).Assembly.GetName().Version;
            return assemblyVersion is null
                ? "1.0.0"
                : $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}";
        }
    }

    /// <summary>Actualización ya descargada esperando el cierre de la app.</summary>
    public UpdateInfo? PendingUpdate { get; private set; }

    public bool HasPendingUpdate => PendingUpdate is not null;

    /// <summary>
    /// Pregunta a GitHub si hay una versión nueva. Devuelve null si no hay ninguna,
    /// si no hay internet o si la app no está instalada. Nunca lanza.
    /// </summary>
    public async Task<UpdateInfo?> CheckAsync()
    {
        if (!IsSupported || _manager is null)
        {
            return null;
        }

        try
        {
            var update = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
            RecordCheck();
            return update;
        }
        catch
        {
            // Sin internet, GitHub caído o un release a medio publicar: se ignora.
            return null;
        }
    }

    /// <summary>
    /// Descarga la actualización. Velopack baja solo las diferencias contra la versión
    /// instalada, así que suele mover pocos megas en vez del paquete entero.
    /// Devuelve true si quedó lista para aplicarse.
    /// </summary>
    public async Task<bool> DownloadAsync(UpdateInfo update, IProgress<int>? progress = null)
    {
        if (!IsSupported || _manager is null)
        {
            return false;
        }

        try
        {
            await _manager
                .DownloadUpdatesAsync(update, p => progress?.Report(p))
                .ConfigureAwait(false);

            PendingUpdate = update;
            return true;
        }
        catch
        {
            PendingUpdate = null;
            return false;
        }
    }

    /// <summary>
    /// Deja el actualizador esperando a que la app termine para reemplazar los archivos.
    /// Se llama al cerrar: así nunca interrumpe el trabajo ni obliga a reiniciar.
    /// </summary>
    public void ApplyPendingOnExit()
    {
        if (!IsSupported || _manager is null || PendingUpdate is null)
        {
            return;
        }

        try
        {
            _manager.WaitExitThenApplyUpdates(PendingUpdate, silent: true, restart: false);
        }
        catch
        {
            // Si falla, la actualización se vuelve a intentar en el próximo arranque.
        }
    }

    /// <summary>Chequeo automático de arranque, según lo configurado.</summary>
    public async Task<UpdateInfo?> CheckAndDownloadOnStartupAsync()
    {
        if (!_settingsService.Current.CheckUpdatesOnStartup)
        {
            return null;
        }

        var update = await CheckAsync().ConfigureAwait(false);
        if (update is null)
        {
            return null;
        }

        return await DownloadAsync(update).ConfigureAwait(false) ? update : null;
    }

    private void RecordCheck()
    {
        try
        {
            _settingsService.Update(settings => settings.LastUpdateCheckUtc = DateTime.UtcNow);
        }
        catch
        {
            // Guardar la fecha del último chequeo no es crítico.
        }
    }
}
