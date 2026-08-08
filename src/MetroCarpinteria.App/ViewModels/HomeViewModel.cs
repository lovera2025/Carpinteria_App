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
        var quotes = AppHost.QuoteService.GetPendingSummary();

        Cards =
        [
            new DashboardCard
            {
                Title = "Presupuestos pendientes",
                Value = quotes.Pending.ToString(),
                Description = BuildQuotesDescription(quotes),
                AccentColor = "#8B5A2B"
            },
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

    /// <summary>Lo primero que hay que saber al abrir la app: a quién hay que llamar hoy.</summary>
    private static string BuildQuotesDescription(QuotePendingSummary summary)
    {
        if (summary.DueSoon > 0)
        {
            return summary.DueSoon == 1
                ? "1 por vencer — conviene llamar"
                : $"{summary.DueSoon} por vencer — conviene llamar";
        }

        if (summary.Pending == 0)
        {
            return summary.Expired > 0
                ? $"Ninguno en juego · {summary.Expired} vencidos"
                : "Ninguno esperando respuesta";
        }

        return summary.Pending == 1
            ? "Esperando respuesta del cliente"
            : "Esperando respuesta de los clientes";
    }
}
