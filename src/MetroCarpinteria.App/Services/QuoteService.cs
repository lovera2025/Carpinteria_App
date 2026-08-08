using MetroCarpinteria.App.Data;
using MetroCarpinteria.App.Data.Entities;
using MetroCarpinteria.App.Models;
using Microsoft.EntityFrameworkCore;

namespace MetroCarpinteria.App.Services;

/// <summary>
/// Presupuestos: líneas de materiales, cálculo congelado, vigencia y aprobación.
/// </summary>
/// <remarks>
/// Un presupuesto es un proyecto en estado <see cref="ProjectStatus.Quote"/>. Sus líneas
/// (<see cref="ProjectBudgetLine"/>) son lo cotizado y <b>no tocan el inventario</b>;
/// recién al aprobar se generan los <see cref="ProjectMaterial"/> y los movimientos de
/// stock. Las dos listas conviven a propósito: una es lo prometido y la otra lo entregado.
/// </remarks>
public sealed class QuoteService
{
    private readonly DatabaseService _databaseService;
    private readonly SettingsService _settingsService;

    public QuoteService(DatabaseService databaseService, SettingsService settingsService)
    {
        _databaseService = databaseService;
        _settingsService = settingsService;
    }

    // --- Lectura -------------------------------------------------------------

    public IReadOnlyList<QuoteListItem> GetQuotes(
        QuoteFilter filter,
        string? search,
        bool includeApproved = false,
        bool includeArchived = false)
    {
        using var context = _databaseService.CreateContext();
        var query = context.Projects.AsNoTracking().AsQueryable();

        if (!includeArchived)
        {
            query = query.Where(p => !p.IsArchived);
        }

        if (!includeApproved)
        {
            query = query.Where(p => p.Status == ProjectStatus.Quote || p.Status == ProjectStatus.Rejected);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(p => EF.Functions.Like(p.Title, $"%{term}%")
                || EF.Functions.Like(p.ClientName, $"%{term}%"));
        }

        var rows = query
            .OrderByDescending(p => p.UpdatedAtUtc)
            .Select(p => new
            {
                p.Id,
                p.Title,
                p.ClientName,
                p.Status,
                p.Budget,
                p.IsArchived,
                p.QuotedAtUtc,
                p.QuoteValidUntilUtc,
                LineCount = context.ProjectBudgetLines.Count(l => l.ProjectId == p.Id)
            })
            .ToList();

        var today = DateTime.Today;

        var items = rows
            .Select(p => new QuoteListItem
            {
                Id = p.Id,
                Title = p.Title,
                ClientName = p.ClientName,
                Status = p.Status,
                Budget = p.Budget,
                IsArchived = p.IsArchived,
                LineCount = p.LineCount,
                QuotedAtLocal = ToLocalDate(p.QuotedAtUtc),
                ValidUntilLocal = ToLocalDate(p.QuoteValidUntilUtc),
                Freshness = QuoteRules.GetFreshness(ToLocalDate(p.QuoteValidUntilUtc), today)
            })
            .ToList();

        return filter switch
        {
            QuoteFilter.Rejected => items.Where(i => i.Status == ProjectStatus.Rejected).ToList(),
            QuoteFilter.Current => FilterByFreshness(items, QuoteFreshness.Current, includeNoExpiry: true),
            QuoteFilter.DueSoon => FilterByFreshness(items, QuoteFreshness.DueSoon, includeNoExpiry: false),
            QuoteFilter.Expired => FilterByFreshness(items, QuoteFreshness.Expired, includeNoExpiry: false),
            _ => items
        };
    }

    public QuoteDetail? GetDetail(int projectId)
    {
        using var context = _databaseService.CreateContext();

        var project = context.Projects.AsNoTracking().FirstOrDefault(p => p.Id == projectId);
        if (project is null)
        {
            return null;
        }

        var lines = context.ProjectBudgetLines
            .AsNoTracking()
            .Include(l => l.Product)
            .Where(l => l.ProjectId == projectId)
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.Id)
            .ToList()
            .Select(l => new QuoteLineItem
            {
                Id = l.Id,
                ProductId = l.ProductId,
                Description = l.Description,
                Unit = l.Unit,
                Quantity = l.Quantity,
                UnitCost = l.UnitCost,
                AppliedQuantity = l.AppliedQuantity,
                AvailableStock = l.Product?.CurrentStock,
                SortOrder = l.SortOrder
            })
            .ToList();

