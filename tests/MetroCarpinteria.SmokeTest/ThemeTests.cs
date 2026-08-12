using System.Windows;
using MetroCarpinteria.App.Services;

namespace MetroCarpinteria.SmokeTest;

/// <summary>
/// Contratos del sistema de temas. Corre en el hilo de interfaz porque necesita
/// <see cref="Application.Current"/> con sus diccionarios cargados.
/// </summary>
internal static class ThemeTests
{
    public static void Run(Action<string, Action> run)
    {
        run("Tema: los dos temas declaran exactamente las mismas claves", () =>
        {
            // El error más común de un sistema de temas, y el más difícil de ver a ojo:
            // una clave que existe en claro y falta en oscuro. No falla al compilar; se
            // manifiesta como un panel en blanco recién cuando alguien cambia de tema.
            var light = Load("Resources/Theme/Palette.Light.xaml");
            var dark = Load("Resources/Theme/Palette.Dark.xaml");

            var lightKeys = Keys(light);
            var darkKeys = Keys(dark);

            var missingInDark = lightKeys.Except(darkKeys).OrderBy(k => k).ToList();
            var missingInLight = darkKeys.Except(lightKeys).OrderBy(k => k).ToList();

            Assert.True(
                missingInDark.Count == 0,
                $"Faltan en el tema oscuro: {string.Join(", ", missingInDark)}");
            Assert.True(
                missingInLight.Count == 0,
                $"Faltan en el tema claro: {string.Join(", ", missingInLight)}");
            Assert.True(lightKeys.Count > 30, $"La paleta quedó con solo {lightKeys.Count} claves.");
        });

        run("Tema: las tres escalas declaran las mismas claves", () =>
        {
            var small = Keys(Load("Resources/Theme/Type.Small.xaml"));
            var normal = Keys(Load("Resources/Theme/Type.Normal.xaml"));
            var large = Keys(Load("Resources/Theme/Type.Large.xaml"));

            Assert.True(small.SetEquals(normal), "Chica y Normal no declaran las mismas claves.");
            Assert.True(normal.SetEquals(large), "Normal y Grande no declaran las mismas claves.");
        });

        run("Tema: la letra crece de verdad al subir de escala", () =>
        {
            var small = Load("Resources/Theme/Type.Small.xaml");
            var normal = Load("Resources/Theme/Type.Normal.xaml");
            var large = Load("Resources/Theme/Type.Large.xaml");

            foreach (var key in new[] { "FontSizeBody", "FontSizeTitle", "FontSizeCaption" })
            {
                var s = (double)small[key];
                var n = (double)normal[key];
                var l = (double)large[key];

                Assert.True(s < n && n < l, $"{key} no crece: {s} / {n} / {l}.");
            }

            // El espaciado escala en paralelo: si solo creciera el texto, en tamaño grande
            // los controles se tocarían y la pantalla quedaría peor que antes de agrandar.
            Assert.True(
                (double)small["SpaceMdValue"] < (double)large["SpaceMdValue"],
                "El espaciado no acompaña al tamaño de letra.");
        });

        run("Tema: los diccionarios intercambiables están en las posiciones esperadas", () =>
        {
            // ThemeService reemplaza por índice, así que el orden de App.xaml es contrato.
            var merged = Application.Current.Resources.MergedDictionaries;

            Assert.True(merged.Count > 2, "Faltan diccionarios en App.xaml.");
            Assert.True(
                merged[0].Contains("SurfaceBrush"),
                "El diccionario [0] tendría que ser la paleta.");
            Assert.True(
                merged[1].Contains("FontSizeBody"),
                "El diccionario [1] tendría que ser la escala tipográfica.");
        });

        run("Tema: se puede cambiar de tema y de escala sin recrear la ventana", () =>
        {
            var theme = AppHost.ThemeService;
            var originalTheme = theme.Theme;
            var originalScale = theme.Scale;

            try
            {
                var lightBody = ReadFontSize();
                theme.Apply(AppTheme.Dark, FontScale.Large, persist: false);

                Assert.True(theme.IsDarkActive, "Tendría que haber quedado activo el tema oscuro.");
                Assert.True(
                    ReadFontSize() > lightBody,
                    "La escala grande no cambió el tamaño de letra en caliente.");

                // En oscuro el color de acción pasa a ser el dorado: el marrón sobre fondo
                // oscuro da un contraste cercano a 2:1 y un botón primario desaparecería.
                var brand = (System.Windows.Media.SolidColorBrush)Application.Current
                    .Resources["BrandPrimaryBrush"];
                Assert.True(
                    brand.Color.R > 150 && brand.Color.G > 130,
                    $"En oscuro el color de acción tendría que ser claro, llegó {brand.Color}.");

                theme.Apply(AppTheme.Light, FontScale.Normal, persist: false);
                Assert.False(theme.IsDarkActive, "Tendría que haber vuelto al tema claro.");
                Assert.Equal(ReadFontSize(), lightBody, "El tamaño de letra no volvió al original");
            }
            finally
            {
                theme.Apply(originalTheme, originalScale, persist: false);
            }
        });

        run("Tema: los nombres viejos de pinceles también siguen al tema", () =>
        {
            // Las vistas todavía piden TextPrimaryBrush, CardBackgroundBrush y compañía.
            // Brushes.xaml los redirige a los tokens nuevos con DynamicResource; si esa
            // redirección no propagara el cambio, en oscuro quedaría texto marrón sobre
            // fondo negro y el tema serviría de poco.
            var theme = AppHost.ThemeService;
            var originalTheme = theme.Theme;
            var originalScale = theme.Scale;

            try
            {
                theme.Apply(AppTheme.Light, FontScale.Normal, persist: false);
                var lightText = ReadColor("TextPrimaryBrush");
                var lightCard = ReadColor("CardBackgroundBrush");

                theme.Apply(AppTheme.Dark, FontScale.Normal, persist: false);
                var darkText = ReadColor("TextPrimaryBrush");
                var darkCard = ReadColor("CardBackgroundBrush");

                Assert.True(darkText != lightText, "TextPrimaryBrush no cambió con el tema.");
                Assert.True(darkCard != lightCard, "CardBackgroundBrush no cambió con el tema.");

                // Y que el cambio vaya en la dirección correcta, no que solo sea distinto.
                Assert.True(
                    darkText.R > lightText.R,
                    $"En oscuro el texto tendría que aclararse: {lightText} → {darkText}.");
                Assert.True(
                    darkCard.R < lightCard.R,
                    $"En oscuro la tarjeta tendría que oscurecerse: {lightCard} → {darkCard}.");
            }
            finally
            {
                theme.Apply(originalTheme, originalScale, persist: false);
            }
        });
    }

