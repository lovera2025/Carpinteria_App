using System.Windows;
using System.Windows.Controls;
using MetroCarpinteria.App.Models;
using MetroCarpinteria.App.Services;

namespace MetroCarpinteria.App.Controls;

/// <summary>
/// Dibuja los avisos flotantes. El estado vive en <see cref="NotificationService"/>;
/// esto es solo la parte visible, con el mínimo de lógica en el code-behind.
/// </summary>
public partial class ToastHost : UserControl
{
    public ToastHost()
    {
        InitializeComponent();

        // Se ata solo al servicio, así que ninguna vista tiene que acordarse de hacerlo.
        Loaded += (_, _) =>
        {
            if (ItemsHost.ItemsSource is null && AppHost.IsReady)
            {
                ItemsHost.ItemsSource = AppHost.NotificationService.Items;
            }
        };
    }

    private void OnDismiss(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ToastItem item } && AppHost.IsReady)
        {
            AppHost.NotificationService.Dismiss(item);
        }
    }

    /// <summary>
    /// Copia el detalle técnico de un error. Es para poder pegarlo en un mensaje sin
    /// tener que transcribirlo a mano ni sacarle una foto a la pantalla.
    /// </summary>
    private void OnCopyDetail(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string detail } || string.IsNullOrWhiteSpace(detail))
        {
            return;
        }

        try
        {
            Clipboard.SetText(detail);

            if (AppHost.IsReady)
            {
                AppHost.NotificationService.Info("Detalle copiado al portapapeles.");
            }
        }
        catch (Exception ex)
        {
            // El portapapeles lo puede tener tomado otra aplicación. No vale la pena
            // molestar con eso justo cuando el usuario ya está mirando un error.
            LogService.Warning("ToastHost", $"No se pudo copiar al portapapeles: {ex.Message}");
        }
    }
}
