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
                ValidUntilLocal = ToLocalDate(p.QuoteValidUntilUtc)
            })
            .ToList();

        // El filtro se aplica en memoria porque la vigencia se deriva de la fecha de hoy
        // y no de una columna.

        return filter switch
        {
            QuoteFilter.Rejected => items.Where(i => i.Status == ProjectStatus.Rejected).ToList(),
            QuoteFilter.Current => FilterByFreshness(items, QuoteFreshness.Current, includeNoExpiry: true),
            QuoteFilter.DueSoon => FilterByFreshness(items, QuoteFreshness.DueSoon, includeNoExpiry: false),
            QuoteFilter.Expired => FilterByFreshness(items, QuoteFreshness.Expired, includeNoExpiry: false),
            _ => items
        };
    }

    /// <summary>
    /// Una sola fila de la lista, ya actualizada.
    /// </summary>
    /// <remarks>
    /// Existe para poder refrescar el renglón del presupuesto que se está editando sin
    /// recargar la lista entera. Recargarla vaciaba la colección, y eso hacía que la grilla
    /// reemitiera la selección y el formulario se recargara encima de lo que el usuario
    /// estaba tipeando.
    /// </remarks>
    public QuoteListItem? GetListItem(int projectId)
    {
        using var context = _databaseService.CreateContext();

        return context.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
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
            .AsEnumerable()
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
                ValidUntilLocal = ToLocalDate(p.QuoteValidUntilUtc)
            })
            .FirstOrDefault();
    }

    public QuoteDetail? GetDetail(int projectId)
    {
        using var context = _databaseService.CreateContext();

        var project = context.Projects.AsNoTracking().FirstOrDefault(p => p.Id == projectId);
        if (project is null)
        {
            return null;
        }

        // AsEnumerable antes de tocar cantidades: en las instalaciones viejas las
        // columnas de decimales son TEXT y cualquier comparación que quede en SQL se
        // resuelve como texto ('9.0' > '15.0').
        var rows = context.ProjectBudgetLines
            .AsNoTracking()
            .Include(l => l.Product)
            .Where(l => l.ProjectId == projectId)
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.Id)
            .AsEnumerable()
            .ToList();

        // El faltante se mide sobre el total pedido de cada producto, no línea por
        // línea: dos líneas de 6 contra un stock de 10 alcanzan por separado y no
        // juntas, y al aprobar se descuenta la suma.
        var pendingByProduct = rows
            .Where(l => l.ProductId.HasValue)
            .GroupBy(l => l.ProductId!.Value)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(l => Math.Max(0m, l.Quantity - l.AppliedQuantity)));

        var lines = rows
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
                SortOrder = l.SortOrder,
                HasStockWarning = l.Product is not null
                    && l.ProductId.HasValue
                    && l.Product.CurrentStock < pendingByProduct[l.ProductId.Value]
            })
            .ToList();

        var rates = ReadRates(project);
        var materialsTotal = lines.Sum(l => l.LineTotal);
        var breakdown = RebuildBreakdown(project, rates, materialsTotal);
        var terms = ReadTerms(project);

        return new QuoteDetail
        {
            Terms = terms,
            Commercial = breakdown is null
                ? null
                : CommercialTermsService.Apply(breakdown.FinalPrice, terms),
            Payments = ReadPayments(context, projectId),
            Id = project.Id,
            Title = project.Title,
            ClientName = project.ClientName,
            ClientId = project.ClientId,
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
            Breakdown = breakdown
        };
    }

    private static CommercialTerms ReadTerms(Project project) => new()
    {
        VatPercent = project.VatPercent,
        DiscountMode = project.DiscountMode ?? DiscountMode.None,
        DiscountValue = project.DiscountValue ?? 0m
    };

    private static List<ProjectPaymentItem> ReadPayments(AppDbContext context, int projectId) =>
        context.ProjectPayments
            .AsNoTracking()
            .Where(p => p.ProjectId == projectId)
            .OrderBy(p => p.CreatedAtUtc)
            .AsEnumerable()
            .Select(p => new ProjectPaymentItem
            {
                Id = p.Id,
                Kind = p.Kind,
                Amount = p.Amount,
                Method = p.Method,
                Notes = p.Notes,
                CreatedAtLocal = DateTime.SpecifyKind(p.CreatedAtUtc, DateTimeKind.Utc).ToLocalTime(),
                IsLinkedToCash = p.CashMovementId.HasValue
            })
            .ToList();

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

    /// <summary>
    /// Engancha el presupuesto a una ficha de cliente.
    /// </summary>
    /// <remarks>
    /// <see cref="Project.ClientName"/> se actualiza también, porque es el nombre que va a
    /// salir impreso: cambiar de cliente sin cambiar lo que dice el papel dejaría los dos
    /// datos contándose historias distintas. Lo que no se toca nunca es el nombre de un
    /// presupuesto <em>ya entregado</em>, y de eso se ocupa que esto solo corra sobre
    /// presupuestos editables.
    /// </remarks>
    public void AssignClient(int projectId, int? clientId)
    {
        using var context = _databaseService.CreateContext();
        var project = RequireEditableQuote(context, projectId);

        if (clientId is null)
        {
            project.ClientId = null;
        }
        else
        {
            var client = context.Clients.FirstOrDefault(c => c.Id == clientId.Value)
                ?? throw new InvalidOperationException("Cliente no encontrado.");

            project.ClientId = client.Id;
            project.ClientName = client.Name;
        }

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

        // Budget es lo que el cliente paga: el precio calculado ya con descuento e IVA.
        // Es el número que necesitan Caja, Reportes y el saldo de la seña, y por eso vale
        // más que guardar el precio pelado y que cada pantalla rehaga la cuenta.
        project.Budget = CommercialTermsService.Apply(breakdown.FinalPrice, ReadTerms(project)).Total;
        project.UpdatedAtUtc = DateTime.UtcNow;

        context.SaveChanges();
        return breakdown;
    }

    /// <summary>
    /// Guarda el IVA y el descuento pactados y recalcula el total.
    /// </summary>
    /// <remarks>
    /// Igual que el resto del cálculo, se guardan las entradas y no los importes: el
    /// desglose comercial se reconstruye cada vez que se muestra el presupuesto.
    /// </remarks>
    public CommercialBreakdown SaveCommercialTerms(int projectId, CommercialTerms terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        ValidateTerms(terms);

        using var context = _databaseService.CreateContext();
        var project = RequireEditableQuote(context, projectId);

        project.VatPercent = terms.VatPercent is > 0 ? terms.VatPercent : null;
        project.DiscountMode = terms.DiscountValue > 0 ? terms.DiscountMode : null;
        project.DiscountValue = terms.DiscountValue > 0 ? terms.DiscountValue : null;

        var lines = context.ProjectBudgetLines
            .Where(l => l.ProjectId == projectId)
            .AsEnumerable()
            .Sum(l => l.LineTotal);

        var breakdown = RebuildBreakdown(project, ReadRates(project), lines);
        var commercial = CommercialTermsService.Apply(breakdown?.FinalPrice ?? 0m, terms);

        // Sin cálculo todavía no hay precio que ajustar: las condiciones quedan guardadas
        // y se aplican solas en cuanto se calcule.
        if (breakdown is not null)
        {
            project.Budget = commercial.Total;
        }

        project.UpdatedAtUtc = DateTime.UtcNow;
        context.SaveChanges();

        return commercial;
    }

    private static void ValidateTerms(CommercialTerms terms)
    {
        if (terms.VatPercent is < 0)
        {
            throw new InvalidOperationException("El IVA no puede ser negativo.");
        }

        if (terms.VatPercent is > 100)
        {
            throw new InvalidOperationException("El IVA no puede superar el 100%.");
        }

        if (terms.DiscountValue < 0)
        {
            throw new InvalidOperationException("El descuento no puede ser negativo.");
        }

        if (terms.DiscountMode == DiscountMode.Percentage && terms.DiscountValue > 100)
        {
            throw new InvalidOperationException("Un descuento en porcentaje no puede superar el 100%.");
        }
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

            // Aprobar es irreversible: descuenta inventario y arranca el trabajo. Un
            // presupuesto sin precio o sin materiales quedó a medio cargar, no es una
            // decisión del taller, y una vez aprobado ya no se puede volver a editar.
            if (project.Budget is null or <= 0)
            {
                throw new InvalidOperationException(
                    "Falta calcular el precio final: un presupuesto sin precio no se puede aprobar.");
            }

            if (!context.ProjectBudgetLines.Any(l => l.ProjectId == project.Id))
            {
                throw new InvalidOperationException(
                    "El presupuesto no tiene materiales cargados. Agregá al menos uno antes de aprobar.");
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

        ProjectStatusPolicy.RequireWorkflow(project.Status, ProjectStatus.Rejected);

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

        ProjectStatusPolicy.RequireWorkflow(project.Status, ProjectStatus.Quote);

        project.Status = ProjectStatus.Quote;
        project.UpdatedAtUtc = DateTime.UtcNow;
        context.SaveChanges();
    }

    /// <summary>
    /// Deshace una aprobación: devuelve al inventario todo lo que se había descontado y
    /// el trabajo vuelve a ser un presupuesto editable.
    /// </summary>
    /// <remarks>
    /// Es el único camino de vuelta desde «En curso», y existe porque el otro —cambiar el
    /// estado a mano desde Proyectos— dejaba el stock descontado sin nada que lo devolviera.
    /// Solo desde «En curso»: si el trabajo ya está terminado o entregado, el material se
    /// usó de verdad y devolverlo al inventario sería inventar existencias que no están.
    /// </remarks>
    public void CancelApproval(int projectId)
    {
        using var context = _databaseService.CreateContext();
        using var transaction = context.Database.BeginTransaction();

        try
        {
            var project = context.Projects.FirstOrDefault(p => p.Id == projectId)
                ?? throw new InvalidOperationException("Proyecto no encontrado.");

            if (project.IsArchived)
            {
                throw new InvalidOperationException("El proyecto está archivado.");
            }

            if (project.Status != ProjectStatus.InProgress)
            {
                throw new InvalidOperationException(
                    "Solo se puede cancelar un trabajo en curso. " +
                    ProjectStatusPolicy.Explain(project.Status, ProjectStatus.Quote));
            }

            ProjectStatusPolicy.RequireWorkflow(project.Status, ProjectStatus.Quote);

            var now = DateTime.UtcNow;

            var materials = context.ProjectMaterials
                .Include(m => m.Product)
                .Where(m => m.ProjectId == projectId)
                .ToList();

            foreach (var material in materials)
            {
                material.Product.CurrentStock += material.Quantity;
                material.Product.UpdatedAtUtc = now;

                context.StockMovements.Add(new StockMovement
                {
                    ProductId = material.ProductId,
                    Type = StockMovementType.In,
                    Quantity = material.Quantity,
                    Reason = $"Trabajo cancelado: {project.Title}",
                    CreatedAtUtc = now
                });
            }

            context.ProjectMaterials.RemoveRange(materials);

            // Las líneas vuelven a figurar como no aplicadas: si el presupuesto se
            // aprueba de nuevo, tiene que volver a descontar todo desde cero.
            foreach (var line in context.ProjectBudgetLines.Where(l => l.ProjectId == projectId))
            {
                line.AppliedQuantity = 0m;
                line.AppliedToStockAtUtc = null;
            }

            project.Status = ProjectStatus.Quote;
            project.UpdatedAtUtc = now;

            context.SaveChanges();
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
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
