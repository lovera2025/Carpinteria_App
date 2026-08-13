using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace MetroCarpinteria.App.Services;

/// <summary>
/// Guarda un <see cref="FlowDocument"/> como PDF, para mandarlo por mensaje sin pasar por
/// la impresora.
/// </summary>
/// <remarks>
/// <para>
/// <b>Sin paquetes externos</b>, por lo mismo que <see cref="QuoteDocumentService"/>: no
/// inflar el ejecutable. Se dibuja cada hoja tal como saldría impresa, se comprime en JPEG
/// y se arma a mano un PDF mínimo. Los bytes del JPEG entran tal cual en el archivo con el
/// filtro <c>/DCTDecode</c>, que es lo que hace que todo esto quepa en un archivo corto.
/// </para>
/// <para>
/// <b>La contra:</b> el texto sale como imagen, así que no se puede seleccionar ni buscar.
/// Se ve exactamente igual que el papel y se imprime igual, que es para lo que se usa. Un
/// PDF de texto obligaría a reescribir los documentos contra una librería de PDF y a tirar
/// el <see cref="FlowDocument"/>, que está ajustado al píxel para entrar en una A4.
/// </para>
/// </remarks>
public sealed class PdfExportService
{
    /// <summary>A4 a 96 ppp, el mismo tamaño con el que se miden los documentos.</summary>
    private const double PageWidthPx = 794;
    private const double PageHeightPx = 1123;

    /// <summary>A4 en puntos PostScript, que es la unidad del PDF.</summary>
    private const double PageWidthPt = 595.276;
    private const double PageHeightPt = 841.89;

    /// <summary>
    /// Cuánto se agranda al dibujar. En 2 quedan 192 ppp: se lee bien en pantalla, imprime
    /// sin escalones y una hoja pesa unos pocos cientos de kilobytes.
    /// </summary>
    private const int Scale = 2;

    private const int JpegQuality = 88;

