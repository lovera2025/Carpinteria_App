namespace MetroCarpinteria.App.Data.Entities;

public class StockMovement
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public StockMovementType Type { get; set; }
    public decimal Quantity { get; set; }

    /// <summary>
    /// La unidad al momento del movimiento, congelada.
    /// </summary>
    /// <remarks>
    /// Antes el historial la leía del producto vivo, así que corregir la unidad de un
    /// producto —de «Metro» a «Metro cuadrado», que es justo lo que uno hace al notar que lo
    /// cargó mal— reescribía todo el pasado: un movimiento de 1500 u. pasaba a leerse como
    /// 1500 m². La cantidad no cambiaba, pero el historial empezaba a decir otra cosa. Misma
    /// razón por la que <see cref="ProjectBudgetLine.Unit"/> también está congelada.
    /// </remarks>
    public string Unit { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
