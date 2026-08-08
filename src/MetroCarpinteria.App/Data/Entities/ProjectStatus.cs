namespace MetroCarpinteria.App.Data.Entities;

public enum ProjectStatus
{
    Quote = 1,
    InProgress = 2,
    Completed = 3,
    Delivered = 4,

    /// <summary>Presupuesto que el cliente no aceptó. Queda como historial de lo cotizado.</summary>
    Rejected = 5
}
