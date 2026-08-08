namespace MetroCarpinteria.App.Data.Entities;

/// <summary>
/// Un renglón de materiales de un presupuesto. No mueve stock: es lo que se le cotizó
/// al cliente. Lo que realmente salió del depósito son los <see cref="ProjectMaterial"/>,
/// que se crean recién al aprobar.
/// </summary>
public class ProjectBudgetLine
{
    public int Id { get; set; }
    public int ProjectId { get; set; }

    /// <summary>Null cuando es un ítem suelto que no está en el catálogo.</summary>
    public int? ProductId { get; set; }

    /// <summary>Nombre congelado al agregar la línea, por si el producto se renombra después.</summary>
    public string Description { get; set; } = string.Empty;

    public string Unit { get; set; } = Models.ProductUnits.Unit;
    public decimal Quantity { get; set; }

    /// <summary>
    /// Precio unitario congelado. También para los ítems del inventario: si mañana sube
    /// el pino, el presupuesto de marzo tiene que seguir mostrando el precio de marzo.
    /// </summary>
    public decimal UnitCost { get; set; }

    public int SortOrder { get; set; }

    /// <summary>
    /// Cuánto de esta línea ya salió del inventario. Si al aprobar no alcanzaba el stock
    /// se descuenta lo que hay y queda un pendiente; sin este dato, reintentar después
    /// volvería a descontar la cantidad entera.
    /// </summary>
    public decimal AppliedQuantity { get; set; }

    /// <summary>Se completa cuando la línea quedó descontada del todo.</summary>
    public DateTime? AppliedToStockAtUtc { get; set; }

    public decimal PendingQuantity => Math.Max(0m, Quantity - AppliedQuantity);

    public DateTime CreatedAtUtc { get; set; }

    public Project Project { get; set; } = null!;
    public Product? Product { get; set; }

    public decimal LineTotal => Math.Round(Quantity * UnitCost, 2, MidpointRounding.AwayFromZero);
}
