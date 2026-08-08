namespace MetroCarpinteria.App.Data.Entities;

public class Project
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Precio final que se le pasa al cliente. Puede ajustarse a mano sobre el calculado.</summary>
    public decimal? Budget { get; set; }

    public ProjectStatus Status { get; set; } = ProjectStatus.Quote;
    public bool IsArchived { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    // Entradas del cálculo congeladas al momento de cotizar. Guardar las entradas y no
    // los importes permite reconstruir el desglose entero sin que se mueva si mañana
    // cambian los porcentajes por defecto del taller.
    public decimal? QuotedMaterialsCost { get; set; }
    public decimal? EstimatedDays { get; set; }
    public decimal? DailyRate { get; set; }
    public decimal? WastePercent { get; set; }
    public decimal? ToolWearPercent { get; set; }
    public decimal? OverheadPercent { get; set; }
    public decimal? ProfitPercent { get; set; }
    public DateTime? QuotedAtUtc { get; set; }
    public DateTime? QuoteValidUntilUtc { get; set; }

    public ICollection<ProjectMaterial> Materials { get; set; } = [];
    public ICollection<ProjectAssignment> Assignments { get; set; } = [];
    public ICollection<ProjectBudgetLine> BudgetLines { get; set; } = [];
}
