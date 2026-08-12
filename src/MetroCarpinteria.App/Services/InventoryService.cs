using MetroCarpinteria.App.Data;
using MetroCarpinteria.App.Data.Entities;
using MetroCarpinteria.App.Helpers;
using MetroCarpinteria.App.Models;
using Microsoft.EntityFrameworkCore;

namespace MetroCarpinteria.App.Services;

public sealed class InventoryService
{
    private readonly DatabaseService _databaseService;

    public InventoryService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public IReadOnlyList<ProductListItem> GetProducts(bool includeArchived, bool lowStockOnly, string? search)
    {
        using var context = _databaseService.CreateContext();
        var query = context.Products.AsNoTracking().AsQueryable();

        if (!includeArchived)
        {
            query = query.Where(p => !p.IsArchived);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(p => EF.Functions.Like(p.Name, $"%{term}%"));
        }

        var products = query
            .OrderBy(p => p.Name)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.CurrentStock,
                p.MinimumStock,
                p.Unit,
                p.CostPrice,
                p.IsArchived
            })
            .ToList();

        var items = products
            .Select(p => new ProductListItem
            {
                Id = p.Id,
                Name = p.Name,
                CurrentStock = p.CurrentStock,
                MinimumStock = p.MinimumStock,
                Unit = p.Unit,
                CostPrice = p.CostPrice,
                IsArchived = p.IsArchived,
                Status = StockRules.GetStatus(p.CurrentStock, p.MinimumStock)
            })
            .ToList();

        if (lowStockOnly)
        {
            items = items
                .Where(p => !p.IsArchived && p.Status != ProductStockStatus.Ok)
                .ToList();
        }

        return items;
    }

    public IReadOnlyList<StockMovementItem> GetRecentMovements(int? productId = null, int limit = 50)
    {
        using var context = _databaseService.CreateContext();
        var query = context.StockMovements
            .AsNoTracking()
            .Include(m => m.Product)
            .AsQueryable();

        if (productId.HasValue)
        {
            query = query.Where(m => m.ProductId == productId.Value);
        }

        return query
            .OrderByDescending(m => m.CreatedAtUtc)
            .Take(limit)
            .Select(m => new StockMovementItem
            {
                Id = m.Id,
                ProductName = m.Product.Name,
                TypeLabel = m.Type == StockMovementType.In ? "Entrada" : "Salida",
                Quantity = m.Quantity,
                Unit = m.Product.Unit,
                Reason = m.Reason,
                CreatedAtLocal = m.CreatedAtUtc.ToLocalTime()
            })
            .ToList();
    }

    public Product CreateProduct(
        string name,
        decimal initialStock,
        decimal minimumStock,
        string unit,
        decimal? costPrice = null)
    {
        ValidateProductInput(name, minimumStock, unit, costPrice);
        unit = ProductUnits.Normalize(unit);

        if (initialStock < 0)
        {
            throw new InvalidOperationException("El stock inicial no puede ser negativo.");
        }

        using var context = _databaseService.CreateContext();
        using var transaction = context.Database.BeginTransaction();

        try
        {
            var now = DateTime.UtcNow;
            var product = new Product
            {
                Name = name.Trim(),
                CurrentStock = initialStock,
                MinimumStock = minimumStock,
                Unit = unit.Trim(),
                CostPrice = costPrice,
                IsArchived = false,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            context.Products.Add(product);
            context.SaveChanges();

            if (initialStock > 0)
            {
                context.StockMovements.Add(new StockMovement
                {
                    ProductId = product.Id,
                    Type = StockMovementType.In,
                    Quantity = initialStock,
                    Reason = "Stock inicial",
                    CreatedAtUtc = now
                });
                context.SaveChanges();
            }

            transaction.Commit();
            return product;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public void UpdateProduct(int id, string name, decimal minimumStock, string unit, decimal? costPrice = null)
    {
        ValidateProductInput(name, minimumStock, unit, costPrice);
        unit = ProductUnits.Normalize(unit);

        using var context = _databaseService.CreateContext();
        var product = context.Products.FirstOrDefault(p => p.Id == id)
            ?? throw new InvalidOperationException("Producto no encontrado.");

        product.Name = name.Trim();
        product.MinimumStock = minimumStock;
        product.Unit = unit.Trim();
        product.CostPrice = costPrice;
        product.UpdatedAtUtc = DateTime.UtcNow;
        context.SaveChanges();
    }

    public void ArchiveProduct(int id)
    {
        using var context = _databaseService.CreateContext();
        var product = context.Products.FirstOrDefault(p => p.Id == id)
            ?? throw new InvalidOperationException("Producto no encontrado.");

        product.IsArchived = true;
        product.UpdatedAtUtc = DateTime.UtcNow;
        context.SaveChanges();
    }

    public void RestoreProduct(int id)
    {
        using var context = _databaseService.CreateContext();
        var product = context.Products.FirstOrDefault(p => p.Id == id)
            ?? throw new InvalidOperationException("Producto no encontrado.");

        product.IsArchived = false;
        product.UpdatedAtUtc = DateTime.UtcNow;
        context.SaveChanges();
    }

    public void DeleteProduct(int id)
    {
        using var context = _databaseService.CreateContext();
        using var transaction = context.Database.BeginTransaction();

        try
        {
            var product = context.Products.FirstOrDefault(p => p.Id == id)
                ?? throw new InvalidOperationException("Producto no encontrado.");

            var hasMovements = context.StockMovements.Any(m => m.ProductId == id);
            if (hasMovements)
            {
                throw new InvalidOperationException(
                    "No se puede eliminar un producto con movimientos. Archivalo en su lugar.");
            }

            // Las líneas de presupuesto también apuntan al producto con FK RESTRICT:
            // sin este chequeo el borrado explotaría contra la base en vez de avisar.
            if (context.ProjectBudgetLines.Any(l => l.ProductId == id))
            {
                throw new InvalidOperationException(
                    "No se puede eliminar un producto usado en un presupuesto. Archivalo en su lugar.");
            }

            context.Products.Remove(product);
            context.SaveChanges();
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public void RegisterMovement(int productId, StockMovementType type, decimal quantity, string reason)
    {
        if (quantity <= 0)
        {
            throw new InvalidOperationException("La cantidad debe ser mayor a cero.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new InvalidOperationException("Indicá un motivo para el movimiento.");
        }

        using var context = _databaseService.CreateContext();
        using var transaction = context.Database.BeginTransaction();

        try
        {
            var product = context.Products.FirstOrDefault(p => p.Id == productId)
                ?? throw new InvalidOperationException("Producto no encontrado.");

            if (product.IsArchived)
            {
                throw new InvalidOperationException("No se pueden registrar movimientos en productos archivados.");
            }

            if (type == StockMovementType.Out && product.CurrentStock < quantity)
            {
                throw new InvalidOperationException(
                    "Stock insuficiente. Disponible: " +
                    AppCulture.QuantityWithUnit(product.CurrentStock, product.Unit));
            }

            product.CurrentStock = type == StockMovementType.In
                ? product.CurrentStock + quantity
                : product.CurrentStock - quantity;
            product.UpdatedAtUtc = DateTime.UtcNow;

            context.StockMovements.Add(new StockMovement
            {
                ProductId = productId,
                Type = type,
                Quantity = quantity,
                Reason = reason.Trim(),
                CreatedAtUtc = DateTime.UtcNow
            });

            context.SaveChanges();
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <summary>Cualquier referencia que impida borrar el producto.</summary>
    public bool HasMovements(int productId) => DescribeDeleteBlock(productId) is not null;

    /// <summary>
    /// Por qué no se puede borrar el producto, o <c>null</c> si se puede.
    /// </summary>
    /// <remarks>
    /// Devuelve el motivo y no un booleano porque es lo que el botón deshabilitado tiene
    /// que poder explicar. Un botón gris sin explicación deja al usuario probando cosas
    /// hasta que se rinde.
    /// </remarks>
    public string? DescribeDeleteBlock(int productId)
    {
        using var context = _databaseService.CreateContext();

        var movements = context.StockMovements.Count(m => m.ProductId == productId);
        var quoteLines = context.ProjectBudgetLines.Count(l => l.ProductId == productId);

        if (movements == 0 && quoteLines == 0)
        {
            return null;
        }

        var reasons = new List<string>();

        if (movements > 0)
        {
            reasons.Add(Phrases.Count(movements, "movimiento de stock", "movimientos de stock"));
        }

        if (quoteLines > 0)
        {
            reasons.Add(Phrases.Count(quoteLines, "presupuesto", "presupuestos"));
        }

        return $"No se puede eliminar: tiene {Phrases.JoinWithAnd(reasons)}. Archivalo en su lugar.";
    }

    private static void ValidateProductInput(string name, decimal minimumStock, string unit, decimal? costPrice)
    {
        if (costPrice is < 0)
        {
            throw new InvalidOperationException("El precio de costo no puede ser negativo.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("El nombre del producto es obligatorio.");
        }

        if (minimumStock < 0)
        {
            throw new InvalidOperationException("El stock mínimo no puede ser negativo.");
        }

        if (string.IsNullOrWhiteSpace(unit))
        {
            throw new InvalidOperationException("La unidad es obligatoria.");
        }
    }
}
