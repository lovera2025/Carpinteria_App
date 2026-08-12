using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using MetroCarpinteria.App.Data.Entities;
using MetroCarpinteria.App.Helpers;
using MetroCarpinteria.App.Models;
using MetroCarpinteria.App.Services;

namespace MetroCarpinteria.App.ViewModels;

/// <summary>El presupuesto abierto: cabecera, carga del detalle y resumen de cada paso.</summary>
public partial class QuotesViewModel
{
    // --- Detalle -------------------------------------------------------------

    public QuoteDetail? Detail
    {
        get => _detail;
        private set
        {
            if (SetProperty(ref _detail, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(CanEditSelected));
                OnPropertyChanged(nameof(CanPrintForClient));
                OnPropertyChanged(nameof(CanAdjustPrice));
                OnPropertyChanged(nameof(FinalPriceOrPlaceholder));
                OnPropertyChanged(nameof(CanEditCommercialTerms));
                PaymentsSection.Load(value);
                OnPropertyChanged(nameof(DetailStatusLabel));
                OnPropertyChanged(nameof(MaterialsTotalDisplay));
                OnPropertyChanged(nameof(ShowPendingStockNotice));
                OnPropertyChanged(nameof(ShowManualAdjustNotice));
                OnPropertyChanged(nameof(IsRejectedSelected));
                NotifyStepSummaries();
            }
        }
    }

    public bool HasSelection => Detail is not null;
    public bool CanEditSelected => Detail is { IsEditable: true };
    public string MaterialsTotalDisplay => Detail?.MaterialsTotalDisplay ?? AppCulture.Money(0m);

    public string DetailStatusLabel
    {
        get
        {
            if (Detail is null)
            {
                return string.Empty;
            }

            return Detail.Status == ProjectStatus.Quote
                ? $"{QuoteRules.GetLabel(Detail.Freshness)} · {Detail.ValidUntilDisplay}"
                : ProjectStatusHelper.GetLabel(Detail.Status);
        }
    }

    /// <summary>
    /// Un presupuesto listo para entregar. Sin desglose el documento sale sin resumen, y
    /// sin precio el bloque de TOTAL sale con un guión.
    /// </summary>
    public bool CanPrintForClient => Detail is { Budget: > 0, Breakdown: not null };

    public bool ShowPendingStockNotice => Detail is { HasPendingStock: true };
    public bool ShowManualAdjustNotice => Detail is { BudgetAdjustedManually: true };
    public bool IsRejectedSelected => Detail is { Status: ProjectStatus.Rejected };

    // --- Formulario de cabecera ----------------------------------------------

    public bool IsFormOpen
    {
        get => _isFormOpen;
        private set => SetProperty(ref _isFormOpen, value);
    }

    public bool IsCreating
    {
        get => _isCreating;
        private set
        {
            if (SetProperty(ref _isCreating, value))
            {
                OnPropertyChanged(nameof(FormHeader));
            }
        }
    }

    public string FormHeader => IsCreating ? "Nuevo presupuesto" : "Editar presupuesto";

    public string FormTitle
    {
        get => _formTitle;
        set => SetProperty(ref _formTitle, value);
    }

    /// <summary>
    /// El cliente del presupuesto. Es texto libre a propósito.
    /// </summary>
    /// <remarks>
    /// Obligar a dar de alta una ficha antes de poder cotizar rompe el flujo del taller:
    /// llega alguien, se le pasa un precio, y recién si acepta importa quién es. Se
    /// escribe el nombre y la ficha se crea o se reusa sola al guardar; mientras tanto,
    /// <see cref="ClientSuggestions"/> ofrece las que ya existen para no duplicar.
    /// </remarks>
    public string FormClientName
    {
        get => _formClientName;
        set
        {
            if (SetProperty(ref _formClientName, value))
            {
                _clientSearchDebouncer.Run(LoadClientSuggestions);
            }
        }
    }

