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

    public int LowStockAlertCount => AppHost.DatabaseService.GetLowStockCount();

    public ICommand NavigateCommand { get; }

    private void Navigate(object? parameter)
    {
        if (parameter is not NavigationSection section)
        {
            return;
        }

        if (!_viewModels.TryGetValue(section, out var viewModel))
        {
            return;
        }

        SelectedSection = section;
        CurrentViewModel = viewModel;
        RefreshSection(section);
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

    public void RefreshDashboardMetrics()
    {
        OnPropertyChanged(nameof(LowStockAlertCount));
        _homeViewModel.RefreshMetrics();
    }
}
