using MetroCarpinteria.App.Data;
using MetroCarpinteria.App.Data.Entities;
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
                IsArchived = p.IsArchived,
                Status = GetStockStatus(p.CurrentStock, p.MinimumStock)
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

    public Product CreateProduct(string name, decimal initialStock, decimal minimumStock, string unit)
    {
        ValidateProductInput(name, minimumStock, unit);
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

    public void UpdateProduct(int id, string name, decimal minimumStock, string unit)
    {
        ValidateProductInput(name, minimumStock, unit);
        unit = ProductUnits.Normalize(unit);

        using var context = _databaseService.CreateContext();
        var product = context.Products.FirstOrDefault(p => p.Id == id)
            ?? throw new InvalidOperationException("Producto no encontrado.");

        product.Name = name.Trim();
        product.MinimumStock = minimumStock;
        product.Unit = unit.Trim();
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
                    $"Stock insuficiente. Disponible: {product.CurrentStock:N2} {product.Unit}");
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

    public bool HasMovements(int productId)
    {
        using var context = _databaseService.CreateContext();
        return context.StockMovements.Any(m => m.ProductId == productId);
    }

    private static void ValidateProductInput(string name, decimal minimumStock, string unit)
    {
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

    private static ProductStockStatus GetStockStatus(decimal current, decimal minimum)
    {
        if (current <= 0)
        {
            return ProductStockStatus.Out;
        }

        if (current <= minimum)
        {
            return ProductStockStatus.Low;
        }

        return ProductStockStatus.Ok;
    }
}
