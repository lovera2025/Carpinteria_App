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

        MovementMethods = PaymentRules.Methods.Select(m => new MethodOption(m)).ToList();
        FilterMethods = new List<MethodOption> { MethodOption.Any }
            .Concat(PaymentRules.Methods.Select(m => new MethodOption(m)))
            .ToList();

        _movementMethod = MovementMethods[0];
        _filterMethod = FilterMethods[0];

        LoadCommand = new RelayCommand(_ => Load());
        RegisterMovementCommand = new RelayCommand(_ => RegisterMovement());
    }

    public ObservableCollection<CashMovementListItem> Movements { get; }

    /// <summary>Cuánto hay por cada medio: el efectivo es lo único que está en el cajón.</summary>
    public ObservableCollection<CashMethodTotal> MethodTotals { get; }

    public IReadOnlyList<MethodOption> MovementMethods { get; }
    public IReadOnlyList<MethodOption> FilterMethods { get; }

    public CashBalance Balance
    {
        get => _balance;
        private set
        {
            if (SetProperty(ref _balance, value))
            {
                OnPropertyChanged(nameof(BalanceSummary));
                OnPropertyChanged(nameof(HasMovements));
            }
        }
    }

    public bool HasMovements => Balance.MovementCount > 0;

    public string BalanceSummary => HasMovements
        ? $"En efectivo: {Balance.CashOnHandDisplay} · Movimientos: {Balance.MovementCount}"
        : "Todavía no hay movimientos en la caja";

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

        var filter = new CashMovementFilter { Method = FilterMethod.Method };

        Movements.Clear();
        foreach (var movement in AppHost.CashRegisterService.GetMovements(filter, MovementLimit))
        {
            Movements.Add(movement);
        }

        _onDataChanged();
        CommandManager.InvalidateRequerySuggested();
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
