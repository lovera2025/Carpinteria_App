using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MetroCarpinteria.App.Helpers;
using MetroCarpinteria.App.Models;

namespace MetroCarpinteria.App.Services;

/// <summary>
/// Arma los documentos imprimibles del presupuesto con <see cref="FlowDocument"/> y los
/// manda al diálogo de impresión de Windows, donde "Microsoft Print to PDF" produce el
/// archivo. Sin paquetes externos: no infla el ejecutable y además imprime en papel.
/// </summary>
public sealed class QuoteDocumentService
{
    // Muestreados del propio logo y de la paleta de marca de Resources/Colors.xaml.
    private static readonly Brush BandBrush = Frozen("#F4E0BF");
    private static readonly Brush BrownBrush = Frozen("#6B4423");
    private static readonly Brush GoldBrush = Frozen("#C4A574");
    private static readonly Brush TextBrush = Frozen("#3D2914");
    private static readonly Brush MutedBrush = Frozen("#7A6555");
    private static readonly Brush CreamBrush = Frozen("#FAF4E8");
    private static readonly Brush OnBrownBrush = Frozen("#F5F0E8");

    private const string BrandName = "METRO CARPINTERÍA";
    private const string BrandTagline = "Diseños a medida · 3777-412207";

    /// <summary>
    /// Ancho útil del documento: A4 a 96 ppp menos los márgenes.
    /// </summary>
    /// <remarks>
    /// Las columnas de las tablas se declaran en píxeles y no con <c>Star</c>: dentro de
    /// un <see cref="FlowDocument"/> el ancho proporcional descoloca la fila —la estira a
    /// lo alto de la página y hace desaparecer el contenido de la última celda—.
    /// </remarks>
    public const double ContentWidth = 694;
    private const double VerticalMargin = 44;

    /// <summary>Una A4 a 96 ppp, que es contra lo que se decide si el documento entra.</summary>
    private const double A4Width = 794;
    private const double A4Height = 1123;

    /// <summary>
    /// Cuánto aire lleva el papel del cliente. Es la perilla que se aprieta cuando el
    /// presupuesto no entra en una A4.
    /// </summary>
    /// <remarks>
    /// <para>
    /// El ajuste ya existía a mano y para un solo caso —la banda de cabecera compacta de la
    /// hoja de costos, que baja el logo justamente porque «cuesta los píxeles que deciden si
    /// el documento entra en una A4»—. Esto es lo mismo, generalizado y elegido midiendo.
    /// </para>
    /// <para>
    /// <see cref="Roomy"/> es exactamente el papel de siempre: un presupuesto normal tiene
    /// que salir idéntico a como salía. Los otros niveles sólo aparecen cuando hace falta.
    /// </para>
    /// </remarks>
    private sealed record Density(
        string Name,
        double FontSize,
        double LogoSize,
        double BrandSize,
        double PageMargin,
        double PhotoMaxHeight,
        double SectionSpacing,
        int ObservationLines)
    {
        public static readonly Density Roomy = new("Holgada", 12, 68, 21, VerticalMargin, 148, 8, 3);
        public static readonly Density Tight = new("Ajustada", 11.5, 56, 19, 36, 122, 6, 3);
        public static readonly Density Compact = new("Compacta", 10.5, 46, 17, 30, 102, 4, 2);

        /// <summary>
        /// El piso. Más apretado que esto el papel deja de leerse, y una hoja ilegible es
        /// peor que dos legibles: por eso el ajuste se rinde acá en vez de seguir achicando.
        /// </summary>
        public static readonly Density Minimum = new("Mínima", 9.5, 38, 15, 24, 88, 3, 1);

        /// <summary>De más holgado a más apretado. Se prueba en este orden.</summary>
        public static readonly IReadOnlyList<Density> Ladder = [Roomy, Tight, Compact, Minimum];
    }

    /// <summary>
    /// Las fotos ya decodificadas, para no volver a leer el disco en cada intento.
    /// </summary>
    /// <remarks>
    /// Sin esto, probar cuatro densidades sobre un presupuesto con ocho fotos son treinta y
    /// dos lecturas y decodificaciones para imprimir una sola hoja.
    /// </remarks>
    private sealed class ImageCache
    {
        private readonly Dictionary<string, BitmapImage?> _byPath = new(StringComparer.OrdinalIgnoreCase);

        public BitmapImage? Load(string path)
        {
            if (_byPath.TryGetValue(path, out var cached))
            {
                return cached;
            }

            var bitmap = LoadQuoteImage(path);
            _byPath[path] = bitmap;
            return bitmap;
        }
    }

    /// <summary>
    /// Documento para entregarle al cliente: el trabajo, las fotos y un solo número.
    /// </summary>
    /// <remarks>
    /// No lleva desglose de ningún tipo —ni el resumen de materiales y mano de obra, ni la
    /// lista de materiales—: el cliente ve el TOTAL y nada más. Lo que explica qué está
    /// comprando es la descripción del trabajo, y por eso acá pesa más que en cualquier
    /// otro lado.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Si el presupuesto no tiene precio o desglose. Los botones ya no lo permiten, pero
    /// esto se llama también desde Proyectos y desde los tests: un presupuesto impreso con
    /// el TOTAL en un guión llega al cliente y no hay forma de arreglarlo después.
    /// </exception>
    public FlowDocument BuildClientQuote(QuoteDetail quote)
    {
        ArgumentNullException.ThrowIfNull(quote);

        if (quote.Budget is null or <= 0)
        {
            throw new InvalidOperationException(
                "Este presupuesto todavía no tiene un precio final: falta calcularlo antes de imprimirlo.");
        }

        if (quote.Breakdown is null)
        {
            throw new InvalidOperationException(
                "Este presupuesto no tiene un cálculo guardado, así que el documento saldría sin resumen.");
        }

        // Las fotos se decodifican una sola vez y se reusan en cada intento de densidad.
        var images = new ImageCache();
        FlowDocument? tightest = null;

        foreach (var density in Density.Ladder)
        {
            tightest = BuildClientQuote(quote, density, images);

            if (CountA4Pages(tightest) == 1)
            {
                return tightest;
            }
        }

        // Ni en el nivel más apretado entra: seis adjuntos con ocho fotos no caben en una
        // A4 por más que se achique. Se entrega en dos hojas antes que ilegible en una, y
        // el cierre viaja junto para que el número no quede huérfano en la primera.
        return tightest!;
    }

