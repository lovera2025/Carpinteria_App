using Velopack;
using Velopack.Sources;

namespace MetroCarpinteria.App.Services;

/// <summary>Cómo terminó una consulta de actualizaciones.</summary>
public enum UpdateCheckOutcome
{
    /// <summary>No es una copia instalada: portable, o corriendo desde el proyecto.</summary>
    NotSupported,

    /// <summary>Se consultó y no hay nada nuevo.</summary>
    UpToDate,

    /// <summary>Hay una versión nueva.</summary>
    Available,

    /// <summary>No se pudo consultar. <b>No</b> significa que estés al día.</summary>
    Failed
}

/// <param name="Update">La versión encontrada. Solo viene con <see cref="UpdateCheckOutcome.Available"/>.</param>
public sealed record UpdateCheck(UpdateCheckOutcome Outcome, UpdateInfo? Update = null);

/// <summary>
/// Busca versiones nuevas en los releases de GitHub y las deja listas para aplicarse
/// al cerrar la app.
///
/// Regla de oro de este servicio: <b>ningún problema de red puede molestar al usuario</b>.
/// La app es de taller y funciona entera sin internet; si no hay conexión, el chequeo
/// falla sin interrumpir nada y todo sigue igual.
/// </summary>
/// <remarks>
/// «Sin molestar» no es lo mismo que «sin dejar rastro». Todo lo que pasa acá queda en el
/// log, y un chequeo que falla se distingue de uno que no encontró nada: mientras los dos
/// se reportaban igual, una máquina que no lograba consultar mostraba «ya tenés la última
/// versión» y se quedaba sin actualizar sin que nadie pudiera darse cuenta.
/// </remarks>
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
        catch (Exception ex)
        {
            // Si Velopack no puede resolver dónde está instalada la app, el updater
            // simplemente no está disponible. No es motivo para impedir que abra.
            LogService.Warning("UpdateService", $"No se pudo iniciar el actualizador: {ex.Message}");
            _manager = null;
        }
    }

    /// <summary>
    /// Deja en el log en qué versión está la copia y si puede actualizarse. Sin esto no hay
    /// forma de saber, mirando el log del taller, si arrancó la copia instalada o una suelta.
    /// </summary>
    public void LogInstallState()
    {
        LogService.Info(
            "UpdateService",
            IsSupported
                ? $"Copia instalada v{CurrentVersion}; el actualizador está disponible."
                : $"Copia NO instalada v{CurrentVersion} (portable o desde el proyecto): no se actualiza sola.");
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
    /// Pregunta a GitHub si hay una versión nueva. Nunca lanza: informa qué pasó.
    /// </summary>
    /// <remarks>
    /// Devolver un resultado y no un <c>UpdateInfo?</c> es el punto: «no encontré nada» y
    /// «no pude preguntar» son cosas distintas y antes se confundían en el mismo null.
    /// </remarks>
    public async Task<UpdateCheck> CheckAsync()
    {
        if (!IsSupported || _manager is null)
        {
            return new UpdateCheck(UpdateCheckOutcome.NotSupported);
        }

        try
        {
            var update = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
            RecordCheck();

            if (update is null)
            {
                LogService.Info("UpdateService", $"Sin novedades: v{CurrentVersion} es la última.");
                return new UpdateCheck(UpdateCheckOutcome.UpToDate);
            }

            LogService.Info(
                "UpdateService",
                $"Hay versión nueva: v{update.TargetFullRelease.Version} (tenés la v{CurrentVersion}).");

            return new UpdateCheck(UpdateCheckOutcome.Available, update);
        }
        catch (Exception ex)
        {
            // Sin internet, GitHub caído o un release a medio publicar. No se le avisa al
            // usuario —la app de taller trabaja sin conexión todo el tiempo— pero queda
            // escrito con el error real: es lo único que permite diagnosticarlo después.
            LogService.Warning("UpdateService", $"No se pudo consultar si hay actualizaciones: {ex.Message}");
            return new UpdateCheck(UpdateCheckOutcome.Failed);
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

            LogService.Info(
                "UpdateService",
                $"v{update.TargetFullRelease.Version} descargada; se instala al cerrar la app.");

            return true;
        }
        catch (Exception ex)
        {
            LogService.Warning("UpdateService", $"No se pudo descargar la actualización: {ex.Message}");
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
            LogService.Info("UpdateService", $"Instalando v{PendingUpdate.TargetFullRelease.Version} al salir.");
        }
        catch (Exception ex)
        {
            // Si falla, la actualización se vuelve a intentar en el próximo arranque.
            LogService.Warning("UpdateService", $"No se pudo dejar la actualización aplicándose: {ex.Message}");
        }
    }

    /// <summary>Chequeo automático de arranque, según lo configurado.</summary>
    /// <returns>La versión ya descargada y lista, o null si no hay nada que avisar.</returns>
    public async Task<UpdateInfo?> CheckAndDownloadOnStartupAsync()
    {
        LogInstallState();

        if (!_settingsService.Current.CheckUpdatesOnStartup)
        {
            LogService.Info("UpdateService", "El chequeo al arrancar está desactivado en Configuración.");
            return null;
        }

        var check = await CheckAsync().ConfigureAwait(false);
        if (check is not { Outcome: UpdateCheckOutcome.Available, Update: not null })
        {
            return null;
        }

        return await DownloadAsync(check.Update).ConfigureAwait(false) ? check.Update : null;
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
