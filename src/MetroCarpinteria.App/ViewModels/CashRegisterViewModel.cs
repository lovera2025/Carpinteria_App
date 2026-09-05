using System.Collections.ObjectModel;
using System.Windows.Input;
using MetroCarpinteria.App.Data.Entities;
using MetroCarpinteria.App.Helpers;
using MetroCarpinteria.App.Models;
using MetroCarpinteria.App.Services;

namespace MetroCarpinteria.App.ViewModels;

/// <summary>
/// La caja fuerte del taller: cuánta plata hay y el historial de todo lo que entró y salió.
/// </summary>
/// <remarks>
/// Sin sesiones que abrir ni cerrar. Los totales que se muestran salen de
/// <see cref="CashRegisterService.GetBalance"/>, calculados sobre todos los movimientos —
/// nunca sumando la lista cargada, que está recortada y filtrada.
/// </remarks>
public class CashRegisterViewModel : ViewModelBase
{
    /// <summary>Cuántos renglones se traen. Alcanza para años de un taller.</summary>
    private const int MovementLimit = 500;

    private readonly Action _onDataChanged;
    private CashBalance _balance = CashBalance.Empty;
    private CashConversionReview? _review;
    private string _movementAmount = string.Empty;
    private string _movementReason = string.Empty;
    private bool _movementIsIncome = true;
    private MethodOption _movementMethod;
    private MethodOption _filterMethod;
    private string _statusMessage = string.Empty;
    private bool _isStatusError;

    public CashRegisterViewModel(Action onDataChanged)
    {
        _onDataChanged = onDataChanged;
        Movements = new ObservableCollection<CashMovementListItem>();
        MethodTotals = new ObservableCollection<CashMethodTotal>();
        SuspiciousOpenings = new ObservableCollection<SuspiciousOpening>();

        MovementMethods = PaymentRules.Methods.Select(m => new MethodOption(m)).ToList();
        FilterMethods = new List<MethodOption> { MethodOption.Any }
            .Concat(PaymentRules.Methods.Select(m => new MethodOption(m)))
            .ToList();

        _movementMethod = MovementMethods[0];
        _filterMethod = FilterMethods[0];

        LoadCommand = new RelayCommand(_ => Load());
        RegisterMovementCommand = new RelayCommand(_ => RegisterMovement());
        DismissSuspiciousCommand = new RelayCommand(_ => DismissSuspicious());
        DiscardOpeningCommand = new AsyncRelayCommand(DiscardOpeningAsync);
    }

    public ObservableCollection<CashMovementListItem> Movements { get; }

    /// <summary>Cuánto hay por cada medio: el efectivo es lo único que está en el cajón.</summary>
    public ObservableCollection<CashMethodTotal> MethodTotals { get; }

    /// <summary>Aperturas viejas que podrían estar contadas dos veces.</summary>
    public ObservableCollection<SuspiciousOpening> SuspiciousOpenings { get; }

    public IReadOnlyList<MethodOption> MovementMethods { get; }
    public IReadOnlyList<MethodOption> FilterMethods { get; }

    /// <summary>
    /// Hay una apertura vieja que puede estar contada dos veces, y nadie dijo qué hacer.
    /// </summary>
    /// <remarks>
    /// Es lo único que se avisa después de convertir la caja. De dónde sale cada peso ya
    /// está en el historial; esto es lo que el historial no puede contestar solo.
    /// </remarks>
    public bool ShowSuspiciousNotice => SuspiciousOpenings.Count > 0;

    public string SuspiciousSummary => _review?.SuspiciousSummary ?? string.Empty;

    public CashBalance Balance
    {
        get => _balance;
        private set
        {
            if (SetProperty(ref _balance, value))
            {
                OnPropertyChanged(nameof(BalanceSummary));
                OnPropertyChanged(nameof(HistorySummary));
                OnPropertyChanged(nameof(HasMovements));
            }
        }
    }

    public bool HasMovements => Balance.MovementCount > 0;

    /// <summary>
    /// El subtítulo de la pantalla. No repite plata: el saldo ya está grande abajo, y
    /// arrancar el encabezado con un número en rojo asusta sin explicar nada.
    /// </summary>
    public string BalanceSummary => Balance.MovementCount switch
    {
        0 => "Todavía no hay movimientos en la caja",
        1 => "1 movimiento registrado",
        var n => $"{n} movimientos registrados"
    };

    /// <summary>
    /// El histórico, en una línea. Va abajo y chico a propósito: es un dato de contexto,
    /// no algo que se mire todos los días, y arriba competía con el saldo.
    /// </summary>
    public string HistorySummary => HasMovements
        ? $"Desde siempre entraron {Balance.IncomeDisplay} y salieron {Balance.ExpenseDisplay}."
        : string.Empty;

    public MethodOption MovementMethod
    {
        get => _movementMethod;
        set => SetProperty(ref _movementMethod, value);
    }

    /// <summary>Qué medio se está mirando. Cambiarlo recarga la lista.</summary>
    public MethodOption FilterMethod
    {
        get => _filterMethod;
        set
        {
            if (SetProperty(ref _filterMethod, value))
            {
                Load();
            }
        }
    }

    public string MovementAmount
    {
        get => _movementAmount;
        set => SetProperty(ref _movementAmount, value);
    }

