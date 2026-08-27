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

            var alreadyPaid = context.ProjectPayments
                .Where(p => p.ProjectId == projectId)
                .AsEnumerable()
                .Sum(p => p.Amount);

            var balance = project.Budget.Value - alreadyPaid;

            // Cobrar de más deja un saldo negativo que después nadie sabe si es una seña
            // doble, una devolución o un error de tipeo.
            if (amount > balance)
            {
                throw new InvalidOperationException(
                    $"No se puede cobrar más que el saldo. Queda por cobrar {AppCulture.Money(balance)}.");
            }

            var now = DateTime.UtcNow;
            int? cashMovementId = null;

            if (method == PaymentMethod.Cash)
            {
                cashMovementId = RegisterCashIncome(context, project, kind, amount, now);
            }

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
    /// Deshace un cobro.
    /// </summary>
    /// <remarks>
    /// Si el cobro pasó por Caja no se borra el movimiento: se asienta uno inverso. Borrar
    /// un ingreso de una sesión ya cerrada descuadraría un arqueo que alguien contó y
    /// firmó ese día. Y el movimiento inverso necesita una caja abierta donde asentarse.
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

            if (payment.CashMovementId.HasValue)
            {
                var session = context.CashSessions.FirstOrDefault(s => s.ClosedAtUtc == null)
                    ?? throw new CashRegisterClosedException(
                        "Este cobro entró por Caja, así que para anularlo hay que asentar la salida " +
                        "en una caja abierta.");

                context.CashMovements.Add(new CashMovement
                {
                    CashSessionId = session.Id,
                    Type = CashMovementType.Expense,
                    Amount = payment.Amount,
                    Reason = $"Anulación de {PaymentRules.GetKindLabel(payment.Kind).ToLowerInvariant()}: " +
                             $"{payment.Project.Title}" +
                             (string.IsNullOrWhiteSpace(reason) ? string.Empty : $" ({reason.Trim()})"),
                    CreatedAtUtc = DateTime.UtcNow
                });
            }

            payment.Project.UpdatedAtUtc = DateTime.UtcNow;
            context.ProjectPayments.Remove(payment);

            context.SaveChanges();
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <summary>Lo cobrado hasta ahora de un trabajo.</summary>
    /// <remarks>
    /// El <c>AsEnumerable</c> antes del <c>Sum</c> no es capricho: en las instalaciones
    /// viejas los importes son TEXT, y una suma que quede en SQL los trata como texto.
    /// </remarks>
    public static decimal ReadPaidTotal(AppDbContext context, int projectId) =>
        context.ProjectPayments
            .Where(p => p.ProjectId == projectId)
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

    private static int RegisterCashIncome(
        AppDbContext context,
        Project project,
        PaymentKind kind,
        decimal amount,
        DateTime now)
    {
        var session = context.CashSessions.FirstOrDefault(s => s.ClosedAtUtc == null)
            ?? throw new CashRegisterClosedException(
                "Para cobrar en efectivo tiene que haber una caja abierta, así el ingreso queda " +
                "asentado en el arqueo del día.");

        var movement = new CashMovement
        {
            CashSessionId = session.Id,
            Type = CashMovementType.Income,
            Amount = amount,
            Reason = $"{PaymentRules.GetKindLabel(kind)}: {project.Title}",
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
