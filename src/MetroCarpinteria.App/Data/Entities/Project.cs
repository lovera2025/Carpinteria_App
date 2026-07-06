namespace MetroCarpinteria.App.Data.Entities;

public class Project
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal? Budget { get; set; }
    public ProjectStatus Status { get; set; } = ProjectStatus.Quote;
    public bool IsArchived { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public ICollection<ProjectMaterial> Materials { get; set; } = [];
    public ICollection<ProjectAssignment> Assignments { get; set; } = [];
}
