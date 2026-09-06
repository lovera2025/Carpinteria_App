using System.Collections.ObjectModel;
using System.Windows.Input;
using MetroCarpinteria.App.Data.Entities;
using MetroCarpinteria.App.Helpers;
using MetroCarpinteria.App.Models;
using MetroCarpinteria.App.Services;

namespace MetroCarpinteria.App.ViewModels;

/// <summary>
/// «Terminados»: los trabajos cerrados, su desglose, y a quién hay que pagarle.
/// </summary>
/// <remarks>
/// Es una pantalla propia y no un filtro de Proyectos porque el dato que él pide primero es
/// el agregado: cuánta plata debe en total. Eso no se puede leer de una lista filtrada.
/// </remarks>
public class SettlementsViewModel : ViewModelBase
{
    private readonly Action _onDataChanged;
    private SettlementProjectItem? _selectedProject;
    private SettlementWorkerItem? _selectedWorker;
    private string _payAmount = string.Empty;
    private CashRegisterViewModel.MethodOption _payMethod;
    private string _statusMessage = string.Empty;
    private bool _isStatusError;

    public SettlementsViewModel(Action onDataChanged)
    {
        _onDataChanged = onDataChanged;

        Projects = new ObservableCollection<SettlementProjectItem>();
        Workers = new ObservableCollection<SettlementWorkerItem>();
        BreakdownLines = new ObservableCollection<BudgetBreakdownLine>();
        Debts = new ObservableCollection<SettlementDebtItem>();

        PayMethods =
        [
            new(PaymentMethod.Cash),
            new(PaymentMethod.Transfer),
            new(PaymentMethod.Card),
            new(PaymentMethod.Check),
            new(PaymentMethod.Other)
        ];
        _payMethod = PayMethods[0];

        LoadCommand = new RelayCommand(_ => Load());
        PayCommand = new RelayCommand(_ => Pay(), _ => CanPay);
        PayAllCommand = new RelayCommand(_ => PayAll(), _ => CanPayAll);
    }

    public ObservableCollection<SettlementProjectItem> Projects { get; }
    public ObservableCollection<SettlementWorkerItem> Workers { get; }
    public ObservableCollection<BudgetBreakdownLine> BreakdownLines { get; }

    /// <summary>A quién le debe, sumando todos los trabajos terminados.</summary>
    public ObservableCollection<SettlementDebtItem> Debts { get; }

    public IReadOnlyList<CashRegisterViewModel.MethodOption> PayMethods { get; }

    public ICommand LoadCommand { get; }
    public ICommand PayCommand { get; }
    public ICommand PayAllCommand { get; }

    public SettlementProjectItem? SelectedProject
    {
        get => _selectedProject;
        set
        {
            if (!SetProperty(ref _selectedProject, value))
            {
                return;
            }

            ShowSelection();
        }
    }

    public SettlementWorkerItem? SelectedWorker
    {
        get => _selectedWorker;
        set
        {
            if (!SetProperty(ref _selectedWorker, value))
            {
                return;
            }

            // El importe se precarga con lo que falta, pero queda editable: puede pagarle
            // una parte, o de más si le adelanta plata.
            PayAmount = value is null || value.Pending <= 0m
                ? string.Empty
                : AppCulture.Quantity(value.Pending);

            OnPropertyChanged(nameof(HasSelectedWorker));
            OnPropertyChanged(nameof(PayHeader));
            OnPropertyChanged(nameof(CanPay));
        }
    }

    public bool HasSelectedWorker => SelectedWorker is not null;

    public string PayHeader => SelectedWorker is null
        ? "Elegí a quién le vas a pagar"
        : $"Pagarle a {SelectedWorker.Description}";

    public string PayAmount
    {
        get => _payAmount;
        set
        {
            if (SetProperty(ref _payAmount, value))
            {
                OnPropertyChanged(nameof(CanPay));
            }
        }
    }

    public CashRegisterViewModel.MethodOption PayMethod
    {
        get => _payMethod;
        set => SetProperty(ref _payMethod, value);
    }

    public bool CanPay => SelectedWorker is not null
        && NumberInput.TryParseMoney(PayAmount, out var amount)
        && amount > 0m;

    /// <summary>Saldar de una lo que falta, que es el caso normal: le paga todo junto.</summary>
    public bool CanPayAll => SelectedWorker is { Pending: > 0m };

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

