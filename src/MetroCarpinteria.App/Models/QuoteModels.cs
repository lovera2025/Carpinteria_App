using MetroCarpinteria.App.Data.Entities;
using MetroCarpinteria.App.Helpers;

namespace MetroCarpinteria.App.Models;

/// <summary>Vigencia de un presupuesto. Se deriva de la fecha, no se guarda.</summary>
public enum QuoteFreshness
{
    NoExpiry,
    Current,
    DueSoon,
    Expired
}

public enum QuoteFilter
{
    All,
    Current,
    DueSoon,
    Expired,
    Rejected
}

/// <summary>
/// Reglas de vigencia. Reciben la fecha de hoy por parámetro para que se puedan probar
/// sin depender del reloj.
/// </summary>
public static class QuoteRules
{
    public const int DueSoonDays = 3;

    public static QuoteFreshness GetFreshness(DateTime? validUntilLocal, DateTime today)
    {
        if (validUntilLocal is null)
        {
            return QuoteFreshness.NoExpiry;
        }

        var remaining = (validUntilLocal.Value.Date - today.Date).Days;

        if (remaining < 0)
        {
            return QuoteFreshness.Expired;
        }

        return remaining <= DueSoonDays ? QuoteFreshness.DueSoon : QuoteFreshness.Current;
    }

    public static string GetLabel(QuoteFreshness freshness) => freshness switch
    {
        QuoteFreshness.Current => "Vigente",
        QuoteFreshness.DueSoon => "Por vencer",
        QuoteFreshness.Expired => "Vencido",
        _ => "Sin vencimiento"
    };

    /// <summary>"hoy" / "ayer" / "hace 6 días", para saber a quién hay que llamar.</summary>
    public static string DescribeAge(DateTime? quotedAtLocal, DateTime today)
    {
        if (quotedAtLocal is null)
        {
            return string.Empty;
        }

        var days = (today.Date - quotedAtLocal.Value.Date).Days;

        return days switch
        {
            <= 0 => "hoy",
            1 => "ayer",
            _ => $"hace {days} días"
        };
    }

    public static IReadOnlyList<QuoteFilterOption> GetFilterOptions() =>
    [
        new() { Filter = QuoteFilter.All, Label = "Todos" },
        new() { Filter = QuoteFilter.Current, Label = "Vigentes" },
        new() { Filter = QuoteFilter.DueSoon, Label = "Por vencer" },
        new() { Filter = QuoteFilter.Expired, Label = "Vencidos" },
        new() { Filter = QuoteFilter.Rejected, Label = "Rechazados" }
    ];
}

public sealed class QuoteFilterOption
{
    public QuoteFilter Filter { get; init; }
    public required string Label { get; init; }
}

public sealed class QuoteListItem
{
    public int Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string ClientName { get; init; } = string.Empty;
    public ProjectStatus Status { get; init; }
    public decimal? Budget { get; init; }
    public bool IsArchived { get; init; }
    public int LineCount { get; init; }
    public DateTime? QuotedAtLocal { get; init; }
    public DateTime? ValidUntilLocal { get; init; }

    /// <summary>
    /// Se deriva de la fecha cada vez que se lee. Guardada al armar la lista quedaba
    /// congelada: con la app abierta de un día para el otro, un presupuesto que vencía
    /// ayer seguía figurando como vigente. Quien avisa que hay que releerla es
    /// <see cref="Services.ClockService"/>.
    /// </summary>
    public QuoteFreshness Freshness => QuoteRules.GetFreshness(ValidUntilLocal, DateTime.Today);

    public string StatusLabel => ProjectStatusHelper.GetLabel(Status);
    public string BudgetDisplay => AppCulture.Money(Budget);
    public string FreshnessLabel => QuoteRules.GetLabel(Freshness);
    public bool IsRejected => Status == ProjectStatus.Rejected;

    public string AgeDisplay => IsRejected
        ? "Rechazado"
        : QuoteRules.DescribeAge(QuotedAtLocal, DateTime.Today);

    public string ValidUntilDisplay => ValidUntilLocal.HasValue
        ? $"Válido hasta {AppCulture.ShortDate(ValidUntilLocal.Value)}"
        : "Sin vencimiento";
}

