using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using MetroCarpinteria.App.Data.Entities;
using MetroCarpinteria.App.Helpers;
using MetroCarpinteria.App.Models;
using MetroCarpinteria.App.Services;

namespace MetroCarpinteria.App.ViewModels;

/// <summary>Las líneas de material del presupuesto y el formulario para cargarlas.</summary>
public partial class QuotesViewModel
{
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
                _productSearchDebouncer.Run(LoadProducts);
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
            if (!NumberInput.TryParseQuantity(MaterialQuantity, out var quantity))
            {
                return "—";
            }

            NumberInput.TryParseMoney(MaterialUnitCost, out var unitCost);
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

    // --- Selector de productos ------------------------------------------------

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

        // Van con NumberInput.Format y no con AppCulture: estos dos son campos editables,
        // y lo que se escribe acá se relee tal cual al confirmar.
        MaterialQuantity = NumberInput.Format(line.Quantity);
        MaterialUnitCost = NumberInput.Format(line.UnitCost);
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
            var quantity = NumberInput.ParseQuantityOrThrow(MaterialQuantity, "Cantidad");
            var unitCost = string.IsNullOrWhiteSpace(MaterialUnitCost)
                ? 0m
                : NumberInput.ParseMoneyOrThrow(MaterialUnitCost, "Precio unitario");

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
            CalcMaterials = NumberInput.Format(Detail.MaterialsTotal);
        }
    }

}
