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

    /// <summary>
    /// Cuánto se le sumó al precio del trabajo por este material. Null es «lo puso él».
    /// </summary>
    /// <remarks>
    /// Es la decisión que la app tomaba sola y siempre para el mismo lado: cargar madera
    /// después de aprobar salía del bolsillo del taller, sin preguntar y sin que se viera.
    /// A veces es así —calculó de menos y lo absorbe—, y a veces el cliente pidió algo más
    /// y se le cobra. Guardar el importe y no un tilde permite además que lo edite: la app
    /// propone la cuenta, pero el número final lo pone él.
    /// </remarks>
    public decimal? BilledAmount { get; set; }

    public DateTime AssignedAtUtc { get; set; }

    /// <summary>Se lo cobró al cliente en vez de ponerlo él.</summary>
    public bool WasBilled => BilledAmount is > 0m;

    /// <summary>Lo que costó este material, con el precio congelado. Null si no se sabe.</summary>
    public decimal? LineCost => UnitCost is null ? null : Quantity * UnitCost.Value;
    public Project Project { get; set; } = null!;
    public Product Product { get; set; } = null!;
}
