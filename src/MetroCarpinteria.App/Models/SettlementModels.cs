using MetroCarpinteria.App.Helpers;

namespace MetroCarpinteria.App.Models;

/// <summary>
/// Lo que le toca a una persona por un trabajo terminado, y cuánto de eso ya cobró.
/// </summary>
/// <remarks>
/// <para>
/// Lo pagado no se guarda acá ni en la asignación: sale de sumar los egresos de caja
/// anotados contra esta línea. Los movimientos <b>son</b> el registro del pago, así que
/// pagar en varias veces sale solo y no hay dos números que puedan discrepar.
/// </para>
/// <para>
/// El jefe no aparece en esta lista. Lo suyo —«esto lo puedo sacar yo: mi ganancia»— es un
/// egreso normal con el proyecto anotado, no una línea de mano de obra.
/// </para>
/// </remarks>
public sealed class SettlementWorkerItem
{
    public int ProjectId { get; init; }
    public int LaborLineId { get; init; }

    /// <summary>Ficha en Personal. Null cuando se cotizó a alguien suelto, sin legajo.</summary>
    public int? EmployeeId { get; init; }

    public required string Description { get; init; }
    public decimal Days { get; init; }
    public decimal DailyRate { get; init; }

    /// <summary>Días × jornal, con los valores congelados al cotizar.</summary>
    public decimal Due { get; init; }

    /// <summary>La suma de los egresos de caja anotados contra esta línea.</summary>
    public decimal Paid { get; init; }

    public decimal Pending => Math.Max(0m, Due - Paid);

    /// <summary>
    /// Saldado es <c>pagado &gt;= le toca</c>, no <c>==</c>. Con pagos parciales, exigir
    /// igualdad deja a alguien figurando como pendiente por un centavo para siempre.
    /// </summary>
    public bool IsSettled => Paid >= Due;

    /// <summary>Cobró algo pero no todo: la fila tiene que mostrar las dos cosas.</summary>
    public bool IsPartiallyPaid => Paid > 0m && !IsSettled;

    /// <summary>Se le pagó de más. No es un error a corregir: puede ser un adelanto.</summary>
    public bool IsOverpaid => Paid > Due;

    public string RateDisplay => Days == 1m
        ? $"1 día × {AppCulture.Money(DailyRate)}"
        : $"{AppCulture.Quantity(Days)} días × {AppCulture.Money(DailyRate)}";

    public string DueDisplay => AppCulture.Money(Due);
    public string PaidDisplay => AppCulture.Money(Paid);
    public string PendingDisplay => AppCulture.Money(Pending);

    public string StatusLabel => IsSettled
        ? IsOverpaid ? "Saldado · cobró de más" : "Saldado"
        : IsPartiallyPaid ? "Cobró una parte" : "Pendiente";

    /// <summary>«Le pagaste $ 20.000 de $ 66.000», para no obligar a restar de cabeza.</summary>
    public string ProgressDisplay => IsSettled
        ? $"Cobró {PaidDisplay}"
        : Paid > 0m
            ? $"Cobró {PaidDisplay} de {DueDisplay} · falta {PendingDisplay}"
            : $"Le debés {DueDisplay}";
}

/// <summary>Un trabajo terminado con su desglose y lo que se le debe a cada uno.</summary>
public sealed class SettlementProjectItem
{
    public int Id { get; init; }
    public required string Title { get; init; }
    public required string ClientName { get; init; }
    public decimal? Budget { get; init; }

    /// <summary>
    /// Cuándo se lo tocó por última vez. No hay fecha de «terminado» guardada, así que la
    /// pantalla no la inventa: dice lo que este dato es.
    /// </summary>
    public DateTime UpdatedAtLocal { get; init; }

    /// <summary>El desglose reconstruido. Null si el trabajo nunca se calculó.</summary>
    public BudgetBreakdown? Breakdown { get; init; }

