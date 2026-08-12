using System.Windows;
using Microsoft.Win32;

namespace MetroCarpinteria.App.Services;

public enum AppTheme
{
    /// <summary>Sigue lo que tenga configurado Windows.</summary>
    System = 0,
    Light = 1,
    Dark = 2
}

public enum FontScale
{
    Small = 0,
    Normal = 1,
    Large = 2
}

/// <summary>
/// Cambia el tema y el tamaño de letra sin reiniciar la aplicación.
/// <para>
/// Funciona porque los diccionarios están separados por lo que cambia y lo que no: la
/// paleta y la escala son solo hojas —colores, medidas, efectos— y viven en las posiciones
/// 0 y 1 de <c>MergedDictionaries</c>; los estilos viven después y consumen todo por
/// <c>DynamicResource</c>. Un estilo implícito se resuelve una sola vez, al entrar el
/// elemento al árbol visual, así que no se puede intercambiar; un color sí.
/// </para>
/// </summary>
public sealed class ThemeService
{
    private const int PaletteIndex = 0;
    private const int TypographyIndex = 1;

    private readonly SettingsService _settingsService;
    private bool _watchingSystem;

    public ThemeService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public AppTheme Theme { get; private set; } = AppTheme.System;
    public FontScale Scale { get; private set; } = FontScale.Normal;

    /// <summary>Qué tema se está viendo realmente: resuelve <see cref="AppTheme.System"/>.</summary>
    public bool IsDarkActive => Resolve(Theme) == AppTheme.Dark;

    /// <summary>Aplica lo guardado en la configuración. Se llama una vez al arrancar.</summary>
    public void ApplySaved()
    {
        var settings = _settingsService.Current;
        Apply(settings.Theme, settings.FontScale, persist: false);
    }

    public void Apply(AppTheme theme, FontScale scale, bool persist = true)
    {
        Theme = theme;
        Scale = scale;

        SwapDictionary(PaletteIndex, Resolve(theme) == AppTheme.Dark
            ? "Resources/Theme/Palette.Dark.xaml"
            : "Resources/Theme/Palette.Light.xaml");

        SwapDictionary(TypographyIndex, scale switch
        {
            FontScale.Small => "Resources/Theme/Type.Small.xaml",
            FontScale.Large => "Resources/Theme/Type.Large.xaml",
            _ => "Resources/Theme/Type.Normal.xaml"
        });

        WatchSystemThemeIfNeeded(theme);

        if (persist)
        {
            _settingsService.Update(s =>
            {
                s.Theme = theme;
                s.FontScale = scale;
            });
        }
    }

    public void SetTheme(AppTheme theme) => Apply(theme, Scale);

    public void SetScale(FontScale scale) => Apply(Theme, scale);

    /// <summary>
    /// Reemplaza un diccionario por posición.
    /// <para>
    /// Por posición y no con <c>Clear()</c> + <c>Add()</c>: un <c>Clear</c> invalida todos
    /// los recursos de golpe, lo que hace parpadear la ventana entera y puede tirar si
    /// algún binding se resuelve justo en ese momento. Reemplazar el elemento invalida
    /// únicamente las claves afectadas.
    /// </para>
    /// </summary>
    private static void SwapDictionary(int index, string relativeUri)
    {
        var app = Application.Current;
        if (app is null)
        {
            // Pasa en los tests, que instancian vistas sin Application.
            return;
        }

        var merged = app.Resources.MergedDictionaries;
        if (merged.Count <= index)
        {
            LogService.Warning("ThemeService", $"No existe el diccionario en la posición {index}.");
            return;
        }

        // El assembly va explícito en la URI: la app también se instancia desde el proyecto
        // de pruebas, y ahí una ruta relativa se resolvería contra el assembly equivocado.
        var dictionary = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/MetroCarpinteria;component/{relativeUri}", UriKind.Absolute)
        };

        merged[index] = dictionary;
    }

    private AppTheme Resolve(AppTheme theme) =>
        theme == AppTheme.System ? ReadWindowsTheme() : theme;

    /// <summary>
    /// Lee la preferencia de Windows. Si no se puede, se asume claro: es el tema con el
    /// que nació la app y el que está probado en el taller.
    /// </summary>
    private static AppTheme ReadWindowsTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

            var value = key?.GetValue("AppsUseLightTheme");
            return value is int light && light == 0 ? AppTheme.Dark : AppTheme.Light;
        }
        catch (Exception ex)
        {
            LogService.Warning("ThemeService", $"No se pudo leer el tema de Windows: {ex.Message}");
            return AppTheme.Light;
        }
    }

    private void WatchSystemThemeIfNeeded(AppTheme theme)
    {
        if (theme != AppTheme.System)
        {
            if (_watchingSystem)
            {
                SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
                _watchingSystem = false;
            }

            return;
        }

        if (!_watchingSystem)
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            _watchingSystem = true;
        }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General || Theme != AppTheme.System)
        {
            return;
        }

        // El evento llega en un hilo del sistema; tocar recursos exige el hilo de interfaz.
        Application.Current?.Dispatcher.Invoke(() => Apply(AppTheme.System, Scale, persist: false));
    }
}
