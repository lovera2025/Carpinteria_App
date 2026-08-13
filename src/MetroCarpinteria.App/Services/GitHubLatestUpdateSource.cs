using Velopack;
using Velopack.Logging;
using Velopack.Sources;

namespace MetroCarpinteria.App.Services;

/// <summary>
/// Dónde busca Velopack las versiones nuevas.
/// </summary>
/// <remarks>
/// <see cref="GithubSource"/> pega a la API de GitHub (60 pedidos por hora sin token) y
/// después baja el <c>releases.win.json</c> de <b>cada</b> release histórico. Si uno solo
/// de esos archivos falla —timeout, 429, un json viejo— el chequeo entero tira y la app
/// instalada se queda sin enterarse de la versión nueva.
///
/// La URL estable <c>/releases/latest/download/</c> es un solo archivo, sin API. Si esa
/// lectura no da nada, recién ahí se cae a la API.
/// </remarks>
internal sealed class GitHubLatestUpdateSource : IUpdateSource
{
    internal const string RepositoryUrl = "https://github.com/lovera2025/Carpinteria_App";
    internal const string LatestDownloadUrl = RepositoryUrl + "/releases/latest/download/";

    private readonly SimpleWebSource _latest = new(LatestDownloadUrl);
    private readonly GithubSource _github = new(RepositoryUrl, accessToken: null, prerelease: false);
    private IUpdateSource _active;

    public GitHubLatestUpdateSource()
    {
        _active = _latest;
    }

    public async Task<VelopackAssetFeed> GetReleaseFeed(
        IVelopackLogger logger,
        string? appId,
        string channel,
        Guid? stagingId = null,
        VelopackAsset? latestLocalRelease = null)
    {
        try
        {
            var feed = await _latest
                .GetReleaseFeed(logger, appId, channel, stagingId, latestLocalRelease)
                .ConfigureAwait(false);

            if (feed.Assets is { Length: > 0 })
            {
                _active = _latest;
                return feed;
            }

            logger.Log(
                VelopackLogLevel.Warning,
                "El feed de /releases/latest/download/ vino vacío; se prueba la API de GitHub.",
                null);
        }
        catch (Exception ex)
        {
            logger.Log(
                VelopackLogLevel.Warning,
                $"No se pudo leer el feed directo de GitHub Releases: {ex.Message}",
                ex);
        }

        _active = _github;
        return await _github
            .GetReleaseFeed(logger, appId, channel, stagingId, latestLocalRelease)
            .ConfigureAwait(false);
    }

    public Task DownloadReleaseEntry(
        IVelopackLogger logger,
        VelopackAsset releaseEntry,
        string localFile,
        Action<int> progress,
        CancellationToken cancelToken = default)
    {
        return _active.DownloadReleaseEntry(logger, releaseEntry, localFile, progress, cancelToken);
    }
}