    /// <summary>Fichas que coinciden con lo tipeado. Vacío cuando no hace falta elegir.</summary>
    public ObservableCollection<ClientListItem> ClientSuggestions { get; } = [];

    public bool HasClientSuggestions => ClientSuggestions.Count > 0;

    private void LoadClientSuggestions()
    {
        ClientSuggestions.Clear();

        if (!string.IsNullOrWhiteSpace(FormClientName) && AppHost.IsReady)
        {
            foreach (var client in AppHost.ClientService.Search(FormClientName, limit: 5))
            {
                // El que ya está escrito completo no se ofrece: elegirlo no cambiaría nada.
                if (!string.Equals(
                        ClientRules.Normalize(client.Name),
                        ClientRules.Normalize(FormClientName),
                        StringComparison.Ordinal))
                {
                    ClientSuggestions.Add(client);
                }
            }
        }

        OnPropertyChanged(nameof(HasClientSuggestions));
    }

    /// <summary>Completa el nombre con el de una ficha existente.</summary>
    private void PickClient(object? parameter)
    {
        if (parameter is not ClientListItem client)
        {
            return;
        }

        FormClientName = client.Name;
        ClientSuggestions.Clear();
        OnPropertyChanged(nameof(HasClientSuggestions));
    }

    public string FormDescription
    {
        get => _formDescription;
        set => SetProperty(ref _formDescription, value);
    }

    public DateTime? FormValidUntil
    {
        get => _formValidUntil;
        set => SetProperty(ref _formValidUntil, value);
    }

    // --- Resumen de cada paso -------------------------------------------------
    // Cada encabezado dice en qué estado está su paso, así la pantalla contesta
    // "¿y ahora qué hago?" sin que haya que recorrerla entera.

    public string MaterialsStepSummary
    {
        get
        {
            if (Detail is null)
            {
                return string.Empty;
            }

            if (Lines.Count == 0)
            {
                return "Todavía no cargaste materiales";
            }

            var label = Lines.Count == 1 ? "1 material" : $"{Lines.Count} materiales";
            return $"{label} · {Detail.MaterialsTotalDisplay}";
        }
    }

    public string PriceStepSummary
    {
        get
        {
            if (Detail is null)
            {
                return string.Empty;
            }

            // Lo que paga el cliente, no el precio pelado: con IVA pactado, mostrar el
            // calculado acá y el total en la barra fija son dos números distintos para
            // la misma cosa.
            return HasMissingData ? MissingDataMessage : FinalPriceOrPlaceholder;
        }
    }

    public string DeliverStepSummary
    {
        get
        {
            if (Detail is null)
            {
                return string.Empty;
            }

            if (Detail.Status == ProjectStatus.Rejected)
            {
                return "Rechazado";
            }

            if (!Detail.IsEditable)
            {
                return "Ya aprobado";
            }

            return HasResult ? "Listo para imprimir" : "Falta calcular el precio";
        }
    }

    private void NotifyStepSummaries()
    {
        OnPropertyChanged(nameof(MaterialsStepSummary));
        OnPropertyChanged(nameof(PriceStepSummary));
        OnPropertyChanged(nameof(DeliverStepSummary));
    }

