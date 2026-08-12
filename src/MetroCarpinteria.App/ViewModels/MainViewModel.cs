using System.Collections.ObjectModel;
using System.Windows.Input;
using MetroCarpinteria.App.Helpers;
using MetroCarpinteria.App.Models;
using MetroCarpinteria.App.Services;

namespace MetroCarpinteria.App.ViewModels;

public class MainViewModel : ObservableObject
{
    private readonly Dictionary<NavigationSection, ObservableObject> _viewModels;
    private NavigationSection _selectedSection = NavigationSection.Home;
    private ObservableObject _currentViewModel;
    private NavItem _selectedNavItem;
    private readonly HomeViewModel _homeViewModel;
    private readonly InventoryViewModel _inventoryViewModel;
    private readonly CashRegisterViewModel _cashRegisterViewModel;
    private readonly QuotesViewModel _quotesViewModel;
    private readonly ProjectsViewModel _projectsViewModel;
    private readonly StaffViewModel _staffViewModel;
    private readonly ReportsViewModel _reportsViewModel;
    private readonly SettingsViewModel _settingsViewModel;
    private bool _hasUpdateReady;
    private string _updateReadyVersion = string.Empty;
    private int _lowStockAlertCount;

    public MainViewModel()
    {
        _homeViewModel = new HomeViewModel();
        _inventoryViewModel = new InventoryViewModel(RefreshDashboardMetrics);
        _cashRegisterViewModel = new CashRegisterViewModel(RefreshDashboardMetrics);
        _quotesViewModel = new QuotesViewModel(RefreshDashboardMetrics);
        _projectsViewModel = new ProjectsViewModel(RefreshDashboardMetrics);
        _staffViewModel = new StaffViewModel(RefreshDashboardMetrics);
        _reportsViewModel = new ReportsViewModel();
        _settingsViewModel = new SettingsViewModel();

        NavItems = new ObservableCollection<NavItem>
        {
            new() { Section = NavigationSection.Home, Title = "Inicio", Icon = "🏠" },
            new() { Section = NavigationSection.Inventory, Title = "Inventario", Icon = "📦" },
            new() { Section = NavigationSection.CashRegister, Title = "Caja", Icon = "💰" },
            new() { Section = NavigationSection.Quotes, Title = "Presupuestos", Icon = "🧮" },
            new() { Section = NavigationSection.Projects, Title = "Proyectos", Icon = "🪚" },
            new() { Section = NavigationSection.Staff, Title = "Personal", Icon = "👷" },
            new() { Section = NavigationSection.Reports, Title = "Reportes", Icon = "📊" },
            new() { Section = NavigationSection.Settings, Title = "Configuración", Icon = "⚙️" },
            new() { Section = NavigationSection.About, Title = "Acerca de", Icon = "ℹ️" }
        };

        _viewModels = new Dictionary<NavigationSection, ObservableObject>
        {
            [NavigationSection.Home] = _homeViewModel,
            [NavigationSection.Inventory] = _inventoryViewModel,
            [NavigationSection.CashRegister] = _cashRegisterViewModel,
            [NavigationSection.Quotes] = _quotesViewModel,
            [NavigationSection.Projects] = _projectsViewModel,
            [NavigationSection.Staff] = _staffViewModel,
            [NavigationSection.Reports] = _reportsViewModel,
            [NavigationSection.Settings] = _settingsViewModel,
            [NavigationSection.About] = new AboutViewModel()
        };

        _currentViewModel = _viewModels[NavigationSection.Home];
        _selectedNavItem = NavItems[0];
        NavigateCommand = new RelayCommand(Navigate);

        AppHost.ClockService.DayChanged += OnDayChanged;
    }

