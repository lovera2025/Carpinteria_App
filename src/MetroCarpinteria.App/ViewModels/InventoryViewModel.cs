using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using MetroCarpinteria.App.Data.Entities;
using MetroCarpinteria.App.Helpers;
using MetroCarpinteria.App.Models;
using MetroCarpinteria.App.Services;

namespace MetroCarpinteria.App.ViewModels;

public class InventoryViewModel : ObservableObject
{
    private readonly Action _onDataChanged;
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
    private string _formCurrentStockDisplay = string.Empty;
    private string _movementQuantity = string.Empty;
    private string _movementReason = string.Empty;
    private bool _movementIsEntry = true;
    private string _statusMessage = string.Empty;
    private bool _isStatusError;

    public InventoryViewModel(Action onDataChanged)
    {
        _onDataChanged = onDataChanged;
        Products = new ObservableCollection<ProductListItem>();
        RecentMovements = new ObservableCollection<StockMovementItem>();
        AvailableUnits = ProductUnits.All;

        LoadCommand = new RelayCommand(_ => LoadProducts());
        NewProductCommand = new RelayCommand(_ => StartNewProduct());
        EditProductCommand = new RelayCommand(_ => StartEditProduct(), _ => SelectedProduct is not null);
        SaveProductCommand = new RelayCommand(_ => SaveProduct());
        CancelFormCommand = new RelayCommand(_ => CloseForm());
        ArchiveProductCommand = new RelayCommand(_ => ArchiveSelected(), _ => CanArchiveSelected);
        RestoreProductCommand = new RelayCommand(_ => RestoreSelected(), _ => CanRestoreSelected);
        DeleteProductCommand = new RelayCommand(_ => DeleteSelected(), _ => CanDeleteSelected);
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

            LoadMovementsForSelection();
            OnPropertyChanged(nameof(CanArchiveSelected));
            OnPropertyChanged(nameof(CanRestoreSelected));
            OnPropertyChanged(nameof(CanDeleteSelected));
            OnPropertyChanged(nameof(SelectedProductStockDisplay));
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                LoadProducts();
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

    public string FormName
    {
        get => _formName;
        set => SetProperty(ref _formName, value);
    }

    public string FormInitialStock
    {
        get => _formInitialStock;
        set => SetProperty(ref _formInitialStock, value);
    }

    public string FormMinimumStock
    {
        get => _formMinimumStock;
        set => SetProperty(ref _formMinimumStock, value);
    }

    public string FormUnit
    {
        get => _formUnit;
        set => SetProperty(ref _formUnit, value);
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
    public bool CanDeleteSelected => SelectedProduct is not null
        && !AppHost.InventoryService.HasMovements(SelectedProduct.Id);

    public ICommand LoadCommand { get; }
    public ICommand NewProductCommand { get; }
    public ICommand EditProductCommand { get; }
    public ICommand SaveProductCommand { get; }
    public ICommand CancelFormCommand { get; }
    public ICommand ArchiveProductCommand { get; }
    public ICommand RestoreProductCommand { get; }
    public ICommand DeleteProductCommand { get; }
    public ICommand RegisterMovementCommand { get; }

    public void LoadProducts()
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

        LoadMovementsForSelection();
        _onDataChanged();
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
        OnPropertyChanged(nameof(CanDeleteSelected));
        OnPropertyChanged(nameof(SelectedProductStockDisplay));
    }

    private void StartNewProduct()
    {
        FormName = string.Empty;
        FormInitialStock = "0";
        FormMinimumStock = "0";
        FormUnit = ProductUnits.Unit;
        FormCurrentStockDisplay = string.Empty;
        IsCreating = true;
        IsFormOpen = true;
        ClearStatus();
    }

    private void StartEditProduct()
    {
        if (SelectedProduct is null)
        {
            return;
        }

        FormName = SelectedProduct.Name;
        FormMinimumStock = SelectedProduct.MinimumStock.ToString(CultureInfo.CurrentCulture);
        FormUnit = SelectedProduct.Unit;
        FormCurrentStockDisplay = SelectedProduct.StockDisplay;
        IsCreating = false;
        IsFormOpen = true;
        ClearStatus();
    }

    private void SaveProduct()
    {
        try
        {
            if (!TryParseDecimal(FormMinimumStock, out var minimumStock))
            {
                throw new InvalidOperationException("Stock mínimo inválido.");
            }

            if (IsCreating)
            {
                if (!TryParseDecimal(FormInitialStock, out var initialStock))
                {
                    throw new InvalidOperationException("Stock inicial inválido.");
                }

                var product = AppHost.InventoryService.CreateProduct(
                    FormName, initialStock, minimumStock, FormUnit);
                SetStatus($"Producto «{product.Name}» creado.", isError: false);
            }
            else if (SelectedProduct is not null)
            {
                AppHost.InventoryService.UpdateProduct(
                    SelectedProduct.Id, FormName, minimumStock, FormUnit);
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

    private void ArchiveSelected()
    {
        if (SelectedProduct is null)
        {
            return;
        }

        var result = MessageBox.Show(
            $"¿Archivar «{SelectedProduct.Name}»?\n\nSeguirá en el historial pero no aparecerá en la lista activa.",
            "Confirmar archivo",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            AppHost.InventoryService.ArchiveProduct(SelectedProduct.Id);
            SetStatus($"Producto «{SelectedProduct.Name}» archivado.", isError: false);
            LoadProducts();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
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

    private void DeleteSelected()
    {
        if (SelectedProduct is null)
        {
            return;
        }

        var result = MessageBox.Show(
            $"¿Eliminar permanentemente «{SelectedProduct.Name}»?\n\nEsta acción no se puede deshacer.",
            "Confirmar eliminación",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var name = SelectedProduct.Name;
            AppHost.InventoryService.DeleteProduct(SelectedProduct.Id);
            SelectedProduct = null;
            SetStatus($"Producto «{name}» eliminado.", isError: false);
            LoadProducts();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
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
            if (!TryParseDecimal(MovementQuantity, out var quantity))
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

    private void ClearStatus()
    {
        StatusMessage = string.Empty;
        IsStatusError = false;
    }
}
