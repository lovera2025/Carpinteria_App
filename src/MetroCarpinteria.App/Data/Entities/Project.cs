namespace MetroCarpinteria.App.Data.Entities;

public class Project
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// El nombre del cliente tal como se escribió en este presupuesto. Sigue existiendo
    /// junto a <see cref="ClientId"/> a propósito: es la instantánea de lo que se entregó,
    /// y no se mueve aunque después se corrija la ficha del cliente.
    /// </summary>
    public string ClientName { get; set; } = string.Empty;

    /// <summary>
    /// Ficha del cliente, si está vinculada. Null en los presupuestos viejos que nunca se
    /// asociaron a ninguna.
    /// </summary>
    /// <remarks>
    /// SQLite no admite agregar una clave foránea con <c>ALTER TABLE</c>, así que en las
    /// bases migradas la integridad la garantizan los servicios y no el motor.
    /// </remarks>
    public int? ClientId { get; set; }

    public Client? Client { get; set; }

    public string? Description { get; set; }

    /// <summary>Precio final que se le pasa al cliente. Puede ajustarse a mano sobre el calculado.</summary>
    public decimal? Budget { get; set; }

    /// <summary>
    /// Líneas del desglose que absorben el recorte a mano, separadas por coma
    /// (<c>Waste,Profit</c>). Null o vacío es «solo cambió lo que se le cobra».
    /// </summary>
    public string? PriceAdjustmentTargets { get; set; }

    public ProjectStatus Status { get; set; } = ProjectStatus.Quote;
    public bool IsArchived { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    // Entradas del cálculo congeladas al momento de cotizar. Guardar las entradas y no
    // los importes permite reconstruir el desglose entero sin que se mueva si mañana
    // cambian los porcentajes por defecto del taller.
    public decimal? QuotedMaterialsCost { get; set; }

    /// <summary>Días que le lleva el trabajo al jefe. Los operarios traen los suyos en <see cref="LaborLines"/>.</summary>
    public decimal? EstimatedDays { get; set; }

    /// <summary>Jornal del jefe. Cada operario tiene el suyo en su <see cref="ProjectLaborLine"/>.</summary>
    public decimal? DailyRate { get; set; }
    public decimal? WastePercent { get; set; }
    public decimal? ToolWearPercent { get; set; }
    public decimal? OverheadPercent { get; set; }
    public decimal? ProfitPercent { get; set; }
    public DateTime? QuotedAtUtc { get; set; }
    public DateTime? QuoteValidUntilUtc { get; set; }

    // Condiciones comerciales. También son entradas y no importes: el descuento y el IVA
    // se recalculan sobre el subtotal cada vez que se muestra el presupuesto.

    /// <summary>Alícuota de IVA en porcentaje. Null es «sin IVA discriminado».</summary>
    public decimal? VatPercent { get; set; }

    /// <summary>Null o <see cref="Entities.DiscountMode.None"/> es «sin descuento».</summary>
    public DiscountMode? DiscountMode { get; set; }

    /// <summary>Se lee según <see cref="DiscountMode"/>: porcentaje o importe.</summary>
    public decimal? DiscountValue { get; set; }

    /// <summary>
    /// Si el aviso de seña sale debajo del TOTAL en el presupuesto del cliente.
    /// No se prende solo al cobrar: el jefe lo marca cuando quiere que vaya.
    /// </summary>
    public bool ShowCommitmentNote { get; set; }

    /// <summary>Importe que se nombra en el aviso. Null o cero es «no hay aviso».</summary>
    public decimal? CommitmentAmount { get; set; }

    /// <summary>
    /// Texto libre del aviso. Vacío usa la frase de siempre: entregando X para comprar
    /// materiales y empezar, el resto al terminar.
    /// </summary>
    public string? CommitmentText { get; set; }

    public ICollection<ProjectMaterial> Materials { get; set; } = [];
    public ICollection<ProjectAssignment> Assignments { get; set; } = [];
    public ICollection<ProjectBudgetLine> BudgetLines { get; set; } = [];
    public ICollection<ProjectLaborLine> LaborLines { get; set; } = [];
    public ICollection<ProjectPayment> Payments { get; set; } = [];
    public ICollection<ProjectQuoteImage> QuoteImages { get; set; } = [];
    public ICollection<ProjectQuoteAttachment> Attachments { get; set; } = [];
}
