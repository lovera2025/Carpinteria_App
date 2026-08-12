using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MetroCarpinteria.App.Services;

namespace MetroCarpinteria.App.Controls;

/// <summary>
/// Parte visible de <see cref="DialogService"/>. Se instancia una sola vez, en la ventana
/// principal, por encima del contenido de página.
/// </summary>
public partial class DialogHost : UserControl
{
    private DialogService? _service;

    public DialogHost()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!AppHost.IsReady || _service is not null)
        {
            return;
        }

        _service = AppHost.DialogService;
        _service.CurrentChanged += OnCurrentChanged;

        // Recién con la capa montada el servicio puede esperar una respuesta; hasta
        // entonces cae al cuadro de Windows en vez de quedarse esperando para siempre.
        _service.HasHost = true;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_service is null)
        {
            return;
        }

        _service.CurrentChanged -= OnCurrentChanged;
        _service.HasHost = false;
        _service = null;
    }

    private void OnCurrentChanged(object? sender, EventArgs e)
    {
        var request = _service?.Current;

        if (request is null)
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        TitleText.Text = request.Title;
        MessageText.Text = request.Message;
        IconText.Text = request.Icon;
        ConfirmButton.Content = request.ConfirmText;
        CancelButton.Content = request.CancelText;
        CancelButton.Visibility = request.ShowCancel ? Visibility.Visible : Visibility.Collapsed;

        // Una acción destructiva se ve distinta de una pregunta cualquiera: es la
        // diferencia entre "¿guardar?" y "esto no se puede deshacer".
        var destructive = request.Kind is DialogKind.Danger;
        IconBadge.Background = (System.Windows.Media.Brush)FindResource(
            destructive ? "StateDangerBrush" : "BrandPrimaryBrush");
        ConfirmButton.Style = (Style)FindResource(
            destructive ? "DangerButtonStyle" : "PrimaryButtonStyle");

        Visibility = Visibility.Visible;

        // El foco va a Cancelar y no a Confirmar: si alguien viene apretando Enter,
        // que el impulso no le borre un producto.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var target = request.ShowCancel ? (Button)CancelButton : ConfirmButton;
            target.Focus();
            Keyboard.Focus(target);
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void OnConfirm(object sender, RoutedEventArgs e) => _service?.Complete(true);

    private void OnCancel(object sender, RoutedEventArgs e) => _service?.Complete(false);
}
