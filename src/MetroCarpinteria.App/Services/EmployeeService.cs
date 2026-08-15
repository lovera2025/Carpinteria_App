using MetroCarpinteria.App.Data;
using MetroCarpinteria.App.Data.Entities;
using MetroCarpinteria.App.Helpers;
using MetroCarpinteria.App.Models;
using Microsoft.EntityFrameworkCore;

namespace MetroCarpinteria.App.Services;

public sealed class EmployeeService
{
    private readonly DatabaseService _databaseService;

    public EmployeeService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public IReadOnlyList<EmployeeListItem> GetEmployees(bool includeArchived, string? search)
    {
        using var context = _databaseService.CreateContext();
        var query = context.Employees.AsNoTracking().AsQueryable();

        if (!includeArchived)
        {
            query = query.Where(e => !e.IsArchived);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(e => EF.Functions.Like(e.FullName, $"%{term}%")
                || (e.Role != null && EF.Functions.Like(e.Role, $"%{term}%")));
        }

        return query
            .OrderBy(e => e.FullName)
            .Select(e => new EmployeeListItem
            {
                Id = e.Id,
                FullName = e.FullName,
                Phone = e.Phone,
                Role = e.Role,
                DailyRate = e.DailyRate,
                IsArchived = e.IsArchived,
                ActiveAssignmentCount = context.ProjectAssignments.Count(a => a.EmployeeId == e.Id),
                UnpaidAssignmentCount = context.ProjectAssignments.Count(
                    a => a.EmployeeId == e.Id && !a.IsPaid)
            })
            .ToList();
    }

    public IReadOnlyList<ProjectAssignmentItem> GetEmployeeAssignments(int employeeId)
    {
        using var context = _databaseService.CreateContext();
        return context.ProjectAssignments
            .AsNoTracking()
            .Include(a => a.Project)
            .Include(a => a.Employee)
            .Where(a => a.EmployeeId == employeeId)
            .OrderByDescending(a => a.AssignedAtUtc)
            .Select(a => new ProjectAssignmentItem
            {
                Id = a.Id,
                EmployeeName = a.Employee.FullName,
                EmployeeRole = a.Employee.Role,
                ProjectTitle = a.Project.Title,
                Notes = a.Notes,
                AssignedAtLocal = a.AssignedAtUtc.ToLocalTime(),
                IsPaid = a.IsPaid
            })
            .ToList();
    }

    /// <param name="dailyRate">
    /// Lo que cobra por día. Null es «todavía no lo sé»: se puede dar de alta a alguien sin
    /// haber arreglado el jornal, y al cotizarlo se escribe a mano.
    /// </param>
    public Employee Create(string fullName, string? phone, string? role, decimal? dailyRate = null)
    {
        ValidateEmployee(fullName, dailyRate);

        using var context = _databaseService.CreateContext();
        var now = DateTime.UtcNow;
        var employee = new Employee
        {
            FullName = fullName.Trim(),
            Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
            Role = string.IsNullOrWhiteSpace(role) ? null : role.Trim(),
            DailyRate = dailyRate,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        context.Employees.Add(employee);
        context.SaveChanges();
        return employee;
    }

    /// <remarks>
    /// Cambiar el jornal acá <b>no toca los presupuestos ya hechos</b>: cada línea de mano
    /// de obra guarda el suyo congelado. Solo cambia lo que se propone de acá en adelante.
    /// </remarks>
    public void Update(int id, string fullName, string? phone, string? role, decimal? dailyRate = null)
    {
        ValidateEmployee(fullName, dailyRate);

        using var context = _databaseService.CreateContext();
        var employee = context.Employees.FirstOrDefault(e => e.Id == id)
            ?? throw new InvalidOperationException("Empleado no encontrado.");

        employee.FullName = fullName.Trim();
        employee.Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        employee.Role = string.IsNullOrWhiteSpace(role) ? null : role.Trim();
        employee.DailyRate = dailyRate;
        employee.UpdatedAtUtc = DateTime.UtcNow;
        context.SaveChanges();
    }

    public void Archive(int id)
    {
        using var context = _databaseService.CreateContext();
        var employee = context.Employees.FirstOrDefault(e => e.Id == id)
            ?? throw new InvalidOperationException("Empleado no encontrado.");

        employee.IsArchived = true;
        employee.UpdatedAtUtc = DateTime.UtcNow;
        context.SaveChanges();
    }

    public void Restore(int id)
    {
        using var context = _databaseService.CreateContext();
        var employee = context.Employees.FirstOrDefault(e => e.Id == id)
            ?? throw new InvalidOperationException("Empleado no encontrado.");

        employee.IsArchived = false;
        employee.UpdatedAtUtc = DateTime.UtcNow;
        context.SaveChanges();
    }

    public void Delete(int id)
    {
        using var context = _databaseService.CreateContext();
        var employee = context.Employees.FirstOrDefault(e => e.Id == id)
            ?? throw new InvalidOperationException("Empleado no encontrado.");

        if (context.ProjectAssignments.Any(a => a.EmployeeId == id))
        {
            throw new InvalidOperationException(
                "No se puede eliminar un empleado con asignaciones. Archivalo en su lugar.");
        }

        context.Employees.Remove(employee);
        context.SaveChanges();
    }

    public bool HasAssignments(int id) => DescribeDeleteBlock(id) is not null;

    /// <summary>Por qué no se puede borrar el empleado, o <c>null</c> si se puede.</summary>
    public string? DescribeDeleteBlock(int id)
    {
        using var context = _databaseService.CreateContext();
        var assignments = context.ProjectAssignments.Count(a => a.EmployeeId == id);

        return assignments == 0
            ? null
            : $"No se puede eliminar: está en {Phrases.Count(assignments, "proyecto", "proyectos")}. " +
              "Archivalo en su lugar.";
    }

    private static void ValidateEmployee(string fullName, decimal? dailyRate)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new InvalidOperationException("El nombre del empleado es obligatorio.");
        }

        if (dailyRate is <= 0)
        {
            throw new InvalidOperationException(
                "El jornal tiene que ser mayor a cero. Dejalo vacío si todavía no lo arreglaste.");
        }
    }
}
