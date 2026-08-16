using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MetroCarpinteria.App.Data.Entities;
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

        // Dos operarios además del jefe, para que la vista previa muestre el desglose por
        // persona de la hoja de costos, que es lo que hay que mirar a ojo.
        AddQuotedWorkers(fixture.QuoteId);

        var quote = AppHost.QuoteService.GetDetail(fixture.QuoteId)
            ?? throw new InvalidOperationException("No se encontró el presupuesto sembrado.");

        Console.WriteLine($"Presupuesto: «{quote.Title}» para {quote.ClientName}");
        Console.WriteLine($"  Calculado : {quote.Breakdown?.FinalPriceDisplay}");
        Console.WriteLine($"  Con IVA y descuento: {quote.Commercial?.TotalDisplay}");
        Console.WriteLine($"  Cobrado   : {quote.PaidTotalDisplay}  ·  Saldo: {quote.BalanceDisplay}");
        Console.WriteLine();

        Save(
            service.BuildClientQuote(quote),
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
            service.BuildClientQuote(plain),
            outputDirectory,
            "presupuesto-simple");

        RenderPhotoExamples(outputDirectory, fixture, service);
        RenderLoadedQuote(outputDirectory, fixture, service);

        Console.WriteLine();
        Console.WriteLine($"Documentos generados en {outputDirectory}");

        Dispatcher.CurrentDispatcher.InvokeShutdown();
    }

    /// <summary>Carga dos operarios de Personal en el presupuesto y recalcula el precio.</summary>
    private static void AddQuotedWorkers(int quoteId)
    {
        var cristian = AppHost.EmployeeService.Create("Cristian Gómez", null, "Oficial carpintero", 25000m);
        var diego = AppHost.EmployeeService.Create("Diego Ruiz", null, "Ayudante", 22000m);

        AppHost.QuoteService.AddLaborLine(quoteId, cristian.Id, cristian.FullName, 5m, 25000m);
        AppHost.QuoteService.AddLaborLine(quoteId, diego.Id, diego.FullName, 3m, 22000m);

        var detail = AppHost.QuoteService.GetDetail(quoteId)!;

        AppHost.QuoteService.SaveCalculation(
            quoteId,
            detail.CalculationMaterials,
            detail.EstimatedDays ?? 1m,
            detail.DailyRate ?? 0m,
            detail.Rates ?? BudgetRates.Defaults());
    }

    private static void RenderPhotoExamples(
        string outputDirectory,
        TestFixture fixture,
        QuoteDocumentService service)
    {
        var samples = Path.Combine(outputDirectory, "_samples");
        var photos = new[]
        {
            SampleJpeg.Write(samples, "cocina.jpg", Color.FromRgb(107, 68, 35), "Cocina similar"),
            SampleJpeg.Write(samples, "placard.jpg", Color.FromRgb(196, 165, 116), "Placard blanco"),
            SampleJpeg.Write(samples, "mesa.jpg", Color.FromRgb(61, 41, 20), "Mesa de roble"),
            SampleJpeg.Write(samples, "estante.jpg", Color.FromRgb(122, 101, 85), "Estante a medida")
        };

        string[] captions =
        [
            "Cocina similar en melamina blanca",
            "Placard de dos cuerpos",
            "Mesa de roble macizo",
            "Estante a medida para el living"
        ];

        SaveQuoteWithPhotos(
            fixture,
            service,
            outputDirectory,
            "presupuesto-sin-fotos",
            "Control sin fotos",
            photos.Take(0),
            captions);

        SaveQuoteWithPhotos(
            fixture,
            service,
            outputDirectory,
            "presupuesto-1-foto",
            "Placard con una referencia",
            photos.Take(1),
            captions);

        SaveQuoteWithPhotos(
            fixture,
            service,
            outputDirectory,
            "presupuesto-2-fotos",
            "Cocina con dos referencias",
            photos.Take(2),
            captions);

        var fourId = SaveQuoteWithPhotos(
            fixture,
            service,
            outputDirectory,
            "presupuesto-4-fotos",
            "Living a medida",
            photos,
            captions);

        var withFour = AppHost.QuoteService.GetDetail(fourId)!;
        Save(service.BuildCostSheet(withFour), outputDirectory, "hoja-de-costos-con-4-fotos");
    }

    /// <summary>
    /// El caso que pone a prueba el ajuste a la A4: tres trabajos adjuntos, fotos en varios
    /// de ellos y una seña. Se guarda con el tilde apagado y prendido, que es la diferencia
    /// que hay que poder mirar de un vistazo.
    /// </summary>
    private static void RenderLoadedQuote(
        string outputDirectory,
        TestFixture fixture,
        QuoteDocumentService service)
    {
        var samples = Path.Combine(outputDirectory, "_samples");
        var photo = SampleJpeg.Write(samples, "cargado.jpg", Color.FromRgb(107, 68, 35), "Referencia");

        var parentId = AppHost.QuoteService.CreateQuote(
            "Cocina completa", "Cliente con varios trabajos", "Bajo mesada y alacenas en melamina").Id;

        AppHost.QuoteService.AddInventoryLine(parentId, fixture.BoardProductId, 12m, 1200m);
        AppHost.QuoteService.SaveCalculation(parentId, 14400m, 4m, 30000m, BudgetRates.Defaults());
        AppHost.QuoteImageService.AddFromFile(parentId, photo, "Cocina de referencia");

        string[] titles = ["Placard del dormitorio", "Mueble del baño", "Biblioteca del living"];
        string[] notes =
        [
            "Dos cuerpos con puertas corredizas",
            "Bajo mesada con puerta y estante",
            "Cinco estantes de 1,80 m"
        ];

        for (var i = 0; i < titles.Length; i++)
        {
            var attachedId = AppHost.QuoteService.CreateQuote(
                titles[i], "Cliente con varios trabajos", notes[i]).Id;

            AppHost.QuoteService.AddInventoryLine(attachedId, fixture.BoardProductId, 5m, 1200m);
            AppHost.QuoteService.SaveCalculation(attachedId, 6000m, 2m, 30000m, BudgetRates.Defaults());
            AppHost.QuoteImageService.AddFromFile(attachedId, photo, $"Referencia de {titles[i].ToLowerInvariant()}");
            AppHost.QuoteService.AttachQuote(parentId, attachedId);
        }

        // Por transferencia y no en efectivo: cobrar en efectivo exige una caja abierta, y
        // este runner sólo dibuja papeles.
        AppHost.PaymentService.RegisterPayment(
            parentId, PaymentKind.Deposit, 60000m, PaymentMethod.Transfer, "Seña de la cocina");

        // Y una seña sobre uno de los adjuntos, que es lo que el saldo tiene que restar
        // cuando el tilde está prendido.
        AppHost.PaymentService.RegisterPayment(
            AppHost.QuoteService.GetDetail(parentId)!.Attachments[0].ProjectId,
            PaymentKind.Deposit,
            25000m,
            PaymentMethod.Transfer,
            "Seña del placard");

        var apart = AppHost.QuoteService.GetDetail(parentId)!;
        Console.WriteLine(
            $"Presupuesto cargado: «{apart.Title}» — {apart.Attachments.Count} adjunto(s), " +
            $"{apart.Images.Count + apart.Attachments.Sum(a => a.Images.Count)} foto(s)");

        Save(service.BuildClientQuote(apart), outputDirectory, "presupuesto-cargado-separado");

        AppHost.QuoteService.SaveIncludeAttachmentsInTotal(parentId, include: true);
        var together = AppHost.QuoteService.GetDetail(parentId)!;

        Console.WriteLine(
            $"  Con el tilde prendido: TOTAL {together.PrintedTotalDisplay} · " +
            $"saldo {together.PrintedBalanceDisplay}");

        Save(service.BuildClientQuote(together), outputDirectory, "presupuesto-cargado-sumado");
    }

    private static int SaveQuoteWithPhotos(
        TestFixture fixture,
        QuoteDocumentService service,
        string outputDirectory,
        string name,
        string title,
        IEnumerable<string> photoPaths,
        IReadOnlyList<string> captions)
    {
        var id = AppHost.QuoteService.CreateQuote(title, "Cliente de muestra", "Trabajo ilustrado").Id;
        AppHost.QuoteService.AddInventoryLine(id, fixture.BoardProductId, 6m, 1200m);
        AppHost.QuoteService.SaveCalculation(id, 7200m, 2m, 30000m, BudgetRates.Defaults());

        var index = 0;
        foreach (var path in photoPaths)
        {
            AppHost.QuoteImageService.AddFromFile(id, path, captions[index]);
            index++;
        }

        var detail = AppHost.QuoteService.GetDetail(id)!;
        Console.WriteLine($"Presupuesto ilustrado: «{detail.Title}» — {detail.Images.Count} foto(s)");
        Save(service.BuildClientQuote(detail), outputDirectory, name);
        return id;
    }

    /// <remarks>
    /// El dibujo de las hojas lo hace <see cref="QuoteDocumentService.RenderPages"/>, que es
    /// el mismo que usa la vista previa de la app. Acá quedan sólo la medición del margen y
    /// la escritura de los PNG: si el papel se ve mal en uno de los dos lados, se ve mal en
    /// los dos, que es exactamente lo que se quiere de una vista previa.
    /// </remarks>
    private static void Save(FlowDocument document, string directory, string name)
    {
        var needed = MeasureRequiredPageHeight(document);
        var pages = QuoteDocumentService.RenderPages(document);

        var slack = PageHeight - needed;
        var fit = slack >= 0 ? $"entra con {slack:F0} px de sobra" : $"se pasa por {-slack:F0} px";

        Console.WriteLine(
            $"  {name}: {pages.Count} hoja(s) — necesita {needed:F0} px de alto, A4 tiene {PageHeight:F0}: {fit}");

        for (var i = 0; i < pages.Count; i++)
        {
            var suffix = pages.Count > 1 ? $"-hoja{i + 1}" : string.Empty;
            var path = Path.Combine(directory, $"{name}{suffix}.png");

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(pages[i]));

            using var stream = File.Create(path);
            encoder.Save(stream);
        }
    }
}
