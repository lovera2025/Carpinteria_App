namespace MetroCarpinteria.App.Data.Entities;

public enum ProjectStatus
{
    Quote = 1,

    /// <summary>El trabajo se está haciendo. En la pantalla se llama «En taller».</summary>
    InProgress = 2,

    /// <summary>Terminado. En la pantalla se llama «Listo».</summary>
    Completed = 3,

    /// <summary>
    /// Ya no se usa: duplicaba a <see cref="Completed"/> y nadie lo marcaba. La migración
    /// v12 reescribió las filas que estaban acá, pero el valor queda por si alguna se salvó.
    /// </summary>
    Delivered = 4,

    /// <summary>Presupuesto que el cliente no aceptó. Queda como historial de lo cotizado.</summary>
    Rejected = 5,

    /// <summary>
    /// Aprobado pero todavía no arrancado: el stock ya se descontó y los materiales están
    /// reservados, pero nadie se puso a trabajar.
    /// </summary>
    /// <remarks>
    /// Es un valor nuevo y no un reuso de <see cref="InProgress"/> a propósito: cuando se
    /// agregó, todos los trabajos en curso ya estaban andando de verdad y tenían que
    /// quedarse en «En taller».
    /// </remarks>
    Approved = 6
}