    /// <summary>
    /// Al cambiar el día se recarga lo que está a la vista. Las vigencias de los
    /// presupuestos se derivan de la fecha, y las listas guardan objetos ya construidos:
    /// sin recargarlas, la pantalla sigue mostrando los estados del día anterior.
    /// </summary>
    private void OnDayChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(CurrentDate));
        RefreshSection(SelectedSection);
    }

    public ObservableCollection<NavItem> NavItems { get; }

    public ObservableObject CurrentViewModel
    {
        get => _currentViewModel;
        private set => SetProperty(ref _currentViewModel, value);
    }

    public NavigationSection SelectedSection
    {
        get => _selectedSection;
        private set => SetProperty(ref _selectedSection, value);
    }

    public NavItem SelectedNavItem
    {
        get => _selectedNavItem;
        set
        {
            if (!SetProperty(ref _selectedNavItem, value) || value is null)
            {
                return;
            }

            SelectedSection = value.Section;
            CurrentViewModel = _viewModels[value.Section];
            RefreshSection(value.Section);
        }
    }

    public string CurrentDate => DateTime.Now.ToString("dddd, d 'de' MMMM yyyy", AppCulture.Current);

    /// <summary>
    /// Cantidad de productos bajo el mínimo, para el badge del menú y el aviso de la barra.
    /// <para>
    /// Es un valor cacheado y no un getter que consulte la base. Estando bindeado desde el
    /// XAML, cualquier reevaluación de binding disparaba una consulta con su propia conexión
    /// a SQLite, sincrónica sobre el hilo de la interfaz. Se refresca cuando algo cambia.
    /// </para>
    /// </summary>
    public int LowStockAlertCount
    {
        get => _lowStockAlertCount;
        private set => SetProperty(ref _lowStockAlertCount, value);
    }

    /// <summary>Versión en ejecución, para el pie del menú lateral.</summary>
    public string AppVersionDisplay => $"v{AppHost.UpdateService.CurrentVersion}";

    /// <summary>Hay una actualización descargada esperando el cierre de la app.</summary>
    public bool HasUpdateReady
    {
        get => _hasUpdateReady;
        private set
        {
            if (SetProperty(ref _hasUpdateReady, value))
            {
                OnPropertyChanged(nameof(UpdateBadgeCount));
            }
        }
    }

    /// <summary>El badge del menú se dibuja igual que el de stock bajo, con un número.</summary>
    public int UpdateBadgeCount => HasUpdateReady ? 1 : 0;

    public string UpdateReadyVersion
    {
        get => _updateReadyVersion;
        private set => SetProperty(ref _updateReadyVersion, value);
    }

    /// <summary>
    /// La llama el chequeo de arranque cuando ya bajó una versión nueva. Solo prende
    /// el aviso: la actualización se aplica sola al cerrar.
    /// </summary>
    public void NotifyUpdateReady(string version)
    {
        UpdateReadyVersion = version;
        HasUpdateReady = true;
        _settingsViewModel.NotifyUpdateReady(version);
    }

    public ICommand NavigateCommand { get; }

    /// <summary>
    /// Cambia de sección desde código (los accesos rápidos de Inicio, los atajos de teclado).
    /// <para>
    /// Va a través de <see cref="SelectedNavItem"/> y no seteando las propiedades a mano:
    /// así también se marca el ítem del menú y se actualiza el título de la barra superior.
    /// Antes navegaba de verdad pero el menú seguía resaltando la sección anterior y el
    /// encabezado mostraba el nombre equivocado.
    /// </para>
    /// </summary>
    private void Navigate(object? parameter)
    {
        if (parameter is not NavigationSection section)
        {
            return;
        }

        var item = NavItems.FirstOrDefault(i => i.Section == section);
        if (item is not null)
        {
            SelectedNavItem = item;
        }
    }

    private void RefreshSection(NavigationSection section)
    {
        switch (section)
        {
            case NavigationSection.Inventory:
                _inventoryViewModel.LoadProducts();
                break;
            case NavigationSection.CashRegister:
                _cashRegisterViewModel.Load();
                break;
            case NavigationSection.Quotes:
                _quotesViewModel.Load();
                break;
            case NavigationSection.Projects:
                _projectsViewModel.Load();
                break;
            case NavigationSection.Staff:
                _staffViewModel.Load();
                break;
            case NavigationSection.Reports:
                _reportsViewModel.Load();
                break;
            case NavigationSection.Settings:
                _settingsViewModel.Refresh();
                break;
        }

        RefreshDashboardMetrics();
    }

    public bool IsSectionSelected(NavigationSection section) => SelectedSection == section;

    /// <summary>
    /// Recalcula lo que se ve fuera de la sección actual. La llaman los ViewModels cuando
    /// cambian datos, así que un fallo acá no puede tumbar la acción que la disparó.
    /// </summary>
    public void RefreshDashboardMetrics()
    {
        try
        {
            LowStockAlertCount = AppHost.DatabaseService.GetLowStockCount();
        }
        catch (Exception ex)
        {
            LogService.Error("MainViewModel", "No se pudo contar el stock bajo", ex);
        }

        _homeViewModel.RefreshMetrics();
    }
}
