namespace MetroCarpinteria.App.Models;

public enum ProductStockStatus
{
    Ok,
    Low,
    Out
}

/// <summary>
/// Regla única de "stock bajo" para toda la app: la lista de Inventario, el badge
/// del menú, la tarjeta de Inicio y Reportes.
/// </summary>
/// <remarks>
/// Se evalúa siempre en memoria a propósito. EF Core guarda los <c>decimal</c> como
/// TEXT en SQLite, así que compararlos del lado del SQL ordena alfabéticamente:
/// '9.0' &gt; '15.0' y '0.0' nunca es &lt;= 0. Antes cada contador traducía su propio
/// predicado a SQL y devolvía números distintos de los que mostraba la lista.
/// </remarks>
public static class StockRules
{
    public static ProductStockStatus GetStatus(decimal current, decimal minimum)
    {
        if (current <= 0)
        {
            return ProductStockStatus.Out;
        }

        return current <= minimum ? ProductStockStatus.Low : ProductStockStatus.Ok;
    }

    /// <summary>Stock en o por debajo del mínimo, incluyendo el que está en cero.</summary>
    public static bool IsLowOrOut(decimal current, decimal minimum) =>
        GetStatus(current, minimum) != ProductStockStatus.Ok;

    public static bool IsOut(decimal current) => current <= 0;
}

public sealed class ProductListItem
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public decimal CurrentStock { get; init; }
    public decimal MinimumStock { get; init; }
    public string Unit { get; init; } = ProductUnits.Unit;
    public decimal? CostPrice { get; init; }
    public bool IsArchived { get; init; }
    public ProductStockStatus Status { get; init; }

    public string StatusLabel => Status switch
    {
        ProductStockStatus.Low => "Stock bajo",
        ProductStockStatus.Out => "Sin stock",
        _ => "OK"
    };

    public string StockDisplay => Helpers.AppCulture.QuantityWithUnit(CurrentStock, Unit);
    public string MinimumDisplay => Helpers.AppCulture.QuantityWithUnit(MinimumStock, Unit);
    public string CostPriceDisplay => Helpers.AppCulture.Money(CostPrice);

    /// <summary>
    /// Nombre, precio y stock para los selectores de material: los tres datos que se
    /// necesitan para decidir, sin tener que ir hasta Inventario a mirarlos.
    /// </summary>
    public string PickerDisplay => CostPrice.HasValue
        ? $"{Name} — {CostPriceDisplay} — hay {StockDisplay}"
        : $"{Name} — hay {StockDisplay}";
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

    public string QuantityDisplay => Helpers.AppCulture.QuantityWithUnit(Quantity, Unit);
}