    /// <summary>
    /// Que cambien las claves del diccionario no alcanza: un elemento que pidió su color con
    /// StaticResource se quedó con el pincel de entonces y no se entera de nada. Este test
    /// mira el color efectivo de un control ya dibujado, que es lo que ve el usuario.
    /// </summary>
    public static void RunRepaintCheck(Action<string, Action> run)
    {
        run("Tema: un control ya dibujado se repinta al cambiar de tema", () =>
        {
            var theme = AppHost.ThemeService;
            var originalTheme = theme.Theme;
            var originalScale = theme.Scale;

            try
            {
                theme.Apply(AppTheme.Light, FontScale.Normal, persist: false);

                // Contra la ventana real y su árbol visual: un elemento suelto, sin padre
                // conectado, no recibe la invalidación de recursos y el test no probaría
                // lo que efectivamente ve el usuario.
                var window = new MetroCarpinteria.App.MainWindow();
                window.Measure(new Size(1280, 800));
                window.Arrange(new Rect(0, 0, 1280, 800));
                window.UpdateLayout();

                var lightBack = ReadWindowBackground(window);

                theme.Apply(AppTheme.Dark, FontScale.Normal, persist: false);
                window.UpdateLayout();

                var darkBack = ReadWindowBackground(window);

                Assert.True(
                    darkBack != lightBack,
                    $"El fondo de la ventana no se repintó: siguió en {lightBack}.");
                Assert.True(
                    darkBack.R < lightBack.R,
                    $"En oscuro el fondo tendría que oscurecerse: {lightBack} → {darkBack}.");
            }
            finally
            {
                theme.Apply(originalTheme, originalScale, persist: false);
            }
        });
    }

    private static System.Windows.Media.Color ReadWindowBackground(Window window) =>
        ((System.Windows.Media.SolidColorBrush)window.Background).Color;

    private static double ReadFontSize() => (double)Application.Current.Resources["FontSizeBody"];

    private static System.Windows.Media.Color ReadColor(string key) =>
        ((System.Windows.Media.SolidColorBrush)Application.Current.Resources[key]).Color;

    private static ResourceDictionary Load(string relativeUri) => new()
    {
        Source = new Uri($"pack://application:,,,/MetroCarpinteria;component/{relativeUri}", UriKind.Absolute)
    };

    private static HashSet<string> Keys(ResourceDictionary dictionary) =>
        dictionary.Keys.Cast<object>().Select(k => k.ToString()!).ToHashSet();
}
