namespace MetroCarpinteria.App.Data.Entities;

/// <summary>
/// De dónde salió un movimiento de caja.
/// </summary>
/// <remarks>
/// Va como columna y no se deduce del texto del motivo: el motivo se puede editar —es
/// texto y no mueve saldos— así que clasificar por él haría que corregir una palabra
/// cambiara de categoría a la plata.
/// </remarks>
public enum CashMovementOrigin
{
    /// <summary>Cargado a mano desde Caja: un gasto del taller, plata que se saca.</summary>
    Manual = 0,

    /// <summary>Un cobro de un trabajo.</summary>
    Payment = 1,

    /// <summary>La salida que compensa un cobro anulado.</summary>
    PaymentCancellation = 2,

    /// <summary>
    /// La apertura de una caja de las de antes, convertida en movimiento por la migración.
    /// </summary>
    SessionOpening = 3,

    /// <summary>La diferencia de un arqueo viejo, convertida en movimiento.</summary>
    SessionAdjustment = 4,

    /// <summary>Corrige el importe de otro movimiento sin borrarlo.</summary>
    Correction = 5,

    /// <summary>Un pago a un operario por un trabajo.</summary>
    WorkerPayment = 6
}
