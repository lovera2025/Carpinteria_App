using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using MetroCarpinteria.App.Data.Entities;
using MetroCarpinteria.App.Helpers;
using MetroCarpinteria.App.Models;
using MetroCarpinteria.App.Services;

namespace MetroCarpinteria.App.ViewModels;

/// <summary>La calculadora: entradas, porcentajes, desglose y precio final.</summary>
public partial class QuotesViewModel
{
    // --- Calculadora ----------------------------------------------------------

    public string CalcMaterials
    {
        get => _calcMaterials;
        set
        {
            if (SetProperty(ref _calcMaterials, value))
            {
                AutoCalculate();
            }
        }
    }

    public string CalcDays
    {
        get => _calcDays;
        set
        {
            if (SetProperty(ref _calcDays, value))
            {
                AutoCalculate();
            }
        }
    }

    public string CalcDailyRate
    {
        get => _calcDailyRate;
        set
        {
            if (SetProperty(ref _calcDailyRate, value))
            {
                AutoCalculate();
            }
        }
    }

    /// <summary>Qué falta para poder calcular. Vacío cuando el precio ya está.</summary>
    public string MissingDataMessage => _missingDataMessage;

    public bool HasMissingData => !string.IsNullOrEmpty(_missingDataMessage);

    /// <summary>
    /// El número de la barra fija: lo que el cliente va a pagar. Si el precio se ajustó a
    /// mano es el ajustado, no el calculado, porque es el que sale impreso.
    /// </summary>
    public string FinalPriceOrPlaceholder
    {
        get
        {
            if (Detail?.Budget is > 0)
            {
                return AppCulture.Money(Detail.Budget);
            }

            return Breakdown is null ? "—" : Breakdown.FinalPriceDisplay;
        }
    }

    /// <summary>
    /// Precio final que se le pasa al cliente, editable. El taller redondea para cerrar
    /// la venta —"son doscientos ochenta y siete mil, dejámelo en doscientos ochenta"— y
    /// ese número tiene que ser el que va en el documento, no una anotación al margen.
    /// </summary>
    public string AdjustedPrice
    {
        get => _adjustedPrice;
        set => SetProperty(ref _adjustedPrice, value);
    }

    public bool CanAdjustPrice => Detail is { IsEditable: true } && HasResult;

    public string RateWaste
    {
        get => _rateWaste;
        set
        {
            if (SetProperty(ref _rateWaste, value))
            {
                OnPropertyChanged(nameof(RatesSummary));
                RecalculateIfShowing();
            }
        }
    }

    public string RateToolWear
    {
        get => _rateToolWear;
        set
        {
            if (SetProperty(ref _rateToolWear, value))
            {
                OnPropertyChanged(nameof(RatesSummary));
                RecalculateIfShowing();
            }
        }
    }

    public string RateOverhead
    {
        get => _rateOverhead;
        set
        {
            if (SetProperty(ref _rateOverhead, value))
            {
                OnPropertyChanged(nameof(RatesSummary));
                RecalculateIfShowing();
            }
        }
    }

    public string RateProfit
    {
        get => _rateProfit;
        set
        {
            if (SetProperty(ref _rateProfit, value))
            {
                OnPropertyChanged(nameof(RatesSummary));
                RecalculateIfShowing();
            }
        }
    }

    public bool IsAdvancedOpen
    {
        get => _isAdvancedOpen;
        set => SetProperty(ref _isAdvancedOpen, value);
    }

    /// <summary>
    /// Encabezado del panel de ajustes: muestra los porcentajes vigentes sin tener
    /// que abrirlo. Antes había que desplegarlo para saber con qué se estaba calculando.
    /// </summary>
    public string RatesSummary =>
        $"Ajustes avanzados  —  desperdicio {RateWaste}% · desgaste {RateToolWear}% · " +
        $"gastos {RateOverhead}% · ganancia {RateProfit}%";

    public BudgetBreakdown? Breakdown
    {
        get => _breakdown;
        private set
        {
            if (SetProperty(ref _breakdown, value))
            {
                OnPropertyChanged(nameof(HasResult));
                OnPropertyChanged(nameof(FinalPriceDisplay));
                OnPropertyChanged(nameof(FinalPriceOrPlaceholder));
                OnPropertyChanged(nameof(PriceStepSummary));
                OnPropertyChanged(nameof(CanAdjustPrice));
            }
        }
    }

    public bool HasResult => Breakdown is not null;
    public string FinalPriceDisplay => Breakdown?.FinalPriceDisplay ?? AppCulture.Money(0m);

    // --- Cálculo --------------------------------------------------------------

