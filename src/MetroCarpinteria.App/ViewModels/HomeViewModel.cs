using MetroCarpinteria.App.Helpers;
using MetroCarpinteria.App.Models;
using MetroCarpinteria.App.Services;

namespace MetroCarpinteria.App.ViewModels;

public class HomeViewModel : ObservableObject
{
    private int _lowStockCount;
    private string _lowStockValue = "0";

    public HomeViewModel()
    {
        RefreshMetrics();
    }

    public IReadOnlyList<DashboardCard> Cards { get; private set; } = [];

    public string WelcomeMessage => "Bienvenido al panel de gestión";
    public string ContactInfo => "Diseños a medida | 3777-412207";

    public void RefreshMetrics()
    {
        _lowStockCount = AppHost.DatabaseService.GetLowStockCount();
        _lowStockValue = _lowStockCount.ToString();

        var cashOpen = AppHost.CashRegisterService.HasOpenSession();
        var cashState = AppHost.CashRegisterService.GetOpenSessionState();
        var activeProjects = AppHost.DatabaseService.GetActiveProjectCount();
        var employeeCount = AppHost.DatabaseService.GetEmployeeCount();

        Cards =
        [
            new DashboardCard
            {
                Title = "Stock bajo",
                Value = _lowStockValue,
                Description = _lowStockCount == 1
                    ? "Producto por debajo del mínimo"
                    : "Productos por debajo del mínimo",
                AccentColor = "#C45C26"
            },
            new DashboardCard
            {
                Title = "Caja",
                Value = cashOpen ? "Abierta" : "Cerrada",
                Description = cashOpen && cashState is not null
                    ? $"Esperado: {cashState.ExpectedDisplay}"
                    : "Sin sesión abierta",
                AccentColor = cashOpen ? "#4A7C59" : "#6B4423"
            },
            new DashboardCard
            {
                Title = "Proyectos activos",
                Value = activeProjects.ToString(),
                Description = activeProjects == 1 ? "En curso en el taller" : "En curso en el taller",
                AccentColor = "#4A7C59"
            },
            new DashboardCard
            {
                Title = "Personal",
                Value = employeeCount.ToString(),
                Description = employeeCount == 1
                    ? "Colaborador registrado"
                    : "Colaboradores registrados",
                AccentColor = "#C4A574"
            }
        ];

        OnPropertyChanged(nameof(Cards));
    }
}