public sealed class QuoteLineItem
{
    public int Id { get; init; }
    public int? ProductId { get; init; }
    public string Description { get; init; } = string.Empty;
    public string Unit { get; init; } = ProductUnits.Unit;
    public decimal Quantity { get; init; }
    public decimal UnitCost { get; init; }
    public decimal AppliedQuantity { get; init; }
    public decimal? AvailableStock { get; init; }
    public int SortOrder { get; init; }

    public bool IsFromInventory => ProductId.HasValue;
    public decimal LineTotal => Math.Round(Quantity * UnitCost, 2, MidpointRounding.AwayFromZero);

    public string QuantityDisplay => AppCulture.QuantityWithUnit(Quantity, Unit);
    public string UnitCostDisplay => AppCulture.Money(UnitCost);
    public string LineTotalDisplay => AppCulture.Money(LineTotal);
    public string OriginLabel => IsFromInventory ? "Inventario" : "Suelto";

    /// <summary>
    /// Falta stock para cubrir la línea. Es solo un aviso: un presupuesto puede cotizar
    /// material que todavía no se compró.
    /// </summary>
    /// <remarks>
    /// Lo calcula <see cref="Services.QuoteService.GetDetail"/> sumando lo pedido en
    /// todas las líneas del mismo producto. Derivarlo acá solo con
    /// <see cref="Quantity"/> haría que dos líneas de 6 sobre un stock de 10 se vieran
    /// las dos cubiertas, aunque juntas no lleguen.
    /// </remarks>
    public bool HasStockWarning { get; init; }

    public string StockDisplay => AvailableStock.HasValue
        ? $"Stock: {AppCulture.QuantityWithUnit(AvailableStock.Value, Unit)}"
        : "No está en el inventario";
}

/// <summary>Un operario cotizado en el presupuesto, tal como se ve en la calculadora.</summary>
public sealed class QuoteLaborLineItem
{
    public int Id { get; init; }

    /// <summary>Ficha en Personal. Null cuando se cargó a alguien suelto.</summary>
    public int? EmployeeId { get; init; }

    /// <summary>Nombre congelado al agregarlo.</summary>
    public string Description { get; init; } = string.Empty;

    public decimal Days { get; init; }
    public decimal DailyRate { get; init; }
    public int SortOrder { get; init; }

    /// <summary>Rol de la ficha, si sigue existiendo. Solo para mostrar.</summary>
    public string? Role { get; init; }

    public decimal LineTotal => Math.Round(Days * DailyRate, 2, MidpointRounding.AwayFromZero);

    public string DaysDisplay => Days == 1m ? "1 día" : $"{AppCulture.Quantity(Days)} días";
    public string RateDisplay => $"{DaysDisplay} × {AppCulture.Money(DailyRate)}";
    public string LineTotalDisplay => AppCulture.Money(LineTotal);

    /// <summary>«5 días × $ 25.000 · Oficial carpintero», sin el punto si no hay rol.</summary>
    public string SummaryDisplay => string.IsNullOrWhiteSpace(Role)
        ? RateDisplay
        : $"{RateDisplay}  ·  {Role}";
}

public sealed class QuoteApprovalShortfall
{
    public string Description { get; init; } = string.Empty;
    public decimal Missing { get; init; }
    public string Unit { get; init; } = ProductUnits.Unit;

    public string Display => $"{Description} — faltan {AppCulture.QuantityWithUnit(Missing, Unit)}";
}

public sealed class QuoteApprovalResult
{
    public int ProjectId { get; init; }
    public int DiscountedLines { get; init; }
    public IReadOnlyList<QuoteApprovalShortfall> Shortfalls { get; init; } = [];

    public bool HasShortfalls => Shortfalls.Count > 0;

    /// <summary>
    /// Un presupuesto puede no tener materiales —solo mano de obra, o la madera la pone el
    /// cliente—, y ahí decir «se descontaron todos» sería mentir sobre el inventario.
    /// </summary>
    public string Summary => HasShortfalls
        ? $"Aprobado. Se descontó el stock disponible y quedan {Shortfalls.Count} materiales por comprar."
        : DiscountedLines == 0
            ? "Aprobado. No había materiales que descontar del inventario."
            : "Aprobado. Se descontaron todos los materiales del inventario.";
}