    // --- Totales de arriba ----------------------------------------------------

    public decimal TotalPending => Projects.Sum(p => p.TotalPending);

    public string TotalPendingDisplay => AppCulture.Money(TotalPending);

    public bool HasDebts => Debts.Count > 0;

    public string HeadlineDetail => Projects.Count == 0
        ? "Todavía no hay trabajos terminados."
        : TotalPending > 0m
            ? $"Le debés plata a {Phrases.Count(Debts.Count, "persona", "personas")} " +
              $"por {Phrases.Count(Projects.Count(p => p.HasPending), "trabajo", "trabajos")}."
            : "No le debés mano de obra a nadie.";

    public bool HasProjects => Projects.Count > 0;

    public void Load() => SafeLoad(LoadCore, "Terminados");

    private void LoadCore()
    {
        var previousProjectId = SelectedProject?.Id;
        var previousWorkerId = SelectedWorker?.LaborLineId;

        Projects.Clear();
        foreach (var project in AppHost.SettlementService.GetFinished())
        {
            Projects.Add(project);
        }

        Debts.Clear();
        foreach (var debt in AppHost.SettlementService.GetPendingByWorker())
        {
            Debts.Add(debt);
        }

        // Se vuelve a lo que estaba seleccionado: pagarle a alguien recarga la lista, y
        // perder la selección obligaría a buscar el trabajo de nuevo en cada pago.
        SelectedProject = Projects.FirstOrDefault(p => p.Id == previousProjectId)
            ?? Projects.FirstOrDefault();

        if (previousWorkerId is not null)
        {
            SelectedWorker = Workers.FirstOrDefault(w => w.LaborLineId == previousWorkerId);
        }

        OnPropertyChanged(nameof(TotalPending));
        OnPropertyChanged(nameof(TotalPendingDisplay));
        OnPropertyChanged(nameof(HasDebts));
        OnPropertyChanged(nameof(HasProjects));
        OnPropertyChanged(nameof(HeadlineDetail));
    }

    private void ShowSelection()
    {
        Workers.Clear();
        BreakdownLines.Clear();

        if (SelectedProject is not null)
        {
            foreach (var worker in SelectedProject.Workers)
            {
                Workers.Add(worker);
            }

            if (SelectedProject.Breakdown is not null)
            {
                // El desglose compacto: la mano de obra ya se abre persona por persona en
                // la tabla de abajo, y repetirla arriba dice dos veces lo mismo.
                foreach (var line in SelectedProject.Breakdown.CompactLines)
                {
                    BreakdownLines.Add(line);
                }
            }
        }

        SelectedWorker = null;

        OnPropertyChanged(nameof(HasSelectedProject));
        OnPropertyChanged(nameof(HasBreakdown));
        OnPropertyChanged(nameof(HasWorkers));
        OnPropertyChanged(nameof(ShowNoWorkersNote));
        OnPropertyChanged(nameof(NoWorkersMessage));
    }

    public bool HasSelectedProject => SelectedProject is not null;
    public bool HasBreakdown => BreakdownLines.Count > 0;
    public bool HasWorkers => Workers.Count > 0;

    /// <summary>El trabajo lo hizo el jefe solo: hay que explicarlo, no dejar el hueco.</summary>
    public bool ShowNoWorkersNote => SelectedProject is not null && Workers.Count == 0;

    public string NoWorkersMessage => SelectedProject is null
        ? string.Empty
        : "Este trabajo no tiene operarios cotizados: lo hiciste vos solo. Tu parte no se " +
          "paga desde acá, es un egreso normal de la caja.";

    private void PayAll()
    {
        if (SelectedWorker is { Pending: > 0m } worker)
        {
            PayAmount = AppCulture.Quantity(worker.Pending);
            Pay();
        }
    }

    private void Pay()
    {
        if (SelectedWorker is not { } worker)
        {
            return;
        }

        try
        {
            if (!NumberInput.TryParseMoney(PayAmount, out var amount))
            {
                throw new InvalidOperationException("Importe inválido.");
            }

            AppHost.SettlementService.Pay(
                worker.LaborLineId,
                amount,
                PayMethod.Method ?? PaymentMethod.Cash);

            SetStatus(
                $"Le pagaste {AppCulture.Money(amount)} a {worker.Description}. Salió de la caja.",
                isError: false);

            PayAmount = string.Empty;
            Load();
            _onDataChanged();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

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
}
