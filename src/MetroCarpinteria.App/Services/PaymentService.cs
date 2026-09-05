using MetroCarpinteria.App.Data;
using MetroCarpinteria.App.Data.Entities;
using MetroCarpinteria.App.Helpers;
using MetroCarpinteria.App.Models;
using Microsoft.EntityFrameworkCore;

namespace MetroCarpinteria.App.Services;

/// <summary>
/// Señas y pagos a cuenta de un trabajo, con su reflejo en Caja.
/// </summary>
/// <remarks>
/// El saldo nunca se guarda: es <c>Budget − suma de los cobros</c>, calculado al leer. Un
/// saldo persistido hay que mantenerlo al día con cada cambio de precio y con cada cobro,
/// y basta que una de las dos cosas falle para que la cuenta del cliente quede mal.
/// </remarks>
public sealed class PaymentService
{
    private readonly DatabaseService _databaseService;

    public PaymentService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    /// <summary>
    /// Registra un cobro. Si entró en efectivo, además lo asienta en la caja abierta.
    /// </summary>
    /// <exception cref="CashRegisterClosedException">
    /// Si el cobro es en efectivo y no hay caja abierta. La pantalla la usa para ofrecer
    /// abrirla sin perder lo que el usuario venía cargando.
    /// </exception>
    public ProjectPaymentItem RegisterPayment(
        int projectId,
        PaymentKind kind,
        decimal amount,
        PaymentMethod method,
        string? notes = null)
    {
        if (amount <= 0)
        {
            throw new InvalidOperationException("El importe del cobro tiene que ser mayor a cero.");
        }

        using var context = _databaseService.CreateContext();
        using var transaction = context.Database.BeginTransaction();

        try
        {
            var project = context.Projects.FirstOrDefault(p => p.Id == projectId)
                ?? throw new InvalidOperationException("Proyecto no encontrado.");

            if (project.IsArchived)
            {
                throw new InvalidOperationException("El proyecto está archivado.");
            }

            if (project.Budget is null or <= 0)
            {
                throw new InvalidOperationException(
                    "Falta el precio del trabajo: sin total no se puede saber cuánto queda por cobrar.");
            }

            var alreadyPaid = ReadPaidTotal(context, projectId);

            var balance = project.Budget.Value - alreadyPaid;

            // Cobrar de más deja un saldo negativo que después nadie sabe si es una seña
            // doble, una devolución o un error de tipeo.
            if (amount > balance)
            {
                throw new InvalidOperationException(
                    $"No se puede cobrar más que el saldo. Queda por cobrar {AppCulture.Money(balance)}.");
            }

            var now = DateTime.UtcNow;

            // Todo cobro asienta, sea efectivo o no: la caja es el libro de la plata del
            // taller, no solo del cajón.
            int? cashMovementId = RegisterIncome(context, project, kind, amount, method, now);

            var payment = new ProjectPayment
            {
                ProjectId = projectId,
                Kind = kind,
                Amount = amount,
                Method = method,
                CashMovementId = cashMovementId,
                Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
                CreatedAtUtc = now
            };

            context.ProjectPayments.Add(payment);
            project.UpdatedAtUtc = now;

            context.SaveChanges();
            transaction.Commit();

            return new ProjectPaymentItem
            {
                Id = payment.Id,
                Kind = payment.Kind,
                Amount = payment.Amount,
                Method = payment.Method,
                Notes = payment.Notes,
                CreatedAtLocal = now.ToLocalTime(),
                IsLinkedToCash = cashMovementId.HasValue
            };
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <summary>
    /// Anula un cobro: lo saca de la cuenta del cliente y compensa la plata en Caja.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>No se borra nada.</b> El movimiento original queda y se asienta uno inverso; el
    /// cobro queda marcado como anulado, con su motivo, en vez de desaparecer de la ficha
    /// del trabajo. Antes la fila se borraba de verdad: la plata quedaba compensada en
    /// Caja, pero del proyecto no quedaba ni rastro de que ese cobro hubiera existido, y
    /// meses después nadie podía contestarle al cliente por qué la cuenta decía lo que
    /// decía.
    /// </para>
    /// <para>
    /// Anular no depende del estado de nada: la caja no se abre ni se cierra.
    /// </para>
    /// </remarks>
    public void CancelPayment(int paymentId, string reason)
    {
        using var context = _databaseService.CreateContext();
        using var transaction = context.Database.BeginTransaction();

        try
        {
            var payment = context.ProjectPayments
                .Include(p => p.Project)
                .FirstOrDefault(p => p.Id == paymentId)
                ?? throw new InvalidOperationException("Cobro no encontrado.");

            if (!payment.IsActive)
            {
                throw new InvalidOperationException("Este cobro ya estaba anulado.");
            }

            var now = DateTime.UtcNow;
            var trimmed = reason?.Trim();

            context.CashMovements.Add(new CashMovement
            {
                Type = CashMovementType.Expense,
                Amount = payment.Amount,
                Method = payment.Method,
                Origin = CashMovementOrigin.PaymentCancellation,
                ProjectId = payment.ProjectId,
                ProjectPaymentId = payment.Id,
                Reason = $"Anulación de {PaymentRules.GetKindLabel(payment.Kind).ToLowerInvariant()}: " +
                         $"{payment.Project.Title} — {payment.Project.ClientName}" +
                         (string.IsNullOrWhiteSpace(trimmed) ? string.Empty : $" ({trimmed})"),
                CreatedAtUtc = now
            });

            payment.CancelledAtUtc = now;
            payment.CancelReason = string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
            payment.Project.UpdatedAtUtc = now;

            context.SaveChanges();
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <summary>Lo cobrado hasta ahora de un trabajo, sin contar lo anulado.</summary>
    /// <remarks>
    /// <para>
    /// El <c>AsEnumerable</c> antes del <c>Sum</c> no es capricho: en las instalaciones
    /// viejas los importes son TEXT, y una suma que quede en SQL los trata como texto.
    /// </para>
    /// <para>
    /// El filtro de anulados tampoco: los cobros anulados ya no se borran, así que sin
    /// esto seguirían contando y el cliente aparecería debiendo de menos.
    /// </para>
    /// </remarks>
    public static decimal ReadPaidTotal(AppDbContext context, int projectId) =>
        context.ProjectPayments
            .Where(p => p.ProjectId == projectId && p.CancelledAtUtc == null)
            .AsEnumerable()
            .Sum(p => p.Amount);

    /// <summary>
    /// Corta si el precio nuevo dejaría al cliente con plata a favor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es la otra mitad de la regla que <see cref="RegisterPayment"/> ya cuida desde el lado
    /// del cobro: si no se puede cobrar de más, tampoco se puede bajar el precio hasta que
    /// lo cobrado quede de más. Sin esto se tomaba una seña de $ 50.000, se cerraba el
    /// trabajo en $ 30.000, y la pantalla decía «Cobrado por completo» con saldo cero: los
    /// $ 20.000 que hay que devolver no figuraban en ningún lado y nadie se enteraba.
    /// </para>
    /// <para>
    /// La llaman los dos caminos donde alguien <b>escribe</b> un precio. El recálculo
    /// automático de la calculadora no pasa por acá a propósito: corre en cada salida de
    /// campo, y cortarlo ahí llenaría la pantalla de errores a mitad de la carga. Ese caso
    /// lo cubre el saldo, que ahora muestra la plata a favor en vez de esconderla en un cero.
    /// </para>
    /// </remarks>
    public static void RequireBudgetCoversPayments(AppDbContext context, int projectId, decimal? newBudget)
    {
        var paid = ReadPaidTotal(context, projectId);

        if (paid <= 0m)
        {
            return;
        }

        var price = newBudget ?? 0m;

        if (price >= paid)
        {
            return;
        }

        var credit = AppCulture.Money(paid - price);

        var intent = newBudget is null
            ? "Dejarlo sin precio"
            : $"Un precio de {AppCulture.Money(price)}";

        throw new InvalidOperationException(
            $"Ya se cobraron {AppCulture.Money(paid)} de este trabajo. {intent} le dejaría " +
            $"{credit} a favor al cliente: anulá un cobro primero, o poné " +
            $"{AppCulture.Money(paid)} o más.");
    }

    /// <summary>
    /// Asienta el cobro en la caja fuerte, sea cual sea el medio.
    /// </summary>
    /// <remarks>
    /// Antes solo entraba el efectivo: un cobro por transferencia bajaba el saldo del
    /// cliente y no dejaba rastro en ningún lado. El taller lo reportó como plata que
    /// había cobrado y no le figuraba. El medio se guarda en el movimiento, que es lo que
    /// después permite separar lo que está en el cajón de lo que está en el banco.
    /// </remarks>
    private static int RegisterIncome(
        AppDbContext context,
        Project project,
        PaymentKind kind,
        decimal amount,
        PaymentMethod method,
        DateTime now)
    {
        var movement = new CashMovement
        {
            Type = CashMovementType.Income,
            Amount = amount,
            Method = method,
            Origin = CashMovementOrigin.Payment,
            ProjectId = project.Id,

            // El cliente va en el texto además de la relación: el taller mira el renglón,
            // no la base, y «Seña: Mostrador» sin nombre no le dice de quién es la plata.
            Reason = $"{PaymentRules.GetKindLabel(kind)}: {project.Title} — {project.ClientName}",
            CreatedAtUtc = now
        };

        context.CashMovements.Add(movement);

        // Se guarda ya para tener el Id con el que vincular el cobro; sigue todo dentro
        // de la misma transacción, así que o entran los dos o no entra ninguno.
        context.SaveChanges();
        return movement.Id;
    }
}

/// <summary>
/// Hace falta una caja abierta y no la hay.
/// </summary>
/// <remarks>
/// Es un tipo aparte y no un <see cref="InvalidOperationException"/> más para que la
/// pantalla lo distinga del resto: es el único error de cobro que se resuelve con un botón
/// —«Abrir caja»— en vez de con una corrección de lo tipeado.
/// </remarks>
public sealed class CashRegisterClosedException(string message) : InvalidOperationException(message);