    /// <exception cref="InvalidOperationException">Si no se pudo escribir el archivo.</exception>
    public void Export(FlowDocument document, string filePath)
    {
        ArgumentNullException.ThrowIfNull(document);

        var pages = RenderPages(document);

        if (pages.Count == 0)
        {
            throw new InvalidOperationException("El documento quedó vacío y no hay nada que guardar.");
        }

        try
        {
            using var stream = File.Create(filePath);
            Write(stream, pages);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"No se pudo guardar el PDF: {ex.Message}\n\n" +
                "Si el archivo ya está abierto en otro programa, cerralo y probá de nuevo.",
                ex);
        }
    }

    /// <summary>
    /// Pregunta dónde guardar y escribe el PDF. Devuelve la ruta, o null si se canceló.
    /// </summary>
    /// <remarks>
    /// El diálogo se abre desde acá y no desde la pantalla por el mismo criterio que
    /// <see cref="QuoteDocumentService.Print"/>, que también muestra el cuadro de Windows:
    /// quien llama pide «guardá esto» y no se ocupa de cómo se elige el destino.
    /// </remarks>
    public string? SaveAs(FlowDocument document, string suggestedFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Guardar el presupuesto en PDF",
            Filter = "Documento PDF|*.pdf",
            DefaultExt = ".pdf",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = suggestedFileName,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        Export(document, dialog.FileName);
        return dialog.FileName;
    }

    /// <summary>
    /// Abre el archivo con el lector de PDF del sistema, para revisarlo y mandarlo.
    /// </summary>
    /// <remarks>
    /// No tira nunca: el PDF ya está escrito, que es lo que importaba. Si la máquina no
    /// tiene con qué abrirlo, el usuario lo busca en la carpeta que eligió.
    /// </remarks>
    public static void OpenInDefaultApp(string filePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            LogService.Warning("PdfExportService", $"No se pudo abrir «{filePath}»: {ex.Message}");
        }
    }

    /// <summary>
    /// Nombre sugerido para el archivo, ya sin los caracteres que Windows no acepta.
    /// </summary>
    public static string SuggestFileName(string documentName, int quoteId, string clientName)
    {
        var name = $"{documentName} {quoteId:0000} - {clientName}".Trim();
        var clean = new string(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c).ToArray());

        // Windows tampoco quiere nombres terminados en punto o espacio.
        clean = clean.TrimEnd('.', ' ');

        return clean.Length == 0 ? $"{documentName}.pdf" : $"{clean}.pdf";
    }

    // --- Dibujo ---------------------------------------------------------------

    private sealed record RenderedPage(byte[] Jpeg, int Width, int Height);

    private static List<RenderedPage> RenderPages(FlowDocument document)
    {
        QuoteDocumentService.LayOut(document, PageWidthPx, PageHeightPx);

        var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator;
        paginator.ComputePageCount();

        var pages = new List<RenderedPage>(paginator.PageCount);

        for (var i = 0; i < paginator.PageCount; i++)
        {
            using var page = paginator.GetPage(i);
            pages.Add(RenderPage(page.Visual));
        }

        return pages;
    }

    private static RenderedPage RenderPage(Visual visual)
    {
        var width = (int)(PageWidthPx * Scale);
        var height = (int)(PageHeightPx * Scale);

        var bitmap = new RenderTargetBitmap(width, height, 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);

        // Fondo blanco primero: el documento es transparente y sin esto la hoja sale sobre
        // negro, que no se parece en nada a lo que sale por la impresora.
        var background = new DrawingVisual();
        using (var context = background.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, PageWidthPx, PageHeightPx));
        }

        bitmap.Render(background);
        bitmap.Render(visual);

        var encoder = new JpegBitmapEncoder { QualityLevel = JpegQuality };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var buffer = new MemoryStream();
        encoder.Save(buffer);

        return new RenderedPage(buffer.ToArray(), width, height);
    }

    // --- Escritura del PDF ----------------------------------------------------

    /// <remarks>
    /// Estructura mínima de un PDF 1.4: el catálogo, el árbol de páginas, y por cada hoja
    /// una página, su flujo de contenido y la imagen. Al final la tabla de referencias
    /// cruzadas, que es una lista de en qué byte arranca cada objeto — por eso se va
    /// anotando la posición a medida que se escribe.
    /// </remarks>
    private static void Write(Stream stream, IReadOnlyList<RenderedPage> pages)
    {
        // 1 catálogo + 1 árbol de páginas + 3 objetos por hoja.
        var total = 2 + (pages.Count * 3);
        var offsets = new long[total + 1];

        Ascii(stream, "%PDF-1.4\n");

        // Comentario con bytes altos: le avisa a las herramientas que el archivo es binario
        // y que no lo traten como texto. Es lo que recomienda la especificación.
        stream.Write([0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A]);

        offsets[1] = stream.Position;
        Ascii(stream, "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var kids = string.Join(" ", Enumerable.Range(0, pages.Count).Select(i => $"{PageObject(i)} 0 R"));
        offsets[2] = stream.Position;
        Ascii(stream, $"2 0 obj\n<< /Type /Pages /Kids [{kids}] /Count {pages.Count} >>\nendobj\n");

        for (var i = 0; i < pages.Count; i++)
        {
            var page = pages[i];
            var pageId = PageObject(i);
            var contentId = pageId + 1;
            var imageId = pageId + 2;

            offsets[pageId] = stream.Position;
            Ascii(stream,
                $"{pageId} 0 obj\n" +
                $"<< /Type /Page /Parent 2 0 R " +
                $"/MediaBox [0 0 {Number(PageWidthPt)} {Number(PageHeightPt)}] " +
                $"/Resources << /XObject << /Im0 {imageId} 0 R >> >> " +
                $"/Contents {contentId} 0 R >>\n" +
                "endobj\n");

            // Estira la imagen a la hoja entera. El "cm" toma la unidad cuadrada en la que
            // se dibuja una imagen y la lleva al tamaño de la página.
            var content = $"q {Number(PageWidthPt)} 0 0 {Number(PageHeightPt)} 0 0 cm /Im0 Do Q\n";

            offsets[contentId] = stream.Position;
            Ascii(stream, $"{contentId} 0 obj\n<< /Length {content.Length} >>\nstream\n{content}endstream\nendobj\n");

            offsets[imageId] = stream.Position;
            Ascii(stream,
                $"{imageId} 0 obj\n" +
                $"<< /Type /XObject /Subtype /Image /Width {page.Width} /Height {page.Height} " +
                $"/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode " +
                $"/Length {page.Jpeg.Length} >>\nstream\n");

            stream.Write(page.Jpeg);
            Ascii(stream, "\nendstream\nendobj\n");
        }

        var xref = stream.Position;

        Ascii(stream, $"xref\n0 {total + 1}\n");
        Ascii(stream, "0000000000 65535 f \n");

        for (var id = 1; id <= total; id++)
        {
            // Cada entrada mide exactamente 20 bytes; los lectores cuentan posiciones fijas.
            Ascii(stream, $"{offsets[id].ToString("0000000000", CultureInfo.InvariantCulture)} 00000 n \n");
        }

        Ascii(stream,
            $"trailer\n<< /Size {total + 1} /Root 1 0 R >>\nstartxref\n" +
            $"{xref.ToString(CultureInfo.InvariantCulture)}\n%%EOF\n");
    }

    private static int PageObject(int index) => 3 + (index * 3);

    /// <summary>
    /// El PDF quiere el punto como separador decimal. La app corre en es-AR, así que sin
    /// esto los tamaños de página saldrían con coma y el archivo no abriría.
    /// </summary>
    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static void Ascii(Stream stream, string text) => stream.Write(Encoding.ASCII.GetBytes(text));
}
