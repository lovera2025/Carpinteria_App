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

    /// <summary>Documento para entregarle al cliente. Sin porcentajes internos.</summary>
    public FlowDocument BuildClientQuote(QuoteDetail quote, bool includeMaterialDetail)
    {
        var document = CreateDocument();

        document.Blocks.Add(BuildHeaderBand());
        document.Blocks.Add(BuildTitleRow("PRESUPUESTO", quote));
        document.Blocks.Add(BuildClientBlock(quote));

        if (includeMaterialDetail && quote.Lines.Count > 0)
        {
            document.Blocks.Add(SectionTitle("Detalle de materiales"));
            document.Blocks.Add(BuildMaterialsTable(quote, showUnitCost: false));
        }

        document.Blocks.Add(SectionTitle("Resumen"));

        if (quote.Breakdown is not null)
        {
            // Sin la línea de total: el bloque destacado de abajo ya la muestra.
            document.Blocks.Add(BuildSummaryTable(
                quote.Breakdown.ClientLines.Where(l => !l.IsTotal).ToList()));
        }

        document.Blocks.Add(BuildTotalBlock(quote));
        document.Blocks.Add(BuildValidityNote(quote));
        document.Blocks.Add(BuildSignature());
        document.Blocks.Add(BuildFooter());

        return document;
    }

    /// <summary>
    /// Hoja de costos para el taller. Acá sí van desperdicio, desgaste, gastos y
    /// ganancia: es el documento que <b>no</b> se le da al cliente.
    /// </summary>
    public FlowDocument BuildCostSheet(QuoteDetail quote)
    {
        var document = CreateDocument();

        document.Blocks.Add(BuildHeaderBand());
        document.Blocks.Add(BuildTitleRow("HOJA DE COSTOS", quote));

        document.Blocks.Add(new Paragraph(new Run("Uso interno del taller — no entregar al cliente."))
        {
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = OnBrownBrush,
            Background = BrownBrush,
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 0, 14)
        });

        document.Blocks.Add(BuildClientBlock(quote));

        if (quote.Lines.Count > 0)
        {
            document.Blocks.Add(SectionTitle("Materiales"));
            document.Blocks.Add(BuildMaterialsTable(quote, showUnitCost: true));
        }

        document.Blocks.Add(SectionTitle("Desglose del cálculo"));

        if (quote.Breakdown is not null)
        {
            document.Blocks.Add(BuildSummaryTable(quote.Breakdown.Lines.Where(l => !l.IsTotal).ToList()));
        }
        else
        {
            document.Blocks.Add(Muted("Este presupuesto todavía no tiene un cálculo guardado."));
        }

        document.Blocks.Add(BuildTotalBlock(quote));
        document.Blocks.Add(BuildFooter());

        return document;
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

    private static FlowDocument CreateDocument() => new()
    {
        FontFamily = new FontFamily("Segoe UI"),
        FontSize = 12,
        Foreground = TextBrush,
        PagePadding = new Thickness(50, 44, 50, 44),
        ColumnGap = 0
    };

    /// <summary>
    /// Banda de cabecera del mismo crema que el fondo del logo, así el cuadrado del
    /// JPEG desaparece dentro de la banda en vez de verse pegoteado sobre el papel.
    /// </summary>
    private static Block BuildHeaderBand()
    {
        var table = NoBorderTable();
        table.Columns.Add(Column(96, BandBrush));
        table.Columns.Add(Column(ContentWidth - 96, BandBrush));

        var brand = new Paragraph { Margin = new Thickness(0) };
        brand.Inlines.Add(new Run(BrandName)
        {
            FontSize = 21,
            FontWeight = FontWeights.Bold,
            Foreground = BrownBrush
        });
        brand.Inlines.Add(new LineBreak());
        brand.Inlines.Add(new Run(BrandTagline) { FontSize = 11, Foreground = MutedBrush });

        var row = new TableRow();
        row.Cells.Add(new TableCell(BuildLogoBlock())
        {
            Background = BandBrush,
            Padding = new Thickness(14, 12, 4, 12)
        });
        row.Cells.Add(new TableCell(brand)
        {
            Background = BandBrush,
            Padding = new Thickness(6, 14, 18, 12),
            TextAlignment = TextAlignment.Right
        });

        var group = new TableRowGroup();
        group.Rows.Add(row);
        table.RowGroups.Add(group);
        table.Margin = new Thickness(0, 0, 0, 0);

        return table;
    }

    private static Block BuildLogoBlock()
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
            Width = 68,
            Height = 68,
            Stretch = Stretch.Uniform
        }))
        {
            Margin = new Thickness(0),
            // Un poco más que el alto de la imagen: con la altura justa el renglón la
            // recorta abajo por el espacio de descendentes.
            LineHeight = 76,
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

    private static Block BuildTitleRow(string documentTitle, QuoteDetail quote)
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
        left.Inlines.Add(new LineBreak());
        left.Inlines.Add(new Run($"N.º {quote.Id:0000}") { FontSize = 12, Foreground = MutedBrush });

        var issued = quote.QuotedAtLocal ?? DateTime.Today;
        var right = new Paragraph { Margin = new Thickness(0), TextAlignment = TextAlignment.Right };
        right.Inlines.Add(new Run($"Fecha: {AppCulture.ShortDate(issued)}") { FontSize = 11 });

        if (quote.ValidUntilLocal.HasValue)
        {
            right.Inlines.Add(new LineBreak());
            right.Inlines.Add(new Run($"Válido hasta: {AppCulture.ShortDate(quote.ValidUntilLocal.Value)}")
            {
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            });
        }

        var row = new TableRow();
        row.Cells.Add(new TableCell(left) { Padding = new Thickness(0, 18, 0, 10) });
        row.Cells.Add(new TableCell(right) { Padding = new Thickness(0, 18, 0, 10) });

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
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 0, 0, 6),
            BorderBrush = GoldBrush,
            BorderThickness = new Thickness(0, 0, 0, 2)
        };

        paragraph.Inlines.Add(Label("Cliente: "));
        paragraph.Inlines.Add(new Run(quote.ClientName) { FontWeight = FontWeights.SemiBold });
        paragraph.Inlines.Add(new LineBreak());
        paragraph.Inlines.Add(Label("Trabajo: "));
        paragraph.Inlines.Add(new Run(quote.Title) { FontWeight = FontWeights.SemiBold });

        if (!string.IsNullOrWhiteSpace(quote.Description))
        {
            paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new Run(quote.Description) { FontSize = 11, Foreground = MutedBrush });
        }

        return paragraph;
    }

    private static Block BuildMaterialsTable(QuoteDetail quote, bool showUnitCost)
    {
        var table = NoBorderTable();
        table.Columns.Add(Column(showUnitCost ? ContentWidth - 360 : ContentWidth - 120));
        table.Columns.Add(Column(120));

        if (showUnitCost)
        {
            table.Columns.Add(Column(120));
            table.Columns.Add(Column(120));
        }

        var group = new TableRowGroup();

        var header = new TableRow();
        header.Cells.Add(HeaderCell("Detalle", TextAlignment.Left));
        header.Cells.Add(HeaderCell("Cantidad", TextAlignment.Right));

        if (showUnitCost)
        {
            header.Cells.Add(HeaderCell("P. unitario", TextAlignment.Right));
            header.Cells.Add(HeaderCell("Total", TextAlignment.Right));
        }

        group.Rows.Add(header);

        var alternate = false;
        foreach (var line in quote.Lines)
        {
            var shade = alternate ? CreamBrush : null;
            var row = new TableRow();

            row.Cells.Add(BodyCell(line.Description, TextAlignment.Left, shade));
            row.Cells.Add(BodyCell(line.QuantityDisplay, TextAlignment.Right, shade));

            if (showUnitCost)
            {
                row.Cells.Add(BodyCell(line.UnitCostDisplay, TextAlignment.Right, shade));
                row.Cells.Add(BodyCell(line.LineTotalDisplay, TextAlignment.Right, shade));
            }

            group.Rows.Add(row);
            alternate = !alternate;
        }

        if (showUnitCost)
        {
            var totals = new TableRow();
            totals.Cells.Add(new TableCell(new Paragraph(new Run("Subtotal de materiales")
            {
                FontWeight = FontWeights.SemiBold
            })
            { Margin = new Thickness(0) })
            {
                ColumnSpan = 3,
                Padding = new Thickness(8, 8, 8, 8),
                TextAlignment = TextAlignment.Right,
                BorderBrush = GoldBrush,
                BorderThickness = new Thickness(0, 1, 0, 0)
            });

            totals.Cells.Add(new TableCell(new Paragraph(new Run(quote.MaterialsTotalDisplay)
            {
                FontWeight = FontWeights.Bold
            })
            { Margin = new Thickness(0) })
            {
                Padding = new Thickness(8, 8, 8, 8),
                TextAlignment = TextAlignment.Right,
                BorderBrush = GoldBrush,
                BorderThickness = new Thickness(0, 1, 0, 0)
            });

            group.Rows.Add(totals);
        }

        table.RowGroups.Add(group);
        table.Margin = new Thickness(0, 0, 0, 16);
        return table;
    }

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
            row.Cells.Add(new TableCell(label) { Padding = new Thickness(0, 5, 8, 5) });
            row.Cells.Add(new TableCell(new Paragraph(new Run(line.AmountDisplay)) { Margin = new Thickness(0) })
            {
                Padding = new Thickness(8, 5, 0, 5),
                TextAlignment = TextAlignment.Right
            });

            group.Rows.Add(row);
        }

        table.RowGroups.Add(group);
        table.Margin = new Thickness(0, 0, 0, 14);
        return table;
    }

    private static Block BuildTotalBlock(QuoteDetail quote)
    {
        var table = NoBorderTable();
        table.Columns.Add(Column(ContentWidth - 210, BrownBrush));
        table.Columns.Add(Column(210, BrownBrush));

        var row = new TableRow();

        row.Cells.Add(new TableCell(new Paragraph(new Run("TOTAL")
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

        row.Cells.Add(new TableCell(new Paragraph(new Run(quote.BudgetDisplay)
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
        table.Margin = new Thickness(0, 4, 0, 18);

        return table;
    }

    private static Block BuildValidityNote(QuoteDetail quote)
    {
        var text = quote.ValidUntilLocal.HasValue
            ? $"Precio válido hasta el {AppCulture.ShortDate(quote.ValidUntilLocal.Value)}. " +
              "Pasada esa fecha los valores pueden actualizarse según el costo de los materiales."
            : "Los valores pueden actualizarse según el costo de los materiales.";

        return Muted(text);
    }

    private static Block BuildSignature()
    {
        var table = NoBorderTable();
        table.Columns.Add(Column((ContentWidth - 60) / 2));
        table.Columns.Add(Column(60));
        table.Columns.Add(Column((ContentWidth - 60) / 2));

        var row = new TableRow();
        row.Cells.Add(SignatureCell("Firma del cliente"));
        row.Cells.Add(new TableCell(new Paragraph { Margin = new Thickness(0) }));
        row.Cells.Add(SignatureCell("Metro Carpintería"));

        var group = new TableRowGroup();
        group.Rows.Add(row);
        table.RowGroups.Add(group);
        table.Margin = new Thickness(0, 40, 0, 0);

        return table;
    }

    private static TableCell SignatureCell(string caption) =>
        new(new Paragraph(new Run(caption) { FontSize = 10, Foreground = MutedBrush })
        {
            Margin = new Thickness(0, 6, 0, 0),
            TextAlignment = TextAlignment.Center
        })
        {
            BorderBrush = MutedBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 4, 0, 0)
        };

    private static Block BuildFooter() =>
        new Paragraph(new Run($"{BrandName} · {BrandTagline}"))
        {
            FontSize = 10,
            Foreground = MutedBrush,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 26, 0, 0),
            BorderBrush = GoldBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 8, 0, 0)
        };

    // --- Ayudas de formato ---------------------------------------------------

    private static Table NoBorderTable() => new()
    {
        CellSpacing = 0,
        BorderThickness = new Thickness(0),
        Margin = new Thickness(0)
    };

    private static Block SectionTitle(string text) =>
        new Paragraph(new Run(text.ToUpperInvariant())
        {
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = BrownBrush
        })
        {
            Margin = new Thickness(0, 14, 0, 8),
            BorderBrush = GoldBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 0, 0, 4)
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
            Padding = new Thickness(8, 7, 8, 7),
            TextAlignment = alignment
        };

    private static TableCell BodyCell(string text, TextAlignment alignment, Brush? background = null) =>
        new(new Paragraph(new Run(text) { FontSize = 11 }) { Margin = new Thickness(0) })
        {
            Background = background,
            Padding = new Thickness(8, 6, 8, 6),
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
