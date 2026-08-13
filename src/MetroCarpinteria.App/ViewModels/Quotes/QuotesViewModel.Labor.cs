using MetroCarpinteria.App.Helpers;
using MetroCarpinteria.App.Models;
using MetroCarpinteria.App.Services;

namespace MetroCarpinteria.App.ViewModels;

/// <summary>
/// Los operarios del presupuesto: quiénes trabajan además del jefe, cuántos días y a cuánto.
/// </summary>
/// <remarks>
/// El jefe no está acá: es el par días/jornal de la calculadora, que ya existía. Esto es
/// solo la lista de los demás, y está calcado del alta de materiales —elegir de una lista o
/// escribir suelto, con el total de la línea a la vista antes de confirmar— para que no haya
/// que aprender un formulario nuevo.
/// </remarks>
public partial class QuotesViewModel
{
    // --- Lista ----------------------------------------------------------------

    public bool HasLaborLines => LaborLines.Count > 0;

    /// <summary>Mano de obra completa: el jefe más los operarios.</summary>
    public string LaborTotalDisplay
    {
        get
        {
            var foreman = NumberInput.TryParseQuantity(CalcDays, out var days)
                && NumberInput.TryParseMoney(CalcDailyRate, out var rate)
                    ? Math.Round(days * rate, 2, MidpointRounding.AwayFromZero)
                    : 0m;

            return AppCulture.Money(foreman + LaborLines.Sum(l => l.LineTotal));
        }
    }

    // --- Formulario de alta ---------------------------------------------------

    public bool IsLaborFormOpen
    {
        get => _isLaborFormOpen;
        private set => SetProperty(ref _isLaborFormOpen, value);
    }

    /// <summary>
    /// El operario elegido de Personal. Null significa «lo escribo a mano», que es como se
    /// carga a alguien que no está dado de alta.
    /// </summary>
    public EmployeeListItem? SelectedWorker
    {
        get => _selectedWorker;
        set
        {
            if (SetProperty(ref _selectedWorker, value))
            {
                // El jornal de la ficha es una propuesta, no una imposición: se puede pisar
                // para este presupuesto sin tocarle el legajo a nadie.
                if (value?.DailyRate is > 0)
                {
                    WorkerDailyRate = NumberInput.Format(value.DailyRate.Value);
                }

                OnPropertyChanged(nameof(IsLooseWorker));
                OnPropertyChanged(nameof(CanConfirmWorker));
            }
        }
    }

    /// <summary>Se cargó a mano y no de la lista de Personal.</summary>
    public bool IsLooseWorker => SelectedWorker is null;

    /// <summary>Nombre para el que no está en Personal.</summary>
    public string LooseWorkerName
    {
        get => _looseWorkerName;
        set
        {
            if (SetProperty(ref _looseWorkerName, value))
            {
                OnPropertyChanged(nameof(CanConfirmWorker));
            }
        }
    }

    public string WorkerDays
    {
        get => _workerDays;
        set
        {
            if (SetProperty(ref _workerDays, value))
            {
                NotifyWorkerLineTotal();
            }
        }
    }

    public string WorkerDailyRate
    {
        get => _workerDailyRate;
        set
        {
            if (SetProperty(ref _workerDailyRate, value))
            {
                NotifyWorkerLineTotal();
            }
        }
    }

    /// <summary>El total de la línea antes de confirmar, para no cargar a ciegas.</summary>
    public string WorkerLineTotalDisplay =>
        NumberInput.TryParseQuantity(WorkerDays, out var days)
        && NumberInput.TryParseMoney(WorkerDailyRate, out var rate)
            ? AppCulture.Money(Math.Round(days * rate, 2, MidpointRounding.AwayFromZero))
            : AppCulture.Money(0m);

    public bool CanConfirmWorker =>
        SelectedWorker is not null || !string.IsNullOrWhiteSpace(LooseWorkerName);

    // --- Acciones -------------------------------------------------------------

    private void OpenLaborForm()
    {
        ReloadAvailableWorkers();

        SelectedWorker = null;
        LooseWorkerName = string.Empty;
        WorkerDays = NumberInput.TryParseQuantity(CalcDays, out var days) && days > 0
            // Arranca con los días del jefe, que es el caso normal: el operario está en la
            // obra los mismos días. Se corrige cuando no.
            ? NumberInput.Format(days)
            : "1";
        WorkerDailyRate = string.Empty;

        IsLaborFormOpen = true;
    }

    private void CloseLaborForm()
    {
        IsLaborFormOpen = false;
        SelectedWorker = null;
        LooseWorkerName = string.Empty;
    }

    private void ConfirmWorker()
    {
        if (Detail is null)
        {
            return;
        }

        try
        {
            var name = SelectedWorker?.FullName ?? LooseWorkerName;
            var days = NumberInput.ParseQuantityOrThrow(WorkerDays, "Días");
            var rate = NumberInput.ParseMoneyOrThrow(WorkerDailyRate, "Jornal");

            AppHost.QuoteService.AddLaborLine(Detail.Id, SelectedWorker?.Id, name, days, rate);

            CloseLaborForm();
            ReloadAfterLaborChange();
            SetStatus($"{name.Trim()} agregado a la mano de obra.", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private void RemoveLaborLine(object? parameter)
    {
        if (parameter is not QuoteLaborLineItem line)
        {
            return;
        }

        try
        {
            AppHost.QuoteService.RemoveLaborLine(line.Id);
            ReloadAfterLaborChange();
            SetStatus($"{line.Description} quitado de la mano de obra.", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    /// <summary>
    /// Relee las líneas y vuelve a calcular: agregar o quitar a alguien cambia el precio.
    /// </summary>
    /// <remarks>
    /// Recarga el detalle en vez de la lista entera por lo mismo de siempre: vaciar la
    /// colección hace que la grilla reemita la selección y el formulario se recargue encima
    /// de lo que el usuario está tipeando.
    /// </remarks>
    private void ReloadAfterLaborChange()
    {
        if (Detail is null)
        {
            return;
        }

        ReloadLaborLines(AppHost.QuoteService.GetDetail(Detail.Id));

        // Recalcula y guarda el precio nuevo con la mano de obra que quedó.
        Calculate(silent: true);
        RefreshRow(Detail.Id);
        NotifyStepSummaries();
    }

    private void ReloadLaborLines(QuoteDetail? detail)
    {
        LaborLines.Clear();

        foreach (var line in detail?.LaborLines ?? [])
        {
            LaborLines.Add(line);
        }

        OnPropertyChanged(nameof(HasLaborLines));
        OnPropertyChanged(nameof(LaborTotalDisplay));
    }

    /// <summary>
    /// Refresca el desplegable con el personal activo. Se relee al abrir el formulario y no
    /// una sola vez al arrancar: se puede dar de alta a alguien en Personal y volver acá.
    /// </summary>
    private void ReloadAvailableWorkers()
    {
        AvailableWorkers.Clear();

        foreach (var employee in AppHost.EmployeeService.GetEmployees(includeArchived: false, search: null))
        {
            AvailableWorkers.Add(employee);
        }

        OnPropertyChanged(nameof(HasAvailableWorkers));
    }

    public bool HasAvailableWorkers => AvailableWorkers.Count > 0;

    private void NotifyWorkerLineTotal() => OnPropertyChanged(nameof(WorkerLineTotalDisplay));

    /// <summary>El total de mano de obra cambia también al tocar los campos del jefe.</summary>
    private void NotifyLaborTotal() => OnPropertyChanged(nameof(LaborTotalDisplay));
}
