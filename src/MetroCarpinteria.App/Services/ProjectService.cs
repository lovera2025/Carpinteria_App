using MetroCarpinteria.App.Data;
using MetroCarpinteria.App.Data.Entities;
using MetroCarpinteria.App.Helpers;
using MetroCarpinteria.App.Models;
using Microsoft.EntityFrameworkCore;

namespace MetroCarpinteria.App.Services;

public sealed class ProjectService
{
    private readonly DatabaseService _databaseService;

    public ProjectService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public IReadOnlyList<ProjectListItem> GetProjects(
        bool includeArchived,
        ProjectStatus? statusFilter,
        string? search)
    {
        using var context = _databaseService.CreateContext();
        var query = context.Projects.AsNoTracking().AsQueryable();

        if (!includeArchived)
        {
            query = query.Where(p => !p.IsArchived);
        }

        if (statusFilter.HasValue)
        {
            query = query.Where(p => p.Status == statusFilter.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(p => EF.Functions.Like(p.Title, $"%{term}%")
                || EF.Functions.Like(p.ClientName, $"%{term}%"));
        }

        return query
            .OrderByDescending(p => p.UpdatedAtUtc)
            .Select(p => new ProjectListItem
            {
                Id = p.Id,
                Title = p.Title,
                ClientName = p.ClientName,
                Description = p.Description,
                Status = p.Status,
                Budget = p.Budget,
                IsArchived = p.IsArchived,
                MaterialCount = context.ProjectMaterials.Count(m => m.ProjectId == p.Id),
                AssignmentCount = context.ProjectAssignments.Count(a => a.ProjectId == p.Id)
            })
            .ToList();
    }

    public IReadOnlyList<ProjectMaterialItem> GetProjectMaterials(int projectId)
    {
        using var context = _databaseService.CreateContext();
        return context.ProjectMaterials
            .AsNoTracking()
            .Include(m => m.Product)
            .Where(m => m.ProjectId == projectId)
            .OrderByDescending(m => m.AssignedAtUtc)
            .Select(m => new ProjectMaterialItem
            {
                Id = m.Id,
                ProductName = m.Product.Name,
                Quantity = m.Quantity,
                Unit = m.Product.Unit,
                AssignedAtLocal = m.AssignedAtUtc.ToLocalTime()
            })
            .ToList();
    }

    public IReadOnlyList<ProjectAssignmentItem> GetProjectAssignments(int projectId)
    {
        using var context = _databaseService.CreateContext();
        return context.ProjectAssignments
            .AsNoTracking()
            .Include(a => a.Employee)
            .Include(a => a.Project)
            .Where(a => a.ProjectId == projectId)
            .OrderByDescending(a => a.AssignedAtUtc)
            .Select(a => new ProjectAssignmentItem
            {
                Id = a.Id,
                EmployeeName = a.Employee.FullName,
                EmployeeRole = a.Employee.Role,
                ProjectTitle = a.Project.Title,
                Notes = a.Notes,
                AssignedAtLocal = a.AssignedAtUtc.ToLocalTime()
            })
            .ToList();
    }

    public Project Create(string title, string clientName, string? description, decimal? budget, ProjectStatus status)
    {
        ValidateProject(title, clientName, budget);

        using var context = _databaseService.CreateContext();
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Title = title.Trim(),
            ClientName = clientName.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Budget = budget,
            Status = status,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        context.Projects.Add(project);
        context.SaveChanges();
        return project;
    }

    public void Update(int id, string title, string clientName, string? description, decimal? budget, ProjectStatus status)
    {
        ValidateProject(title, clientName, budget);

        using var context = _databaseService.CreateContext();
        var project = context.Projects.FirstOrDefault(p => p.Id == id)
            ?? throw new InvalidOperationException("Proyecto no encontrado.");

        // El formulario ya solo ofrece los estados alcanzables, pero el que garantiza es
        // esto: las transiciones que faltan acá son las que mueven inventario, y saltearlas
        // deja el stock descontado sin trabajo que lo justifique.
        ProjectStatusPolicy.RequireManual(project.Status, status);

        project.Title = title.Trim();
        project.ClientName = clientName.Trim();
        project.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        project.Budget = budget;
        project.Status = status;
        project.UpdatedAtUtc = DateTime.UtcNow;
        context.SaveChanges();
    }

    public void Archive(int id)
    {
        using var context = _databaseService.CreateContext();
        var project = context.Projects.FirstOrDefault(p => p.Id == id)
            ?? throw new InvalidOperationException("Proyecto no encontrado.");

        project.IsArchived = true;
        project.UpdatedAtUtc = DateTime.UtcNow;
        context.SaveChanges();
    }

    public void Restore(int id)
    {
        using var context = _databaseService.CreateContext();
        var project = context.Projects.FirstOrDefault(p => p.Id == id)
            ?? throw new InvalidOperationException("Proyecto no encontrado.");

        project.IsArchived = false;
        project.UpdatedAtUtc = DateTime.UtcNow;
        context.SaveChanges();
    }

    public void Delete(int id)
    {
        using var context = _databaseService.CreateContext();
        var project = context.Projects.FirstOrDefault(p => p.Id == id)
            ?? throw new InvalidOperationException("Proyecto no encontrado.");

        if (context.ProjectMaterials.Any(m => m.ProjectId == id)
            || context.ProjectAssignments.Any(a => a.ProjectId == id)
            || context.ProjectBudgetLines.Any(l => l.ProjectId == id))
        {
            throw new InvalidOperationException(
                "No se puede eliminar un proyecto con materiales, personal o presupuesto cargado. Archivalo.");
        }

        context.Projects.Remove(project);
        context.SaveChanges();
    }

    public void AssignMaterial(int projectId, int productId, decimal quantity)
    {
        if (quantity <= 0)
        {
            throw new InvalidOperationException("La cantidad debe ser mayor a cero.");
        }

        using var context = _databaseService.CreateContext();
        using var transaction = context.Database.BeginTransaction();

        try
        {
            var project = context.Projects.FirstOrDefault(p => p.Id == projectId)
                ?? throw new InvalidOperationException("Proyecto no encontrado.");

            if (project.IsArchived)
            {
                throw new InvalidOperationException("No se pueden asignar materiales a proyectos archivados.");
            }

            var product = context.Products.FirstOrDefault(p => p.Id == productId)
                ?? throw new InvalidOperationException("Producto no encontrado.");

            if (product.IsArchived)
            {
                throw new InvalidOperationException("El producto está archivado.");
            }

            if (product.CurrentStock < quantity)
            {
                throw new InvalidOperationException(
                    "Stock insuficiente. Disponible: " +
                    AppCulture.QuantityWithUnit(product.CurrentStock, product.Unit));
            }

            var now = DateTime.UtcNow;
            product.CurrentStock -= quantity;
            product.UpdatedAtUtc = now;

            context.StockMovements.Add(new StockMovement
            {
                ProductId = productId,
                Type = StockMovementType.Out,
                Quantity = quantity,
                Reason = $"Asignado a proyecto: {project.Title}",
                CreatedAtUtc = now
            });

            context.ProjectMaterials.Add(new ProjectMaterial
            {
                ProjectId = projectId,
                ProductId = productId,
                Quantity = quantity,
                AssignedAtUtc = now
            });

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

    public void AssignEmployee(int projectId, int employeeId, string? notes)
    {
        using var context = _databaseService.CreateContext();

        var project = context.Projects.FirstOrDefault(p => p.Id == projectId)
            ?? throw new InvalidOperationException("Proyecto no encontrado.");

        if (project.IsArchived)
        {
            throw new InvalidOperationException("No se puede asignar personal a proyectos archivados.");
        }

        var employee = context.Employees.FirstOrDefault(e => e.Id == employeeId)
            ?? throw new InvalidOperationException("Empleado no encontrado.");

        if (employee.IsArchived)
        {
            throw new InvalidOperationException("El empleado está archivado.");
        }

        if (context.ProjectAssignments.Any(a => a.ProjectId == projectId && a.EmployeeId == employeeId))
        {
            throw new InvalidOperationException("Este empleado ya está asignado al proyecto.");
        }

        var now = DateTime.UtcNow;
        context.ProjectAssignments.Add(new ProjectAssignment
        {
            ProjectId = projectId,
            EmployeeId = employeeId,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            AssignedAtUtc = now
        });

        project.UpdatedAtUtc = now;
        context.SaveChanges();
    }

    public void RemoveMaterial(int materialId)
    {
        using var context = _databaseService.CreateContext();
        using var transaction = context.Database.BeginTransaction();

        try
        {
            var material = context.ProjectMaterials
                .Include(m => m.Project)
                .Include(m => m.Product)
                .FirstOrDefault(m => m.Id == materialId)
                ?? throw new InvalidOperationException("Material no encontrado.");

            if (material.Project.IsArchived)
            {
                throw new InvalidOperationException("No se pueden quitar materiales de proyectos archivados.");
            }

            var now = DateTime.UtcNow;
            material.Product.CurrentStock += material.Quantity;
            material.Product.UpdatedAtUtc = now;

            context.StockMovements.Add(new StockMovement
            {
                ProductId = material.ProductId,
                Type = StockMovementType.In,
                Quantity = material.Quantity,
                Reason = $"Devuelto desde proyecto: {material.Project.Title}",
                CreatedAtUtc = now
            });

            material.Project.UpdatedAtUtc = now;
            context.ProjectMaterials.Remove(material);
            context.SaveChanges();
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public void RemoveAssignment(int assignmentId)
    {
        using var context = _databaseService.CreateContext();
        var assignment = context.ProjectAssignments
            .Include(a => a.Project)
            .FirstOrDefault(a => a.Id == assignmentId)
            ?? throw new InvalidOperationException("Asignación no encontrada.");

        assignment.Project.UpdatedAtUtc = DateTime.UtcNow;
        context.ProjectAssignments.Remove(assignment);
        context.SaveChanges();
    }

    public bool HasDependencies(int projectId) => DescribeDeleteBlock(projectId) is not null;

    /// <summary>Por qué no se puede borrar el proyecto, o <c>null</c> si se puede.</summary>
    public string? DescribeDeleteBlock(int projectId)
    {
        using var context = _databaseService.CreateContext();

        var materials = context.ProjectMaterials.Count(m => m.ProjectId == projectId);
        var assignments = context.ProjectAssignments.Count(a => a.ProjectId == projectId);
        var budgetLines = context.ProjectBudgetLines.Count(l => l.ProjectId == projectId);

        if (materials == 0 && assignments == 0 && budgetLines == 0)
        {
            return null;
        }

        var reasons = new List<string>();

        if (materials > 0)
        {
            reasons.Add(Phrases.Count(materials, "material entregado", "materiales entregados"));
        }

        if (assignments > 0)
        {
            reasons.Add(Phrases.Count(assignments, "persona asignada", "personas asignadas"));
        }

        if (budgetLines > 0)
        {
            reasons.Add(Phrases.Count(budgetLines, "línea de presupuesto", "líneas de presupuesto"));
        }

        return $"No se puede eliminar: tiene {Phrases.JoinWithAnd(reasons)}. Archivalo en su lugar.";
    }

    private static void ValidateProject(string title, string clientName, decimal? budget)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new InvalidOperationException("El título del proyecto es obligatorio.");
        }

        if (string.IsNullOrWhiteSpace(clientName))
        {
            throw new InvalidOperationException("El nombre del cliente es obligatorio.");
        }

        if (budget is < 0)
        {
            throw new InvalidOperationException("El presupuesto no puede ser negativo.");
        }
    }
}
