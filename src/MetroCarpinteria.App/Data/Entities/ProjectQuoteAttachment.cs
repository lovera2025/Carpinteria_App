namespace MetroCarpinteria.App.Data.Entities;

/// <summary>
/// Un presupuesto colgado de otro, del mismo cliente, para mostrarlos juntos en el papel.
/// </summary>
/// <remarks>
/// No fusiona trabajos ni suma totales: el principal sigue con su precio, y el adjunto
/// aparece como anexo (descripción, total y fotos). Cada uno se aprueba y se cobra aparte.
/// </remarks>
public class ProjectQuoteAttachment
{
    public int Id { get; set; }

    /// <summary>El presupuesto que se está entregando, el que lleva el TOTAL grande.</summary>
    public int ParentProjectId { get; set; }

    /// <summary>El otro trabajo que se lista como anexo.</summary>
    public int AttachedProjectId { get; set; }

    public int SortOrder { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public Project Parent { get; set; } = null!;
    public Project Attached { get; set; } = null!;
}
