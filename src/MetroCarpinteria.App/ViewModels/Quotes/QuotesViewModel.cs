using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using MetroCarpinteria.App.Data.Entities;
using MetroCarpinteria.App.Helpers;
using MetroCarpinteria.App.Models;
using MetroCarpinteria.App.Services;

namespace MetroCarpinteria.App.ViewModels;

/// <summary>
/// Presupuestos. Es la pantalla más grande de la app, así que está partida en varios
/// archivos por tema: lista, editor, materiales, fotos, calculadora y ciclo de vida.
/// </summary>
/// <remarks>
/// <para>
/// Son <c>partial</c> del mismo tipo y no sub-ViewModels expuestos como propiedades. La
/// alternativa obligaba a reescribir las ~200 rutas de binding del XAML
/// (<c>{Binding CalcDays}</c> pasaría a <c>{Binding Calculator.CalcDays}</c>) y los tests,
/// todo junto. Con <c>partial</c> el tipo es el mismo: cero cambios afuera.
/// </para>
/// <para>
/// Acá viven los campos, el constructor, los comandos y el estado compartido. Cada archivo
/// se queda con su tema.
/// </para>
/// </remarks>
public partial class QuotesViewModel : ViewModelBase
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
    private string _formClientPhone = string.Empty;
    private string _formClientEmail = string.Empty;
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

    private bool _isLaborFormOpen;
    private EmployeeListItem? _selectedWorker;
    private string _looseWorkerName = string.Empty;
    private string _workerDays = "1";
    private string _workerDailyRate = string.Empty;

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
    private string _adjustedPrice = string.Empty;

    private bool _showCommitmentNote;
    private string _commitmentAmount = string.Empty;
    private string _commitmentText = string.Empty;

    private bool _isAttachmentPickerOpen;
    private bool _isSiblingFormOpen;
    private string _siblingTitle = string.Empty;

    /// <summary>
    /// Mientras se carga un presupuesto se asignan los tres campos de cálculo de una.
    /// Sin esta guarda, cada asignación dispararía un cálculo y una escritura a la base.
    /// </summary>
    private bool _isLoadingDetail;

    /// <summary>
    /// Estamos tocando la colección de la lista por dentro. Mientras esté prendida, la
    /// grilla puede reemitir la selección todo lo que quiera: no se recarga el detalle.
    /// </summary>
    private bool _isRefreshingList;

    /// <summary>Agrupan el tecleo para no consultar la base letra por letra.</summary>
    private readonly Debouncer _searchDebouncer = new();
    private readonly Debouncer _productSearchDebouncer = new();
    private readonly Debouncer _clientSearchDebouncer = new();

    private string _statusMessage = string.Empty;
    private bool _isStatusError;

    public QuotesViewModel(Action onDataChanged)
    {
        _onDataChanged = onDataChanged;

        Quotes = [];
        Lines = [];
        LaborLines = [];
        AvailableWorkers = [];
        Images = [];
        Attachments = [];
        AttachableQuotes = [];
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
        PickClientCommand = new RelayCommand(PickClient);

        AddMaterialCommand = new RelayCommand(_ => OpenMaterialForm(), _ => CanEditSelected);
        ConfirmMaterialCommand = new RelayCommand(_ => ConfirmMaterial(), _ => CanConfirmMaterial);
        CancelMaterialCommand = new RelayCommand(_ => CloseMaterialForm());
        EditLineCommand = new RelayCommand(EditLine, _ => CanEditSelected);
        RemoveLineCommand = new RelayCommand(RemoveLine, _ => CanEditSelected);

        AddLaborLineCommand = new RelayCommand(_ => OpenLaborForm(), _ => CanEditSelected);
        ConfirmWorkerCommand = new RelayCommand(_ => ConfirmWorker(), _ => CanConfirmWorker);
        CancelWorkerCommand = new RelayCommand(_ => CloseLaborForm());
        RemoveLaborLineCommand = new RelayCommand(RemoveLaborLine, _ => CanEditSelected);

        CalculateCommand = new RelayCommand(_ => Calculate());
        SaveDefaultRatesCommand = new RelayCommand(_ => SaveDefaultRates());
        RestoreDefaultRatesCommand = new RelayCommand(_ => RestoreDefaultRates());
        ApplyAdjustedPriceCommand = new RelayCommand(_ => ApplyAdjustedPrice(), _ => CanAdjustPrice);
        RestoreCalculatedPriceCommand = new RelayCommand(
            _ => RestoreCalculatedPrice(), _ => CanAdjustPrice && ShowManualAdjustNotice);

        ApproveCommand = new AsyncRelayCommand(ApproveAsync, () => CanEditSelected);
        RejectCommand = new AsyncRelayCommand(RejectAsync, () => CanEditSelected);
        ReopenCommand = new RelayCommand(_ => Reopen(), _ => Detail is { Status: ProjectStatus.Rejected });
        DuplicateCommand = new RelayCommand(_ => Duplicate(), _ => Detail is not null);
        ApplyPendingCommand = new RelayCommand(_ => ApplyPending(), _ => Detail is { HasPendingStock: true });
        // El presupuesto del cliente exige precio y desglose: sin eso salía impreso con el
        // TOTAL en un guión, que es peor que no entregar nada. La hoja de costos sí se
        // puede sacar a medio hacer, es de uso interno y sirve para revisar los materiales.
        PrintClientCommand = new RelayCommand(_ => PrintClient(), _ => CanPrintForClient);
        PrintCostSheetCommand = new RelayCommand(_ => PrintCostSheet(), _ => Detail is not null);
        SavePdfClientCommand = new RelayCommand(_ => SavePdfClient(), _ => CanPrintForClient);
        SavePdfCostSheetCommand = new RelayCommand(_ => SavePdfCostSheet(), _ => Detail is not null);

        AddImagesCommand = new RelayCommand(_ => AddImages(), _ => CanAddImages);
        PasteImageCommand = new RelayCommand(_ => PasteImage(), _ => CanAddImages);
        RemoveImageCommand = new RelayCommand(RemoveImage, _ => CanEditImages);
        SaveImageCaptionCommand = new RelayCommand(SaveImageCaption, _ => CanEditImages);

        OpenAttachmentPickerCommand = new RelayCommand(_ => OpenAttachmentPicker(), _ => CanManageAttachments);
        CloseAttachmentPickerCommand = new RelayCommand(_ => IsAttachmentPickerOpen = false);
        AttachQuoteCommand = new RelayCommand(AttachQuote, _ => CanManageAttachments);
        DetachQuoteCommand = new RelayCommand(DetachQuote, _ => CanManageAttachments);
        OpenSiblingFormCommand = new RelayCommand(_ => OpenSiblingForm(), _ => CanManageAttachments);
        CancelSiblingFormCommand = new RelayCommand(_ => CloseSiblingForm());
        CreateSiblingQuoteCommand = new RelayCommand(_ => CreateSiblingQuote(), _ => CanCreateSibling);

        _selectedVat = VatOptions[0];
        _selectedDiscountMode = DiscountModeOptions[0];

        // Cobrar cambia el saldo y el renglón de la lista, así que la sección avisa y
        // acá se recarga todo.
        PaymentsSection = new PaymentsSectionViewModel(ReloadListAndDetail);
    }

    /// <summary>Señas y pagos. El mismo bloque que usa Proyectos.</summary>
    public PaymentsSectionViewModel PaymentsSection { get; }

    public ObservableCollection<QuoteListItem> Quotes { get; }
    public ObservableCollection<QuoteLineItem> Lines { get; }
    public ObservableCollection<QuoteLaborLineItem> LaborLines { get; }
    public ObservableCollection<EmployeeListItem> AvailableWorkers { get; }
    public ObservableCollection<QuoteImageRow> Images { get; }
    public ObservableCollection<QuoteAttachmentItem> Attachments { get; }
    public ObservableCollection<QuoteListItem> AttachableQuotes { get; }
    public ObservableCollection<BreakdownLineItem> BreakdownLines { get; }
    public ObservableCollection<QuoteApprovalShortfall> Shortfalls { get; }
    public ObservableCollection<ProductListItem> AvailableProducts { get; }
    public IReadOnlyList<string> AvailableUnits { get; }
    public IReadOnlyList<QuoteFilterOption> FilterOptions { get; }

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
    public ICommand PickClientCommand { get; }
    public ICommand AddMaterialCommand { get; }
    public ICommand ConfirmMaterialCommand { get; }
    public ICommand CancelMaterialCommand { get; }
    public ICommand EditLineCommand { get; }
    public ICommand RemoveLineCommand { get; }
    public ICommand AddLaborLineCommand { get; }
    public ICommand ConfirmWorkerCommand { get; }
    public ICommand CancelWorkerCommand { get; }
    public ICommand RemoveLaborLineCommand { get; }
    public ICommand CalculateCommand { get; }
    public ICommand SaveDefaultRatesCommand { get; }
    public ICommand RestoreDefaultRatesCommand { get; }
    public ICommand ApplyAdjustedPriceCommand { get; }
    public ICommand RestoreCalculatedPriceCommand { get; }
    public ICommand ApproveCommand { get; }
    public ICommand RejectCommand { get; }
    public ICommand ReopenCommand { get; }
    public ICommand DuplicateCommand { get; }
    public ICommand ApplyPendingCommand { get; }
    public ICommand PrintClientCommand { get; }
    public ICommand PrintCostSheetCommand { get; }
    public ICommand SavePdfClientCommand { get; }
    public ICommand SavePdfCostSheetCommand { get; }
    public ICommand AddImagesCommand { get; }
    public ICommand PasteImageCommand { get; }
    public ICommand RemoveImageCommand { get; }
    public ICommand SaveImageCaptionCommand { get; }
    public ICommand OpenAttachmentPickerCommand { get; }
    public ICommand CloseAttachmentPickerCommand { get; }
    public ICommand AttachQuoteCommand { get; }
    public ICommand DetachQuoteCommand { get; }
    public ICommand OpenSiblingFormCommand { get; }
    public ICommand CancelSiblingFormCommand { get; }
    public ICommand CreateSiblingQuoteCommand { get; }

    // --- Carga ----------------------------------------------------------------

    /// <summary>
    /// Trae todo de nuevo. La llaman «Actualizar» y la navegación a la sección, que son
    /// justamente los momentos en que el usuario pide ver el estado actual: acá el detalle
    /// se relee siempre, aunque siga abierto el mismo presupuesto.
    /// </summary>
    public void Load() => SafeLoad(
        () =>
        {
            LoadProducts();
            ReloadListAndDetail();
        },
        "Presupuestos");

    // --- Utilidades -----------------------------------------------------------

    private static string FormatOptional(decimal? value) => NumberInput.Format(value);

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

    private void ClearStatus()
    {
        StatusMessage = string.Empty;
        IsStatusError = false;
    }
}
