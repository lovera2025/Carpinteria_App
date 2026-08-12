using MetroCarpinteria.App.Helpers;

namespace MetroCarpinteria.App.Models;

/// <summary>Un cliente en la lista, con su historial resumido.</summary>
public sealed class ClientListItem
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? TaxId { get; init; }
    public string? Address { get; init; }
    public string? Notes { get; init; }
    public bool IsArchived { get; init; }

    /// <summary>Todo lo que se le cotizó, aprobado o no.</summary>
    public int QuoteCount { get; init; }

    /// <summary>Lo que efectivamente se convirtió en trabajo.</summary>
    public int ApprovedCount { get; init; }

    /// <summary>Suma de los trabajos aprobados. Es lo facturado, no lo cotizado.</summary>
    public decimal Invoiced { get; init; }

    /// <summary>Lo que falta cobrar de esos trabajos.</summary>
    public decimal Balance { get; init; }

    public DateTime? LastQuotedAtLocal { get; init; }

    public string InvoicedDisplay => AppCulture.Money(Invoiced);
    public string BalanceDisplay => AppCulture.Money(Balance);
    public bool HasBalance => Balance > 0;

    public string ContactDisplay
    {
        get
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(Phone))
            {
                parts.Add(Phone);
            }

            if (!string.IsNullOrWhiteSpace(Email))
            {
                parts.Add(Email);
            }

            return parts.Count == 0 ? "Sin datos de contacto" : string.Join("  ·  ", parts);
        }
    }

    /// <summary>Con qué frecuencia vuelve. Es lo que distingue un cliente de un conocido.</summary>
    public string HistoryDisplay => QuoteCount switch
    {
        0 => "Todavía sin presupuestos",
        _ => $"{Phrases.Count(QuoteCount, "presupuesto", "presupuestos")} · " +
             $"{ApprovedCount} aprobado{(ApprovedCount == 1 ? string.Empty : "s")}"
    };

    public string LastQuotedDisplay => LastQuotedAtLocal is null
        ? string.Empty
        : $"Último: {AppCulture.ShortDate(LastQuotedAtLocal.Value)}";
}

/// <summary>Un trabajo del historial de un cliente.</summary>
public sealed class ClientProjectItem
{
    public int Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public Data.Entities.ProjectStatus Status { get; init; }
    public decimal? Budget { get; init; }
    public decimal Paid { get; init; }
    public DateTime? QuotedAtLocal { get; init; }

    public string StatusLabel => ProjectStatusHelper.GetLabel(Status);
    public string BudgetDisplay => AppCulture.Money(Budget);
    public decimal Balance => Math.Max(0m, (Budget ?? 0m) - Paid);
    public string BalanceDisplay => AppCulture.Money(Balance);
    public bool HasBalance => Balance > 0;

    public string DateDisplay => QuotedAtLocal is null
        ? string.Empty
        : AppCulture.ShortDate(QuotedAtLocal.Value);
}

/// <summary>
/// Dos fichas que podrían ser la misma persona.
/// </summary>
/// <remarks>
/// La app <b>propone</b>, no decide. Cada par muestra los trabajos e importes de los dos
/// lados justamente para que el carpintero pueda mirar y decir «no, ése es el hijo».
/// </remarks>
public sealed class ClientDuplicateCandidate
{
    public required ClientListItem Left { get; init; }
    public required ClientListItem Right { get; init; }

    /// <summary>Por qué se propone el par: parecido, prefijo o teléfono.</summary>
    public required string Reason { get; init; }

    public double Similarity { get; init; }

    /// <summary>Clave estable del par, para recordar un «son distintas».</summary>
    public string PairKey => BuildPairKey(Left.Id, Right.Id);

    public static string BuildPairKey(int first, int second) =>
        first < second ? $"{first}-{second}" : $"{second}-{first}";

    /// <summary>
    /// Cuál conviene conservar: la que tiene más historial. Fusionar hacia la ficha con
    /// más trabajos deja menos que reasignar y menos que revisar después.
    /// </summary>
    public ClientListItem SuggestedTarget => Left.QuoteCount >= Right.QuoteCount ? Left : Right;

    public ClientListItem SuggestedSource => ReferenceEquals(SuggestedTarget, Left) ? Right : Left;
}
