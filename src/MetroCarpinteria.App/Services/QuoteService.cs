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
    private readonly QuoteImageService? _imageService;

    public QuoteService(
        DatabaseService databaseService,
        SettingsService settingsService,
        QuoteImageService? imageService = null)
    {
        _databaseService = databaseService;
        _settingsService = settingsService;
        _imageService = imageService;
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

        var laborLines = ReadLaborLines(context, projectId);
        var rates = ReadRates(project);
        var materialsTotal = lines.Sum(l => l.LineTotal);
        var unadjusted = RebuildRaw(project, rates, materialsTotal, laborLines);
        var terms = ReadTerms(project);
        var breakdown = ApplyStoredAdjustment(project, unadjusted);
        var calculatedTotal = unadjusted is null
            ? (decimal?)null
            : CommercialTermsService.Apply(unadjusted.FinalPrice, terms).Total;

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
            LaborLines = laborLines,
            Breakdown = breakdown,
            UnadjustedBreakdown = unadjusted,
            PriceAdjustmentTargets = BudgetLineKinds.ParseTargets(project.PriceAdjustmentTargets),
            CalculatedTotal = calculatedTotal,
            Images = _imageService?.List(projectId) ?? [],
            Attachments = ReadAttachments(context, projectId),
            ShowCommitmentNote = project.ShowCommitmentNote,
            CommitmentAmount = project.CommitmentAmount,
            CommitmentText = project.CommitmentText
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

    private List<QuoteAttachmentItem> ReadAttachments(AppDbContext context, int parentId)
    {
        var rows = context.ProjectQuoteAttachments
            .AsNoTracking()
            .Where(a => a.ParentProjectId == parentId)
            .OrderBy(a => a.SortOrder)
            .ThenBy(a => a.Id)
            .Select(a => new { a.Id, a.AttachedProjectId })
            .ToList();

        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Select(r => r.AttachedProjectId).ToList();
        var projects = context.Projects
            .AsNoTracking()
            .Where(p => ids.Contains(p.Id) && !p.IsArchived)
            .ToDictionary(p => p.Id);

        var items = new List<QuoteAttachmentItem>(rows.Count);

        foreach (var row in rows)
        {
            if (!projects.TryGetValue(row.AttachedProjectId, out var project))
            {
                continue;
            }

            items.Add(new QuoteAttachmentItem
            {
                AttachmentId = row.Id,
                ProjectId = project.Id,
                Title = project.Title,
                Description = project.Description,
                Budget = project.Budget,
                Images = _imageService?.List(project.Id) ?? []
            });
        }

        return items;
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

    /// <summary>
    /// Aviso de seña debajo del TOTAL. Vacío o importe cero apaga el aviso aunque el
    /// tilde esté prendido: no tiene sentido imprimir «entregando $ 0».
    /// </summary>
    public void SaveCommitmentNote(int projectId, bool show, decimal? amount, string? text)
    {
        if (amount is < 0)
        {
            throw new InvalidOperationException("El importe de la seña no puede ser negativo.");
        }

        using var context = _databaseService.CreateContext();
        var project = RequireEditableQuote(context, projectId);

        project.ShowCommitmentNote = show;
        project.CommitmentAmount = amount is > 0 ? amount : null;
        project.CommitmentText = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        project.UpdatedAtUtc = DateTime.UtcNow;
        context.SaveChanges();
    }

    /// <summary>
    /// Presupuestos del mismo cliente que se pueden colgar de éste: no archivados, no
    /// él mismo, y que todavía no están en la lista.
    /// </summary>
    public IReadOnlyList<QuoteListItem> GetAttachableQuotes(int parentId)
    {
        using var context = _databaseService.CreateContext();
        var parent = context.Projects.AsNoTracking().FirstOrDefault(p => p.Id == parentId)
            ?? throw new InvalidOperationException("Presupuesto no encontrado.");

        var attachedIds = context.ProjectQuoteAttachments
            .AsNoTracking()
            .Where(a => a.ParentProjectId == parentId)
            .Select(a => a.AttachedProjectId)
            .ToHashSet();

        var candidates = context.Projects
            .AsNoTracking()
            .Where(p => p.Id != parentId && !p.IsArchived)
            .OrderByDescending(p => p.UpdatedAtUtc)
            .Select(p => new
            {
                p.Id,
                p.Title,
                p.ClientName,
                p.ClientId,
                p.Status,
                p.Budget,
                p.IsArchived,
                p.QuotedAtUtc,
                p.QuoteValidUntilUtc,
                LineCount = context.ProjectBudgetLines.Count(l => l.ProjectId == p.Id)
            })
            .ToList();

        return candidates
            .Where(p => !attachedIds.Contains(p.Id) && SameClient(parent.ClientId, parent.ClientName, p.ClientId, p.ClientName))
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
    }

    public void AttachQuote(int parentId, int attachedId)
    {
        if (parentId == attachedId)
        {
            throw new InvalidOperationException("Un presupuesto no se puede adjuntar a sí mismo.");
        }

        using var context = _databaseService.CreateContext();
        var parent = context.Projects.FirstOrDefault(p => p.Id == parentId)
            ?? throw new InvalidOperationException("Presupuesto no encontrado.");
        var attached = context.Projects.FirstOrDefault(p => p.Id == attachedId)
            ?? throw new InvalidOperationException("El presupuesto a adjuntar no existe.");

        if (parent.IsArchived || attached.IsArchived)
        {
            throw new InvalidOperationException("No se puede adjuntar un presupuesto archivado.");
        }

        if (!SameClient(parent.ClientId, parent.ClientName, attached.ClientId, attached.ClientName))
        {
            throw new InvalidOperationException(
                "Solo se pueden adjuntar presupuestos del mismo cliente.");
        }

        if (context.ProjectQuoteAttachments.Any(a => a.ParentProjectId == parentId && a.AttachedProjectId == attachedId))
        {
            throw new InvalidOperationException("Ese presupuesto ya está adjunto a éste.");
        }

        var sort = context.ProjectQuoteAttachments
            .Where(a => a.ParentProjectId == parentId)
            .Select(a => (int?)a.SortOrder)
            .Max() ?? 0;

        context.ProjectQuoteAttachments.Add(new ProjectQuoteAttachment
        {
            ParentProjectId = parentId,
            AttachedProjectId = attachedId,
            SortOrder = sort + 1,
            CreatedAtUtc = DateTime.UtcNow
        });

        parent.UpdatedAtUtc = DateTime.UtcNow;
        context.SaveChanges();
    }

    public void DetachQuote(int parentId, int attachmentId)
    {
        using var context = _databaseService.CreateContext();
        var row = context.ProjectQuoteAttachments
            .FirstOrDefault(a => a.Id == attachmentId && a.ParentProjectId == parentId)
            ?? throw new InvalidOperationException("Ese adjunto ya no está en este presupuesto.");

        context.ProjectQuoteAttachments.Remove(row);

        var parent = context.Projects.FirstOrDefault(p => p.Id == parentId);
        if (parent is not null)
        {
            parent.UpdatedAtUtc = DateTime.UtcNow;
        }

        context.SaveChanges();
    }

    /// <summary>
    /// Crea un presupuesto vacío del mismo cliente, lo cuelga del abierto y lo devuelve
    /// para cargarle materiales y precio.
    /// </summary>
    public int CreateSiblingQuote(int parentId, string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new InvalidOperationException("El título del presupuesto es obligatorio.");
        }

        using var context = _databaseService.CreateContext();
        using var transaction = context.Database.BeginTransaction();

        try
        {
            var parent = context.Projects.FirstOrDefault(p => p.Id == parentId)
                ?? throw new InvalidOperationException("Presupuesto no encontrado.");

            if (parent.IsArchived)
            {
                throw new InvalidOperationException("No se puede adjuntar un presupuesto archivado.");
            }

            ValidateHeader(title.Trim(), parent.ClientName);

            var now = DateTime.UtcNow;
            var validity = DateTime.Today.AddDays(Math.Max(0, _settingsService.Current.DefaultQuoteValidityDays));

            var sibling = new Project
            {
                Title = title.Trim(),
                ClientName = parent.ClientName,
                ClientId = parent.ClientId,
                Status = ProjectStatus.Quote,
                QuotedAtUtc = now,
                QuoteValidUntilUtc = ToUtcFromLocalDate(validity),
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            context.Projects.Add(sibling);
            context.SaveChanges();

            var sort = context.ProjectQuoteAttachments
                .Where(a => a.ParentProjectId == parentId)
                .Select(a => (int?)a.SortOrder)
                .Max() ?? 0;

            context.ProjectQuoteAttachments.Add(new ProjectQuoteAttachment
            {
                ParentProjectId = parentId,
                AttachedProjectId = sibling.Id,
                SortOrder = sort + 1,
                CreatedAtUtc = now
            });

            parent.UpdatedAtUtc = now;
            context.SaveChanges();
            transaction.Commit();
            return sibling.Id;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static bool SameClient(int? leftId, string leftName, int? rightId, string rightName)
    {
        if (leftId is int left && rightId is int right)
        {
            return left == right;
        }

        var a = ClientRules.Normalize(leftName);
        var b = ClientRules.Normalize(rightName);
        return a.Length > 0 && string.Equals(a, b, StringComparison.Ordinal);
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

    // --- Mano de obra ---------------------------------------------------------

    /// <summary>
    /// Suma un operario al presupuesto. El jefe no pasa por acá: sigue siendo el par
    /// días/jornal del proyecto.
    /// </summary>
    /// <param name="employeeId">
    /// Ficha de Personal, o null para alguien suelto que no está dado de alta.
    /// </param>
    public void AddLaborLine(int projectId, int? employeeId, string description, decimal days, decimal dailyRate)
    {
        var name = ValidateLaborLine(description, days, dailyRate);

        using var context = _databaseService.CreateContext();
        var project = RequireEditableQuote(context, projectId);

        if (employeeId.HasValue && !context.Employees.Any(e => e.Id == employeeId.Value))
        {
            throw new InvalidOperationException("El empleado elegido ya no existe.");
        }

        var nextOrder = context.ProjectLaborLines
            .Where(l => l.ProjectId == projectId)
            .Select(l => (int?)l.SortOrder)
            .Max() ?? 0;

        context.ProjectLaborLines.Add(new ProjectLaborLine
        {
            ProjectId = projectId,
            EmployeeId = employeeId,
            Description = name,
            Days = days,
            DailyRate = dailyRate,
            SortOrder = nextOrder + 1,
            CreatedAtUtc = DateTime.UtcNow
        });

        project.UpdatedAtUtc = DateTime.UtcNow;
        context.SaveChanges();
    }

    /// <summary>
    /// Corrige los días y el jornal de un operario ya cargado. De quién se trata no se
    /// cambia: para eso se quita la línea y se carga de nuevo, igual que los materiales.
    /// </summary>
    public void UpdateLaborLine(int lineId, decimal days, decimal dailyRate)
    {
        using var context = _databaseService.CreateContext();
        var line = context.ProjectLaborLines.FirstOrDefault(l => l.Id == lineId)
            ?? throw new InvalidOperationException("Línea de mano de obra no encontrada.");

        ValidateLaborLine(line.Description, days, dailyRate);

        var project = RequireEditableQuote(context, line.ProjectId);

        line.Days = days;
        line.DailyRate = dailyRate;
        project.UpdatedAtUtc = DateTime.UtcNow;
        context.SaveChanges();
    }

    public void RemoveLaborLine(int lineId)
    {
        using var context = _databaseService.CreateContext();
        var line = context.ProjectLaborLines.FirstOrDefault(l => l.Id == lineId)
            ?? throw new InvalidOperationException("Línea de mano de obra no encontrada.");

        var project = RequireEditableQuote(context, line.ProjectId);

        context.ProjectLaborLines.Remove(line);
        project.UpdatedAtUtc = DateTime.UtcNow;
        context.SaveChanges();
    }

    /// <returns>El nombre ya recortado, que es el que se guarda.</returns>
    private static string ValidateLaborLine(string description, decimal days, decimal dailyRate)
    {
        var name = description?.Trim() ?? string.Empty;

        if (name.Length == 0)
        {
            throw new InvalidOperationException("Poné de quién es la mano de obra.");
        }

        if (name.Length > 200)
        {
            throw new InvalidOperationException("El nombre es demasiado largo.");
        }

        if (days <= 0)
        {
            throw new InvalidOperationException("Los días tienen que ser mayores a cero.");
        }

        if (dailyRate <= 0)
        {
            throw new InvalidOperationException("El jornal tiene que ser mayor a cero.");
        }

        return name;
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
        using var context = _databaseService.CreateContext();
        var project = RequireEditableQuote(context, projectId);

        // Los operarios se leen, no se escriben: los administra AddLaborLine y compañía.
        // Esto corre en cada LostFocus de la calculadora y no puede andar borrando y
        // recreando filas por cada tecla.
        var breakdown = BudgetCalculatorService.Calculate(new BudgetInput
        {
            MaterialsCost = materialsCost,
            Days = days,
            DailyRate = dailyRate,
            LaborLines = ToCalculatorInput(ReadLaborLines(context, projectId)),
            Rates = rates
        });

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
        project.PriceAdjustmentTargets = null;
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

        var breakdown = RebuildRaw(
            project, ReadRates(project), lines, ReadLaborLines(context, projectId));

        var commercial = CommercialTermsService.Apply(breakdown?.FinalPrice ?? 0m, terms);

        // Sin cálculo todavía no hay precio que ajustar: las condiciones quedan guardadas
        // y se aplican solas en cuanto se calcule.
        if (breakdown is not null)
        {
            project.Budget = commercial.Total;
            project.PriceAdjustmentTargets = null;
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

    /// <summary>
    /// Ajuste manual del precio final, para redondear lo que se le pasa al cliente.
    /// </summary>
    /// <param name="absorbInto">
    /// Líneas del desglose que absorben la diferencia. Vacío o null deja el desglose
    /// intacto: solo cambia lo que se le cobra.
    /// </param>
    public void SetFinalPrice(
        int projectId,
        decimal? finalPrice,
        IReadOnlyList<BudgetLineKind>? absorbInto = null)
    {
        if (finalPrice is < 0)
        {
            throw new InvalidOperationException("El precio final no puede ser negativo.");
        }

        using var context = _databaseService.CreateContext();
        var project = RequireEditableQuote(context, projectId);

        var targets = absorbInto is null
            ? []
            : absorbInto.Where(BudgetLineKinds.CanAbsorb).Distinct().ToList();

        if (targets.Count > 0)
        {
            if (finalPrice is null or <= 0)
            {
                throw new InvalidOperationException("El precio final tiene que ser mayor a cero.");
            }

            var laborLines = ReadLaborLines(context, project.Id);
            var rates = ReadRates(project);
            var raw = RebuildRaw(project, rates, project.QuotedMaterialsCost ?? 0m, laborLines)
                ?? throw new InvalidOperationException(
                    "Falta calcular el precio antes de recortar el desglose.");

            var commercial = CommercialTermsService.Apply(raw.FinalPrice, ReadTerms(project));
            var targetCost = BudgetCalculatorService.TargetCostTotal(
                raw.FinalPrice, commercial.Total, finalPrice.Value);

            BudgetCalculatorService.ApplyPriceAdjustment(raw, targets, targetCost);
            project.PriceAdjustmentTargets = BudgetLineKinds.FormatTargets(targets);
        }
        else
        {
            project.PriceAdjustmentTargets = null;
        }

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
            AssignQuotedWorkers(context, project);

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
        int copyId;

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

            var sourceLabor = context.ProjectLaborLines
                .AsNoTracking()
                .Include(l => l.Employee)
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

            foreach (var line in sourceLabor)
            {
                // Mismo criterio que los materiales: quien está en Personal se recotiza con
                // el jornal de hoy, y el que se cargó suelto conserva el suyo porque no hay
                // de dónde refrescarlo.
                context.ProjectLaborLines.Add(new ProjectLaborLine
                {
                    ProjectId = copy.Id,
                    EmployeeId = line.EmployeeId,
                    Description = line.Employee?.FullName ?? line.Description,
                    Days = line.Days,
                    DailyRate = line.Employee?.DailyRate ?? line.DailyRate,
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
                    LaborLines = ToCalculatorInput(ReadLaborLines(context, copy.Id)),
                    Rates = rates
                });

                copy.QuotedMaterialsCost = breakdown.MaterialsCost;
                copy.Budget = breakdown.FinalPrice;
                context.SaveChanges();
            }

            copyId = copy.Id;
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }

        try
        {
            _imageService?.CopyTo(projectId, copyId);
        }
        catch
        {
            _imageService?.DeleteFilesForProject(copyId);
            throw;
        }

        return copyId;
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

    /// <summary>
    /// Deja asignados al proyecto los operarios que se cotizaron, para no cargarlos dos veces.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Solo los que salieron de Personal: al que se escribió suelto no hay a quién
    /// engancharlo. Las asignaciones que ya existan se respetan —hay un índice único por
    /// (proyecto, empleado)— y no se pisa la nota de una asignación hecha a mano.
    /// </para>
    /// <para>
    /// No falla si algo no cierra: aprobar tiene que descontar el stock y arrancar el
    /// trabajo. Que además quede la asignación es una comodidad, no una condición.
    /// </para>
    /// </remarks>
    private static void AssignQuotedWorkers(AppDbContext context, Project project)
    {
        var quoted = context.ProjectLaborLines
            .Where(l => l.ProjectId == project.Id && l.EmployeeId != null)
            .Select(l => l.EmployeeId!.Value)
            .Distinct()
            .ToList();

        if (quoted.Count == 0)
        {
            return;
        }

        var already = context.ProjectAssignments
            .Where(a => a.ProjectId == project.Id)
            .Select(a => a.EmployeeId)
            .ToHashSet();

        var alive = context.Employees
            .Where(e => quoted.Contains(e.Id))
            .Select(e => e.Id)
            .ToHashSet();

        foreach (var employeeId in quoted.Where(id => !already.Contains(id) && alive.Contains(id)))
        {
            context.ProjectAssignments.Add(new ProjectAssignment
            {
                ProjectId = project.Id,
                EmployeeId = employeeId,
                Notes = "Cotizado en el presupuesto",
                AssignedAtUtc = DateTime.UtcNow
            });
        }
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

    /// <summary>
    /// Los operarios cotizados, en orden. El rol sale de la ficha solo para mostrarlo: el
    /// nombre y el jornal salen de la línea, que los tiene congelados.
    /// </summary>
    private static List<QuoteLaborLineItem> ReadLaborLines(AppDbContext context, int projectId) =>
        context.ProjectLaborLines
            .AsNoTracking()
            .Include(l => l.Employee)
            .Where(l => l.ProjectId == projectId)
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.Id)
            // AsEnumerable antes de tocar los decimales: en las bases viejas son TEXT y
            // cualquier orden o comparación que quede en SQL se resuelve como texto.
            .AsEnumerable()
            .Select(l => new QuoteLaborLineItem
            {
                Id = l.Id,
                EmployeeId = l.EmployeeId,
                Description = l.Description,
                Days = l.Days,
                DailyRate = l.DailyRate,
                SortOrder = l.SortOrder,
                Role = l.Employee?.Role
            })
            .ToList();

    private static List<LaborLineInput> ToCalculatorInput(IEnumerable<QuoteLaborLineItem> lines) =>
        lines
            .Select(l => new LaborLineInput
            {
                Description = l.Description,
                Days = l.Days,
                DailyRate = l.DailyRate
            })
            .ToList();

    private static BudgetBreakdown? RebuildRaw(
        Project project,
        BudgetRates? rates,
        decimal materialsFromLines,
        IReadOnlyList<QuoteLaborLineItem> laborLines)
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
            LaborLines = ToCalculatorInput(laborLines),
            Rates = rates
        });
    }

    /// <summary>
    /// Si hay claves de recorte, las aplica sobre el cálculo. Si no cierran —datos viejos
    /// o un recorte que ya no cubre— se muestra el desglose original: no puede impedir
    /// abrir el presupuesto.
    /// </summary>
    private static BudgetBreakdown? ApplyStoredAdjustment(Project project, BudgetBreakdown? calculated)
    {
        if (calculated is null)
        {
            return null;
        }

        var targets = BudgetLineKinds.ParseTargets(project.PriceAdjustmentTargets);
        if (targets.Count == 0 || project.Budget is null)
        {
            return calculated;
        }

        var commercial = CommercialTermsService.Apply(calculated.FinalPrice, ReadTerms(project));
        var targetCost = BudgetCalculatorService.TargetCostTotal(
            calculated.FinalPrice, commercial.Total, project.Budget.Value);

        try
        {
            return BudgetCalculatorService.ApplyPriceAdjustment(calculated, targets, targetCost);
        }
        catch (InvalidOperationException)
        {
            return calculated;
        }
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
