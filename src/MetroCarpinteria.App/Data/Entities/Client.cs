namespace MetroCarpinteria.App.Data.Entities;

/// <summary>Ficha de un cliente del taller.</summary>
/// <remarks>
/// <see cref="Project.ClientName"/> no se elimina al aparecer esta tabla: queda como
/// instantánea de cómo se escribió el nombre en ese presupuesto. Un presupuesto ya
/// entregado tiene que poder reimprimirse igual aunque después se corrija la ficha, y
/// mantener las dos cosas hace que la migración sea reversible en la práctica.
/// </remarks>
public class Client
{
    public int Id { get; set; }

    /// <summary>El nombre tal como se muestra, con acentos y mayúsculas como se tipeó.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>La clave de comparación. La calcula <see cref="Models.ClientRules.Normalize"/>.</summary>
    public string NormalizedName { get; set; } = string.Empty;

    public string? Phone { get; set; }
    public string? Email { get; set; }

    /// <summary>CUIT o CUIL, para la facturación.</summary>
    public string? TaxId { get; set; }

    public string? Address { get; set; }
    public string? Notes { get; set; }

    public bool IsArchived { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public ICollection<Project> Projects { get; set; } = [];
}