    /// <summary>Arma el papel del cliente con un nivel de densidad concreto.</summary>
    private static FlowDocument BuildClientQuote(QuoteDetail quote, Density density, ImageCache images)
    {
        var document = CreateDocument(density);

        document.Blocks.Add(BuildHeaderBand(density));
        document.Blocks.Add(BuildTitleRow("PRESUPUESTO", quote));
        document.Blocks.Add(BuildClientBlock(quote));

        AddWorksSection(document, quote, density);
        AddReferenceSection(document, quote.PrintableImages, "Referencias", density, images);
        AddReferenceSection(
            document,
            quote.Attachments.SelectMany(a => a.PrintableImages).ToList(),
            "Fotos de los adjuntos",
            density,
            images);

        // El pie comercial solo aparece si se pactó algo. Un descuento que el cliente
        // negoció tiene que figurar en el papel, y el IVA discriminado es una condición
        // comercial y no un costo interno: por eso sobreviven al recorte.
        if (quote.Commercial is { IsPlain: false } commercial)
        {
            document.Blocks.Add(BuildSummaryTable(commercial.Lines.Where(l => !l.IsTotal).ToList()));
        }

        // De acá al final es el cierre, y va todo junto: el cliente termina de leer en el
        // número, y debajo le queda el lugar para anotar lo que se acuerde de palabra.
        // Las cuentas del papel, no las de este presupuesto suelto: con el tilde prendido
        // el total suma los adjuntos y el saldo resta también lo cobrado de ellos.
        document.Blocks.Add(BuildTotalBlock(quote.PrintedTotalDisplay, density));

        if (quote.HasCommitmentNote)
        {
            document.Blocks.Add(BuildCommitmentNote(quote));
        }

        // Si el cliente ya adelantó plata, lo que necesita ver es cuánto falta.
        if (quote.HasPrintedPayments)
        {
            document.Blocks.Add(BuildPaidRow(quote.PrintedPaidTotalDisplay, density));
            document.Blocks.Add(BuildBalanceRow(quote.PrintedBalanceDisplay, density));
        }

        document.Blocks.Add(BuildObservationsBlock(density));
        document.Blocks.Add(BuildValidityNote(quote));
        document.Blocks.Add(BuildFooter(density));

        KeepClosingParagraphsTogether(document, CountClosingBlocks(quote));

        return document;
    }

    /// <summary>Cuántos bloques del final tienen que viajar juntos.</summary>
    private static int CountClosingBlocks(QuoteDetail quote)
    {
        // Total, observaciones, vigencia y pie, siempre.
        var closing = 4;

        if (quote.HasCommitmentNote)
        {
            closing++;
        }

        if (quote.HasPrintedPayments)
        {
            closing += 2;
        }

        return closing;
    }

    /// <summary>En cuántas hojas A4 entra el documento tal como está.</summary>
    private static int CountA4Pages(FlowDocument document)
    {
        LayOut(document, A4Width, A4Height);

        var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator;
        paginator.ComputePageCount();
        return paginator.PageCount;
    }

    /// <summary>
    /// Hoja de costos para el taller. Acá sí van desperdicio, desgaste, gastos y
    /// ganancia: es el documento que <b>no</b> se le da al cliente.
    /// </summary>
    public FlowDocument BuildCostSheet(QuoteDetail quote)
    {
        var document = CreateDocument();

        document.Blocks.Add(BuildHeaderBand(compact: true));
        document.Blocks.Add(BuildTitleRow("HOJA DE COSTOS", quote));

        document.Blocks.Add(new Paragraph(new Run("Uso interno del taller — no entregar al cliente."))
        {
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = OnBrownBrush,
            Background = BrownBrush,
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 0, 0, 8)
        });

        document.Blocks.Add(BuildClientBlock(quote));

        if (quote.Lines.Count > 0)
        {
            document.Blocks.Add(SectionTitle("Materiales"));
            document.Blocks.Add(BuildMaterialsTable(quote));
        }

        document.Blocks.Add(SectionTitle("Desglose del cálculo"));

        if (quote.Breakdown is not null)
        {
            document.Blocks.Add(BuildSummaryTable(quote.Breakdown.CompactLines.Where(l => !l.IsTotal).ToList()));
        }
        else
        {
            document.Blocks.Add(Muted("Este presupuesto todavía no tiene un cálculo guardado."));
        }

        // Con un solo renglón —el jefe— no hay nada que desglosar: el desglose de arriba
        // ya lo dice todo.
        if (quote.Breakdown is { HasWorkers: true } withWorkers)
        {
            document.Blocks.Add(SectionTitle("Mano de obra por persona"));
            document.Blocks.Add(BuildLaborTable(withWorkers));
        }

        if (quote.Commercial is { IsPlain: false } commercial)
        {
            document.Blocks.Add(SectionTitle("Condiciones comerciales"));
            document.Blocks.Add(BuildSummaryTable(commercial.Lines.Where(l => !l.IsTotal).ToList()));
        }

        // La hoja de costos es papel interno de este trabajo: acá el total es el suyo, no
        // el del conjunto que se le imprime al cliente.
        document.Blocks.Add(BuildTotalBlock(quote.BudgetDisplay, Density.Roomy));

        if (quote.HasPayments)
        {
            document.Blocks.Add(BuildPaidRow(quote.PaidTotalDisplay, Density.Roomy));
            document.Blocks.Add(BuildBalanceRow(quote.BalanceDisplay, Density.Roomy));
        }

        document.Blocks.Add(BuildEffectiveMarginBlock(quote));
        document.Blocks.Add(BuildFooter());

        KeepClosingParagraphsTogether(document, quote.HasPayments ? 5 : 3);