    /// <summary>
    /// Lee los campos de la calculadora. Devuelve <c>false</c> y qué falta, en castellano,
    /// en vez de tirar: los datos incompletos son el estado normal mientras se carga un
    /// presupuesto, no un error.
    /// </summary>
    /// <remarks>
    /// Es el único lugar donde se validan las entradas del cálculo. Había dos caminos con
    /// reglas distintas —el automático exigía días y jornal mayores a cero, y el botón
    /// «Recalcular» solo que no fueran negativos—, así que apretando el botón se guardaba
    /// como precio final el costo de los materiales, sin una sola hora de trabajo cotizada.
    /// </remarks>
    private bool TryBuildInput(out BudgetInput input, out string missing)
    {
        input = new BudgetInput();

        if (!NumberInput.TryParseMoney(CalcMaterials, out var materials))
        {
            missing = "Cargá el costo de materiales";
            return false;
        }

        if (!NumberInput.TryParseQuantity(CalcDays, out var days) || days <= 0)
        {
            missing = "Falta cuántos días lleva el trabajo";
            return false;
        }

        if (!NumberInput.TryParseMoney(CalcDailyRate, out var dailyRate) || dailyRate <= 0)
        {
            missing = "Falta el valor del jornal";
            return false;
        }

        BudgetRates rates;
        try
        {
            rates = ReadRatesFromForm();
        }
        catch (InvalidOperationException ex)
        {
            // Los porcentajes vienen precargados: si acá falla algo es porque los editaron
            // a mano, y el mensaje del parser ya nombra cuál quedó mal.
            missing = ex.Message;
            return false;
        }

        input = new BudgetInput
        {
            MaterialsCost = materials,
            Days = days,
            DailyRate = dailyRate,
            Rates = rates
        };

        missing = string.Empty;
        return true;
    }

    /// <param name="silent">
    /// El recálculo automático corre al salir de cada campo, así que no publica avisos:
    /// deja el cartel de qué falta y nada más. El botón «Recalcular» sí confirma.
    /// </param>
    private void Calculate(bool silent = false)
    {
        if (!TryBuildInput(out var input, out var missing))
        {
            ShowBreakdown(null);
            SetMissingData(missing);

            if (!silent)
            {
                SetStatus(missing, isError: true);
            }

            return;
        }

        SetMissingData(string.Empty);

        try
        {
            var result = BudgetCalculatorService.Calculate(input);
            ShowBreakdown(result);

            // Se guarda solo en presupuestos editables. Los TextBox de la vista usan
            // LostFocus, así que esto corre al salir del campo y no en cada tecla.
            if (Detail is { IsEditable: true })
            {
                AppHost.QuoteService.SaveCalculation(
                    Detail.Id, input.MaterialsCost, input.Days, input.DailyRate, input.Rates);

                // Solo el renglón y el precio guardado: recargar la lista o releer el
                // detalle entero desde acá era lo que pisaba los campos que el usuario
                // tenía abiertos.
                RefreshRow(Detail.Id);
                RefreshDetailAfterCalculation();

                if (!silent)
                {
                    SetStatus($"Precio final: {result.FinalPriceDisplay}. Guardado en el presupuesto.", isError: false);
                }
            }
            else if (!silent)
            {
                SetStatus($"Precio final: {result.FinalPriceDisplay}.", isError: false);
            }
        }
        catch (Exception ex)
        {
            ShowBreakdown(null);
            SetMissingData(ex.Message);

            if (!silent)
            {
                SetStatus(ex.Message, isError: true);
            }
        }
    }

    private void RecalculateIfShowing()
    {
        if (HasResult)
        {
            Calculate(silent: true);
        }
    }

    /// <summary>
    /// Recalcula solo, sin que haya que apretar un botón. Mientras falten datos deja el
    /// precio en blanco y explica qué falta, en vez de tirar un error: el usuario todavía
    /// está cargando el presupuesto y un cartel rojo a mitad de camino no ayuda.
    /// </summary>
    private void AutoCalculate()
    {
        if (_isLoadingDetail)
        {
            return;
        }

        // Sin presupuesto abierto no hay nada que recalcular ni nada que le falte.
        if (Detail is null)
        {
            ShowBreakdown(null);
            SetMissingData(string.Empty);
            return;
        }

        Calculate(silent: true);
    }

    /// <summary>
    /// Relee el presupuesto <b>sin tocar los campos de la calculadora</b>, para que el
    /// precio guardado y los avisos que dependen de él queden al día.
    /// </summary>
    /// <remarks>
    /// La diferencia con <c>LoadDetail</c> es justamente ésa: <c>LoadDetail</c> siembra los
    /// campos con lo guardado, que es lo correcto al abrir otro presupuesto y lo que pisaba
    /// lo tipeado cuando corría a mitad de una edición.
    /// </remarks>
    private void RefreshDetailAfterCalculation()
    {
        if (Detail is null)
        {
            return;
        }

        var refreshed = AppHost.QuoteService.GetDetail(Detail.Id);
        if (refreshed is null)
        {
            return;
        }

        Detail = refreshed;
        AdjustedPrice = NumberInput.Format(refreshed.Budget);
    }

