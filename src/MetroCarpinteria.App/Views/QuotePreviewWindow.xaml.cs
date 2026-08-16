using System.Windows;
using System.Windows.Documents;
using MetroCarpinteria.App.Services;

namespace MetroCarpinteria.App.Views;

/// <summary>
/// Muestra el papel antes de gastarlo, con las hojas tal como salen impresas.
/// </summary>
/// <remarks>
/// <para>
/// Existe porque el diálogo de impresión de Windows no puede mostrarlo: el que abre una app
/// WPF es el clásico, que nunca tuvo panel de previsualización, y además el documento se le
/// entrega recién después de que el usuario acepta. Mientras está abierto, Windows no tiene
/// nada que previsualizar, y por eso avisa que no hay vista previa.
/// </para>
/// <para>
/// Las hojas se dibujan con <see cref="QuoteDocumentService.RenderPages"/>, el mismo método
/// que usa el runner de documentos para verificar el papel: lo que se ve acá es lo que se
/// verifica, no una maqueta aparte.
/// </para>
/// </remarks>
public sealed partial class QuotePreviewWindow : Window
{
    private readonly FlowDocument _document;
    private readonly string _jobName;
    private readonly Action? _savePdf;

    /// <param name="savePdf">
    /// Qué hacer con «Guardar PDF». Null esconde el botón: el recibo, por ejemplo, no tiene
    /// exportador propio y ofrecerlo apagado sólo confundiría.
    /// </param>
    public QuotePreviewWindow(FlowDocument document, string jobName, Action? savePdf = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        InitializeComponent();

        _document = document;
        _jobName = jobName;
        _savePdf = savePdf;

        var pages = QuoteDocumentService.RenderPages(document);

        TitleText.Text = jobName;
        PageCountText.Text = pages.Count == 1
            ? "1 hoja"
            : $"{pages.Count} hojas";

        PagesList.ItemsSource = pages;
        SavePdfButton.Visibility = savePdf is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Si el papel llegó a la impresora. Lo mira quien abrió la ventana.</summary>
    public bool Printed { get; private set; }

    /// <summary>Abre la vista previa y espera. Devuelve true si terminó imprimiendo.</summary>
    public static bool ShowFor(FlowDocument document, string jobName, Action? savePdf = null)
    {
        var window = new QuotePreviewWindow(document, jobName, savePdf);

        // La ventana principal puede no estar montada —en los tests corre sin shell—, y
        // asignar un dueño sin mostrar tira.
        if (Application.Current?.MainWindow is { IsLoaded: true } owner && !ReferenceEquals(owner, window))
        {
            window.Owner = owner;
        }

        window.ShowDialog();
        return window.Printed;
    }

    private void OnPrint(object sender, RoutedEventArgs e)
    {
        // El diálogo de Windows sigue siendo el que imprime: acá sólo se decide cuándo
        // abrirlo, con el papel ya visto.
        if (AppHost.QuoteDocumentService.Print(_document, _jobName))
        {
            Printed = true;
            Close();
        }
    }

    private void OnSavePdf(object sender, RoutedEventArgs e)
    {
        _savePdf?.Invoke();
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();
}
