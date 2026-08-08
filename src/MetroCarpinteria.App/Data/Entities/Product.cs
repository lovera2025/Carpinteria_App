namespace MetroCarpinteria.App.Data.Entities;

using MetroCarpinteria.App.Models;

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal CurrentStock { get; set; }
    public decimal MinimumStock { get; set; }
    public string Unit { get; set; } = ProductUnits.Unit;

    /// <summary>Precio de costo por unidad. Se usa para prellenar las líneas de presupuesto.</summary>
    public decimal? CostPrice { get; set; }

    public bool IsArchived { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
