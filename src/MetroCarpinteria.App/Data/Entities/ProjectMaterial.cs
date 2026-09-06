namespace MetroCarpinteria.App.Data.Entities;

public class ProjectMaterial
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public int ProductId { get; set; }
    public decimal Quantity { get; set; }

    /// <summary>
    /// Lo que costaba la unidad cuando se asignó, congelado. Null cuando el producto no
    /// tenía precio de costo cargado: eso es «no sé cuánto costaba», y se muestra así.
    /// </summary>
    /// <remarks>
    /// Sin esto, «cuánto gastaste en este trabajo» se calculaba con el precio de hoy, y lo
    /// que costó un mueble de agosto cambiaba solo en octubre al subir la melamina. Es la
    /// misma razón por la que <see cref="ProjectBudgetLine.UnitCost"/> está congelado.
    /// </remarks>
    public decimal? UnitCost { get; set; }

    public DateTime AssignedAtUtc { get; set; }

    /// <summary>Lo que costó este material, con el precio congelado. Null si no se sabe.</summary>
    public decimal? LineCost => UnitCost is null ? null : Quantity * UnitCost.Value;
    public Project Project { get; set; } = null!;
    public Product Product { get; set; } = null!;
}
