using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MetroCarpinteria.App.Models;
using MetroCarpinteria.App.Services;
using WpfApp = MetroCarpinteria.App.App;

namespace MetroCarpinteria.SmokeTest;

/// <summary>
/// Dibuja a imagen los documentos que se le entregan al cliente y los que quedan en el
/// taller, hoja por hoja.
/// </summary>
/// <remarks>
/// Los tests verifican qué <b>dice</b> cada documento —que el del cliente no muestre la
/// ganancia, que el total cierre—, pero nadie puede revisar cómo <b>queda</b> sin verlo.
/// Y es el único papel que sale de la app y termina en la mano de otra persona: un renglón
/// cortado o una tabla partida en dos hojas no se arregla después de entregarlo.
/// <para>Se invoca con <c>--documents &lt;carpeta&gt;</c>.</para>
/// </remarks>
internal static class DocumentRunner
{
    // A4 a 96 ppp, que es la resolución en la que WPF mide.
    private const double PageWidth = 794;
    private const double PageHeight = 1123;

    /// <summary>El mismo que usa <see cref="QuoteDocumentService.LayOut"/>.</summary>
    private const double VerticalMargin = 44;

    /// <summary>
    /// El alto de hoja más chico con el que el documento todavía entra de una sola vez.
    /// </summary>
    /// <remarks>
    /// Se busca por bisección en vez de medir el visual: <c>ContentBox</c> devuelve el área
    /// de la hoja y los límites del visual incluyen el lienzo entero, así que ninguno de
    /// los dos dice cuánto ocupa el contenido. Lo que importa igual es esto: a partir de
    /// qué alto deja de partirse. Con el número exacto se puede decidir cuánto recortar,
    /// en vez de sacar márgenes a ojo hasta que entre.
    /// </remarks>
    private static double MeasureRequiredPageHeight(FlowDocument document)
    {
        var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator;
        double tooShort = 200;
        double enough = 20000;

        while (enough - tooShort > 2)
        {
            var middle = (tooShort + enough) / 2;

            QuoteDocumentService.LayOut(document, PageWidth, middle);
            paginator.ComputePageCount();

            if (paginator.PageCount <= 1)
            {
                enough = middle;
            }
            else
            {
                tooShort = middle;
            }
        }

        return enough;
    }

    public static int Run(string outputDirectory)
    {
        var error = (Exception?)null;

        var thread = new Thread(() =>
        {
            try
            {
                Render(outputDirectory);
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
            Console.WriteLine($"Falló la generación de los documentos: {error}");
            return 1;
        }

        return 0;
    }

    private static void Render(string outputDirectory)
    {
        if (Application.Current is null)
        {
            var app = new WpfApp();
            app.InitializeComponent();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }

        Directory.CreateDirectory(outputDirectory);

        using var fixture = TestFixture.CreateSeeded();
        var service = AppHost.QuoteDocumentService;

        var quote = AppHost.QuoteService.GetDetail(fixture.QuoteId)
            ?? throw new InvalidOperationException("No se encontró el presupuesto sembrado.");

        Console.WriteLine($"Presupuesto: «{quote.Title}» para {quote.ClientName}");
        Console.WriteLine($"  Calculado : {quote.Breakdown?.FinalPriceDisplay}");
        Console.WriteLine($"  Con IVA y descuento: {quote.Commercial?.TotalDisplay}");
        Console.WriteLine($"  Cobrado   : {quote.PaidTotalDisplay}  ·  Saldo: {quote.BalanceDisplay}");
        Console.WriteLine();

        Save(
            service.BuildClientQuote(quote, includeMaterialDetail: true),
            outputDirectory,
            "presupuesto-cliente");

        Save(
            service.BuildCostSheet(quote),
            outputDirectory,
            "hoja-de-costos");

        // Y el caso simple, que es el más común en el taller: sin IVA ni descuento.
        var plainId = AppHost.QuoteService.CreateQuote(
            "Mesa de pino", "Vecino del taller", "Dos cajones y estante").Id;

        AppHost.QuoteService.AddInventoryLine(plainId, fixture.BoardProductId, 6m, 1200m);
        AppHost.QuoteService.SaveCalculation(plainId, 7200m, 2m, 30000m, BudgetRates.Defaults());

        var plain = AppHost.QuoteService.GetDetail(plainId)!;
        Console.WriteLine($"Presupuesto simple: «{plain.Title}» — {plain.BudgetDisplay}");

        Save(
            service.BuildClientQuote(plain, includeMaterialDetail: true),
            outputDirectory,
            "presupuesto-simple");

        Console.WriteLine();
        Console.WriteLine($"Documentos generados en {outputDirectory}");

        Dispatcher.CurrentDispatcher.InvokeShutdown();
    }

    private static void Save(FlowDocument document, string directory, string name)
    {
        var needed = MeasureRequiredPageHeight(document);

        QuoteDocumentService.LayOut(document, PageWidth, PageHeight);

        var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator;
        paginator.ComputePageCount();

        var slack = PageHeight - needed;
        var fit = slack >= 0 ? $"entra con {slack:F0} px de sobra" : $"se pasa por {-slack:F0} px";

        Console.WriteLine(
            $"  {name}: {paginator.PageCount} hoja(s) — necesita {needed:F0} px de alto, A4 tiene {PageHeight:F0}: {fit}");

        for (var i = 0; i < paginator.PageCount; i++)
        {
            using var page = paginator.GetPage(i);

            var bitmap = new RenderTargetBitmap(
                (int)PageWidth, (int)PageHeight, 96, 96, PixelFormats.Pbgra32);

            // Fondo blanco primero: el documento es transparente y sin esto la hoja sale
            // sobre negro, que no se parece en nada a lo que sale por la impresora.
            var background = new DrawingVisual();
            using (var context = background.RenderOpen())
            {
                context.DrawRectangle(Brushes.White, null, new Rect(0, 0, PageWidth, PageHeight));
            }

            bitmap.Render(background);
            bitmap.Render(page.Visual);

            var suffix = paginator.PageCount > 1 ? $"-hoja{i + 1}" : string.Empty;
            var path = Path.Combine(directory, $"{name}{suffix}.png");

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            using var stream = File.Create(path);
            encoder.Save(stream);
        }
    }
}
