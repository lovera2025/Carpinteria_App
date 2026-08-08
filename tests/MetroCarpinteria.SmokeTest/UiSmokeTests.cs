using System.Windows;
using System.Windows.Documents;
using System.Windows.Threading;
using MetroCarpinteria.App.Data.Entities;
using MetroCarpinteria.App.Models;
using MetroCarpinteria.App.Services;
using MetroCarpinteria.App.ViewModels;
using MetroCarpinteria.App.Views;
using WpfApp = MetroCarpinteria.App.App;

namespace MetroCarpinteria.SmokeTest;

internal static class UiSmokeTests
{
    public static void Run(Action<string, Action> run)
    {
        Exception? threadError = null;

        var thread = new Thread(() =>
        {
            try
            {
                RunOnUiThread(run);
            }
            catch (Exception ex)
            {
                threadError = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadError is not null)
        {
            run("UI thread bootstrap", () => throw threadError);
        }
    }

    private static void RunOnUiThread(Action<string, Action> run)
    {
        if (Application.Current is null)
        {
            var app = new WpfApp();
            app.InitializeComponent();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }

        AppHost.Initialize();

        run("UI: MainWindow loads", () =>
        {
            var window = new MetroCarpinteria.App.MainWindow();
            window.Measure(new Size(1280, 800));
            window.Arrange(new Rect(0, 0, 1280, 800));
            window.UpdateLayout();

            if (window.DataContext is not MainViewModel)
            {
                throw new InvalidOperationException("MainWindow no tiene MainViewModel.");
            }
        });

        run("UI: navigate all sections", () =>
        {
            var window = new MetroCarpinteria.App.MainWindow();
            var viewModel = (MainViewModel)window.DataContext!;
            window.Measure(new Size(1280, 800));
            window.Arrange(new Rect(0, 0, 1280, 800));

            foreach (var item in viewModel.NavItems.ToList())
            {
                viewModel.SelectedNavItem = item;
                window.UpdateLayout();

                if (viewModel.CurrentViewModel is null)
                {
                    throw new InvalidOperationException($"Sin ViewModel para {item.Title}.");
                }
            }
        });

        run("UI: HomeViewModel metrics", () =>
        {
            var viewModel = new HomeViewModel();
            if (viewModel.Cards.Count == 0)
            {
                throw new InvalidOperationException("HomeViewModel no generó tarjetas del panel.");
            }
        });

        run("UI: AboutViewModel", () =>
        {
            var viewModel = new AboutViewModel();
            if (string.IsNullOrWhiteSpace(viewModel.BrandName))
            {
                throw new InvalidOperationException("AboutViewModel sin datos de marca.");
            }
        });

        run("UI: InventoryView + ViewModel", () =>
        {
            var viewModel = new InventoryViewModel(() => { });
            viewModel.LoadProducts();
            LoadView(() => new InventoryView(), viewModel);
        });
        run("UI: CashRegisterView + ViewModel", () =>
        {
            var viewModel = new CashRegisterViewModel(() => { });
            viewModel.Load();
            LoadView(() => new CashRegisterView(), viewModel);
        });
        run("UI: ProjectsView + ViewModel", () =>
        {
            var viewModel = new ProjectsViewModel(() => { });
            viewModel.Load();
            LoadView(() => new ProjectsView(), viewModel);
        });
        run("UI: QuotesView + ViewModel", () =>
        {
            var viewModel = new QuotesViewModel(() => { });
            viewModel.Load();
            LoadView(() => new QuotesView(), viewModel);
        });
        run("UI: Presupuestos no preselecciona un material", () =>
        {
            // Antes se elegía solo el primer producto del inventario, así que era fácil
            // cargar un tornillo sin querer con solo tipear la cantidad y confirmar.
            var viewModel = new QuotesViewModel(() => { });
            viewModel.Load();

            // Sin productos cargados la prueba no probaría nada: mejor que falle a que
            // pase en falso y tape una regresión.
            if (viewModel.AvailableProducts.Count == 0)
            {
                throw new InvalidOperationException(
                    "El inventario no tiene productos activos, así que esta prueba no verifica nada.");
            }

            if (viewModel.SelectedProduct is not null)
            {
                throw new InvalidOperationException(
                    $"No debería venir un producto preseleccionado, vino «{viewModel.SelectedProduct.Name}».");
            }

            if (viewModel.CanConfirmMaterial)
            {
                throw new InvalidOperationException(
                    "Sin material elegido no se tendría que poder confirmar.");
            }
        });
        run("UI: el buscador filtra los materiales", () =>
        {
            var viewModel = new QuotesViewModel(() => { });
            viewModel.Load();

            var total = viewModel.AvailableProducts.Count;
            var first = viewModel.AvailableProducts[0].Name;

            // Buscar por el nombre completo del primero tiene que dejar menos resultados
            // que la lista entera (o al menos seguir encontrándolo).
            viewModel.ProductSearch = first;

            if (viewModel.AvailableProducts.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Buscar «{first}» no devolvió nada, pero ese producto existe.");
            }

            if (viewModel.AvailableProducts.Count > total)
            {
                throw new InvalidOperationException("El filtro devolvió más productos que la lista sin filtrar.");
            }

            // Y limpiarlo devuelve la lista completa.
            viewModel.ProductSearch = string.Empty;
            if (viewModel.AvailableProducts.Count != total)
            {
                throw new InvalidOperationException(
                    $"Al limpiar la búsqueda esperaba {total} productos, hay {viewModel.AvailableProducts.Count}.");
            }
        });
        run("UI: StaffView + ViewModel", () =>
        {
            var viewModel = new StaffViewModel(() => { });
            viewModel.Load();
            LoadView(() => new StaffView(), viewModel);
        });
        run("UI: ReportsView + ViewModel", () =>
        {
            var reportsVm = new ReportsViewModel();
            reportsVm.Load();
            LoadView(() => new ReportsView(), reportsVm);
        });
        run("UI: SettingsView + ViewModel", () => LoadView(() => new SettingsView(), new SettingsViewModel()));

        run("UI: MainViewModel dashboard metrics", () =>
        {
            var viewModel = new MainViewModel();
            viewModel.RefreshDashboardMetrics();
            _ = viewModel.LowStockAlertCount;
            _ = viewModel.CurrentDate;
        });

        RunDocumentTests(run);

        Dispatcher.CurrentDispatcher.InvokeShutdown();
    }

    /// <summary>
    /// Los documentos se arman con tipos de WPF, así que necesitan el hilo STA.
    /// El chequeo importante es comercial: el papel del cliente no puede mostrar el margen.
    /// </summary>
    private static void RunDocumentTests(Action<string, Action> run)
    {
        var service = new QuoteDocumentService();
        var quote = BuildSampleQuote();

        run("PDF: el documento del cliente NO muestra la ganancia", () =>
        {
            var text = ToText(service.BuildClientQuote(quote, includeMaterialDetail: true));

            foreach (var forbidden in new[] { "Ganancia", "Desperdicio", "Desgaste", "Gastos adicionales", "30%", "16%" })
            {
                if (text.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"El presupuesto del cliente no debía contener «{forbidden}».");
                }
            }

            foreach (var expected in new[] { "PRESUPUESTO", "Cliente de prueba", "Mesa de prueba", "TOTAL" })
            {
                if (!text.Contains(expected, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Faltaba «{expected}» en el documento del cliente.");
                }
            }
        });

        run("PDF: la hoja de costos SÍ muestra el desglose", () =>
        {
            var text = ToText(service.BuildCostSheet(quote));

            foreach (var expected in new[] { "HOJA DE COSTOS", "no entregar al cliente", "Ganancia", "Desperdicio" })
            {
                if (!text.Contains(expected, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Faltaba «{expected}» en la hoja de costos.");
                }
            }
        });

        run("PDF: el documento imprime la fecha de validez", () =>
        {
            var text = ToText(service.BuildClientQuote(quote, includeMaterialDetail: false));

            if (!text.Contains("Válido hasta", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("El presupuesto debía indicar hasta cuándo vale el precio.");
            }
        });
    }

    private static QuoteDetail BuildSampleQuote()
    {
        var breakdown = BudgetCalculatorService.Calculate(new BudgetInput
        {
            MaterialsCost = 100000m,
            Days = 3m,
            DailyRate = 30000m,
            Rates = BudgetRates.Defaults()
        });

        return new QuoteDetail
        {
            Id = 42,
            Title = "Mesa de prueba",
            ClientName = "Cliente de prueba",
            Description = "Roble macizo",
            Status = ProjectStatus.Quote,
            Budget = breakdown.FinalPrice,
            QuotedAtLocal = DateTime.Today,
            ValidUntilLocal = DateTime.Today.AddDays(15),
            QuotedMaterialsCost = 100000m,
            EstimatedDays = 3m,
            DailyRate = 30000m,
            Rates = BudgetRates.Defaults(),
            Breakdown = breakdown,
            Lines =
            [
                new QuoteLineItem
                {
                    Id = 1,
                    Description = "Tabla de roble",
                    Unit = "Metro",
                    Quantity = 10m,
                    UnitCost = 10000m
                }
            ]
        };
    }

    private static string ToText(FlowDocument document) =>
        new TextRange(document.ContentStart, document.ContentEnd).Text;

    private static void LoadView(Func<FrameworkElement> createView, object dataContext)
    {
        var view = createView();
        view.DataContext = dataContext;
        view.Measure(new Size(900, 600));
        view.Arrange(new Rect(0, 0, 900, 600));
        view.UpdateLayout();
    }
}
