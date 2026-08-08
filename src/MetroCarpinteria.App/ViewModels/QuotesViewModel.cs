using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using MetroCarpinteria.App.Data.Entities;
using MetroCarpinteria.App.Helpers;
using MetroCarpinteria.App.Models;
using MetroCarpinteria.App.Services;

namespace MetroCarpinteria.App.ViewModels;

public class QuotesViewModel : ObservableObject
{
    private readonly Action _onDataChanged;

    private QuoteListItem? _selectedQuote;
    private QuoteDetail? _detail;
    private QuoteFilterOption _selectedFilter;
    private string _searchText = string.Empty;
    private bool _showApproved;

    private bool _isFormOpen;
    private bool _isCreating;
    private string _formTitle = string.Empty;
    private string _formClientName = string.Empty;
    private string _formDescription = string.Empty;
    private DateTime? _formValidUntil;

    private bool _isMaterialFormOpen;
    private bool _materialFromInventory = true;
    private ProductListItem? _selectedProduct;
    private string _productSearch = string.Empty;
    private string _materialQuantity = "1";
    private string _materialUnitCost = string.Empty;
    private string _looseDescription = string.Empty;
    private string _looseUnit = ProductUnits.Unit;
    private bool _saveToCatalog;
    private QuoteLineItem? _editingLine;

    private string _calcMaterials = "0";
    private string _calcDays = "1";
    private string _calcDailyRate = string.Empty;
    private string _rateWaste = string.Empty;
    private string _rateToolWear = string.Empty;
    private string _rateOverhead = string.Empty;
    private string _rateProfit = string.Empty;
    private bool _isAdvancedOpen;
    private BudgetBreakdown? _breakdown;
    private string _missingDataMessage = string.Empty;

    /// <summary>
    /// Mientras se carga un presupuesto se asignan los tres campos de cálculo de una.
    /// Sin esta guarda, cada asignación dispararía un cálculo y una escritura a la base.
    /// </summary>
    private bool _isLoadingDetail;

    private string _statusMessage = string.Empty;
    private bool _isStatusError;

    public QuotesViewModel(Action onDataChanged)
    {
        _onDataChanged = onDataChanged;

        Quotes = [];
        Lines = [];
        BreakdownLines = [];
        Shortfalls = [];
        AvailableProducts = [];
        AvailableUnits = ProductUnits.All;
        FilterOptions = QuoteRules.GetFilterOptions();
        _selectedFilter = FilterOptions[0];

        LoadRatesFromSettings();
        _calcDailyRate = FormatOptional(AppHost.Settings.DefaultDailyRate);

        LoadCommand = new RelayCommand(_ => Load());
        NewQuoteCommand = new RelayCommand(_ => StartNew());
        EditQuoteCommand = new RelayCommand(_ => StartEdit(), _ => Detail is { IsEditable: true });
        SaveQuoteCommand = new RelayCommand(_ => SaveQuote());
        CancelFormCommand = new RelayCommand(_ => CloseForm());

        AddMaterialCommand = new RelayCommand(_ => OpenMaterialForm(), _ => CanEditSelected);
        ConfirmMaterialCommand = new RelayCommand(_ => ConfirmMaterial(), _ => CanConfirmMaterial);
        CancelMaterialCommand = new RelayCommand(_ => CloseMaterialForm());
        EditLineCommand = new RelayCommand(EditLine, _ => CanEditSelected);
        RemoveLineCommand = new RelayCommand(RemoveLine, _ => CanEditSelected);

        CalculateCommand = new RelayCommand(_ => Calculate());
        SaveDefaultRatesCommand = new RelayCommand(_ => SaveDefaultRates());
        RestoreDefaultRatesCommand = new RelayCommand(_ => RestoreDefaultRates());

        ApproveCommand = new RelayCommand(_ => Approve(), _ => CanEditSelected);
        RejectCommand = new RelayCommand(_ => Reject(), _ => CanEditSelected);
        ReopenCommand = new RelayCommand(_ => Reopen(), _ => Detail is { Status: ProjectStatus.Rejected });
        DuplicateCommand = new RelayCommand(_ => Duplicate(), _ => Detail is not null);
        ApplyPendingCommand = new RelayCommand(_ => ApplyPending(), _ => Detail is { HasPendingStock: true });
        PrintClientCommand = new RelayCommand(_ => PrintClient(), _ => Detail is not null);
        PrintCostSheetCommand = new RelayCommand(_ => PrintCostSheet(), _ => Detail is not null);
    }

