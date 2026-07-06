using MetroCarpinteria.App.Data;
using MetroCarpinteria.App.Data.Entities;
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
                IsArchived = e.IsArchived,
                ActiveAssignmentCount = context.ProjectAssignments.Count(a => a.EmployeeId == e.Id)
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
                AssignedAtLocal = a.AssignedAtUtc.ToLocalTime()
            })
            .ToList();
    }

    public Employee Create(string fullName, string? phone, string? role)
    {
        ValidateEmployee(fullName);

        using var context = _databaseService.CreateContext();
        var now = DateTime.UtcNow;
        var employee = new Employee
        {
            FullName = fullName.Trim(),
            Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
            Role = string.IsNullOrWhiteSpace(role) ? null : role.Trim(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        context.Employees.Add(employee);
        context.SaveChanges();
        return employee;
    }

    public void Update(int id, string fullName, string? phone, string? role)
    {
        ValidateEmployee(fullName);

        using var context = _databaseService.CreateContext();
        var employee = context.Employees.FirstOrDefault(e => e.Id == id)
            ?? throw new InvalidOperationException("Empleado no encontrado.");

        employee.FullName = fullName.Trim();
        employee.Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        employee.Role = string.IsNullOrWhiteSpace(role) ? null : role.Trim();
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

    public bool HasAssignments(int id)
    {
        using var context = _databaseService.CreateContext();
        return context.ProjectAssignments.Any(a => a.EmployeeId == id);
    }

    private static void ValidateEmployee(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new InvalidOperationException("El nombre del empleado es obligatorio.");
        }
    }
}