        return document;
    }

    /// <summary>
    /// Recibo de un cobro: seña, pago a cuenta o saldo. Mismo estilo que el presupuesto,
    /// sin desglose interno.
    /// </summary>
    public FlowDocument BuildReceipt(QuoteDetail quote, ProjectPaymentItem payment)
    {
        ArgumentNullException.ThrowIfNull(quote);
        ArgumentNullException.ThrowIfNull(payment);

        var document = CreateDocument();

        document.Blocks.Add(BuildHeaderBand());
        document.Blocks.Add(BuildTitleRow("RECIBO", payment.CreatedAtLocal, validUntilLocal: null));
        document.Blocks.Add(BuildClientBlock(quote));

        var concept = new Paragraph
        {
            Background = CreamBrush,
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 8)
        };
        concept.Inlines.Add(Label("Concepto: "));
        concept.Inlines.Add(new Run(payment.KindLabel) { FontWeight = FontWeights.SemiBold });
        concept.Inlines.Add(new LineBreak());
        concept.Inlines.Add(Label("Medio: "));
        concept.Inlines.Add(new Run(payment.MethodLabel) { FontWeight = FontWeights.SemiBold });

        if (!string.IsNullOrWhiteSpace(payment.Notes))
        {
            concept.Inlines.Add(new LineBreak());
            concept.Inlines.Add(Label("Notas: "));
            concept.Inlines.Add(new Run(payment.Notes));
        }

        document.Blocks.Add(concept);
        document.Blocks.Add(BuildReceivedBlock(payment.AmountDisplay));

        // El saldo de este presupuesto, que es el que acompaña al cobro que se acaba de
        // registrar. El recibo no lista los adjuntos, así que tampoco puede mostrar un
        // saldo del conjunto sin decir de dónde sale.
        document.Blocks.Add(BuildPaidRow(quote.PaidTotalDisplay, Density.Roomy));
        document.Blocks.Add(BuildBalanceRow(quote.BalanceDisplay, Density.Roomy));

        document.Blocks.Add(BuildObservationsBlock(Density.Roomy));
        document.Blocks.Add(BuildFooter());

        KeepClosingParagraphsTogether(document, 4);
        return document;
    }

    private static Block BuildReceivedBlock(string amount)
    {
        var table = NoBorderTable();
        table.Columns.Add(Column(ContentWidth - 210, BrownBrush));
        table.Columns.Add(Column(210, BrownBrush));

        var row = new TableRow();

        row.Cells.Add(new TableCell(new Paragraph(new Run("RECIBIDO")
        {
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = OnBrownBrush
        })
        { Margin = new Thickness(0) })
        {
            Background = BrownBrush,
            Padding = new Thickness(16, 13, 8, 13)
        });

        row.Cells.Add(new TableCell(new Paragraph(new Run(amount)
        {
            FontSize = 19,
            FontWeight = FontWeights.Bold,
            Foreground = OnBrownBrush
        })
        { Margin = new Thickness(0) })
        {
            Background = BrownBrush,
            Padding = new Thickness(8, 10, 16, 10),
            TextAlignment = TextAlignment.Right
        });

        var group = new TableRowGroup();
        group.Rows.Add(row);
        table.RowGroups.Add(group);
        table.Margin = new Thickness(0, 4, 0, 8);

        return table;
    }

    /// <summary>
    /// Cuánta ganancia queda después de resignar el descuento.
    /// </summary>
    /// <remarks>
    /// Es el número que hace útil la hoja interna. Un descuento del 15% sobre un margen del
    /// 30% deja 12%, y eso hay que verlo antes de dar la mano, no al cerrar el mes. Como es
    /// exactamente lo que no puede salir del taller, va solo en este documento.
    /// </remarks>
    private static Block BuildEffectiveMarginBlock(QuoteDetail quote)
    {
        if (quote.Breakdown is null || quote.Commercial is null)
        {
            return Muted("Sin cálculo guardado no se puede medir el margen.");
        }

        var margin = CommercialTermsService.EffectiveMargin(quote.Breakdown, quote.Commercial);

        if (margin is null)
        {
            return Muted("Sin base gravada no se puede medir el margen.");
        }

        var paragraph = new Paragraph
        {
            Background = margin.Value < 0 ? BandBrush : CreamBrush,
            Padding = new Thickness(14, 8, 14, 8),
            Margin = new Thickness(0, 6, 0, 0)
        };

        paragraph.Inlines.Add(new Run("Margen efectivo:  ")
        {
            FontSize = 12,
            Foreground = MutedBrush
        });

        paragraph.Inlines.Add(new Run(AppCulture.Percent(margin.Value))
        {
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = TextBrush
        });

        paragraph.Inlines.Add(new Run(
            $"   (ganancia {AppCulture.Money(quote.Breakdown.Profit)}" +
            (quote.Commercial.HasDiscount
                ? $" menos {AppCulture.Money(quote.Commercial.Discount)} de descuento"
                : string.Empty) +
            $", sobre {quote.Commercial.TaxableBaseDisplay})")
        {
            FontSize = 10,
            Foreground = MutedBrush
        });

        if (margin.Value < 0)
        {
            paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new Run("El descuento se comió toda la ganancia: este trabajo sale a pérdida.")
            {
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = TextBrush
            });
        }

        return paragraph;
    }

    /// <summary>
    /// Dibuja cada hoja del documento como una imagen, tal como va a salir impresa.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es lo que hace posible la vista previa. El diálogo de impresión de Windows no puede
    /// mostrarla: <see cref="System.Windows.Controls.PrintDialog"/> es el diálogo clásico y
    /// nunca tuvo panel de previsualización, y además el documento se le entrega recién
    /// después de que el usuario acepta, así que mientras está abierto no hay nada que
    /// mostrar.
    /// </para>
    /// <para>
    /// El fondo blanco se pinta primero a propósito: el documento es transparente y sin eso
    /// las hojas salen sobre negro, que no se parece en nada a lo que sale por la impresora.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<BitmapSource> RenderPages(FlowDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        LayOut(document, A4Width, A4Height);

        var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator;
        paginator.ComputePageCount();

        var pages = new List<BitmapSource>(paginator.PageCount);

        for (var i = 0; i < paginator.PageCount; i++)
        {
            using var page = paginator.GetPage(i);

            var bitmap = new RenderTargetBitmap(
                (int)A4Width, (int)A4Height, 96, 96, PixelFormats.Pbgra32);

            var background = new DrawingVisual();
            using (var context = background.RenderOpen())
            {
                context.DrawRectangle(Brushes.White, null, new Rect(0, 0, A4Width, A4Height));
            }

            bitmap.Render(background);
            bitmap.Render(page.Visual);
            bitmap.Freeze();

            pages.Add(bitmap);
        }

        return pages;
    }

    /// <summary>Abre el diálogo de impresión. Devuelve false si el usuario canceló.</summary>
    public bool Print(FlowDocument document, string jobName)
    {
        var dialog = new PrintDialog();

        if (dialog.ShowDialog() != true)
        {
            return false;
        }

        LayOut(document, dialog.PrintableAreaWidth, dialog.PrintableAreaHeight);
        dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, jobName);
        return true;
    }

    /// <summary>
    /// Ajusta el documento a la hoja centrando el bloque de contenido de ancho fijo. El
    /// ancho de columna tiene que ser el del contenido y no el de la hoja: si sobra, WPF
    /// arma columnas más anchas que la página y parte el documento en hojas de más.
    /// </summary>
    public static void LayOut(FlowDocument document, double pageWidth, double pageHeight)
    {
        var horizontal = Math.Max(24, (pageWidth - ContentWidth) / 2);

        document.PageWidth = pageWidth;
        document.PageHeight = pageHeight;
        document.PagePadding = new Thickness(horizontal, VerticalMargin, horizontal, VerticalMargin);
        document.ColumnWidth = Math.Max(1, pageWidth - (horizontal * 2));
    }

    // --- Bloques -------------------------------------------------------------

    /// <summary>Documento con el aire de siempre. Lo usan la hoja de costos y el recibo.</summary>
    private static FlowDocument CreateDocument() => CreateDocument(Density.Roomy);

    private static FlowDocument CreateDocument(Density density) => new()
    {
        FontFamily = new FontFamily("Segoe UI"),
        FontSize = density.FontSize,
        Foreground = TextBrush,
        PagePadding = new Thickness(50, density.PageMargin, 50, density.PageMargin),
        ColumnGap = 0
    };

    /// <summary>
    /// Banda de cabecera del mismo crema que el fondo del logo, así el cuadrado del
    /// JPEG desaparece dentro de la banda en vez de verse pegoteado sobre el papel.
    /// </summary>
    /// <param name="compact">
    /// Banda más baja, para la hoja de costos. Es un papel de trabajo del taller y no algo
    /// que se entrega: el logo grande ahí no aporta nada y cuesta los píxeles que deciden si
    /// el documento entra en una A4.
    /// </param>
    private static Block BuildHeaderBand(bool compact = false) =>
        BuildHeaderBand(compact ? 50 : 68, compact ? 17 : 21, compact ? 8 : 12);

    /// <summary>La banda del papel del cliente, que se achica con el resto del documento.</summary>
    private static Block BuildHeaderBand(Density density) =>
        BuildHeaderBand(density.LogoSize, density.BrandSize, density.SectionSpacing + 4);

    private static Block BuildHeaderBand(double logoSize, double brandSize, double vertical)
    {
        var table = NoBorderTable();
        table.Columns.Add(Column(96, BandBrush));
        table.Columns.Add(Column(ContentWidth - 96, BandBrush));

        var brand = new Paragraph { Margin = new Thickness(0) };
        brand.Inlines.Add(new Run(BrandName)
        {
            FontSize = brandSize,
            FontWeight = FontWeights.Bold,
            Foreground = BrownBrush
        });
        brand.Inlines.Add(new LineBreak());
        brand.Inlines.Add(new Run(BrandTagline) { FontSize = 11, Foreground = MutedBrush });

        var row = new TableRow();
        row.Cells.Add(new TableCell(BuildLogoBlock(logoSize))
        {
            Background = BandBrush,
            Padding = new Thickness(14, vertical, 4, vertical)
        });
        row.Cells.Add(new TableCell(brand)
        {
            Background = BandBrush,
            Padding = new Thickness(6, vertical + 2, 18, vertical),
            TextAlignment = TextAlignment.Right
        });

        var group = new TableRowGroup();
        group.Rows.Add(row);
        table.RowGroups.Add(group);
        table.Margin = new Thickness(0, 0, 0, 0);

        return table;
    }

    private static Block BuildLogoBlock(double size)
    {
        var image = LoadLogo();

        if (image is null)
        {
            return new Paragraph { Margin = new Thickness(0) };
        }

        // InlineUIContainer y no BlockUIContainer: dentro de una celda de tabla, el
        // contenedor de bloque estira la fila a lo alto de toda la página.
        return new Paragraph(new InlineUIContainer(new Image
        {
            Source = image,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform
        }))
        {
            Margin = new Thickness(0),
            // Un poco más que el alto de la imagen: con la altura justa el renglón la
            // recorta abajo por el espacio de descendentes.
            LineHeight = size + 8,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            TextAlignment = TextAlignment.Left
        };
    }

    private static BitmapImage? _logo;

    private static BitmapImage? LoadLogo()
    {
        if (_logo is not null)
        {
            return _logo;
        }

        // Forma explícita con el nombre del ensamblado: la forma corta resuelve contra el
        // ensamblado de entrada, así que se caía cuando el documento se arma desde otro
        // host (por ejemplo los tests).
        foreach (var uri in new[]
                 {
                     "pack://application:,,,/MetroCarpinteria;component/Assets/logo-circle.jpeg",
                     "pack://application:,,,/Assets/logo-circle.jpeg"
                 })
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(uri, UriKind.Absolute);
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();

                _logo = image;
                return _logo;
            }
            catch
            {
                // Se prueba la siguiente forma; si ninguna resuelve el documento se
                // imprime igual, sin el logo.
            }
        }

        return null;
    }

    private static Block BuildTitleRow(string documentTitle, QuoteDetail quote) =>
        BuildTitleRow(documentTitle, quote.QuotedAtLocal ?? DateTime.Today, quote.ValidUntilLocal);

    private static Block BuildTitleRow(string documentTitle, DateTime issued, DateTime? validUntilLocal)
    {
        var table = NoBorderTable();
        table.Columns.Add(Column(ContentWidth / 2));
        table.Columns.Add(Column(ContentWidth / 2));

        var left = new Paragraph { Margin = new Thickness(0) };
        left.Inlines.Add(new Run(documentTitle)
        {
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = BrownBrush
        });

        var right = new Paragraph { Margin = new Thickness(0), TextAlignment = TextAlignment.Right };
        right.Inlines.Add(new Run($"Fecha: {AppCulture.ShortDate(issued)}") { FontSize = 11 });

        if (validUntilLocal.HasValue)
        {
            right.Inlines.Add(new LineBreak());
            right.Inlines.Add(new Run($"Válido hasta: {AppCulture.ShortDate(validUntilLocal.Value)}")
            {
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            });
        }

        var row = new TableRow();
        row.Cells.Add(new TableCell(left) { Padding = new Thickness(0, 10, 0, 6) });
        row.Cells.Add(new TableCell(right) { Padding = new Thickness(0, 10, 0, 6) });

        var group = new TableRowGroup();
        group.Rows.Add(row);
        table.RowGroups.Add(group);

        return table;
    }

    private static Block BuildClientBlock(QuoteDetail quote)
    {
        var paragraph = new Paragraph
        {
            Background = CreamBrush,
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 4),
            BorderBrush = GoldBrush,
            BorderThickness = new Thickness(0, 0, 0, 2)
        };

        // Sólo el cliente. El trabajo y su descripción bajaron a la lista de trabajos, que
        // es donde cada uno va con su precio al lado: repetirlos acá arriba obligaba a leer
        // dos veces lo mismo antes de llegar al número.
        paragraph.Inlines.Add(Label("Cliente: "));
        paragraph.Inlines.Add(new Run(quote.ClientName) { FontWeight = FontWeights.SemiBold });

        return paragraph;
    }

    /// <summary>
    /// Agrega un texto respetando los saltos de línea que haya escrito el usuario.
    /// </summary>
    /// <remarks>
    /// El campo de descripción del editor acepta varios renglones, y en el papel tienen que
    /// verse como renglones y no como un párrafo corrido. Las líneas vacías se saltean para
    /// que un Enter de más no abra un agujero en la hoja.
    /// </remarks>
    private static void AddMultiline(
        InlineCollection inlines,
        string text,
        double? fontSize = null,
        Brush? foreground = null)
    {
        var lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0)
            {
                inlines.Add(new LineBreak());
            }

            var run = new Run(lines[i]);

            if (fontSize is { } size)
            {
                run.FontSize = size;
            }

            if (foreground is not null)
            {
                run.Foreground = foreground;
            }

            inlines.Add(run);
        }
    }

    /// <summary>
    /// Fotos de referencia. Si no hay ninguna usable no agrega bloques: el presupuesto
    /// sin fotos tiene que seguir cabiendo en una hoja A4.
    /// </summary>
    private static void AddReferenceSection(
        FlowDocument document,
        IReadOnlyList<QuoteImageItem> images,
        string title) =>
        AddReferenceSection(document, images, title, Density.Roomy, new ImageCache());

    private static void AddReferenceSection(
        FlowDocument document,
        IReadOnlyList<QuoteImageItem> images,
        string title,
        Density density,
        ImageCache cache)
    {
        if (images.Count == 0)
        {
            return;
        }

        document.Blocks.Add(SectionTitle(title, density));
        document.Blocks.Add(BuildReferencesTable(images, density, cache));
    }

    /// <summary>
    /// Lo que se está cotizando: el trabajo principal y los adjuntos del mismo cliente,
    /// cada uno con su descripción y su precio.
    /// </summary>
    /// <remarks>
    /// Antes esto era una sección «Otros trabajos» separada, con el principal viviendo
    /// arriba en la caja del cliente. Puestos en una sola lista se leen como lo que son:
    /// los renglones que explican el número del final.
    /// </remarks>
    private static void AddWorksSection(FlowDocument document, QuoteDetail quote, Density density)
    {
        var numbered = quote.HasAttachments;

        document.Blocks.Add(SectionTitle(numbered ? "Trabajos" : "Trabajo", density));

        var table = NoBorderTable();
        table.Columns.Add(Column(ContentWidth - 160));
        table.Columns.Add(Column(160));

        var group = new TableRowGroup();
        group.Rows.Add(BuildWorkRow(
            numbered ? 1 : null, quote.Title, quote.Description, quote.BudgetDisplay, density));

        var number = 2;
        foreach (var attachment in quote.Attachments)
        {
            group.Rows.Add(BuildWorkRow(
                number++, attachment.Title, attachment.Description, attachment.BudgetDisplay, density));
        }

        table.RowGroups.Add(group);
        table.Margin = new Thickness(0, 0, 0, density.SectionSpacing);
        document.Blocks.Add(table);

        // La aclaración sólo tiene sentido cuando el TOTAL efectivamente no los suma. Con
        // el tilde prendido diría lo contrario de lo que muestra el número.
        if (quote is { HasAttachments: true, IncludeAttachmentsInTotal: false })
        {
            var names = quote.Attachments.Select(a => a.Title).ToList();
            document.Blocks.Add(Muted(
                $"{Phrases.JoinWithAnd(names)} " +
                (names.Count == 1
                    ? "es otro trabajo del mismo cliente y no está incluido en este total."
                    : "son otros trabajos del mismo cliente y no están incluidos en este total.")));
        }
    }

    /// <param name="number">
    /// El orden dentro de la lista, o null cuando hay un solo trabajo: numerar un único
    /// renglón «Trabajo 1» es ruido.
    /// </param>
    private static TableRow BuildWorkRow(
        int? number,
        string title,
        string? description,
        string amountDisplay,
        Density density)
    {
        var label = new Paragraph { Margin = new Thickness(0) };

        if (number is { } position)
        {
            label.Inlines.Add(new Run($"Trabajo {position}")
            {
                FontSize = density.FontSize - 2,
                Foreground = MutedBrush
            });
            label.Inlines.Add(new LineBreak());
        }

        label.Inlines.Add(new Run(title) { FontWeight = FontWeights.SemiBold });

        // La descripción es lo único que le dice al cliente qué está comprando: sin
        // desglose, es lo que justifica el precio del renglón.
        if (!string.IsNullOrWhiteSpace(description))
        {
            label.Inlines.Add(new LineBreak());
            AddMultiline(label.Inlines, description, density.FontSize - 1, MutedBrush);
        }

        var row = new TableRow();
        row.Cells.Add(new TableCell(label) { Padding = new Thickness(0, 5, 8, 5) });
        row.Cells.Add(new TableCell(new Paragraph(new Run(amountDisplay)
        {
            FontWeight = FontWeights.SemiBold
        })
        { Margin = new Thickness(0) })
        {
            Padding = new Thickness(8, 5, 0, 5),
            TextAlignment = TextAlignment.Right
        });

        return row;
    }

    private static Block BuildCommitmentNote(QuoteDetail quote) =>
        new Paragraph(new Run(quote.CommitmentNoteDisplay))
        {
            FontSize = 11,
            FontStyle = FontStyles.Italic,
            Foreground = TextBrush,
            Margin = new Thickness(0, 0, 0, 8)
        };

    private static Block BuildReferencesTable(
        IReadOnlyList<QuoteImageItem> images,
        Density density,
        ImageCache cache)
    {
        var table = NoBorderTable();
        var cellWidth = (ContentWidth - 12) / 2;
        table.Columns.Add(Column(cellWidth));
        table.Columns.Add(Column(cellWidth));
        table.Margin = new Thickness(0, 0, 0, 4);

        var group = new TableRowGroup();

        for (var i = 0; i < images.Count; i += 2)
        {
            var row = new TableRow();
            row.Cells.Add(ReferenceCell(images[i], cellWidth, density, cache));
            row.Cells.Add(i + 1 < images.Count
                ? ReferenceCell(images[i + 1], cellWidth, density, cache)
                : new TableCell());
            group.Rows.Add(row);
        }

        table.RowGroups.Add(group);
        return table;
    }

    private static TableCell ReferenceCell(
        QuoteImageItem item,
        double width,
        Density density,
        ImageCache cache)
    {
        var cell = new TableCell { Padding = new Thickness(0, 0, 10, 10) };
        var bitmap = cache.Load(item.FullPath);

        if (bitmap is not null)
        {
            var maxWidth = Math.Max(32, width - 8);
            var maxHeight = density.PhotoMaxHeight;
            var scale = Math.Min(
                maxWidth / Math.Max(1, bitmap.PixelWidth),
                maxHeight / Math.Max(1, bitmap.PixelHeight));
            scale = Math.Min(scale, 1);

            var displayWidth = bitmap.PixelWidth * scale;
            var displayHeight = bitmap.PixelHeight * scale;

            // InlineUIContainer y no BlockUIContainer: dentro de una celda, el de bloque
            // estira la fila a lo alto de la página.
            var imageParagraph = new Paragraph(new InlineUIContainer(new Image
            {
                Source = bitmap,
                Width = displayWidth,
                Height = displayHeight,
                Stretch = Stretch.Uniform
            }))
            {
                Margin = new Thickness(0),
                LineHeight = displayHeight + 6,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight
            };

            cell.Blocks.Add(imageParagraph);
        }

        if (!string.IsNullOrWhiteSpace(item.Caption))
        {
            cell.Blocks.Add(new Paragraph(new Run(item.Caption)
            {
                FontSize = 10,
                FontStyle = FontStyles.Italic,
                Foreground = MutedBrush
            })
            {
                Margin = new Thickness(0, 4, 0, 0)
            });
        }

        return cell;
    }

    private static BitmapImage? LoadQuoteImage(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            // Una foto ilegible no puede impedir imprimir el presupuesto.
            return null;
        }
    }

    /// <summary>
    /// Los materiales con su precio unitario. Solo la hoja de costos: el papel del cliente
    /// no lleva ninguna lista.
    /// </summary>
    /// <remarks>
    /// Sin fila de subtotal a propósito. El desglose empieza justo abajo con
    /// «Materiales $ 14.000», que es exactamente el mismo número: repetirlo dos renglones
    /// después no agregaba nada y costaba los píxeles que hacen que la hoja entre en una A4.
    /// </remarks>
    private static Block BuildMaterialsTable(QuoteDetail quote)
    {
        var table = NoBorderTable();
        table.Columns.Add(Column(ContentWidth - 360));
        table.Columns.Add(Column(120));
        table.Columns.Add(Column(120));
        table.Columns.Add(Column(120));

        var group = new TableRowGroup();

        var header = new TableRow();
        header.Cells.Add(HeaderCell("Detalle", TextAlignment.Left));
        header.Cells.Add(HeaderCell("Cantidad", TextAlignment.Right));
        header.Cells.Add(HeaderCell("P. unitario", TextAlignment.Right));
        header.Cells.Add(HeaderCell("Total", TextAlignment.Right));
        group.Rows.Add(header);

        var alternate = false;
        foreach (var line in quote.Lines)
        {
            var shade = alternate ? CreamBrush : null;
            var row = new TableRow();

            row.Cells.Add(BodyCell(line.Description, TextAlignment.Left, shade));
            row.Cells.Add(BodyCell(line.QuantityDisplay, TextAlignment.Right, shade));
            row.Cells.Add(BodyCell(line.UnitCostDisplay, TextAlignment.Right, shade));
            row.Cells.Add(BodyCell(line.LineTotalDisplay, TextAlignment.Right, shade));

            group.Rows.Add(row);
            alternate = !alternate;
        }

        table.RowGroups.Add(group);
        table.Margin = new Thickness(0, 0, 0, 10);
        return table;
    }

    /// <summary>
    /// Qué cobra cada uno y qué le suma al precio final.
    /// </summary>
    /// <remarks>
    /// La segunda columna es el punto de la tabla: el jefe pesa su jornal más gastos y
    /// ganancia; un operario, solo lo que cobra. Un ayudante de $ 22.000 por día durante
    /// tres días le suma $ 66.000 al presupuesto. Como el renglón del jefe incluye la
    /// ganancia, la tabla <b>solo puede salir en la hoja interna</b>.
    /// </remarks>
    private static Block BuildLaborTable(BudgetBreakdown breakdown)
    {
        const double amountWidth = 140;
        const double loadedWidth = 150;

        var table = NoBorderTable();
        table.Columns.Add(Column(ContentWidth - amountWidth - loadedWidth));
        table.Columns.Add(Column(amountWidth));
        table.Columns.Add(Column(loadedWidth));

        var group = new TableRowGroup();

        var header = new TableRow();
        header.Cells.Add(HeaderCell("Persona", TextAlignment.Left));
        header.Cells.Add(HeaderCell("Jornal", TextAlignment.Right));
        header.Cells.Add(HeaderCell("Pesa en el precio", TextAlignment.Right));
        group.Rows.Add(header);

        var alternate = false;
        foreach (var share in breakdown.LaborShares)
        {
            var shade = alternate ? CreamBrush : null;

            // Nombre y jornada en un solo renglón. Con el detalle en una segunda línea, la
            // hoja de costos con cuatro operarios se pasaba a una segunda página.
            var who = new Paragraph { Margin = new Thickness(0) };
            who.Inlines.Add(new Run(share.Description) { FontSize = 11 });
            who.Inlines.Add(new Run($"   {share.RateDisplay}") { FontSize = 10, Foreground = MutedBrush });

            var row = new TableRow();
            row.Cells.Add(new TableCell(who)
            {
                Background = shade,
                Padding = new Thickness(8, 5, 8, 5)
            });
            row.Cells.Add(BodyCell(share.AmountDisplay, TextAlignment.Right, shade));
            row.Cells.Add(new TableCell(new Paragraph(new Run(share.LoadedDisplay)
            {
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            })
            { Margin = new Thickness(0) })
            {
                Background = shade,
                Padding = new Thickness(8, 5, 8, 5),
                TextAlignment = TextAlignment.Right
            });

            group.Rows.Add(row);
            alternate = !alternate;
        }

        var totals = new TableRow();
        totals.Cells.Add(TotalCell("Total", TextAlignment.Left));
        totals.Cells.Add(TotalCell(AppCulture.Money(breakdown.Labor), TextAlignment.Right));
        totals.Cells.Add(TotalCell(
            AppCulture.Money(breakdown.Labor + breakdown.Overhead + breakdown.Profit),
            TextAlignment.Right));
        group.Rows.Add(totals);

        table.RowGroups.Add(group);
        table.Margin = new Thickness(0, 0, 0, 14);
        return table;
    }

    private static TableCell TotalCell(string text, TextAlignment alignment) =>
        new(new Paragraph(new Run(text) { FontWeight = FontWeights.Bold }) { Margin = new Thickness(0) })
        {
            Padding = new Thickness(8, 7, 8, 7),
            TextAlignment = alignment,
            BorderBrush = GoldBrush,
            BorderThickness = new Thickness(0, 1, 0, 0)
        };

    private static Block BuildSummaryTable(IReadOnlyList<BudgetBreakdownLine> lines)
    {
        var table = NoBorderTable();
        table.Columns.Add(Column(ContentWidth - 160));
        table.Columns.Add(Column(160));

        var group = new TableRowGroup();

        foreach (var line in lines)
        {
            var label = new Paragraph { Margin = new Thickness(0) };
            label.Inlines.Add(new Run(line.Label));

            if (!string.IsNullOrWhiteSpace(line.Detail))
            {
                label.Inlines.Add(new Run($"  ({line.Detail})") { FontSize = 10, Foreground = MutedBrush });
            }

            var row = new TableRow();
            row.Cells.Add(new TableCell(label) { Padding = new Thickness(0, 3, 8, 3) });
            row.Cells.Add(new TableCell(new Paragraph(new Run(line.AmountDisplay)) { Margin = new Thickness(0) })
            {
                Padding = new Thickness(8, 3, 0, 3),
                TextAlignment = TextAlignment.Right
            });

            group.Rows.Add(row);
        }

        table.RowGroups.Add(group);
        table.Margin = new Thickness(0, 0, 0, 10);
        return table;
    }

    /// <summary>
    /// El número grande. Cierra el papel: el cliente termina de leer en el importe.
    /// </summary>
    /// <remarks>
    /// Es un <see cref="Paragraph"/> y no una tabla de dos columnas, que es como estaba,
    /// porque <see cref="KeepClosingParagraphsTogether"/> sólo puede encadenar párrafos.
    /// Como tabla, el TOTAL cortaba la cadena y podía quedar solo al pie de una hoja con
    /// las observaciones y la vigencia en la siguiente. El rótulo va arriba y el importe
    /// alineado a la derecha debajo, que es lo que permite el formato en un solo bloque.
    /// </remarks>
    private static Block BuildTotalBlock(string amountDisplay, Density density)
    {
        var paragraph = new Paragraph
        {
            Background = BrownBrush,
            Padding = new Thickness(16, 10, 16, 12),
            Margin = new Thickness(0, density.SectionSpacing, 0, density.SectionSpacing),
            TextAlignment = TextAlignment.Right
        };

        paragraph.Inlines.Add(new Run("TOTAL")
        {
            FontSize = density.FontSize + 2,
            FontWeight = FontWeights.Bold,
            Foreground = OnBrownBrush
        });
        paragraph.Inlines.Add(new LineBreak());
        paragraph.Inlines.Add(new Run(amountDisplay)
        {
            FontSize = density.FontSize + 8,
            FontWeight = FontWeights.Bold,
            Foreground = OnBrownBrush
        });

        return paragraph;
    }

    private static Block BuildPaidRow(string paidDisplay, Density density) =>
        BuildClosingRow("Entregado a cuenta", "- " + paidDisplay, CreamBrush, density, emphasis: false);

    /// <summary>
    /// Lo que el cliente quiere saber cuando ya adelantó plata: cuánto le queda.
    /// </summary>
    private static Block BuildBalanceRow(string balanceDisplay, Density density) =>
        BuildClosingRow("SALDO A PAGAR", balanceDisplay, BandBrush, density, emphasis: true);

    private static Block BuildClosingRow(
        string label,
        string amount,
        Brush background,
        Density density,
        bool emphasis)
    {
        var paragraph = new Paragraph
        {
            Background = background,
            Padding = new Thickness(16, 6, 16, 6),
            Margin = new Thickness(0, 0, 0, 2),
            TextAlignment = TextAlignment.Right
        };

        var weight = emphasis ? FontWeights.Bold : FontWeights.Normal;

        paragraph.Inlines.Add(new Run(label + "  ")
        {
            FontSize = density.FontSize,
            FontWeight = weight,
            Foreground = TextBrush
        });
        paragraph.Inlines.Add(new Run(amount)
        {
            FontSize = density.FontSize + 2,
            FontWeight = weight,
            Foreground = TextBrush
        });

        return paragraph;
    }

    /// <summary>
    /// Renglones en blanco para escribir a mano lo que se acuerde en el momento.
    /// </summary>
    /// <remarks>
    /// Es lo último que se recorta cuando el papel no entra —de tres renglones a uno— pero
    /// nunca desaparece: el presupuesto se termina de cerrar de palabra en el taller, y sin
    /// un lugar para anotarlo eso queda escrito en el margen o no queda escrito.
    /// </remarks>
    private static Block BuildObservationsBlock(Density density)
    {
        var paragraph = new Paragraph
        {
            Margin = new Thickness(0, density.SectionSpacing, 0, density.SectionSpacing),
            BorderBrush = GoldBrush,

            // Enmarcado arriba y abajo: sin el cierre, el espacio en blanco se lee como un
            // hueco de maquetación y no como el lugar donde hay que escribir.
            BorderThickness = new Thickness(0, 1, 0, 1),
            Padding = new Thickness(0, 5, 0, 5)
        };

        paragraph.Inlines.Add(new Run("Observaciones")
        {
            FontSize = density.FontSize - 1,
            FontWeight = FontWeights.SemiBold,
            Foreground = MutedBrush
        });

        // Los renglones son líneas vacías con interlineado: dibujar rayas pediría una tabla,
        // y una tabla acá volvería a cortar la cadena del cierre.
        //
        // El relleno es un espacio duro y no uno común: el espacio común se colapsa al
        // maquetar y los renglones quedaban en una franja de ocho píxeles, imposible de
        // escribir a mano.
        for (var i = 0; i < density.ObservationLines; i++)
        {
            paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new Run(" ") { FontSize = density.FontSize + 4 });
        }

        return paragraph;
    }

    private static Block BuildValidityNote(QuoteDetail quote)
    {
        var text = quote.ValidUntilLocal.HasValue
            ? $"Precio válido hasta el {AppCulture.ShortDate(quote.ValidUntilLocal.Value)}. " +
              "Pasada esa fecha los valores pueden actualizarse según el costo de los materiales."
            : "Los valores pueden actualizarse según el costo de los materiales.";

        return Muted(text);
    }

    /// <remarks>
    /// El margen superior es contenido: cada píxel de aire acá empuja el pie hacia la hoja
    /// siguiente. La hoja de costos completa —materiales, desglose, mano de obra por persona,
    /// condiciones, saldo y margen— entra en A4 con unos 30 píxeles de sobra, así que este
    /// espaciado no es negociable: aflojarlo cuesta una hoja entera impresa con nada más que
    /// esta línea. La raya dorada ya separa lo suficiente.
    /// <para>
    /// Lo mide <c>--documents</c> del smoke test, que imprime cuántos píxeles sobran o faltan.
    /// </para>
    /// </remarks>
    private static Block BuildFooter() => BuildFooter(Density.Roomy);

    private static Block BuildFooter(Density density) =>
        new Paragraph(new Run($"{BrandName} · {BrandTagline}"))
        {
            FontSize = 10,
            Foreground = MutedBrush,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, Math.Min(10, density.SectionSpacing + 2), 0, 0),
            BorderBrush = GoldBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 6, 0, 0)
        };

    /// <summary>
    /// Ata los párrafos del cierre para que un salto de página no los separe.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sin esto, un documento apenas más largo que la hoja imprimía una segunda con nada
    /// más que el pie de página. Encadenados, si no entran pasan juntos: una hoja con el
    /// margen y el pie se entiende; una con una sola línea de marca, no.
    /// </para>
    /// <para>
    /// Solo alcanza a los <see cref="Paragraph"/>: <c>KeepWithNext</c> está definido ahí y
    /// no en <see cref="Block"/>, así que las tablas —el TOTAL, el saldo, la firma— no
    /// pueden encadenarse y cortan la cadena. Es lo máximo que permite el formato.
    /// </para>
    /// </remarks>
    private static void KeepClosingParagraphsTogether(FlowDocument document, int closingBlockCount)
    {
        var blocks = document.Blocks.ToList();
        var first = Math.Max(0, blocks.Count - closingBlockCount);

        // Hasta el anteúltimo: cada uno se ata con el que le sigue.
        for (var i = first; i < blocks.Count - 1; i++)
        {
            if (blocks[i] is Paragraph paragraph)
            {
                paragraph.KeepWithNext = true;
            }
        }
    }

    // --- Ayudas de formato ---------------------------------------------------

    private static Table NoBorderTable() => new()
    {
        CellSpacing = 0,
        BorderThickness = new Thickness(0),
        Margin = new Thickness(0)
    };

    /// <remarks>
    /// El aire de acá es contenido: cada título de sección se repite cuatro o cinco veces en
    /// la hoja de costos, y ahí los píxeles deciden si el documento entra en una A4.
    /// </remarks>
    private static Block SectionTitle(string text) => SectionTitle(text, Density.Roomy);

    private static Block SectionTitle(string text, Density density) =>
        new Paragraph(new Run(text.ToUpperInvariant())
        {
            FontSize = density.FontSize - 1,
            FontWeight = FontWeights.Bold,
            Foreground = BrownBrush
        })
        {
            Margin = new Thickness(0, density.SectionSpacing, 0, density.SectionSpacing - 3),
            BorderBrush = GoldBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 0, 0, 3)
        };

    private static Block Muted(string text) =>
        new Paragraph(new Run(text))
        {
            FontSize = 10,
            Foreground = MutedBrush,
            Margin = new Thickness(0, 0, 0, 8)
        };

    private static Run Label(string text) => new(text) { Foreground = MutedBrush, FontSize = 11 };

    private static TableCell HeaderCell(string text, TextAlignment alignment) =>
        new(new Paragraph(new Run(text)
        {
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = BrownBrush
        })
        { Margin = new Thickness(0) })
        {
            Background = BandBrush,
            Padding = new Thickness(8, 5, 8, 5),
            TextAlignment = alignment
        };

    private static TableCell BodyCell(string text, TextAlignment alignment, Brush? background = null) =>
        new(new Paragraph(new Run(text) { FontSize = 11 }) { Margin = new Thickness(0) })
        {
            Background = background,
            Padding = new Thickness(8, 5, 8, 5),
            TextAlignment = alignment
        };

    /// <summary>Columna de ancho fijo. Ver la nota de <see cref="ContentWidth"/> sobre Star.</summary>
    private static TableColumn Column(double width, Brush? background = null) =>
        new() { Width = new GridLength(width), Background = background };

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