    private void LoadDetail()
    {
        _isLoadingDetail = true;

        try
        {
            Lines.Clear();
            Shortfalls.Clear();

            if (SelectedQuote is null)
            {
                Detail = null;
                Breakdown = null;
                BreakdownLines.Clear();
                SetMissingData(string.Empty);
                return;
            }

            var detail = AppHost.QuoteService.GetDetail(SelectedQuote.Id);
            Detail = detail;

            if (detail is null)
            {
                return;
            }

            foreach (var line in detail.Lines)
            {
                Lines.Add(line);
            }

            CalcMaterials = NumberInput.Format(detail.CalculationMaterials);
            CalcDays = detail.EstimatedDays.HasValue ? NumberInput.Format(detail.EstimatedDays.Value) : "1";
            CalcDailyRate = detail.DailyRate.HasValue
                ? NumberInput.Format(detail.DailyRate.Value)
                : FormatOptional(AppHost.Settings.DefaultDailyRate);

            ApplyRates(detail.Rates ?? AppHost.Settings.BudgetRates);
            ShowBreakdown(detail.Breakdown);

            // Se limpia el cartel de qué falta: si venimos de un presupuesto a medio
            // cargar, el aviso quedaba pegado sobre uno que ya tiene todo. Cuando falte
            // algo de verdad, el AutoCalculate del final lo vuelve a poner.
            SetMissingData(string.Empty);

            // Campo editable: va con NumberInput.Format, no con AppCulture.
            AdjustedPrice = NumberInput.Format(detail.Budget);

            LoadCommercialTerms(detail);
            PaymentsSection.Load(detail);
        }
        finally
        {
            _isLoadingDetail = false;
        }

        // Un presupuesto guardado sin cálculo muestra desde el arranque qué le falta.
        if (Detail is not null && Breakdown is null)
        {
            AutoCalculate();
        }

        NotifyStepSummaries();
    }

    // --- Cabecera -------------------------------------------------------------

    private void StartNew()
    {
        FormTitle = string.Empty;
        FormClientName = string.Empty;
        FormDescription = string.Empty;
        FormValidUntil = DateTime.Today.AddDays(Math.Max(0, AppHost.Settings.DefaultQuoteValidityDays));
        IsCreating = true;
        IsFormOpen = true;
        ClearStatus();
    }

    private void StartEdit()
    {
        if (Detail is null)
        {
            return;
        }

        FormTitle = Detail.Title;
        FormClientName = Detail.ClientName;
        FormDescription = Detail.Description ?? string.Empty;
        FormValidUntil = Detail.ValidUntilLocal;
        IsCreating = false;
        IsFormOpen = true;
        ClearStatus();
    }

    private void SaveQuote()
    {
        try
        {
            if (IsCreating)
            {
                var created = AppHost.QuoteService.CreateQuote(
                    FormTitle, FormClientName, FormDescription, FormValidUntil);

                LinkClient(created.Id);

                CloseForm();
                LoadQuotes();
                SelectedQuote = Quotes.FirstOrDefault(q => q.Id == created.Id);
                SetStatus($"Presupuesto «{created.Title}» creado.", isError: false);
                return;
            }

            if (Detail is null)
            {
                return;
            }

            AppHost.QuoteService.UpdateQuote(
                Detail.Id, FormTitle, FormClientName, FormDescription, FormValidUntil);

            LinkClient(Detail.Id);

            CloseForm();
            ReloadListAndDetail();
            SetStatus("Presupuesto actualizado.", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    /// <summary>
    /// Engancha el presupuesto a la ficha del cliente, creándola si es la primera vez.
    /// </summary>
    /// <remarks>
    /// Un fallo acá no puede tumbar el guardado: el presupuesto ya se guardó y su
    /// <c>ClientName</c> quedó bien, que es lo que sale impreso. La ficha es organización,
    /// no un requisito para cotizar.
    /// </remarks>
    private void LinkClient(int projectId)
    {
        if (string.IsNullOrWhiteSpace(FormClientName))
        {
            return;
        }

        try
        {
            var client = AppHost.ClientService.GetOrCreate(FormClientName);
            AppHost.QuoteService.AssignClient(projectId, client.Id);
        }
        catch (Exception ex)
        {
            LogService.Warning("QuotesViewModel", $"No se pudo vincular la ficha del cliente: {ex.Message}");
        }
    }

    private void CloseForm()
    {
        IsFormOpen = false;
        IsCreating = false;
        ClientSuggestions.Clear();
        OnPropertyChanged(nameof(HasClientSuggestions));
    }

}
