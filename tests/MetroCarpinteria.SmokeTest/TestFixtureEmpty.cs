using System.IO;
using MetroCarpinteria.App.Services;

namespace MetroCarpinteria.SmokeTest;

/// <summary>
/// Carpeta de datos recién creada, sin nada cargado. Sirve para ver cómo queda la app
/// el primer día, que es justamente el estado que antes no mostraba ninguna pista.
/// </summary>
internal sealed class TestFixtureEmpty : IDisposable
{
    private TestFixtureEmpty(AppPaths paths) => Paths = paths;

    public AppPaths Paths { get; }

    public static TestFixtureEmpty Create()
    {
        var root = Path.Combine(Path.GetTempPath(), $"MetroCarpinteriaVacia_{Guid.NewGuid():N}");
        var paths = new AppPaths(root);

        AppHost.ResetForTests();
        AppHost.Initialize(paths);

        return new TestFixtureEmpty(paths);
    }

    public void Dispose()
    {
        AppHost.ResetForTests();

        try
        {
            if (Directory.Exists(Paths.RootDirectory))
            {
                Directory.Delete(Paths.RootDirectory, recursive: true);
            }
        }
        catch
        {
            // Carpeta temporal: la limpia el sistema si quedó tomada.
        }
    }
}