    private void SetMissingData(string message)
    {
        if (SetProperty(ref _missingDataMessage, message, nameof(MissingDataMessage)))
        {
            OnPropertyChanged(nameof(HasMissingData));
            OnPropertyChanged(nameof(FinalPriceOrPlaceholder));
            NotifyStepSummaries();
        }
    }

    private void ShowBreakdown(BudgetBreakdown? breakdown)
    {
        Breakdown = breakdown;
        BreakdownLines.Clear();

        if (breakdown is null)
        {
            return;
        }

        foreach (var line in breakdown.Lines.Where(l => !l.IsTotal))
        {
            BreakdownLines.Add(line);
        }
    }

    private BudgetRates ReadRatesFromForm() => new()
    {
        WastePercent = NumberInput.ParseQuantityOrThrow(RateWaste, "Porcentaje de desperdicio"),
        ToolWearPercent = NumberInput.ParseQuantityOrThrow(RateToolWear, "Porcentaje de desgaste"),
        OverheadPercent = NumberInput.ParseQuantityOrThrow(RateOverhead, "Porcentaje de gastos adicionales"),
        ProfitPercent = NumberInput.ParseQuantityOrThrow(RateProfit, "Porcentaje de ganancia")
    };

    private void LoadRatesFromSettings() => ApplyRates(AppHost.Settings.BudgetRates);

    private void ApplyRates(BudgetRates rates)
    {
        // Campos editables: se escriben con el mismo formato con el que se releen.
        // Con AppCulture, un 16,5% volvía a entrar como 165%.
        _rateWaste = NumberInput.Format(rates.WastePercent);
        _rateToolWear = NumberInput.Format(rates.ToolWearPercent);
        _rateOverhead = NumberInput.Format(rates.OverheadPercent);
        _rateProfit = NumberInput.Format(rates.ProfitPercent);

        OnPropertyChanged(nameof(RateWaste));
        OnPropertyChanged(nameof(RateToolWear));
        OnPropertyChanged(nameof(RateOverhead));
        OnPropertyChanged(nameof(RateProfit));
    }

    /// <summary>
    /// Fija a mano el precio que se le cobra al cliente, sin tocar el cálculo. El desglose
    /// queda igual: sirve para ver cuánto se resignó al redondear.
    /// </summary>
    private void ApplyAdjustedPrice()
    {
        if (Detail is not { IsEditable: true })
        {
            return;
        }

        try
        {
            var price = NumberInput.ParseMoneyOrThrow(AdjustedPrice, "Precio final");

            if (price <= 0)
            {
                throw new InvalidOperationException("El precio final tiene que ser mayor a cero.");
            }

            AppHost.QuoteService.SetFinalPrice(Detail.Id, price);
            ReloadListAndDetail();

            SetStatus($"Precio final fijado en {AppCulture.Money(price)}.", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private void RestoreCalculatedPrice()
    {
        if (Detail is not { IsEditable: true } || Breakdown is null)
        {
            return;
        }

        // El display se toma antes de recargar: al volver de LoadQuotes, Breakdown
        // apunta a otra instancia.
        var calculated = Breakdown.FinalPrice;
        var display = Breakdown.FinalPriceDisplay;

        try
        {
            AppHost.QuoteService.SetFinalPrice(Detail.Id, calculated);
            ReloadListAndDetail();

            SetStatus($"Precio final vuelto al calculado: {display}.", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private void SaveDefaultRates()
    {
        try
        {
            var rates = ReadRatesFromForm();

            AppHost.SettingsService.Update(settings =>
            {
                settings.BudgetRates = rates;

                if (NumberInput.TryParseMoney(CalcDailyRate, out var dailyRate) && dailyRate > 0)
                {
                    settings.DefaultDailyRate = dailyRate;
                }
            });

            SetStatus("Guardado como valores por defecto para los próximos presupuestos.", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private void RestoreDefaultRates()
    {
        var defaults = BudgetRates.Defaults();
        ApplyRates(defaults);
        RecalculateIfShowing();

        // El texto sale de los valores reales: antes decía "16 / 9 / 50 / 30" fijo
        // y quedaba mintiendo apenas alguien cambiara los recomendados.
        SetStatus($"Porcentajes restaurados a {DescribeRates(defaults)}.", isError: false);
    }

    /// <summary>Los cuatro porcentajes en el orden en que aparecen en el formulario.</summary>
    public static string DescribeRates(BudgetRates rates) =>
        $"desperdicio {AppCulture.Percent(rates.WastePercent)} · " +
        $"desgaste {AppCulture.Percent(rates.ToolWearPercent)} · " +
        $"gastos {AppCulture.Percent(rates.OverheadPercent)} · " +
        $"ganancia {AppCulture.Percent(rates.ProfitPercent)}";

    /// <summary>Tooltip del botón que restaura los recomendados.</summary>
    public string RestoreRatesTooltip => $"Vuelve a {DescribeRates(BudgetRates.Defaults())}";

}
