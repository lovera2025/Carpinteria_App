namespace MetroCarpinteria.App.Data.Entities;

public class ProjectAssignment
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public int EmployeeId { get; set; }
    public string? Notes { get; set; }
    public DateTime AssignedAtUtc { get; set; }

    /// <summary>Si ya se le pagó el jornal de este trabajo. Arranca en pendiente.</summary>
    public bool IsPaid { get; set; }
    public Project Project { get; set; } = null!;
    public Employee Employee { get; set; } = null!;
}
