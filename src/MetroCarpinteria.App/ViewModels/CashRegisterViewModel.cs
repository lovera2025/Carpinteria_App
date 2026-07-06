using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using MetroCarpinteria.App.Data.Entities;
using MetroCarpinteria.App.Helpers;
using MetroCarpinteria.App.Models;
using MetroCarpinteria.App.Services;

namespace MetroCarpinteria.App.ViewModels;

public class CashRegisterViewModel : ObservableObject
{
    private readonly Action _onDataChanged;
    private OpenCashSessionState? _currentSession;
    private string _openAmount = "0";
    private string _openNotes = string.Empty;
    private string _movementAmount = string.Empty;
    private string _movementReason = string.Empty;
    private bool _movementIsIncome = true;
    private string _closeCountedAmount = string.Empty;
    private string _closeNotes = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isStatusError;

    public CashRegisterViewModel(Action onDataChanged)
    {
        _onDataChanged = onDataChanged;
        Movements = new ObservableCollection<CashMovementListItem>();
        SessionHistory = new ObservableCollection<CashSessionListItem>();

        LoadCommand = new RelayCommand(_ => Load());
        OpenSessionCommand = new RelayCommand(_ => OpenSession(), _ => !HasOpenSession);
        RegisterMovementCommand = new RelayCommand(_ => RegisterMovement(), _ => HasOpenSession);
        CloseSessionCommand = new RelayCommand(_ => CloseSession(), _ => HasOpenSession);
    }

    public ObservableCollection<CashMovementListItem> Movements { get; }
    public ObservableCollection<CashSessionListItem> SessionHistory { get; }

    public bool HasOpenSession => CurrentSession is not null;

    public OpenCashSessionState? CurrentSession
    {
        get => _currentSession;
        private set
        {
            if (SetProperty(ref _currentSession, value))
            {
                OnPropertyChanged(nameof(HasOpenSession));
                OnPropertyChanged(nameof(SessionSummary));
                OnPropertyChanged(nameof(OpenedAtDisplay));
            }
        }
    }

    public string SessionSummary => CurrentSession is null
        ? "No hay caja abierta"
        : $"Caja abierta · Esperado: {CurrentSession.ExpectedDisplay}";

    public string OpenedAtDisplay => CurrentSession is null
        ? string.Empty
        : $"Desde {CurrentSession.OpenedAtLocal:dd/MM/yyyy HH:mm}";

    public string OpenAmount
    {
        get => _openAmount;
        set => SetProperty(ref _openAmount, value);
    }

    public string OpenNotes
    {
        get => _openNotes;
        set => SetProperty(ref _openNotes, value);
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

    public string CloseCountedAmount
    {
        get => _closeCountedAmount;
        set => SetProperty(ref _closeCountedAmount, value);
    }

    public string CloseNotes
    {
        get => _closeNotes;
        set => SetProperty(ref _closeNotes, value);
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
    public ICommand OpenSessionCommand { get; }
    public ICommand RegisterMovementCommand { get; }
    public ICommand CloseSessionCommand { get; }

    public void Load()
    {
        CurrentSession = AppHost.CashRegisterService.GetOpenSessionState();

        Movements.Clear();
        foreach (var movement in AppHost.CashRegisterService.GetCurrentMovements())
        {
            Movements.Add(movement);
        }

        SessionHistory.Clear();
        foreach (var session in AppHost.CashRegisterService.GetSessionHistory())
        {
            SessionHistory.Add(session);
        }

        _onDataChanged();
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    private void OpenSession()
    {
        try
        {
            if (!TryParseDecimal(OpenAmount, out var amount))
            {
                throw new InvalidOperationException("Monto inicial inválido.");
            }

            AppHost.CashRegisterService.OpenSession(amount, OpenNotes);
            OpenAmount = "0";
            OpenNotes = string.Empty;
            SetStatus("Caja abierta correctamente.", isError: false);
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
            if (!TryParseDecimal(MovementAmount, out var amount))
            {
                throw new InvalidOperationException("Monto inválido.");
            }

            var type = MovementIsIncome ? CashMovementType.Income : CashMovementType.Expense;
            AppHost.CashRegisterService.RegisterMovement(type, amount, MovementReason);

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

    private void CloseSession()
    {
        if (CurrentSession is null)
        {
            return;
        }

        if (!TryParseDecimal(CloseCountedAmount, out var counted))
        {
            SetStatus("Monto contado inválido.", isError: true);
            return;
        }

        var expected = CurrentSession.ExpectedBalance;
        var difference = counted - expected;
        var diffText = difference.ToString("C", CultureInfo.GetCultureInfo("es-AR"));

        var message = difference == 0
            ? $"¿Cerrar caja?\n\nContado: {counted:C} (cuadra exacto)"
            : $"¿Cerrar caja?\n\nEsperado: {expected:C}\nContado: {counted:C}\nDiferencia: {diffText}";

        var result = MessageBox.Show(
            message,
            "Confirmar cierre de caja",
            MessageBoxButton.YesNo,
            difference == 0 ? MessageBoxImage.Question : MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            AppHost.CashRegisterService.CloseSession(counted, CloseNotes);
            CloseCountedAmount = string.Empty;
            CloseNotes = string.Empty;
            SetStatus("Caja cerrada correctamente.", isError: false);
            Load();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private static bool TryParseDecimal(string value, out decimal result)
    {
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out result)
            || decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    }

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        IsStatusError = isError;
    }
}