public sealed class QuotePendingSummary
{
    public int Pending { get; init; }
    public int DueSoon { get; init; }
    public int Expired { get; init; }
}

/// <summary>Presupuesto completo: datos, líneas y el desglose reconstruido.</summary>
public sealed class QuoteDetail
{
    public int Id { get; init; }
    public string Title { get; init; } = string.Empty;

    /// <summary>El nombre tal como se escribió acá. Es lo que sale impreso.</summary>
    public string ClientName { get; init; } = string.Empty;

    /// <summary>Ficha vinculada, si la hay. Null en los presupuestos que nunca se asociaron.</summary>
    public int? ClientId { get; init; }

    public string? Description { get; init; }
    public ProjectStatus Status { get; init; }
    public bool IsArchived { get; init; }
    public decimal? Budget { get; init; }
    public DateTime? QuotedAtLocal { get; init; }
    public DateTime? ValidUntilLocal { get; init; }
    public decimal? QuotedMaterialsCost { get; init; }
    public decimal? EstimatedDays { get; init; }
    public decimal? DailyRate { get; init; }
    public BudgetRates? Rates { get; init; }
    public IReadOnlyList<QuoteLineItem> Lines { get; init; } = [];

    /// <summary>
    /// Los operarios cotizados. Vacío es «lo hace el jefe solo», que es como quedan todos
    /// los presupuestos anteriores a que esto existiera.
    /// </summary>
    public IReadOnlyList<QuoteLaborLineItem> LaborLines { get; init; } = [];

    /// <summary>Desglose reconstruido desde las entradas congeladas. Null si nunca se calculó.</summary>
    /// <remarks>
    /// Si hubo recorte a mano con líneas marcadas, ya viene aplicado: es el que suma el
    /// precio que se le cobra. El cálculo original está en <see cref="UnadjustedBreakdown"/>.
    /// </remarks>
    public BudgetBreakdown? Breakdown { get; init; }

    /// <summary>El desglose de la fórmula, sin el recorte a mano. Null si nunca se calculó.</summary>
    public BudgetBreakdown? UnadjustedBreakdown { get; init; }

    /// <summary>Líneas marcadas para absorber el recorte. Vacío si el desglose no se tocó.</summary>
    public IReadOnlyList<BudgetLineKind> PriceAdjustmentTargets { get; init; } = [];

    /// <summary>
    /// Total con IVA/descuento sobre el cálculo, sin el recorte a mano. Es el número de
    /// «Volver al calculado».
    /// </summary>
    public decimal? CalculatedTotal { get; init; }

    /// <summary>Lo pactado con el cliente: IVA y descuento. Nunca null; vacío es «nada pactado».</summary>
    public CommercialTerms Terms { get; init; } = CommercialTerms.None();

    /// <summary>
    /// El tramo comercial reconstruido sobre el desglose. Null si todavía no hay cálculo.
    /// </summary>
    public CommercialBreakdown? Commercial { get; init; }

    public IReadOnlyList<ProjectPaymentItem> Payments { get; init; } = [];

    /// <summary>
    /// Fotos de referencia. Incluye las que faltan en disco: la UI avisa, la impresión
    /// las saltea. Un archivo perdido no puede impedir entregar el presupuesto.
    /// </summary>
    public IReadOnlyList<QuoteImageItem> Images { get; init; } = [];

    /// <summary>Las que de verdad se pueden imprimir: archivo presente y nombre seguro.</summary>
    public IReadOnlyList<QuoteImageItem> PrintableImages =>
        Images.Where(i => !i.IsMissing).ToList();

    /// <summary>Otros trabajos del mismo cliente colgados de éste.</summary>
    public IReadOnlyList<QuoteAttachmentItem> Attachments { get; init; } = [];

    public bool HasAttachments => Attachments.Count > 0;

    /// <summary>
    /// Si los adjuntos suman al número grande del papel del cliente.
    /// </summary>
    /// <remarks>
    /// Apagado, adjuntar es sólo agrupar varios trabajos en una hoja y cada uno se cobra
    /// por separado. Prendido, el cliente ve un solo total por todo.
    /// </remarks>
    public bool IncludeAttachmentsInTotal { get; init; }

    /// <summary>El aviso de seña está prendido y hay un importe para nombrar.</summary>
    public bool HasCommitmentNote => ShowCommitmentNote && CommitmentAmount is > 0;

