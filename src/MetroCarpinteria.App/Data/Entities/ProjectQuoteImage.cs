namespace MetroCarpinteria.App.Data.Entities;

/// <summary>
/// Foto de referencia de un presupuesto. El archivo vive en disco; acá solo el nombre
/// y el pie. Copiada al adjuntar, igual que el precio: si mañana se borra el original
/// del celular, el presupuesto sigue teniéndola.
/// </summary>
public class ProjectQuoteImage
{
    public int Id { get; set; }
    public int ProjectId { get; set; }

    /// <summary>
    /// Solo el nombre del archivo (<c>{guid}.jpg</c>), nunca una ruta. Así un respaldo
    /// restaurado en otra PC sigue encontrando las fotos.
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    public string? Caption { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public Project Project { get; set; } = null!;
}
