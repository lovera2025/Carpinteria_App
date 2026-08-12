using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using MetroCarpinteria.App.Data.Entities;
using MetroCarpinteria.App.Helpers;
using MetroCarpinteria.App.Models;
using MetroCarpinteria.App.Services;

namespace MetroCarpinteria.App.ViewModels;

public class InventoryViewModel : ViewModelBase
{
    private readonly Action _onDataChanged;

    /// <summary>Agrupa el tecleo del buscador para no consultar la base letra por letra.</summary>
    private readonly Debouncer _searchDebouncer = new();

    private ProductListItem? _selectedProduct;
    private string _searchText = string.Empty;
    private bool _showArchived;
    private bool _lowStockOnly;
    private bool _isFormOpen;
    private bool _isCreating;
    private string _formName = string.Empty;
    private string _formInitialStock = "0";
    private string _formMinimumStock = "0";
    private string _formUnit = ProductUnits.Unit;
    private string _formCostPrice = string.Empty;
    private string _formCurrentStockDisplay = string.Empty;
    private string _movementQuantity = string.Empty;
    private string _movementReason = string.Empty;
    private bool _movementIsEntry = true;
    private string _statusMessage = string.Empty;
    private bool _isStatusError;

    /// <summary>
    /// Por qué no se puede borrar el producto elegido, o null si se puede.
    /// <para>
    /// Se calcula una vez por selección y no dentro del predicado del comando. Enganchado
    /// al requery global, ese predicado corría con cada tecla y cada clic: eran dos
    /// consultas a SQLite, sincrónicas sobre el hilo de la interfaz, decenas de veces por
    /// segundo mientras alguien tipeaba en el buscador.
    /// </para>
    /// </summary>
    private string? _deleteBlockReason;

    public InventoryViewModel(Action onDataChanged)
    {
        _onDataChanged = onDataChanged;
        Products = new ObservableCollection<ProductListItem>();
        RecentMovements = new ObservableCollection<StockMovementItem>();
        AvailableUnits = ProductUnits.All;

        LoadCommand = new RelayCommand(_ => LoadProducts());
        NewProductCommand = new RelayCommand(_ => StartNewProduct());
        EditProductCommand = new RelayCommand(_ => StartEditProduct(), _ => SelectedProduct is not null);
        SaveProductCommand = new RelayCommand(_ => SaveProduct(), _ => CanSaveProduct);
        CancelFormCommand = new RelayCommand(_ => CloseForm());
        ArchiveProductCommand = new AsyncRelayCommand(ArchiveSelectedAsync, () => CanArchiveSelected);
        RestoreProductCommand = new RelayCommand(_ => RestoreSelected(), _ => CanRestoreSelected);
        DeleteProductCommand = new AsyncRelayCommand(
            DeleteSelectedAsync, () => CanDeleteSelected, observeRequery: false);
        RegisterMovementCommand = new RelayCommand(_ => RegisterMovement(), _ => SelectedProduct is not null && !SelectedProduct.IsArchived);
    }

    public ObservableCollection<ProductListItem> Products { get; }
    public ObservableCollection<StockMovementItem> RecentMovements { get; }
    public IReadOnlyList<string> AvailableUnits { get; }

    public ProductListItem? SelectedProduct
    {
        get => _selectedProduct;
        set
        {
            if (!SetProperty(ref _selectedProduct, value))
            {
                return;
            }

            RefreshDeleteBlockReason();
            LoadMovementsForSelection();
            OnPropertyChanged(nameof(CanArchiveSelected));
            OnPropertyChanged(nameof(CanRestoreSelected));
            OnPropertyChanged(nameof(SelectedProductStockDisplay));
        }
    }