    public IReadOnlyList<SettlementWorkerItem> Workers { get; init; } = [];

    /// <summary>Lo que se cotizó de materiales, que es con lo que se armó el precio.</summary>
    public decimal QuotedMaterials { get; init; }

    /// <summary>
    /// Lo que salió del inventario para este trabajo, valuado con los costos congelados.
    /// </summary>
    public decimal SpentMaterials { get; init; }

    /// <summary>Cuánto de lo cargado después se le sumó al cliente.</summary>
    public decimal BilledExtras { get; init; }

    /// <summary>
    /// Gastó más material del que cotizó. Es plata que sale de su ganancia, salvo la parte
    /// que le haya sumado al cliente.
    /// </summary>
    public bool SpentMoreThanQuoted => SpentMaterials > QuotedMaterials;

    /// <summary>Lo que puso él de su bolsillo: lo gastado de más que no le cobró a nadie.</summary>
    public decimal AbsorbedMaterials =>
        Math.Max(0m, SpentMaterials - QuotedMaterials - BilledExtras);

    public string QuotedMaterialsDisplay => AppCulture.Money(QuotedMaterials);
    public string SpentMaterialsDisplay => AppCulture.Money(SpentMaterials);
    public string AbsorbedMaterialsDisplay => AppCulture.Money(AbsorbedMaterials);

    /// <summary>
    /// La frase que contesta «gasté más de lo que cobré». Vacía cuando no hay nada que
    /// avisar: si gastó lo que cotizó, no hay por qué decir nada.
    /// </summary>
    public string MaterialsNote
    {
        get
        {
            if (!SpentMoreThanQuoted)
            {
                return string.Empty;
            }

            var note = $"Cotizaste {QuotedMaterialsDisplay} de materiales y gastaste {SpentMaterialsDisplay}.";

            if (BilledExtras > 0m && AbsorbedMaterials > 0m)
            {
                return note + $" Le sumaste {AppCulture.Money(BilledExtras)} al trabajo y " +
                       $"{AbsorbedMaterialsDisplay} los pusiste vos.";
            }

            if (BilledExtras > 0m)
            {
                return note + $" La diferencia se la sumaste al trabajo.";
            }

            return note + $" Los {AbsorbedMaterialsDisplay} de más salen de tu ganancia.";
        }
    }

    public bool HasMaterialsNote => MaterialsNote.Length > 0;

    public decimal TotalDue => Workers.Sum(w => w.Due);
    public decimal TotalPaid => Workers.Sum(w => w.Paid);
    public decimal TotalPending => Workers.Sum(w => w.Pending);

    /// <summary>No hay operarios cotizados: el trabajo lo hizo el jefe solo.</summary>
    public bool IsForemanOnly => Workers.Count == 0;

    public bool HasPending => TotalPending > 0m;

    public string BudgetDisplay => AppCulture.Money(Budget);
    public string TotalDueDisplay => AppCulture.Money(TotalDue);
    public string TotalPaidDisplay => AppCulture.Money(TotalPaid);
    public string TotalPendingDisplay => AppCulture.Money(TotalPending);

    public string UpdatedDisplay => AppCulture.ShortDate(UpdatedAtLocal);

    public string StatusLabel => IsForemanOnly
        ? "Sin operarios"
        : HasPending
            ? $"Falta pagar {TotalPendingDisplay}"
            : "Todo pagado";
}

/// <summary>Cuánto se le debe a una persona sumando todos sus trabajos terminados.</summary>
public sealed class SettlementDebtItem
{
    public int? EmployeeId { get; init; }
    public required string Description { get; init; }
    public decimal Pending { get; init; }
    public int ProjectCount { get; init; }

    public string PendingDisplay => AppCulture.Money(Pending);

    public string Summary => ProjectCount == 1
        ? $"{Description}: {PendingDisplay}"
        : $"{Description}: {PendingDisplay} en {ProjectCount} trabajos";
}
