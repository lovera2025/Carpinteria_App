using System.IO;
using MetroCarpinteria.App.Services;
using Microsoft.Data.Sqlite;

namespace MetroCarpinteria.SmokeTest;

/// <summary>
/// Lo que pasa cuando el arranque no puede completarse.
/// <para>
/// Antes, un fallo en <c>AppHost.Initialize()</c> dejaba todas las propiedades estáticas en
/// null, pero la ventana principal abría igual porque <c>StartupUri</c> no consulta a nadie.
/// El resultado era una cascada de <c>NullReferenceException</c> donde el error original
/// nunca aparecía. Estos tests fijan el contrato que evita ese estado.
/// </para>
/// </summary>
internal static class StartupTests
{
    public static void Run(Action<string, Action> run)
    {
        run("Arranque: un fallo deja AppHost marcado como no listo", () =>
        {
            var root = NewTempRoot();
            try
            {
                AppHost.ResetForTests();

                // Un archivo donde va la carpeta de datos: crear el directorio falla.
                Directory.CreateDirectory(root);
                File.WriteAllText(Path.Combine(root, "data"), "no soy una carpeta");

                Assert.Throws<Exception>(
                    () => AppHost.Initialize(new AppPaths(root)),
                    string.Empty);

                Assert.False(AppHost.IsReady, "IsReady tenía que quedar en false tras el fallo.");
            }
            finally
            {
                AppHost.ResetForTests();
                TryDelete(root);
            }
        });

        run("Arranque: una base de una versión más nueva corta el arranque", () =>
        {
            var root = NewTempRoot();
            try
            {
                var paths = new AppPaths(root);
                paths.EnsureDirectories();

                AppHost.ResetForTests();
                AppHost.Initialize(paths);
                AppHost.ResetForTests();

                // Simula una base escrita por una versión futura de la app.
                SetUserVersion(paths.DatabasePath, SchemaMigrator.LatestVersion + 5);

                var migrator = new SchemaMigrator(paths.DatabasePath);
                var error = Assert.Throws<SchemaTooNewException>(
                    () => migrator.MigrateToLatest(),
                    "versión más nueva");

                Assert.Equal(error.FileVersion, SchemaMigrator.LatestVersion + 5, "versión leída del archivo");
                Assert.Equal(error.SupportedVersion, SchemaMigrator.LatestVersion, "versión soportada");
            }
            finally
            {
                AppHost.ResetForTests();
                TryDelete(root);
            }
        });

        run("Log: se escribe, tiene el mensaje y no tira si el disco falla", () =>
        {
            var root = NewTempRoot();
            try
            {
                LogService.Bootstrap(root);

                Assert.NotNull(LogService.CurrentFile, "el log tenía que quedar apuntando a un archivo");
                LogService.Error("PruebaDeLog", "algo salió mal", new InvalidOperationException("detalle"));

                var content = File.ReadAllText(LogService.CurrentFile!);
                Assert.True(content.Contains("PruebaDeLog"), "faltaba el contexto en el log.");
                Assert.True(content.Contains("algo salió mal"), "faltaba el mensaje en el log.");
                Assert.True(content.Contains("detalle"), "faltaba el detalle de la excepción.");
                Assert.True(content.Contains("[ERR]"), "faltaba el nivel en el log.");
            }
            finally
            {
                // Deja el logger apuntando a la carpeta real para el resto de la corrida.
                LogService.Bootstrap();
                TryDelete(root);
            }
        });

        run("Log: preparar la carpeta nunca tira", () =>
        {
            // Un logger que tira excepciones es peor que no tener logger: rompería
            // justo el flujo que se estaba tratando de registrar.
            LogService.Bootstrap("\0ruta::invalida");
            LogService.Info("PruebaDeLog", "esto no tiene que romper nada");

            LogService.Bootstrap();
        });
    }

    private static string NewTempRoot() =>
        Path.Combine(Path.GetTempPath(), $"MetroCarpinteriaStartup_{Guid.NewGuid():N}");

    private static void SetUserVersion(string databasePath, int version)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA user_version = {version};";
        command.ExecuteNonQuery();
        SqliteConnection.ClearAllPools();
    }

    private static void TryDelete(string path)
    {
        try
        {
            SqliteConnection.ClearAllPools();

            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Carpeta temporal: si queda tomada, la limpia el sistema.
        }
    }
}