    public string MovementReason
    {
        get => _movementReason;
        set => SetProperty(ref _movementReason, value);
    }

    public bool MovementIsIncome
    {
        get => _movementIsIncome;
        set => SetProperty(ref _movementIsIncome, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsStatusError
    {
        get => _isStatusError;
        private set => SetProperty(ref _isStatusError, value);
    }

    public ICommand LoadCommand { get; }
    public ICommand RegisterMovementCommand { get; }
    public ICommand DismissSuspiciousCommand { get; }
    public ICommand DiscardOpeningCommand { get; }

    public void Load() => SafeLoad(LoadCore, "Caja");

    private void LoadCore()
    {
        // El saldo se pide aparte y no se saca de la lista: la lista viene filtrada y
        // recortada, así que sumarla daría un total que no es el de la caja.
        Balance = AppHost.CashRegisterService.GetBalance();

        MethodTotals.Clear();
        foreach (var total in Balance.ByMethod)
        {
            MethodTotals.Add(total);
        }

        LoadReview();

        var filter = new CashMovementFilter { Method = FilterMethod.Method };

        Movements.Clear();
        foreach (var movement in AppHost.CashRegisterService.GetMovements(filter, MovementLimit))
        {
            Movements.Add(movement);
        }

        _onDataChanged();
        CommandManager.InvalidateRequerySuggested();
    }

    private void LoadReview()
    {
        _review = AppHost.Settings.CashSafeReviewedAtUtc is null
            ? AppHost.CashRegisterService.GetConversionReview()
            : null;

        SuspiciousOpenings.Clear();

        foreach (var suspicious in _review?.Suspicious ?? [])
        {
            SuspiciousOpenings.Add(suspicious);
        }

        OnPropertyChanged(nameof(ShowSuspiciousNotice));
        OnPropertyChanged(nameof(SuspiciousSummary));
    }

    /// <summary>
    /// El taller dice que el saldo está bien y el aviso no vuelve a aparecer.
    /// </summary>
    /// <remarks>
    /// Sin diálogo de confirmación. Es un aviso, no una decisión grave, y hacerle
    /// confirmar dos veces para sacar un cartel es la clase de ceremonia que termina con
    /// que nadie lea ningún cartel.
    /// </remarks>
    private void DismissSuspicious()
    {
        try
        {
            AppHost.SettingsService.Update(s => s.CashSafeReviewedAtUtc = DateTime.UtcNow);
            Load();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    /// <summary>
    /// Descuenta una apertura que ya venía contada, sin borrar el renglón.
    /// </summary>
    private async Task DiscardOpeningAsync(object? parameter)
    {
        if (parameter is not SuspiciousOpening opening)
        {
            return;
        }

        var confirmed = await AppHost.DialogService.ConfirmAsync(
            "Descontar una apertura duplicada",
            $"{opening.Explanation}\n\n" +
            "No se borra nada: se asienta un movimiento que la descuenta, y los dos quedan " +
            "a la vista en el historial.",
            confirmText: "Descontarla");

        if (!confirmed)
        {
            return;
        }

        try
        {
            AppHost.CashRegisterService.DiscardDuplicatedOpening(opening.MovementId);
            SetStatus($"Se descontó la apertura de {opening.AmountDisplay}.", isError: false);
            Load();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private void RegisterMovement()
    {
        try
        {
            if (!NumberInput.TryParseMoney(MovementAmount, out var amount))
            {
                throw new InvalidOperationException("Monto inválido.");
            }

            var type = MovementIsIncome ? CashMovementType.Income : CashMovementType.Expense;

            AppHost.CashRegisterService.RegisterMovement(
                type,
                amount,
                MovementReason,
                MovementMethod.Method ?? PaymentMethod.Cash);

            MovementAmount = string.Empty;
            MovementReason = string.Empty;
            var label = MovementIsIncome ? "Ingreso" : "Egreso";
            SetStatus($"{label} registrado.", isError: false);
            Load();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    /// <summary>
    /// Publica el resultado de una acción.
    /// <para>
    /// Antes esto llenaba una barra fija arriba de la pantalla que <b>nunca se borraba</b>:
    /// un "Producto creado." quedaba ahí para siempre, y al rato no se sabía si
    /// correspondía a lo de recién o a algo de veinte minutos antes. Ahora va al aviso
    /// flotante, que se descarta solo.
    /// </para>
    /// <para>
    /// StatusMessage se sigue actualizando porque los tests lo leen para verificar qué
    /// pasó tras una acción.
    /// </para>
    /// </summary>
    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        IsStatusError = isError;

        if (string.IsNullOrWhiteSpace(message) || !AppHost.IsReady)
        {
            return;
        }

        if (isError)
        {
            AppHost.NotificationService.Warning(message);
        }
        else
        {
            AppHost.NotificationService.Success(message);
        }
    }

    /// <summary>Un medio de pago para los combos. <c>Method</c> null es «todos».</summary>
    public sealed class MethodOption
    {
        public MethodOption(PaymentMethod method)
        {
            Method = method;
            Label = PaymentRules.GetMethodLabel(method);
        }

        private MethodOption()
        {
            Method = null;
            Label = "Todos los medios";
        }

        public PaymentMethod? Method { get; }
        public string Label { get; }

        public static MethodOption Any { get; } = new();
    }
}
