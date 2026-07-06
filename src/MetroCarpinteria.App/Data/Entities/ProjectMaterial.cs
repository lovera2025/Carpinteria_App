namespace MetroCarpinteria.App.Data.Entities;

public class ProjectMaterial
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public int ProductId { get; set; }
    public decimal Quantity { get; set; }
    public DateTime AssignedAtUtc { get; set; }
    public Project Project { get; set; } = null!;
    public Product Product { get; set; } = null!;
}