    private void RefreshDeleteBlockReason()
    {
        _deleteBlockReason = SelectedProduct is null
            ? "Elegí un producto de la lista."
            : AppHost.InventoryService.DescribeDeleteBlock(SelectedProduct.Id);

        OnPropertyChanged(nameof(CanDeleteSelected));
        OnPropertyChanged(nameof(DeleteBlockTooltip));
        (DeleteProductCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                _searchDebouncer.Run(LoadProducts);
            }
        }
    }

    public bool ShowArchived
    {
        get => _showArchived;
        set
        {
            if (SetProperty(ref _showArchived, value))
            {
                LoadProducts();
            }
        }
    }

    public bool LowStockOnly
    {
        get => _lowStockOnly;
        set
        {
            if (SetProperty(ref _lowStockOnly, value))
            {
                LoadProducts();
            }
        }
    }

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
                OnPropertyChanged(nameof(IsEditing));
                OnPropertyChanged(nameof(FormTitle));
                OnPropertyChanged(nameof(ShowInitialStockField));
            }
        }
    }

    public bool IsEditing => !IsCreating;
    public bool ShowInitialStockField => IsCreating;
    public string FormTitle => IsCreating ? "Nuevo producto" : "Editar producto";

    /// <summary>
    /// Los campos del formulario se validan al tipear, no al guardar. El error aparece
    /// debajo del campo que lo causó y el botón Guardar queda deshabilitado hasta que
    /// no quede ninguno: antes había que apretar Guardar para enterarse de qué faltaba.
    /// </summary>
    public string FormName
    {
        get => _formName;
        set
        {
            if (SetProperty(ref _formName, value))
            {
                Validate(nameof(FormName),
                    () => Validators.Required(value, "El nombre del producto"),
                    () => Validators.MaxLength(value, 200, "El nombre"));
                OnPropertyChanged(nameof(CanSaveProduct));
            }
        }
    }

    public string FormInitialStock
    {
        get => _formInitialStock;
        set
        {
            if (SetProperty(ref _formInitialStock, value))
            {
                Validate(nameof(FormInitialStock),
                    () => Validators.NonNegativeQuantity(value, "El stock inicial"));
                OnPropertyChanged(nameof(CanSaveProduct));
            }
        }
    }

    public string FormMinimumStock
    {
        get => _formMinimumStock;
        set
        {
            if (SetProperty(ref _formMinimumStock, value))
            {
                Validate(nameof(FormMinimumStock),
                    () => Validators.NonNegativeQuantity(value, "El stock mínimo"));
                OnPropertyChanged(nameof(CanSaveProduct));
            }
        }
    }

    /// <summary>El formulario está completo y sin errores.</summary>
    public bool CanSaveProduct => !HasErrors && !string.IsNullOrWhiteSpace(FormName);

    public string FormUnit
    {
        get => _formUnit;
        set => SetProperty(ref _formUnit, value);
    }

    /// <summary>Precio de costo por unidad. Prellena las líneas de presupuesto.</summary>
    public string FormCostPrice
    {
        get => _formCostPrice;
        set => SetProperty(ref _formCostPrice, value);
    }

    public string FormCurrentStockDisplay
    {
        get => _formCurrentStockDisplay;
        private set => SetProperty(ref _formCurrentStockDisplay, value);
    }

    public string MovementQuantity
    {
        get => _movementQuantity;
        set => SetProperty(ref _movementQuantity, value);
    }

    public string MovementReason
    {
        get => _movementReason;
        set => SetProperty(ref _movementReason, value);
    }

    public bool MovementIsEntry
    {
        get => _movementIsEntry;
        set => SetProperty(ref _movementIsEntry, value);
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

    public string SelectedProductStockDisplay =>
        SelectedProduct is null ? string.Empty : SelectedProduct.StockDisplay;

    public bool CanArchiveSelected => SelectedProduct is { IsArchived: false };
    public bool CanRestoreSelected => SelectedProduct is { IsArchived: true };
    public bool CanDeleteSelected => SelectedProduct is not null && _deleteBlockReason is null;

    /// <summary>
    /// Lo que dice el botón «Eliminar» al pasarle el mouse. Antes se deshabilitaba sin
    /// explicar nada y no había forma de saber qué lo estaba bloqueando.
    /// </summary>
    public string DeleteBlockTooltip =>
        _deleteBlockReason ?? "Elimina el producto definitivamente.";

    public ICommand LoadCommand { get; }
    public ICommand NewProductCommand { get; }
    public ICommand EditProductCommand { get; }
    public ICommand SaveProductCommand { get; }
    public ICommand CancelFormCommand { get; }
    public ICommand ArchiveProductCommand { get; }
    public ICommand RestoreProductCommand { get; }
    public ICommand DeleteProductCommand { get; }
    public ICommand RegisterMovementCommand { get; }

    public void LoadProducts() => SafeLoad(LoadProductsCore, "Inventario");

    private void LoadProductsCore()
    {
        var items = AppHost.InventoryService.GetProducts(ShowArchived, LowStockOnly, SearchText);
        var selectedId = SelectedProduct?.Id;

        Products.Clear();
        foreach (var item in items)
        {
            Products.Add(item);
        }

        SelectedProduct = selectedId.HasValue
            ? Products.FirstOrDefault(p => p.Id == selectedId.Value)
            : Products.FirstOrDefault();

        // Explícito: si la selección quedó igual, el setter no corre y el motivo del
        // bloqueo quedaría con lo que valía antes de recargar.
        RefreshDeleteBlockReason();
        LoadMovementsForSelection();
        _onDataChanged();
        CommandManager.InvalidateRequerySuggested();
    }

    private void LoadMovementsForSelection()
    {
        RecentMovements.Clear();
        var movements = AppHost.InventoryService.GetRecentMovements(SelectedProduct?.Id, 30);
        foreach (var movement in movements)
        {
            RecentMovements.Add(movement);
        }

        OnPropertyChanged(nameof(CanArchiveSelected));
        OnPropertyChanged(nameof(CanRestoreSelected));
        OnPropertyChanged(nameof(SelectedProductStockDisplay));
    }

    private void StartNewProduct()
    {
        FormName = string.Empty;
        FormInitialStock = "0";
        FormMinimumStock = "0";
        FormUnit = ProductUnits.Unit;
        FormCostPrice = string.Empty;
        FormCurrentStockDisplay = string.Empty;
        IsCreating = true;
        IsFormOpen = true;

        // Un formulario recién abierto no muestra errores: marcar en rojo un campo que
        // todavía no se tocó es regañar a alguien por no haber empezado.
        ClearAllErrors();
        OnPropertyChanged(nameof(CanSaveProduct));
        ClearStatus();
    }

    private void StartEditProduct()
    {
        if (SelectedProduct is null)
        {
            return;
        }

        FormName = SelectedProduct.Name;
        FormMinimumStock = NumberInput.Format(SelectedProduct.MinimumStock);
        FormUnit = SelectedProduct.Unit;
        FormCostPrice = NumberInput.Format(SelectedProduct.CostPrice);
        FormCurrentStockDisplay = SelectedProduct.StockDisplay;
        IsCreating = false;
        IsFormOpen = true;
        ClearStatus();
    }

    private void SaveProduct()
    {
        try
        {
            if (!NumberInput.TryParseQuantity(FormMinimumStock, out var minimumStock))
            {
                throw new InvalidOperationException("Stock mínimo inválido.");
            }

            decimal? costPrice = null;
            if (!string.IsNullOrWhiteSpace(FormCostPrice))
            {
                if (!NumberInput.TryParseMoney(FormCostPrice, out var parsedCost))
                {
                    throw new InvalidOperationException("Precio de costo inválido.");
                }

                costPrice = parsedCost;
            }

            if (IsCreating)
            {
                if (!NumberInput.TryParseQuantity(FormInitialStock, out var initialStock))
                {
                    throw new InvalidOperationException("Stock inicial inválido.");
                }

                var product = AppHost.InventoryService.CreateProduct(
                    FormName, initialStock, minimumStock, FormUnit, costPrice);
                SetStatus($"Producto «{product.Name}» creado.", isError: false);
            }
            else if (SelectedProduct is not null)
            {
                AppHost.InventoryService.UpdateProduct(
                    SelectedProduct.Id, FormName, minimumStock, FormUnit, costPrice);
                SetStatus($"Producto «{FormName.Trim()}» actualizado.", isError: false);
            }

            CloseForm();
            LoadProducts();
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

    private async Task ArchiveSelectedAsync()
    {
        if (SelectedProduct is null)
        {
            return;
        }

        var name = SelectedProduct.Name;

        var confirmed = await AppHost.DialogService.ConfirmAsync(
            "Archivar producto",
            $"«{name}» va a dejar de aparecer en la lista.\n\n" +
            "Sus movimientos quedan guardados y lo podés volver a activar cuando quieras.",
            confirmText: "Archivar");

        if (!confirmed)
        {
            return;
        }

        try
        {
            AppHost.InventoryService.ArchiveProduct(SelectedProduct.Id);
            AppHost.NotificationService.Success($"«{name}» quedó archivado.");
            LoadProducts();
        }
        catch (Exception ex)
        {
            AppHost.NotificationService.Error(ex.Message, ex);
        }
    }

    private void RestoreSelected()
    {
        if (SelectedProduct is null)
        {
            return;
        }

        try
        {
            AppHost.InventoryService.RestoreProduct(SelectedProduct.Id);
            SetStatus($"Producto «{SelectedProduct.Name}» restaurado.", isError: false);
            LoadProducts();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private async Task DeleteSelectedAsync()
    {
        if (SelectedProduct is null)
        {
            return;
        }

        var name = SelectedProduct.Name;

        var confirmed = await AppHost.DialogService.ConfirmAsync(
            "Eliminar producto",
            $"Se va a borrar «{name}» del catálogo.\n\nEsto no se puede deshacer. " +
            "Si solo querés sacarlo de la lista, conviene archivarlo.",
            confirmText: "Eliminar",
            isDestructive: true);

        if (!confirmed)
        {
            return;
        }

        try
        {
            AppHost.InventoryService.DeleteProduct(SelectedProduct.Id);
            SelectedProduct = null;
            AppHost.NotificationService.Success($"«{name}» se eliminó del catálogo.");
            LoadProducts();
        }
        catch (Exception ex)
        {
            AppHost.NotificationService.Error(ex.Message, ex);
        }
    }

    private void RegisterMovement()
    {
        if (SelectedProduct is null)
        {
            return;
        }

        try
        {
            if (!NumberInput.TryParseQuantity(MovementQuantity, out var quantity))
            {
                throw new InvalidOperationException("Cantidad inválida.");
            }

            var type = MovementIsEntry ? StockMovementType.In : StockMovementType.Out;
            AppHost.InventoryService.RegisterMovement(
                SelectedProduct.Id, type, quantity, MovementReason);

            var action = MovementIsEntry ? "Entrada" : "Salida";
            SetStatus($"{action} registrada correctamente.", isError: false);
            MovementQuantity = string.Empty;
            MovementReason = string.Empty;
            LoadProducts();
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

    private void ClearStatus()
    {
        StatusMessage = string.Empty;
        IsStatusError = false;
    }
}
