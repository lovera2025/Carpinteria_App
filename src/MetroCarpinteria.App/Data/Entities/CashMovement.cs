namespace MetroCarpinteria.App.Data.Entities;

/// <summary>
/// Un movimiento de la caja fuerte del taller: plata que entró o salió.
/// </summary>
/// <remarks>
/// <para>
/// La caja no tiene sesiones ni arqueo: es un saldo que corre y nunca se reinicia. Lo que
/// el taller quiere saber —cuánto hay, de dónde vino y adónde fue— sale de sumar estos
/// movimientos, así que la lista es el único control de la plata y tiene que estar completa.
/// </para>
/// <para>
/// Nada se borra. Un movimiento equivocado se corrige con otro que lo compensa, y los dos
/// quedan a la vista: borrar deja el saldo bien y la historia muda, que es exactamente
/// cómo aparecen números que después nadie puede explicar.
/// </para>
/// </remarks>
public class CashMovement
{
    public int Id { get; set; }

    /// <summary>
    /// La sesión de caja en la que se asentó, para los movimientos anteriores a que la
    /// caja pasara a ser una sola. Los nuevos no tienen ninguna.
    /// </summary>
    public int? CashSessionId { get; set; }
    public CashSession? CashSession { get; set; }

    public CashMovementType Type { get; set; }
    public decimal Amount { get; set; }

    /// <summary>
    /// Por dónde entró o salió la plata. Es lo que permite decir cuánto hay en efectivo
    /// —lo que está en el cajón— y cuánto en el banco, sin mezclarlos en un solo total.
    /// </summary>
    public PaymentMethod Method { get; set; }

    /// <summary>De qué trabajo entró o salió. Null en un movimiento suelto del taller.</summary>
    public int? ProjectId { get; set; }
    public Project? Project { get; set; }

    /// <summary>El cobro que lo generó, si vino de uno.</summary>
    public int? ProjectPaymentId { get; set; }

    /// <summary>
    /// A quién se le pagó, cuando tiene ficha en Personal. Permite ver cuánto se le pagó a
    /// alguien sumando todos los trabajos.
    /// </summary>
    public int? EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    /// <summary>
    /// La línea de mano de obra que se está pagando.
    /// </summary>
    /// <remarks>
    /// Va aparte de <see cref="EmployeeId"/> porque no todo operario tiene ficha: se puede
    /// cargar a alguien suelto en la mano de obra sin darlo de alta en Personal. La línea
    /// es la identidad que siempre existe, y es contra ella que se calcula cuánto le falta
    /// cobrar por ese trabajo.
    /// </remarks>
    public int? ProjectLaborLineId { get; set; }
    public ProjectLaborLine? ProjectLaborLine { get; set; }

    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
