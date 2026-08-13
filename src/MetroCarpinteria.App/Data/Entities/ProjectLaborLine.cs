namespace MetroCarpinteria.App.Data.Entities;

/// <summary>
/// Un operario cotizado en un presupuesto, con sus días y su jornal.
/// </summary>
/// <remarks>
/// <para>
/// El jefe no vive acá: sigue en <see cref="Project.EstimatedDays"/> y
/// <see cref="Project.DailyRate"/>, que son exactamente eso desde siempre. Gracias a eso un
/// presupuesto viejo se lee como «el jefe, sin operarios» y da el mismo precio que el día
/// que se entregó, sin migrar una sola fila.
/// </para>
/// <para>
/// Es el equivalente de <see cref="ProjectBudgetLine"/> para la mano de obra: guarda las
/// entradas —días y jornal— y no el importe, y congela el nombre y el precio del momento.
/// </para>
/// </remarks>
public class ProjectLaborLine
{
    public int Id { get; set; }
    public int ProjectId { get; set; }

    /// <summary>
    /// Ficha del operario en Personal. Null cuando se cargó a alguien suelto que no está
    /// dado de alta —un changarín por una semana no obliga a crearle un legajo—.
    /// </summary>
    public int? EmployeeId { get; set; }

    /// <summary>
    /// Nombre congelado al agregar la línea, por si después se corrige o se archiva la ficha.
    /// Misma lógica que <see cref="Project.ClientName"/>.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    public decimal Days { get; set; }

    /// <summary>
    /// Jornal congelado. Viene de <see cref="Employee.DailyRate"/> al elegirlo, pero se
    /// puede pisar: si mañana le subís el jornal a alguien, los presupuestos ya hechos
    /// tienen que seguir dando lo mismo.
    /// </summary>
    public decimal DailyRate { get; set; }

    public int SortOrder { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public Project Project { get; set; } = null!;
    public Employee? Employee { get; set; }

    public decimal LineTotal => Math.Round(Days * DailyRate, 2, MidpointRounding.AwayFromZero);
}
