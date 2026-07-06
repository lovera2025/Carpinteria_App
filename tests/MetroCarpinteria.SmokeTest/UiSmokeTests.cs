using System.Windows;
using System.Windows.Threading;
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

        Dispatcher.CurrentDispatcher.InvokeShutdown();
    }

    private static void LoadView(Func<FrameworkElement> createView, object dataContext)
    {
        var view = createView();
        view.DataContext = dataContext;
        view.Measure(new Size(900, 600));
        view.Arrange(new Rect(0, 0, 900, 600));
        view.UpdateLayout();
    }
}
