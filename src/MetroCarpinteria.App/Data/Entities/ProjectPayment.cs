namespace MetroCarpinteria.App.Data.Entities;

/// <summary>Qué representa el cobro dentro del trabajo.</summary>
public enum PaymentKind
{
    /// <summary>Adelanto para arrancar el trabajo.</summary>
    Deposit = 0,

    /// <summary>Entrega a cuenta mientras el trabajo avanza.</summary>
    Partial = 1,

    /// <summary>Lo que faltaba, contra entrega.</summary>
    Final = 2
}

public enum PaymentMethod
{
    Cash = 0,
    Transfer = 1,
    Card = 2,
    Check = 3,
    Other = 4
}

/// <summary>Un cobro recibido a cuenta de un trabajo.</summary>
/// <remarks>
/// El saldo no se guarda: se calcula como <c>Budget − suma de los pagos</c>. Guardar un
/// saldo obliga a mantenerlo al día cada vez que cambia el precio o entra un cobro, y
/// basta que una de esas dos cosas falle para que la cuenta del cliente quede mal.
/// </remarks>
public class ProjectPayment
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public PaymentKind Kind { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }

    /// <summary>Movimiento de Caja que generó este cobro. Todo cobro asienta uno.</summary>
    /// <remarks>
    /// Un pago <b>no se borra</b>: se compensa con un movimiento inverso. Borrar un
    /// ingreso deja el saldo bien y la historia muda, y después nadie puede explicar por
    /// qué la cuenta del cliente dice lo que dice.
    /// </remarks>
    public int? CashMovementId { get; set; }

    /// <summary>Cuándo se anuló, si se anuló. Null es un cobro vigente.</summary>
    /// <remarks>
    /// Se anula por baja lógica y no borrando la fila: un cobro que existió es parte de la
    /// historia del trabajo, y el cliente puede preguntar por él meses después. Antes se
    /// borraba de verdad y del proyecto no quedaba ni rastro de que hubiera pasado.
    /// </remarks>
    public DateTime? CancelledAtUtc { get; set; }

    /// <summary>Por qué se anuló. Lo escribe quien anula.</summary>
    public string? CancelReason { get; set; }

    /// <summary>Un cobro que sigue contando para el saldo del cliente.</summary>
    public bool IsActive => CancelledAtUtc is null;

    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
