using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MetroCarpinteria.App.Models;
using MetroCarpinteria.App.Services;
using MetroCarpinteria.App.ViewModels;
using WpfApp = MetroCarpinteria.App.App;

namespace MetroCarpinteria.SmokeTest;

/// <summary>
/// Genera imágenes de las pantallas para poder revisar el diseño sin abrir la app.
/// <para>
/// Dibuja la ventana con <see cref="RenderTargetBitmap"/> en vez de capturar el escritorio.
/// Es la diferencia entre obtener siempre la aplicación y obtener lo que hubiera encima:
/// no depende del foco, del monitor ni de qué otra ventana esté abierta, y da el mismo
/// resultado en una máquina sin sesión interactiva.
/// </para>
/// <para>Se invoca con <c>--screenshots &lt;carpeta&gt;</c>.</para>
/// </summary>
internal static class ScreenshotRunner
{
    private const int Width = 1400;
    private const int Height = 900;

    public static int Run(string outputDirectory)
    {
        var error = (Exception?)null;

        var thread = new Thread(() =>
        {
            try
            {
                Capture(outputDirectory);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error is not null)
        {
            Console.WriteLine($"Falló la generación de imágenes: {error}");
            return 1;
        }

        return 0;
    }

    private static void Capture(string outputDirectory)
    {
        if (Application.Current is null)
        {
            var app = new WpfApp();
            app.InitializeComponent();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }

        Directory.CreateDirectory(outputDirectory);

        using var fixture = TestFixture.CreateSeeded();
        var theme = AppHost.ThemeService;

        var sections = new (NavigationSection Section, string Name)[]
        {
            (NavigationSection.Home, "inicio"),
            (NavigationSection.Inventory, "inventario"),
            (NavigationSection.CashRegister, "caja"),
            (NavigationSection.Quotes, "presupuestos"),
            (NavigationSection.Projects, "proyectos"),
            (NavigationSection.Staff, "personal"),
            (NavigationSection.Reports, "reportes"),
            (NavigationSection.Settings, "configuracion")
        };

        foreach (var (mode, modeName) in new[] { (AppTheme.Light, "claro"), (AppTheme.Dark, "oscuro") })
        {
            theme.Apply(mode, FontScale.Normal, persist: false);

            foreach (var (section, name) in sections)
            {
                var window = new MetroCarpinteria.App.MainWindow();
                var viewModel = (MainViewModel)window.DataContext!;
                viewModel.NavigateCommand.Execute(section);

                Render(window, Path.Combine(outputDirectory, $"{name}-{modeName}.png"));
            }
        }

        // El pie del panel de Presupuestos: desglose y ajuste del precio final. Queda
        // debajo del pliegue, así que hay que bajar el panel para poder revisarlo.
        theme.Apply(AppTheme.Light, FontScale.Normal, persist: false);
        var priceWindow = new MetroCarpinteria.App.MainWindow();
        ((MainViewModel)priceWindow.DataContext!).NavigateCommand.Execute(NavigationSection.Quotes);
        Render(
            priceWindow,
            Path.Combine(outputDirectory, "presupuestos-precio-ajustado.png"),
            scrollToEnd: true);

        // Y una en letra grande, que es donde se nota si algo recorta.
        theme.Apply(AppTheme.Light, FontScale.Large, persist: false);
        var bigWindow = new MetroCarpinteria.App.MainWindow();
        ((MainViewModel)bigWindow.DataContext!).NavigateCommand.Execute(NavigationSection.Quotes);
        Render(bigWindow, Path.Combine(outputDirectory, "presupuestos-letra-grande.png"));

        theme.Apply(AppTheme.Light, FontScale.Normal, persist: false);

        // Va última porque cambia la carpeta de datos de AppHost, y eso deja inservible
        // la fixture sembrada de arriba.
        CaptureValidationState(outputDirectory);
        CaptureEmptyState(outputDirectory);

        Console.WriteLine($"Imágenes generadas en {outputDirectory}");

        Dispatcher.CurrentDispatcher.InvokeShutdown();
    }

    /// <summary>
    /// Cómo se ve la app el primer día: las listas sin nada y un par de avisos flotantes.
    /// Son dos cosas que solo aparecen en un momento puntual y conviene poder revisarlas
    /// sin tener que provocarlas a mano.
    /// </summary>
    private static void CaptureEmptyState(string outputDirectory)
    {
        using var empty = TestFixtureEmpty.Create();

        AppHost.ThemeService.Apply(AppTheme.Light, FontScale.Normal, persist: false);
        AppHost.NotificationService.Success("Producto «Tabla de roble» guardado.");
        AppHost.NotificationService.Warning("No alcanza el stock para dos líneas del mismo material.");

        var window = new MetroCarpinteria.App.MainWindow();
        ((MainViewModel)window.DataContext!).NavigateCommand.Execute(NavigationSection.Inventory);

        Render(window, Path.Combine(outputDirectory, "inventario-vacio-con-avisos.png"));
        AppHost.NotificationService.Clear();
    }

    /// <summary>
    /// El formulario con un error tipeado: el borde marcado, el mensaje debajo del campo
    /// y Guardar deshabilitado. Es la diferencia con el estado anterior, donde nada de eso
    /// aparecía hasta apretar Guardar.
    /// </summary>
    private static void CaptureValidationState(string outputDirectory)
    {
        var window = new MetroCarpinteria.App.MainWindow();
        var main = (MainViewModel)window.DataContext!;
        main.NavigateCommand.Execute(NavigationSection.Inventory);

        if (main.CurrentViewModel is InventoryViewModel inventory)
        {
            inventory.NewProductCommand.Execute(null);
            inventory.FormName = string.Empty;
            inventory.FormMinimumStock = "quince";
        }

        Render(window, Path.Combine(outputDirectory, "inventario-validacion.png"));
    }

    /// <param name="scrollToEnd">
    /// Baja los paneles hasta el fondo antes de dibujar, para poder revisar lo que queda
    /// abajo de todo sin tener que abrir la app.
    /// </param>
    private static void Render(Window window, string path, bool scrollToEnd = false)
    {
        // Se muestra fuera de la pantalla visible. Sin Show() la ventana no tiene
        // PresentationSource, y sin eso WPF no llega a aplicar las plantillas de los
        // controles ni a disparar Loaded: RenderTargetBitmap devolvía una imagen vacía.
        //
        // Lo que se dibuja sigue siendo el árbol visual de la ventana, no la pantalla,
        // así que nunca aparece nada del escritorio por más que haya otras ventanas.
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -32000;
        window.Top = -32000;
        window.ShowInTaskbar = false;
        window.Width = Width;
        window.Height = Height;
        window.Show();

        try
        {
            window.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);

            // Y una pasada en Background, que es la prioridad en la que WPF encola el
            // recálculo de CanExecute. Sin esto los botones salían dibujados como estaban
            // antes de que la vista cargara sus datos: grises aunque estuvieran habilitados.
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);

            if (window.Content is not UIElement content)
            {
                return;
            }

            // Las vistas arrancan con Opacity=0 y se revelan con una animación de 0,35 s.
            // Acá no se espera a que termine: se fuerza el estado final.
            RevealHiddenElements(content);
            window.UpdateLayout();

            if (scrollToEnd)
            {
                ScrollPanelsToEnd(content);
                window.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
            }

            var bitmap = new RenderTargetBitmap(
                (int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(content);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            using var stream = File.Create(path);
            encoder.Save(stream);
        }
        finally
        {
            window.Close();
        }
    }

    private static void ScrollPanelsToEnd(DependencyObject root)
    {
        if (root is System.Windows.Controls.ScrollViewer { ScrollableHeight: > 0 } scroller)
        {
            scroller.ScrollToEnd();
            scroller.UpdateLayout();
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            ScrollPanelsToEnd(VisualTreeHelper.GetChild(root, i));
        }
    }

    private static void RevealHiddenElements(DependencyObject root)
    {
        if (root is UIElement element)
        {
            // Primero se corta la animación y después se asigna el valor: una animación en
            // curso tiene más prioridad que el valor local, así que setear Opacity sin esto
            // no cambia nada y la vista queda dibujada a medio aparecer.
            element.BeginAnimation(UIElement.OpacityProperty, null);

            if (element.Opacity < 1)
            {
                element.Opacity = 1;
            }
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            RevealHiddenElements(VisualTreeHelper.GetChild(root, i));
        }
    }
}
