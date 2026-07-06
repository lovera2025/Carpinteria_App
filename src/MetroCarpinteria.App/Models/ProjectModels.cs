using MetroCarpinteria.App.Data.Entities;

namespace MetroCarpinteria.App.Models;

public sealed class ProjectListItem
{
    public int Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string ClientName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public ProjectStatus Status { get; init; }
    public decimal? Budget { get; init; }
    public bool IsArchived { get; init; }
    public int MaterialCount { get; init; }
    public int AssignmentCount { get; init; }

    public string StatusLabel => ProjectStatusHelper.GetLabel(Status);

    public string BudgetDisplay => Budget.HasValue
        ? Budget.Value.ToString("C", new System.Globalization.CultureInfo("es-AR"))
        : "—";
}

public sealed class ProjectMaterialItem
{
    public int Id { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public decimal Quantity { get; init; }
    public string Unit { get; init; } = ProductUnits.Unit;
    public DateTime AssignedAtLocal { get; init; }

    public string QuantityDisplay => $"{Quantity:N2} {Unit}";
}

public sealed class ProjectAssignmentItem
{
    public int Id { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public string? EmployeeRole { get; init; }
    public string ProjectTitle { get; init; } = string.Empty;
    public string? Notes { get; init; }
    public DateTime AssignedAtLocal { get; init; }
}

public sealed class EmployeeListItem
{
    public int Id { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public string? Role { get; init; }
    public bool IsArchived { get; init; }
    public int ActiveAssignmentCount { get; init; }
}

public sealed class ProjectStatusOption
{
    public ProjectStatus? Status { get; init; }
    public required string Label { get; init; }
}

public static class ProjectStatusHelper
{
    public static string GetLabel(ProjectStatus status) => status switch
    {
        ProjectStatus.Quote => "Presupuesto",
        ProjectStatus.InProgress => "En curso",
        ProjectStatus.Completed => "Terminado",
        ProjectStatus.Delivered => "Entregado",
        _ => status.ToString()
    };

    public static IReadOnlyList<ProjectStatusOption> GetFilterOptions() =>
    [
        new ProjectStatusOption { Status = null, Label = "Todos los estados" },
        new ProjectStatusOption { Status = ProjectStatus.Quote, Label = "Presupuesto" },
        new ProjectStatusOption { Status = ProjectStatus.InProgress, Label = "En curso" },
        new ProjectStatusOption { Status = ProjectStatus.Completed, Label = "Terminado" },
        new ProjectStatusOption { Status = ProjectStatus.Delivered, Label = "Entregado" }
    ];

    public static IReadOnlyList<ProjectStatusOption> GetEditOptions() =>
    [
        new ProjectStatusOption { Status = ProjectStatus.Quote, Label = "Presupuesto" },
        new ProjectStatusOption { Status = ProjectStatus.InProgress, Label = "En curso" },
        new ProjectStatusOption { Status = ProjectStatus.Completed, Label = "Terminado" },
        new ProjectStatusOption { Status = ProjectStatus.Delivered, Label = "Entregado" }
    ];
}
