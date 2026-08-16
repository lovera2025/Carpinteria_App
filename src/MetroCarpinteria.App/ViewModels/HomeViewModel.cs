using System.Windows.Input;
using MetroCarpinteria.App.Helpers;
using MetroCarpinteria.App.Models;
using MetroCarpinteria.App.Services;

namespace MetroCarpinteria.App.ViewModels;

public class HomeViewModel : ViewModelBase
{
    /// <summary>
    /// Se sube cuando la app cambia lo suficiente como para que la guía valga otra vuelta.
    /// </summary>
    public const int OnboardingVersion = 1;

    private int _lowStockCount;
    private string _lowStockValue = "0";
    private bool _showOnboarding;

    public HomeViewModel()
    {
        DismissOnboardingCommand = new RelayCommand(_ => DismissOnboarding());
        RefreshMetrics();
    }

    public IReadOnlyList<DashboardCard> Cards { get; private set; } = [];

    public string WelcomeMessage => "Bienvenido al panel de gestión";
    public string ContactInfo => "Diseños a medida | 3777-412207";

    // --- Primeros pasos -------------------------------------------------------

    public sealed record OnboardingStep(string Number, string Title, string Description);

    /// <summary>
    /// El recorrido de un trabajo, de punta a punta. Son los cuatro pasos en el orden en
    /// que pasan en el taller, no un tour por los botones de la pantalla.
    /// </summary>
    public IReadOnlyList<OnboardingStep> OnboardingSteps { get; } =
    [
        new("1", "Cargá el inventario",
            "Los materiales con su precio de costo. Cotizar no descuenta stock: recién se descuenta cuando el cliente acepta."),
        new("2", "Armá un presupuesto",
            "Elegís los materiales, ponés los días y el jornal, y el precio sale solo. Los precios quedan congelados con el valor de hoy."),
        new("3", "Entregalo y cobrá la seña",
            "«Imprimir para el cliente» no muestra tu ganancia. La hoja de costos sí, y es la que no se entrega."),
        new("4", "Aprobalo cuando acepte",
            "Ahí se descuenta el stock y el trabajo pasa a Proyectos. Lo que no alcance queda anotado como pendiente de compra.")
    ];

    /// <summary>Nunca es modal ni bloquea: quien ya sabe usar la app la cierra y listo.</summary>
    public bool ShowOnboarding
    {
        get => _showOnboarding;
        private set => SetProperty(ref _showOnboarding, value);
    }

    public ICommand DismissOnboardingCommand { get; }

    private void DismissOnboarding()
    {
        AppHost.SettingsService.Update(s => s.OnboardingCompletedVersion = OnboardingVersion);
        ShowOnboarding = false;
    }

    public void RefreshMetrics() => SafeLoad(RefreshMetricsCore, "Inicio");

    private void RefreshMetricsCore()
    {
        ShowOnboarding = AppHost.Settings.OnboardingCompletedVersion < OnboardingVersion;

        _lowStockCount = AppHost.DatabaseService.GetLowStockCount();
        _lowStockValue = _lowStockCount.ToString();

        var cashOpen = AppHost.CashRegisterService.HasOpenSession();
        var cashState = AppHost.CashRegisterService.GetOpenSessionState();
        var activeProjects = AppHost.DatabaseService.GetActiveProjectCount();
        var approvedProjects = AppHost.DatabaseService.GetApprovedProjectCount();
        var overdueProjects = AppHost.ProjectService.GetOverdueCount();
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
                Description = approvedProjects == 0
                    ? "En el taller, sin nada esperando"
                    : approvedProjects == 1
                        ? "En el taller · 1 aprobado por iniciar"
                        : $"En el taller · {approvedProjects} aprobados por iniciar",
                AccentColor = "#4A7C59"
            },
            new DashboardCard
            {
                Title = "Trabajos atrasados",
                Value = overdueProjects.ToString(),
                Description = overdueProjects == 0
                    ? "Todo dentro de lo prometido"
                    : overdueProjects == 1
                        ? "Pasó la fecha que se prometió"
                        : "Pasaron la fecha que se prometió",
                AccentColor = "#C0392B"
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