    public bool ShowCommitmentNote { get; init; }
    public decimal? CommitmentAmount { get; init; }
    public string? CommitmentText { get; init; }

    /// <summary>Frase que sale debajo del TOTAL, o vacía si el aviso no aplica.</summary>
    public string CommitmentNoteDisplay
    {
        get
        {
            if (!HasCommitmentNote)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(CommitmentText))
            {
                return CommitmentText.Trim();
            }

            return $"Entregando {AppCulture.Money(CommitmentAmount)} como compromiso para comprar " +
                   "materiales y empezar el trabajo. El resto al finalizar.";
        }
    }

    /// <summary>Lo cobrado hasta ahora.</summary>
    public decimal PaidTotal => Payments.Sum(p => p.Amount);

    /// <summary>
    /// Lo que falta cobrar. Se calcula, no se guarda: un saldo guardado hay que mantenerlo
    /// al día con cada cambio de precio y con cada cobro, y basta que una de las dos cosas
    /// falle para que la cuenta del cliente quede mal.
    /// </summary>
    /// <remarks>
    /// Negativo cuando se cobró de más. Antes esto venía con un <c>Math.Max(0m, …)</c> que
    /// convertía ese caso en un cero prolijo: bajar el precio por debajo de una seña ya
    /// tomada dejaba la pantalla diciendo «Cobrado por completo» y la plata que había que
    /// devolver no aparecía en ningún lado.
    /// </remarks>
    public decimal Balance => (Budget ?? 0m) - PaidTotal;

    /// <summary>Se cobró más que el precio: esa diferencia es del cliente.</summary>
    public bool HasCredit => Balance < 0m;

    public bool HasPayments => Payments.Count > 0;
    public bool IsFullyPaid => Budget is > 0 && PaidTotal >= Budget.Value;

    public string PaidTotalDisplay => AppCulture.Money(PaidTotal);

    /// <summary>
    /// El saldo siempre en positivo: de qué lado está la plata lo dice el rótulo.
    /// </summary>
    /// <remarks>
    /// Un «-$ 20.000» se lee como un error de tipeo, no como una deuda del taller.
    /// </remarks>
    public string BalanceDisplay => AppCulture.Money(Math.Abs(Balance));

    /// <summary>Rótulo del saldo en pantalla, desde el lado del taller.</summary>
    public string BalanceLabel => HasCredit ? "SALDO A FAVOR" : "SALDO A COBRAR";

    // --- Las cuentas del papel ------------------------------------------------
    //
    // Van aparte de PaidTotal y Balance a propósito. Esos dos son la plata de ESTE
    // presupuesto y los mira el panel de cobros: quien registra una seña necesita el
    // saldo de lo que tiene delante, no el de un conjunto de trabajos. Lo que cambia
    // según el tilde es sólo lo que se imprime.

    /// <summary>Suma de los presupuestos adjuntos.</summary>
    public decimal AttachmentsTotal => Attachments.Sum(a => a.Budget ?? 0m);

    /// <summary>Lo ya cobrado sobre los adjuntos.</summary>
    public decimal AttachmentsPaidTotal => Attachments.Sum(a => a.PaidTotal);

    /// <summary>El número grande del papel: este trabajo, más los adjuntos si corresponde.</summary>
    public decimal PrintedTotal => IncludeAttachmentsInTotal
        ? (Budget ?? 0m) + AttachmentsTotal
        : Budget ?? 0m;

    /// <summary>Lo cobrado que el papel descuenta, del mismo conjunto que suma el total.</summary>
    public decimal PrintedPaidTotal => IncludeAttachmentsInTotal
        ? PaidTotal + AttachmentsPaidTotal
        : PaidTotal;

    public decimal PrintedBalance => PrintedTotal - PrintedPaidTotal;

    /// <summary>El papel tiene que decir que hay plata a favor, no un saldo en cero.</summary>
    public bool HasPrintedCredit => PrintedBalance < 0m;

    /// <summary>Si el papel tiene que mostrar el bloque de entregado a cuenta y saldo.</summary>
    public bool HasPrintedPayments => PrintedPaidTotal > 0m;

    public string PrintedTotalDisplay => AppCulture.Money(PrintedTotal);
    public string PrintedPaidTotalDisplay => AppCulture.Money(PrintedPaidTotal);
    public string PrintedBalanceDisplay => AppCulture.Money(Math.Abs(PrintedBalance));

    public decimal MaterialsTotal => Lines.Sum(l => l.LineTotal);
    public string MaterialsTotalDisplay => AppCulture.Money(MaterialsTotal);
    public string BudgetDisplay => AppCulture.Money(Budget);

    /// <summary>Materiales con los que se hizo el cálculo: el congelado, o la suma de las líneas.</summary>
    public decimal CalculationMaterials => QuotedMaterialsCost ?? MaterialsTotal;

    public QuoteFreshness Freshness => QuoteRules.GetFreshness(ValidUntilLocal, DateTime.Today);

    public string ValidUntilDisplay => ValidUntilLocal.HasValue
        ? $"válido hasta el {AppCulture.ShortDate(ValidUntilLocal.Value)}"
        : "sin fecha de vencimiento";

    public bool IsEditable => Status == ProjectStatus.Quote && !IsArchived;

    /// <summary>El precio se pactó a mano y ya no sigue al cálculo.</summary>
    public bool IsPriceManual { get; init; }

    /// <summary>
    /// El precio guardado no coincide con el que sale del cálculo: lo pactaron a mano.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Manda <see cref="IsPriceManual"/>, que es el dato guardado. La comparación de
    /// números queda como respaldo para los presupuestos anteriores a que existiera la
    /// marca, donde el único rastro es que los dos totales no coinciden.
    /// </para>
    /// <para>
    /// Se compara contra el total <b>con descuento e IVA</b>, no contra el precio pelado:
    /// si no, pactar un 21% haría que todo presupuesto pareciera ajustado a mano.
    /// </para>
    /// </remarks>
    public bool BudgetAdjustedManually =>
        IsPriceManual
        || (Budget.HasValue
            && CalculatedTotal.HasValue
            && (Budget.Value != CalculatedTotal.Value || PriceAdjustmentTargets.Count > 0));

    /// <summary>
    /// El aviso del precio pactado, con los dos números a la vista.
    /// </summary>
    /// <remarks>
    /// Decir solo «no coincide con el cálculo» obliga a buscar el otro número en otra
    /// parte. Y como ahora recalcular ya no pisa el pactado, este aviso es la única señal
    /// de que el precio dejó de seguir a la fórmula.
    /// </remarks>
    public string ManualPriceNotice => CalculatedTotal.HasValue && Budget.HasValue
        ? $"Precio pactado a mano: {AppCulture.Money(Budget.Value)} " +
          $"(el cálculo da {AppCulture.Money(CalculatedTotal.Value)}). Recalcular no lo toca."
        : "El precio final se pactó a mano y no sigue al cálculo.";

    public bool HasPendingStock => Lines.Any(l => l.IsFromInventory && l.AppliedQuantity < l.Quantity)
        && Status != ProjectStatus.Quote
        && Status != ProjectStatus.Rejected;
}

/// <summary>Una foto adjunta a un presupuesto. El archivo está en disco, no en la base.</summary>
public sealed class QuoteImageItem
{
    public int Id { get; init; }
    public int ProjectId { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string Caption { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public string FullPath { get; init; } = string.Empty;
    public bool IsMissing { get; init; }

    public string CaptionDisplay => string.IsNullOrWhiteSpace(Caption) ? "Sin pie de foto" : Caption;
}

/// <summary>Un presupuesto colgado de otro, tal como se lista en el editor y en el PDF.</summary>
public sealed class QuoteAttachmentItem
{
    public int AttachmentId { get; init; }
    public int ProjectId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public decimal? Budget { get; init; }

    /// <summary>
    /// Lo ya cobrado sobre este adjunto. Sólo pesa cuando los adjuntos entran en el total:
    /// si el papel suma sus precios, tiene que restar también sus señas.
    /// </summary>
    public decimal PaidTotal { get; init; }

    public IReadOnlyList<QuoteImageItem> Images { get; init; } = [];

    public string BudgetDisplay => AppCulture.Money(Budget);

    public IReadOnlyList<QuoteImageItem> PrintableImages =>
        Images.Where(i => !i.IsMissing).ToList();
}
