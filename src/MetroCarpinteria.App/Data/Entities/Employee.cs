namespace MetroCarpinteria.App.Data.Entities;

public class Employee
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Role { get; set; }

    /// <summary>
    /// Cuánto cobra por día. Prellena la línea de mano de obra al elegirlo en un
    /// presupuesto; ahí el valor queda congelado, así que cambiarlo acá no mueve nada de
    /// lo ya cotizado. Null en las fichas viejas y en quien todavía no tiene jornal fijado.
    /// </summary>
    public decimal? DailyRate { get; set; }

    public bool IsArchived { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public ICollection<ProjectAssignment> Assignments { get; set; } = [];
}
