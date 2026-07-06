namespace MetroCarpinteria.App.Models;

public enum ProductStockStatus
{
    Ok,
    Low,
    Out
}

public sealed class ProductListItem
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public decimal CurrentStock { get; init; }
    public decimal MinimumStock { get; init; }
    public string Unit { get; init; } = ProductUnits.Unit;
    public bool IsArchived { get; init; }
    public ProductStockStatus Status { get; init; }

    public string StatusLabel => Status switch
    {
        ProductStockStatus.Low => "Stock bajo",
        ProductStockStatus.Out => "Sin stock",
        _ => "OK"
    };

    public string StockDisplay => $"{CurrentStock:N2} {Unit}";
    public string MinimumDisplay => $"{MinimumStock:N2} {Unit}";
}

public sealed class StockMovementItem
{
    public int Id { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string TypeLabel { get; init; } = string.Empty;
    public decimal Quantity { get; init; }
    public string Unit { get; init; } = ProductUnits.Unit;
    public string Reason { get; init; } = string.Empty;
    public DateTime CreatedAtLocal { get; init; }

    public string QuantityDisplay => $"{Quantity:N2} {Unit}";
}
