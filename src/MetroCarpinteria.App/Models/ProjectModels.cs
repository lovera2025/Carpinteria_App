using MetroCarpinteria.App.Data.Entities;
using MetroCarpinteria.App.Helpers;

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

    /// <summary>
    /// Fecha en la que el trabajo tendría que estar listo: la aprobación más los días
    /// cotizados. Null cuando no se cotizaron días, y entonces nunca figura atrasado.
    /// </summary>
    public DateTime? PromisedDate { get; init; }

    public string StatusLabel => ProjectStatusHelper.GetLabel(Status);

    public string BudgetDisplay => Helpers.AppCulture.Money(Budget);

    /// <remarks>
    /// Sólo avisan los que todavía se pueden terminar. Uno listo cumplió —aunque haya
    /// tardado— y uno archivado ya no es asunto del taller.
    /// </remarks>
    public bool IsOverdue =>
        !IsArchived
        && Status is ProjectStatus.Approved or ProjectStatus.InProgress
        && PromisedDate is { } promised
        && promised < DateTime.Today;

    public int OverdueDays => IsOverdue ? (DateTime.Today - PromisedDate!.Value).Days : 0;

    public string OverdueDisplay => OverdueDays == 1
        ? "Atrasado · 1 día"
        : $"Atrasado · {OverdueDays} días";
}

public sealed class ProjectMaterialItem
{
    public int Id { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public decimal Quantity { get; init; }
    public string Unit { get; init; } = ProductUnits.Unit;
    public DateTime AssignedAtLocal { get; init; }

    public string QuantityDisplay => AppCulture.QuantityWithUnit(Quantity, Unit);
}

public sealed class ProjectAssignmentItem
{
    public int Id { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public string? EmployeeRole { get; init; }
    public string ProjectTitle { get; init; } = string.Empty;
    public string? Notes { get; init; }
    public DateTime AssignedAtLocal { get; init; }
    public bool IsPaid { get; init; }

    public string PaymentStatusLabel => IsPaid ? "Pagado" : "Pendiente";

    public string TogglePaymentLabel => IsPaid ? "Marcar pendiente" : "Marcar pagado";
}

public sealed class EmployeeListItem
{
    public int Id { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public string? Role { get; init; }

    /// <summary>Cuánto cobra por día. Prellena la mano de obra al cotizarlo.</summary>
    public decimal? DailyRate { get; init; }

    public bool IsArchived { get; init; }
    public int ActiveAssignmentCount { get; init; }

    /// <summary>Trabajos en los que todavía no se le pagó el jornal.</summary>
    public int UnpaidAssignmentCount { get; init; }

    public string DailyRateDisplay => AppCulture.Money(DailyRate);

    /// <summary>«Cristian Gómez — Oficial carpintero», para el desplegable de operarios.</summary>
    public string PickerDisplay => string.IsNullOrWhiteSpace(Role)
        ? FullName
        : $"{FullName} — {Role}";
}

public sealed class ProjectStatusOption
{
    public ProjectStatus? Status { get; init; }
    public required string Label { get; init; }

    /// <summary>
    /// El filtro «Atrasados», que no es un estado sino una condición sobre la fecha
    /// prometida.
    /// </summary>
    /// <remarks>
    /// Hace falta este campo aparte porque <see cref="Status"/> en null ya significa
    /// «todos los estados»: sin el discriminador, atrasados y todos serían la misma opción.
    /// </remarks>
    public bool OverdueOnly { get; init; }
}

public static class ProjectStatusHelper
{
    public static string GetLabel(ProjectStatus status) => status switch
    {
        ProjectStatus.Quote => "Presupuesto",
        ProjectStatus.Approved => "Aprobado",
        ProjectStatus.InProgress => "En taller",
        ProjectStatus.Completed => "Listo",
        ProjectStatus.Delivered => "Entregado",
        ProjectStatus.Rejected => "Rechazado",
        _ => status.ToString()
    };

    /// <remarks>
    /// «Entregado» no se ofrece: dejó de usarse en la v12 y la migración vació el estado.
    /// Sigue teniendo etiqueta en <see cref="GetLabel"/> por si alguna fila no migró, pero
    /// nadie tiene que poder volver a meterse ahí.
    /// </remarks>
    public static IReadOnlyList<ProjectStatusOption> GetFilterOptions() =>
    [
        new ProjectStatusOption { Status = null, Label = "Todos los estados" },
        new ProjectStatusOption { Status = ProjectStatus.Quote, Label = "Presupuesto" },
        new ProjectStatusOption { Status = ProjectStatus.Approved, Label = "Aprobado" },
        new ProjectStatusOption { Status = ProjectStatus.InProgress, Label = "En taller" },
        new ProjectStatusOption { Status = ProjectStatus.Completed, Label = "Listo" },
        new ProjectStatusOption { Status = ProjectStatus.Rejected, Label = "Rechazado" },
        new ProjectStatusOption { Status = null, OverdueOnly = true, Label = "Atrasados" }
    ];

    public static IReadOnlyList<ProjectStatusOption> GetEditOptions() =>
    [
        new ProjectStatusOption { Status = ProjectStatus.Quote, Label = "Presupuesto" },
        new ProjectStatusOption { Status = ProjectStatus.Approved, Label = "Aprobado" },
        new ProjectStatusOption { Status = ProjectStatus.InProgress, Label = "En taller" },
        new ProjectStatusOption { Status = ProjectStatus.Completed, Label = "Listo" },
        new ProjectStatusOption { Status = ProjectStatus.Rejected, Label = "Rechazado" }
    ];
}