    public ObservableCollection<QuoteListItem> Quotes { get; }
    public ObservableCollection<QuoteLineItem> Lines { get; }
    public ObservableCollection<BudgetBreakdownLine> BreakdownLines { get; }
    public ObservableCollection<QuoteApprovalShortfall> Shortfalls { get; }
    public ObservableCollection<ProductListItem> AvailableProducts { get; }
    public IReadOnlyList<string> AvailableUnits { get; }
    public IReadOnlyList<QuoteFilterOption> FilterOptions { get; }

    // --- Lista ---------------------------------------------------------------

    public QuoteListItem? SelectedQuote
    {
        get => _selectedQuote;
        set
        {
            if (SetProperty(ref _selectedQuote, value))
            {
                LoadDetail();
            }
        }
    }

    public QuoteFilterOption SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (SetProperty(ref _selectedFilter, value))
            {
                LoadQuotes();
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                LoadQuotes();
            }
        }
    }

    public bool ShowApproved
    {
        get => _showApproved;
        set
        {
            if (SetProperty(ref _showApproved, value))
            {
                LoadQuotes();
            }
        }
    }

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

    public string FormClientName
    {
        get => _formClientName;
        set => SetProperty(ref _formClientName, value);
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

    // --- Formulario de material ----------------------------------------------

    public bool IsMaterialFormOpen
    {
        get => _isMaterialFormOpen;
        private set => SetProperty(ref _isMaterialFormOpen, value);
    }

    public bool MaterialFromInventory
    {
        get => _materialFromInventory;
        set
        {
            if (SetProperty(ref _materialFromInventory, value))
            {
                OnPropertyChanged(nameof(MaterialIsLoose));
                OnPropertyChanged(nameof(CanConfirmMaterial));
            }
        }
    }

    public bool MaterialIsLoose => !MaterialFromInventory;

    /// <summary>Estamos modificando una línea ya cargada, no agregando una nueva.</summary>
    public bool IsEditingLine => _editingLine is not null;

    public string MaterialFormTitle => IsEditingLine ? "Editar material" : "Agregar material";

    public string MaterialConfirmLabel => IsEditingLine ? "Guardar cambios" : "Agregar";

    public ProductListItem? SelectedProduct
    {
        get => _selectedProduct;
        set
        {
            if (!SetProperty(ref _selectedProduct, value))
            {
                return;
            }

            if (value is not null)
            {
                MaterialUnitCost = FormatOptional(value.CostPrice);
            }

            OnPropertyChanged(nameof(SelectedProductStockDisplay));
            OnPropertyChanged(nameof(CanConfirmMaterial));
        }
    }

    public string SelectedProductStockDisplay => SelectedProduct is null
        ? "—"
        : SelectedProduct.StockDisplay;

    /// <summary>Filtra el selector de productos al tipear.</summary>
    public string ProductSearch
    {
        get => _productSearch;
        set
        {
            if (SetProperty(ref _productSearch, value))
            {
                LoadProducts();
            }
        }
    }

    public bool HasNoProductMatches => MaterialFromInventory && AvailableProducts.Count == 0;

    /// <summary>
    /// Sin producto elegido no se puede confirmar. Antes el formulario venía con el primer
    /// producto del inventario ya seleccionado y era fácil cargarlo sin querer.
    /// </summary>
    public bool CanConfirmMaterial => MaterialFromInventory
        ? SelectedProduct is not null
        : !string.IsNullOrWhiteSpace(LooseDescription);

    public string MaterialQuantity
    {
        get => _materialQuantity;
        set
        {
            if (SetProperty(ref _materialQuantity, value))
            {
                OnPropertyChanged(nameof(MaterialLineTotalDisplay));
            }
        }
    }

    public string MaterialUnitCost
    {
        get => _materialUnitCost;
        set
        {
            if (SetProperty(ref _materialUnitCost, value))
            {
                OnPropertyChanged(nameof(MaterialLineTotalDisplay));
            }
        }
    }

    /// <summary>Total de la línea en vivo, para ver el efecto antes de confirmar.</summary>
    public string MaterialLineTotalDisplay
    {
        get
        {
            if (!NumberInput.TryParseDecimal(MaterialQuantity, out var quantity))
            {
                return "—";
            }

            NumberInput.TryParseDecimal(MaterialUnitCost, out var unitCost);
            return AppCulture.Money(Math.Round(quantity * unitCost, 2, MidpointRounding.AwayFromZero));
        }
    }

    public string LooseDescription
    {
        get => _looseDescription;
        set
        {
            if (SetProperty(ref _looseDescription, value))
            {
                OnPropertyChanged(nameof(CanConfirmMaterial));
            }
        }
    }

    public string LooseUnit
    {
        get => _looseUnit;
        set => SetProperty(ref _looseUnit, value);
    }

    public bool SaveToCatalog
    {
        get => _saveToCatalog;
        set => SetProperty(ref _saveToCatalog, value);
    }

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

    /// <summary>El precio final para la barra fija: el número, o un guión si falta algo.</summary>
    public string FinalPriceOrPlaceholder => Breakdown is null
        ? "—"
        : Breakdown.FinalPriceDisplay;

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
            }
        }
    }

    public bool HasResult => Breakdown is not null;
    public string FinalPriceDisplay => Breakdown?.FinalPriceDisplay ?? AppCulture.Money(0m);

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

            return HasMissingData ? MissingDataMessage : FinalPriceDisplay;
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

    // --- Estado ---------------------------------------------------------------

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

    // --- Comandos -------------------------------------------------------------

    public ICommand LoadCommand { get; }
    public ICommand NewQuoteCommand { get; }
    public ICommand EditQuoteCommand { get; }
    public ICommand SaveQuoteCommand { get; }
    public ICommand CancelFormCommand { get; }
    public ICommand AddMaterialCommand { get; }
    public ICommand ConfirmMaterialCommand { get; }
    public ICommand CancelMaterialCommand { get; }
    public ICommand EditLineCommand { get; }
    public ICommand RemoveLineCommand { get; }
    public ICommand CalculateCommand { get; }
    public ICommand SaveDefaultRatesCommand { get; }
    public ICommand RestoreDefaultRatesCommand { get; }
    public ICommand ApproveCommand { get; }
    public ICommand RejectCommand { get; }
    public ICommand ReopenCommand { get; }
    public ICommand DuplicateCommand { get; }
    public ICommand ApplyPendingCommand { get; }
    public ICommand PrintClientCommand { get; }
    public ICommand PrintCostSheetCommand { get; }

    // --- Carga ----------------------------------------------------------------

    public void Load()
    {
        LoadProducts();
        LoadQuotes();
    }

    /// <summary>
    /// Recarga el selector respetando el filtro tipeado. No preselecciona nada a propósito:
    /// elegir el material es del usuario, si no se termina cargando el primero del inventario.
    /// </summary>
    private void LoadProducts()
    {
        var previousId = SelectedProduct?.Id;

        AvailableProducts.Clear();
        foreach (var product in AppHost.InventoryService.GetProducts(false, false, ProductSearch))
        {
            AvailableProducts.Add(product);
        }

        // Si el que ya estaba elegido sigue en la lista filtrada, se mantiene.
        SelectedProduct = previousId is null
            ? null
            : AvailableProducts.FirstOrDefault(p => p.Id == previousId.Value);

        OnPropertyChanged(nameof(HasNoProductMatches));
    }

    private void LoadQuotes()
    {
        var items = AppHost.QuoteService.GetQuotes(SelectedFilter.Filter, SearchText, ShowApproved);
        var selectedId = SelectedQuote?.Id;

        Quotes.Clear();
        foreach (var item in items)
        {
            Quotes.Add(item);
        }

        SelectedQuote = selectedId.HasValue
            ? Quotes.FirstOrDefault(q => q.Id == selectedId.Value) ?? Quotes.FirstOrDefault()
            : Quotes.FirstOrDefault();

        _onDataChanged();
        CommandManager.InvalidateRequerySuggested();
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

            CalcMaterials = AppCulture.Quantity(detail.CalculationMaterials);
            CalcDays = detail.EstimatedDays.HasValue ? AppCulture.Quantity(detail.EstimatedDays.Value) : "1";
            CalcDailyRate = detail.DailyRate.HasValue
                ? AppCulture.Quantity(detail.DailyRate.Value)
                : FormatOptional(AppHost.Settings.DefaultDailyRate);

            ApplyRates(detail.Rates ?? AppHost.Settings.BudgetRates);
            ShowBreakdown(detail.Breakdown);
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

            CloseForm();
            LoadQuotes();
            SetStatus("Presupuesto actualizado.", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private void CloseForm()
    {
        IsFormOpen = false;
        IsCreating = false;
    }

    // --- Materiales -----------------------------------------------------------

    private void OpenMaterialForm()
    {
        _editingLine = null;
        SelectedProduct = null;
        ProductSearch = string.Empty;
        LoadProducts();

        MaterialFromInventory = true;
        MaterialQuantity = "1";
        MaterialUnitCost = string.Empty;
        LooseDescription = string.Empty;
        LooseUnit = ProductUnits.Unit;
        SaveToCatalog = false;

        IsMaterialFormOpen = true;
        NotifyMaterialFormChanged();
        ClearStatus();
    }

    /// <summary>
    /// Reusa el mismo formulario para corregir una línea ya cargada, así no hay que
    /// borrarla y volver a cargarla para cambiar una cantidad.
    /// </summary>
    private void EditLine(object? parameter)
    {
        if (parameter is not QuoteLineItem line || Detail is null)
        {
            return;
        }

        _editingLine = line;
        ProductSearch = string.Empty;
        LoadProducts();

        // El origen de una línea existente no se cambia: se edita cantidad y precio.
        MaterialFromInventory = line.IsFromInventory;

        if (line.IsFromInventory)
        {
            SelectedProduct = AvailableProducts.FirstOrDefault(p => p.Id == line.ProductId);
        }
        else
        {
            LooseDescription = line.Description;
            LooseUnit = line.Unit;
        }

        MaterialQuantity = AppCulture.Quantity(line.Quantity);
        MaterialUnitCost = AppCulture.Quantity(line.UnitCost);
        SaveToCatalog = false;

        IsMaterialFormOpen = true;
        NotifyMaterialFormChanged();
        ClearStatus();
    }

    private void CloseMaterialForm()
    {
        IsMaterialFormOpen = false;
        _editingLine = null;
        NotifyMaterialFormChanged();
    }

    private void NotifyMaterialFormChanged()
    {
        OnPropertyChanged(nameof(IsEditingLine));
        OnPropertyChanged(nameof(MaterialFormTitle));
        OnPropertyChanged(nameof(MaterialConfirmLabel));
        OnPropertyChanged(nameof(CanConfirmMaterial));
        OnPropertyChanged(nameof(MaterialLineTotalDisplay));
        OnPropertyChanged(nameof(HasNoProductMatches));
    }

    private void ConfirmMaterial()
    {
        if (Detail is null)
        {
            return;
        }

        try
        {
            var quantity = NumberInput.ParseOrThrow(MaterialQuantity, "Cantidad");
            var unitCost = string.IsNullOrWhiteSpace(MaterialUnitCost)
                ? 0m
                : NumberInput.ParseOrThrow(MaterialUnitCost, "Precio unitario");

            if (_editingLine is not null)
            {
                AppHost.QuoteService.UpdateLine(_editingLine.Id, quantity, unitCost);
                SetStatus($"«{_editingLine.Description}» actualizado.", isError: false);

                CloseMaterialForm();
                ReloadAfterLineChange();
                return;
            }

            if (MaterialFromInventory)
            {
                var product = SelectedProduct
                    ?? throw new InvalidOperationException("Elegí un producto del inventario.");

                AppHost.QuoteService.AddInventoryLine(Detail.Id, product.Id, quantity, unitCost);
                SetStatus($"«{product.Name}» agregado al presupuesto.", isError: false);
            }
            else
            {
                AppHost.QuoteService.AddLooseLine(
                    Detail.Id, LooseDescription, LooseUnit, quantity, unitCost, SaveToCatalog);

                SetStatus(
                    SaveToCatalog
                        ? $"«{LooseDescription.Trim()}» agregado y guardado en el inventario con stock 0."
                        : $"«{LooseDescription.Trim()}» agregado al presupuesto.",
                    isError: false);
            }

            CloseMaterialForm();
            ReloadAfterLineChange();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private void RemoveLine(object? parameter)
    {
        if (parameter is not QuoteLineItem line)
        {
            return;
        }

        try
        {
            AppHost.QuoteService.RemoveLine(line.Id);
            SetStatus($"«{line.Description}» quitado del presupuesto.", isError: false);
            ReloadAfterLineChange();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private void ReloadAfterLineChange()
    {
        LoadDetail();
        SyncMaterialsFromLines();
        AutoCalculate();
        NotifyStepSummaries();
    }

    /// <summary>
    /// El costo de materiales sale de las líneas cargadas. Antes había que traerlo a mano
    /// con el botón "Usar total" y era fácil calcular sobre un número viejo.
    /// </summary>
    private void SyncMaterialsFromLines()
    {
        if (Detail is not null)
        {
            CalcMaterials = AppCulture.Quantity(Detail.MaterialsTotal);
        }
    }

    // --- Cálculo --------------------------------------------------------------

    private void Calculate(bool silent = false)
    {
        try
        {
            var input = new BudgetInput
            {
                MaterialsCost = NumberInput.ParseOrThrow(CalcMaterials, "Costo de materiales"),
                Days = NumberInput.ParseOrThrow(CalcDays, "Días de trabajo"),
                DailyRate = NumberInput.ParseOrThrow(CalcDailyRate, "Valor del jornal"),
                Rates = ReadRatesFromForm()
            };

            var result = BudgetCalculatorService.Calculate(input);
            ShowBreakdown(result);

            if (Detail is { IsEditable: true })
            {
                AppHost.QuoteService.SaveCalculation(
                    Detail.Id, input.MaterialsCost, input.Days, input.DailyRate, input.Rates);

                LoadQuotes();

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
            SetStatus(ex.Message, isError: true);
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

        if (Detail is null)
        {
            ShowBreakdown(null);
            SetMissingData(string.Empty);
            return;
        }

        if (!NumberInput.TryParseDecimal(CalcMaterials, out var materials))
        {
            ShowBreakdown(null);
            SetMissingData("Cargá el costo de materiales");
            return;
        }

        if (!NumberInput.TryParseDecimal(CalcDays, out var days) || days <= 0)
        {
            ShowBreakdown(null);
            SetMissingData("Falta cuántos días lleva el trabajo");
            return;
        }

        if (!NumberInput.TryParseDecimal(CalcDailyRate, out var dailyRate) || dailyRate <= 0)
        {
            ShowBreakdown(null);
            SetMissingData("Falta el valor del jornal");
            return;
        }

        SetMissingData(string.Empty);

        try
        {
            var input = new BudgetInput
            {
                MaterialsCost = materials,
                Days = days,
                DailyRate = dailyRate,
                Rates = ReadRatesFromForm()
            };

            ShowBreakdown(BudgetCalculatorService.Calculate(input));

            // Se guarda solo en presupuestos editables. Los TextBox de la vista usan
            // LostFocus, así que esto corre al salir del campo y no en cada tecla.
            if (Detail is { IsEditable: true })
            {
                AppHost.QuoteService.SaveCalculation(
                    Detail.Id, input.MaterialsCost, input.Days, input.DailyRate, input.Rates);

                LoadQuotes();
            }
        }
        catch (Exception ex)
        {
            ShowBreakdown(null);
            SetMissingData(ex.Message);
        }
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
        WastePercent = NumberInput.ParseOrThrow(RateWaste, "Porcentaje de desperdicio"),
        ToolWearPercent = NumberInput.ParseOrThrow(RateToolWear, "Porcentaje de desgaste"),
        OverheadPercent = NumberInput.ParseOrThrow(RateOverhead, "Porcentaje de gastos adicionales"),
        ProfitPercent = NumberInput.ParseOrThrow(RateProfit, "Porcentaje de ganancia")
    };

    private void LoadRatesFromSettings() => ApplyRates(AppHost.Settings.BudgetRates);

    private void ApplyRates(BudgetRates rates)
    {
        _rateWaste = AppCulture.Quantity(rates.WastePercent);
        _rateToolWear = AppCulture.Quantity(rates.ToolWearPercent);
        _rateOverhead = AppCulture.Quantity(rates.OverheadPercent);
        _rateProfit = AppCulture.Quantity(rates.ProfitPercent);

        OnPropertyChanged(nameof(RateWaste));
        OnPropertyChanged(nameof(RateToolWear));
        OnPropertyChanged(nameof(RateOverhead));
        OnPropertyChanged(nameof(RateProfit));
    }

    private void SaveDefaultRates()
    {
        try
        {
            var rates = ReadRatesFromForm();

            AppHost.SettingsService.Update(settings =>
            {
                settings.BudgetRates = rates;

                if (NumberInput.TryParseDecimal(CalcDailyRate, out var dailyRate) && dailyRate > 0)
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
        ApplyRates(BudgetRates.Defaults());
        RecalculateIfShowing();
        SetStatus("Porcentajes restaurados a 16 / 9 / 50 / 30.", isError: false);
    }

    // --- Ciclo de vida --------------------------------------------------------

    private void Approve()
    {
        if (Detail is null)
        {
            return;
        }

        var confirm = MessageBox.Show(
            $"¿Aprobar «{Detail.Title}»?\n\n" +
            "El presupuesto pasa a ser un proyecto en curso y se descuenta del inventario " +
            "el stock disponible de los materiales cotizados.",
            "Confirmar aprobación",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var result = AppHost.QuoteService.ApproveQuote(Detail.Id);

            // Los faltantes se cargan después de recargar: al refrescar el detalle se
            // limpia la lista, así que llenarla antes la borraría sin que se vea.
            ShowApproved = true;
            LoadQuotes();
            ShowShortfalls(result);

            SetStatus(result.Summary, isError: false);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private void ApplyPending()
    {
        if (Detail is null)
        {
            return;
        }

        try
        {
            var result = AppHost.QuoteService.ApplyPendingStock(Detail.Id);

            LoadQuotes();
            ShowShortfalls(result);

            SetStatus(
                result.HasShortfalls
                    ? "Se descontó lo que había. Todavía faltan materiales."
                    : "Materiales pendientes descontados del inventario.",
                isError: false);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private void ShowShortfalls(QuoteApprovalResult result)
    {
        Shortfalls.Clear();
        foreach (var shortfall in result.Shortfalls)
        {
            Shortfalls.Add(shortfall);
        }
    }

    private void Reject()
    {
        if (Detail is null)
        {
            return;
        }

        var confirm = MessageBox.Show(
            $"¿Marcar «{Detail.Title}» como rechazado?\n\nQueda guardado como historial de lo que cotizaste.",
            "Confirmar",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            AppHost.QuoteService.RejectQuote(Detail.Id);
            LoadQuotes();
            SetStatus("Presupuesto marcado como rechazado.", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private void Reopen()
    {
        if (Detail is null)
        {
            return;
        }

        try
        {
            AppHost.QuoteService.ReopenQuote(Detail.Id);
            LoadQuotes();
            SetStatus("Presupuesto reabierto.", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private void Duplicate()
    {
        if (Detail is null)
        {
            return;
        }

        try
        {
            var copyId = AppHost.QuoteService.DuplicateQuote(Detail.Id);

            SelectedFilter = FilterOptions[0];
            LoadQuotes();
            SelectedQuote = Quotes.FirstOrDefault(q => q.Id == copyId);
            SetStatus("Copia creada con los precios de hoy.", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    // --- Impresión ------------------------------------------------------------

    private void PrintClient()
    {
        if (Detail is null)
        {
            return;
        }

        try
        {
            var document = AppHost.QuoteDocumentService.BuildClientQuote(Detail, includeMaterialDetail: true);

            if (AppHost.QuoteDocumentService.Print(document, $"Presupuesto {Detail.Id:0000}"))
            {
                SetStatus("Presupuesto enviado a la impresora.", isError: false);
            }
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private void PrintCostSheet()
    {
        if (Detail is null)
        {
            return;
        }

        try
        {
            var document = AppHost.QuoteDocumentService.BuildCostSheet(Detail);

            if (AppHost.QuoteDocumentService.Print(document, $"Hoja de costos {Detail.Id:0000}"))
            {
                SetStatus("Hoja de costos enviada a la impresora.", isError: false);
            }
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    // --- Utilidades -----------------------------------------------------------

    private static string FormatOptional(decimal? value) =>
        value.HasValue ? AppCulture.Quantity(value.Value) : string.Empty;

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        IsStatusError = isError;
    }

    private void ClearStatus()
    {
        StatusMessage = string.Empty;
        IsStatusError = false;
    }
}