        var rates = ReadRates(project);
        var materialsTotal = lines.Sum(l => l.LineTotal);

        return new QuoteDetail
        {
            Id = project.Id,
            Title = project.Title,
            ClientName = project.ClientName,
            Description = project.Description,
            Status = project.Status,
            IsArchived = project.IsArchived,
            Budget = project.Budget,
            QuotedAtLocal = ToLocalDate(project.QuotedAtUtc),
            ValidUntilLocal = ToLocalDate(project.QuoteValidUntilUtc),
            QuotedMaterialsCost = project.QuotedMaterialsCost,
            EstimatedDays = project.EstimatedDays,
            DailyRate = project.DailyRate,
            Rates = rates,
            Lines = lines,
            Breakdown = RebuildBreakdown(project, rates, materialsTotal)
        };
    }

    public QuotePendingSummary GetPendingSummary()
    {
        using var context = _databaseService.CreateContext();

        var dates = context.Projects
            .AsNoTracking()
            .Where(p => !p.IsArchived && p.Status == ProjectStatus.Quote)
            .Select(p => p.QuoteValidUntilUtc)
            .AsEnumerable()
            .Select(ToLocalDate)
            .ToList();

        var today = DateTime.Today;
        var freshness = dates.Select(d => QuoteRules.GetFreshness(d, today)).ToList();

        return new QuotePendingSummary
        {
            Pending = freshness.Count(f => f != QuoteFreshness.Expired),
            DueSoon = freshness.Count(f => f == QuoteFreshness.DueSoon),
            Expired = freshness.Count(f => f == QuoteFreshness.Expired)
        };
    }

    // --- Alta y edición ------------------------------------------------------

    public Project CreateQuote(string title, string clientName, string? description, DateTime? validUntilLocal = null)
    {
        ValidateHeader(title, clientName);

        using var context = _databaseService.CreateContext();
        var now = DateTime.UtcNow;

        var validity = validUntilLocal
            ?? DateTime.Today.AddDays(Math.Max(0, _settingsService.Current.DefaultQuoteValidityDays));

        var project = new Project
        {
            Title = title.Trim(),
            ClientName = clientName.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Status = ProjectStatus.Quote,
            QuotedAtUtc = now,
            QuoteValidUntilUtc = ToUtcFromLocalDate(validity),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        context.Projects.Add(project);
        context.SaveChanges();
        return project;
    }

    public void UpdateQuote(int projectId, string title, string clientName, string? description, DateTime? validUntilLocal)
    {
        ValidateHeader(title, clientName);

        using var context = _databaseService.CreateContext();
        var project = RequireEditableQuote(context, projectId);

        project.Title = title.Trim();
        project.ClientName = clientName.Trim();
        project.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        project.QuoteValidUntilUtc = ToUtcFromLocalDate(validUntilLocal);
        project.UpdatedAtUtc = DateTime.UtcNow;
        context.SaveChanges();
    }

    public void AddInventoryLine(int projectId, int productId, decimal quantity, decimal? unitCost = null)
    {
        ValidateQuantity(quantity);

        using var context = _databaseService.CreateContext();
        var project = RequireEditableQuote(context, projectId);

        var product = context.Products.FirstOrDefault(p => p.Id == productId)
            ?? throw new InvalidOperationException("Producto no encontrado.");

        if (product.IsArchived)
        {
            throw new InvalidOperationException("El producto está archivado.");
        }

        var price = unitCost ?? product.CostPrice ?? 0m;
        ValidateUnitCost(price);

        // Si el producto todavía no tenía precio de costo, se aprovecha el que se acaba
        // de tipear para no volver a preguntarlo la próxima vez.
        if (product.CostPrice is null && price > 0)
        {
            product.CostPrice = price;
            product.UpdatedAtUtc = DateTime.UtcNow;
        }

        AddLine(context, project, product.Id, product.Name, product.Unit, quantity, price);
        context.SaveChanges();
    }

    public void AddLooseLine(
        int projectId,
        string description,
        string unit,
        decimal quantity,
        decimal unitCost,
        bool saveToCatalog)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new InvalidOperationException("El detalle del material es obligatorio.");
        }

        ValidateQuantity(quantity);
        ValidateUnitCost(unitCost);

        using var context = _databaseService.CreateContext();
        using var transaction = context.Database.BeginTransaction();

        try
        {
            var project = RequireEditableQuote(context, projectId);
            var normalizedUnit = ProductUnits.Normalize(unit);
            int? productId = null;

            if (saveToCatalog)
            {
                var now = DateTime.UtcNow;
                var product = new Product
                {
                    Name = description.Trim(),
                    CurrentStock = 0m,
                    MinimumStock = 0m,
                    Unit = normalizedUnit,
                    CostPrice = unitCost,
                    IsArchived = false,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };

                // Arranca en cero y sin movimiento de stock: todavía no se compró nada,
                // solo queda anotado en el catálogo para la próxima vez.
                context.Products.Add(product);
                context.SaveChanges();
                productId = product.Id;
            }

            AddLine(context, project, productId, description.Trim(), normalizedUnit, quantity, unitCost);
            context.SaveChanges();
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public void UpdateLine(int lineId, decimal quantity, decimal unitCost)
    {
        ValidateQuantity(quantity);
        ValidateUnitCost(unitCost);

        using var context = _databaseService.CreateContext();
        var line = context.ProjectBudgetLines.FirstOrDefault(l => l.Id == lineId)
            ?? throw new InvalidOperationException("Línea no encontrada.");

        var project = RequireEditableQuote(context, line.ProjectId);

        line.Quantity = quantity;
        line.UnitCost = unitCost;
        project.UpdatedAtUtc = DateTime.UtcNow;
        context.SaveChanges();
    }

    public void RemoveLine(int lineId)
    {
        using var context = _databaseService.CreateContext();
        var line = context.ProjectBudgetLines.FirstOrDefault(l => l.Id == lineId)
            ?? throw new InvalidOperationException("Línea no encontrada.");

        var project = RequireEditableQuote(context, line.ProjectId);

        context.ProjectBudgetLines.Remove(line);
        project.UpdatedAtUtc = DateTime.UtcNow;
        context.SaveChanges();
    }

    // --- Cálculo -------------------------------------------------------------

    /// <summary>
    /// Calcula y congela las entradas en el proyecto. Guardar las entradas y no los
    /// importes permite reconstruir el desglose sin que se mueva si después cambian
    /// los porcentajes por defecto del taller.
    /// </summary>
    public BudgetBreakdown SaveCalculation(
        int projectId,
        decimal materialsCost,
        decimal days,
        decimal dailyRate,
        BudgetRates rates)
    {
        var breakdown = BudgetCalculatorService.Calculate(new BudgetInput
        {
            MaterialsCost = materialsCost,
            Days = days,
            DailyRate = dailyRate,
            Rates = rates
        });

        using var context = _databaseService.CreateContext();
        var project = RequireEditableQuote(context, projectId);

        project.QuotedMaterialsCost = breakdown.MaterialsCost;
        project.EstimatedDays = breakdown.Days;
        project.DailyRate = breakdown.DailyRate;
        project.WastePercent = breakdown.Rates.WastePercent;
        project.ToolWearPercent = breakdown.Rates.ToolWearPercent;
        project.OverheadPercent = breakdown.Rates.OverheadPercent;
        project.ProfitPercent = breakdown.Rates.ProfitPercent;
        project.Budget = breakdown.FinalPrice;
        project.UpdatedAtUtc = DateTime.UtcNow;

        context.SaveChanges();
        return breakdown;
    }

    /// <summary>Ajuste manual del precio final, para redondear lo que se le pasa al cliente.</summary>
    public void SetFinalPrice(int projectId, decimal? finalPrice)
    {
        if (finalPrice is < 0)
        {
            throw new InvalidOperationException("El precio final no puede ser negativo.");
        }

        using var context = _databaseService.CreateContext();
        var project = RequireEditableQuote(context, projectId);

        project.Budget = finalPrice;
        project.UpdatedAtUtc = DateTime.UtcNow;
        context.SaveChanges();
    }

    // --- Ciclo de vida -------------------------------------------------------

    public QuoteApprovalResult ApproveQuote(int projectId)
    {
        using var context = _databaseService.CreateContext();
        using var transaction = context.Database.BeginTransaction();

        try
        {
            var project = context.Projects.FirstOrDefault(p => p.Id == projectId)
                ?? throw new InvalidOperationException("Presupuesto no encontrado.");

            if (project.IsArchived)
            {
                throw new InvalidOperationException("El presupuesto está archivado.");
            }

            if (project.Status != ProjectStatus.Quote)
            {
                throw new InvalidOperationException("Este presupuesto ya fue aprobado o rechazado.");
            }

            var result = ApplyLinesToStock(context, project);

            project.Status = ProjectStatus.InProgress;
            project.UpdatedAtUtc = DateTime.UtcNow;

            context.SaveChanges();
            transaction.Commit();
            return result;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <summary>Reintenta descontar lo que quedó pendiente cuando no alcanzaba el stock.</summary>
    public QuoteApprovalResult ApplyPendingStock(int projectId)
    {
        using var context = _databaseService.CreateContext();
        using var transaction = context.Database.BeginTransaction();

        try
        {
            var project = context.Projects.FirstOrDefault(p => p.Id == projectId)
                ?? throw new InvalidOperationException("Proyecto no encontrado.");

            if (project.Status is ProjectStatus.Quote or ProjectStatus.Rejected)
            {
                throw new InvalidOperationException("El presupuesto todavía no fue aprobado.");
            }

            var result = ApplyLinesToStock(context, project);
            project.UpdatedAtUtc = DateTime.UtcNow;

            context.SaveChanges();
            transaction.Commit();
            return result;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public void RejectQuote(int projectId)
    {
        using var context = _databaseService.CreateContext();
        var project = RequireEditableQuote(context, projectId);

        project.Status = ProjectStatus.Rejected;
        project.UpdatedAtUtc = DateTime.UtcNow;
        context.SaveChanges();
    }

    public void ReopenQuote(int projectId)
    {
        using var context = _databaseService.CreateContext();
        var project = context.Projects.FirstOrDefault(p => p.Id == projectId)
            ?? throw new InvalidOperationException("Presupuesto no encontrado.");

        if (project.Status != ProjectStatus.Rejected)
        {
            throw new InvalidOperationException("Solo se puede reabrir un presupuesto rechazado.");
        }

        project.Status = ProjectStatus.Quote;
        project.UpdatedAtUtc = DateTime.UtcNow;
        context.SaveChanges();
    }

    /// <summary>Copia el presupuesto con los precios de hoy, para volver a cotizar el mismo trabajo.</summary>
    public int DuplicateQuote(int projectId)
    {
        using var context = _databaseService.CreateContext();
        using var transaction = context.Database.BeginTransaction();

        try
        {
            var source = context.Projects.AsNoTracking().FirstOrDefault(p => p.Id == projectId)
                ?? throw new InvalidOperationException("Presupuesto no encontrado.");

            var sourceLines = context.ProjectBudgetLines
                .AsNoTracking()
                .Include(l => l.Product)
                .Where(l => l.ProjectId == projectId)
                .OrderBy(l => l.SortOrder)
                .ToList();

            var now = DateTime.UtcNow;
            var copy = new Project
            {
                Title = $"{source.Title} (copia)",
                ClientName = source.ClientName,
                Description = source.Description,
                Status = ProjectStatus.Quote,
                QuotedAtUtc = now,
                QuoteValidUntilUtc = ToUtcFromLocalDate(
                    DateTime.Today.AddDays(Math.Max(0, _settingsService.Current.DefaultQuoteValidityDays))),
                EstimatedDays = source.EstimatedDays,
                DailyRate = source.DailyRate,
                WastePercent = source.WastePercent,
                ToolWearPercent = source.ToolWearPercent,
                OverheadPercent = source.OverheadPercent,
                ProfitPercent = source.ProfitPercent,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            context.Projects.Add(copy);
            context.SaveChanges();

            foreach (var line in sourceLines)
            {
                // Los ítems del catálogo se recotizan al precio de hoy; los sueltos
                // conservan el suyo porque no hay de dónde refrescarlo.
                var unitCost = line.Product?.CostPrice ?? line.UnitCost;

                context.ProjectBudgetLines.Add(new ProjectBudgetLine
                {
                    ProjectId = copy.Id,
                    ProductId = line.ProductId,
                    Description = line.Product?.Name ?? line.Description,
                    Unit = line.Product?.Unit ?? line.Unit,
                    Quantity = line.Quantity,
                    UnitCost = unitCost,
                    SortOrder = line.SortOrder,
                    CreatedAtUtc = now
                });
            }

            context.SaveChanges();

            var rates = ReadRates(copy);
            if (rates is not null && copy.EstimatedDays.HasValue && copy.DailyRate.HasValue)
            {
                var materials = context.ProjectBudgetLines
                    .Where(l => l.ProjectId == copy.Id)
                    .AsEnumerable()
                    .Sum(l => l.LineTotal);

                var breakdown = BudgetCalculatorService.Calculate(new BudgetInput
                {
                    MaterialsCost = materials,
                    Days = copy.EstimatedDays.Value,
                    DailyRate = copy.DailyRate.Value,
                    Rates = rates
                });

                copy.QuotedMaterialsCost = breakdown.MaterialsCost;
                copy.Budget = breakdown.FinalPrice;
                context.SaveChanges();
            }

            transaction.Commit();
            return copy.Id;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public bool HasBudgetLines(int projectId)
    {
        using var context = _databaseService.CreateContext();
        return context.ProjectBudgetLines.Any(l => l.ProjectId == projectId);
    }

    // --- Internos ------------------------------------------------------------

    /// <summary>
    /// Descuenta del inventario lo que haya disponible para cada línea pendiente y
    /// devuelve lo que faltó. No bloquea: el trabajo tiene que poder arrancar aunque
    /// falte una bisagra.
    /// </summary>
    private static QuoteApprovalResult ApplyLinesToStock(AppDbContext context, Project project)
    {
        var lines = context.ProjectBudgetLines
            .Include(l => l.Product)
            .Where(l => l.ProjectId == project.Id && l.ProductId != null && l.AppliedToStockAtUtc == null)
            .OrderBy(l => l.SortOrder)
            .ToList();

        var now = DateTime.UtcNow;
        var shortfalls = new List<QuoteApprovalShortfall>();
        var discounted = 0;

        foreach (var line in lines)
        {
            var product = line.Product;
            if (product is null)
            {
                continue;
            }

            var pending = line.Quantity - line.AppliedQuantity;
            if (pending <= 0)
            {
                line.AppliedToStockAtUtc = now;
                continue;
            }

            var available = Math.Max(0m, product.CurrentStock);
            var toDiscount = Math.Min(available, pending);

            if (toDiscount > 0)
            {
                product.CurrentStock -= toDiscount;
                product.UpdatedAtUtc = now;

                context.StockMovements.Add(new StockMovement
                {
                    ProductId = product.Id,
                    Type = StockMovementType.Out,
                    Quantity = toDiscount,
                    Reason = $"Presupuesto aprobado: {project.Title}",
                    CreatedAtUtc = now
                });

                context.ProjectMaterials.Add(new ProjectMaterial
                {
                    ProjectId = project.Id,
                    ProductId = product.Id,
                    Quantity = toDiscount,
                    AssignedAtUtc = now
                });

                line.AppliedQuantity += toDiscount;
                discounted++;
            }

            if (line.AppliedQuantity >= line.Quantity)
            {
                line.AppliedToStockAtUtc = now;
            }
            else
            {
                shortfalls.Add(new QuoteApprovalShortfall
                {
                    Description = line.Description,
                    Missing = line.Quantity - line.AppliedQuantity,
                    Unit = line.Unit
                });
            }
        }

        return new QuoteApprovalResult
        {
            ProjectId = project.Id,
            DiscountedLines = discounted,
            Shortfalls = shortfalls
        };
    }

    private static void AddLine(
        AppDbContext context,
        Project project,
        int? productId,
        string description,
        string unit,
        decimal quantity,
        decimal unitCost)
    {
        var nextOrder = context.ProjectBudgetLines
            .Where(l => l.ProjectId == project.Id)
            .Select(l => (int?)l.SortOrder)
            .Max() ?? 0;

        context.ProjectBudgetLines.Add(new ProjectBudgetLine
        {
            ProjectId = project.Id,
            ProductId = productId,
            Description = description,
            Unit = unit,
            Quantity = quantity,
            UnitCost = unitCost,
            SortOrder = nextOrder + 1,
            CreatedAtUtc = DateTime.UtcNow
        });

        project.UpdatedAtUtc = DateTime.UtcNow;
    }

    private static Project RequireEditableQuote(AppDbContext context, int projectId)
    {
        var project = context.Projects.FirstOrDefault(p => p.Id == projectId)
            ?? throw new InvalidOperationException("Presupuesto no encontrado.");

        if (project.IsArchived)
        {
            throw new InvalidOperationException("El presupuesto está archivado.");
        }

        if (project.Status != ProjectStatus.Quote)
        {
            throw new InvalidOperationException(
                "Solo se puede editar un presupuesto que todavía no fue aprobado ni rechazado.");
        }

        return project;
    }

    private static BudgetRates? ReadRates(Project project)
    {
        if (project.WastePercent is null
            || project.ToolWearPercent is null
            || project.OverheadPercent is null
            || project.ProfitPercent is null)
        {
            return null;
        }

        return new BudgetRates
        {
            WastePercent = project.WastePercent.Value,
            ToolWearPercent = project.ToolWearPercent.Value,
            OverheadPercent = project.OverheadPercent.Value,
            ProfitPercent = project.ProfitPercent.Value
        };
    }

    private static BudgetBreakdown? RebuildBreakdown(Project project, BudgetRates? rates, decimal materialsFromLines)
    {
        if (rates is null || project.EstimatedDays is null || project.DailyRate is null)
        {
            return null;
        }

        return BudgetCalculatorService.Calculate(new BudgetInput
        {
            MaterialsCost = project.QuotedMaterialsCost ?? materialsFromLines,
            Days = project.EstimatedDays.Value,
            DailyRate = project.DailyRate.Value,
            Rates = rates
        });
    }

    private static List<QuoteListItem> FilterByFreshness(
        IEnumerable<QuoteListItem> items,
        QuoteFreshness freshness,
        bool includeNoExpiry)
    {
        return items
            .Where(i => i.Status != ProjectStatus.Rejected
                && (i.Freshness == freshness
                    || (includeNoExpiry && i.Freshness == QuoteFreshness.NoExpiry)))
            .ToList();
    }

    private static void ValidateHeader(string title, string clientName)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new InvalidOperationException("El título del presupuesto es obligatorio.");
        }

        if (string.IsNullOrWhiteSpace(clientName))
        {
            throw new InvalidOperationException("El nombre del cliente es obligatorio.");
        }
    }

    private static void ValidateQuantity(decimal quantity)
    {
        if (quantity <= 0)
        {
            throw new InvalidOperationException("La cantidad debe ser mayor a cero.");
        }
    }

    private static void ValidateUnitCost(decimal unitCost)
    {
        if (unitCost < 0)
        {
            throw new InvalidOperationException("El precio unitario no puede ser negativo.");
        }
    }

    private static DateTime? ToUtcFromLocalDate(DateTime? localDate) => localDate.HasValue
        ? DateTime.SpecifyKind(localDate.Value.Date, DateTimeKind.Local).ToUniversalTime()
        : null;

    private static DateTime? ToLocalDate(DateTime? utc) => utc.HasValue
        ? DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc).ToLocalTime().Date
        : null;
}
