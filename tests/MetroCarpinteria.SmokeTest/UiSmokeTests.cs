using System.Windows;
using System.Windows.Documents;
using System.Windows.Threading;
using MetroCarpinteria.App.Data.Entities;
using MetroCarpinteria.App.Helpers;
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

        // Carpeta temporal con datos conocidos. Antes acá iba AppHost.Initialize() sin
        // argumentos, que apuntaba a la base real: instanciar QuotesViewModel llegaba a
        // grabar en ella vía AutoCalculate.
        using var fixture = TestFixture.CreateSeeded();

        run("UI: los tests no tocan la base de producción", () =>
        {
            var production = new AppPaths();
            Assert.False(
                string.Equals(AppHost.Paths.RootDirectory, production.RootDirectory, StringComparison.OrdinalIgnoreCase),
                $"AppHost quedó apuntando a la carpeta real ({AppHost.Paths.RootDirectory}).");
            Assert.True(AppHost.IsReady, "AppHost debía quedar listo tras inicializar la fixture.");
        });

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
        run("UI: editar una línea sin tocarla no cambia la cantidad", () =>
        {
            // Dos defectos distintos se cruzaban acá, y la cantidad de tres decimales
            // los expone a los dos:
            //
            // 1. AppCulture.Quantity formatea con "0.##" — dos decimales — pero las
            //    cantidades se guardan como decimal(18,3). Abrir el lápiz de una línea
            //    de 2,125 m y confirmar sin tocar nada la dejaba en 2,12.
            // 2. Ese texto se releía con la cultura del sistema, así que en una PC en
            //    inglés "2,125" volvía como 2125. De ahí el en-US forzado: en es-AR el
            //    segundo defecto no se manifiesta y el test no probaría esa mitad.
            var quantity = 2.125m;
            var unitCost = 1234.56m;
            AppHost.QuoteService.AddLooseLine(
                fixture.QuoteId, "Varilla fraccionada", "Metro", quantity, unitCost, saveToCatalog: false);

            var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("en-US");

                var viewModel = new QuotesViewModel(() => { });
                viewModel.Load();
                viewModel.SelectedQuote = viewModel.Quotes.First(q => q.Id == fixture.QuoteId);

                var line = viewModel.Lines.FirstOrDefault(l => l.Description == "Varilla fraccionada")
                    ?? throw new InvalidOperationException("No se encontró la línea recién agregada.");

                viewModel.EditLineCommand.Execute(line);
                Assert.Equal(viewModel.MaterialQuantity, "2,125", "cantidad escrita en el campo editable");
                Assert.Equal(viewModel.MaterialUnitCost, "1234,56", "precio escrito en el campo editable");

                viewModel.ConfirmMaterialCommand.Execute(null);
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = originalCulture;
            }

            var saved = AppHost.QuoteService.GetDetail(fixture.QuoteId)!
                .Lines.First(l => l.Description == "Varilla fraccionada");

            Assert.Equal(saved.Quantity, quantity, "cantidad tras confirmar sin editar");
            Assert.Equal(saved.UnitCost, unitCost, "precio unitario tras confirmar sin editar");
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
        run("UI: «Recalcular» valida igual que el cálculo automático", () =>
        {
            // Los dos caminos tenían reglas distintas: el automático exigía días y jornal
            // mayores a cero, y el botón solo que no fueran negativos. Apretándolo se
            // guardaba como precio final el costo de los materiales, sin una hora cotizada.
            var viewModel = new QuotesViewModel(() => { });
            viewModel.Load();
            viewModel.SelectedQuote = viewModel.Quotes.First(q => q.Id == fixture.QuoteId);

            var priceBefore = AppHost.QuoteService.GetDetail(fixture.QuoteId)!.Budget;

            viewModel.CalcDays = "0";
            viewModel.CalculateCommand.Execute(null);

            Assert.True(
                viewModel.StatusMessage.Contains("días", StringComparison.OrdinalIgnoreCase),
                $"tendría que avisar que faltan los días, dijo «{viewModel.StatusMessage}».");
            Assert.True(viewModel.IsStatusError, "un cálculo incompleto no es un éxito.");
            Assert.True(viewModel.Breakdown is null, "sin días no tendría que quedar un desglose.");

            Assert.Equal(
                AppHost.QuoteService.GetDetail(fixture.QuoteId)!.Budget,
                priceBefore,
                "precio guardado tras un recálculo inválido");
        });

        run("UI: no se imprime para el cliente un presupuesto sin precio", () =>
        {
            // El documento salía con el TOTAL en un guión, y eso ya llegó al cliente.
            var emptyId = AppHost.QuoteService.CreateQuote("Sin calcular", "Cliente sin precio", null).Id;

            var viewModel = new QuotesViewModel(() => { });
            viewModel.Load();
            viewModel.SelectedQuote = viewModel.Quotes.First(q => q.Id == emptyId);

            Assert.False(viewModel.CanPrintForClient, "sin precio ni desglose no se puede entregar.");
            Assert.False(
                viewModel.PrintClientCommand.CanExecute(null),
                "el botón de imprimir tendría que estar deshabilitado.");

            // Con el cálculo hecho sí se habilita.
            AppHost.QuoteService.AddLooseLine(emptyId, "Tapa de pino", "Metro", 2m, 800m, saveToCatalog: false);
            AppHost.QuoteService.SaveCalculation(emptyId, 1600m, 1m, 20000m, BudgetRates.Defaults());

            viewModel.Load();
            viewModel.SelectedQuote = viewModel.Quotes.First(q => q.Id == emptyId);
            Assert.True(viewModel.CanPrintForClient, "con precio y desglose tendría que poder imprimirse.");
        });

        run("UI: el panel de fotos carga en un presupuesto", () =>
        {
            var viewModel = new QuotesViewModel(() => { });
            viewModel.Load();
            viewModel.SelectedQuote = viewModel.Quotes.First(q => q.Id == fixture.QuoteId);

            Assert.True(viewModel.CanEditImages, "un presupuesto abierto tendría que aceptar fotos.");
            Assert.True(viewModel.CanAddImages, "sin fotos todavía se pueden agregar.");
            Assert.True(viewModel.AddImagesCommand.CanExecute(null), "el botón Agregar tendría que estar habilitado.");

            LoadView(() => new MetroCarpinteria.App.Views.Quotes.QuoteImagesPanel(), viewModel);
        });

        run("UI: los estados que ofrece Proyectos dependen del actual", () =>
        {
            // El desplegable ofrecía los cinco siempre: de ahí salía el salteo del ciclo.
            var viewModel = new ProjectsViewModel(() => { });
            viewModel.Load();
            viewModel.SelectedProject = viewModel.Projects.First(p => p.Id == fixture.ActiveProjectId);

            viewModel.EditProjectCommand.Execute(null);

            var offered = viewModel.FormStatusOptions.Select(o => o.Status).ToList();
            Assert.True(offered.Contains(ProjectStatus.InProgress), "tendría que poder quedarse como está.");
            Assert.True(offered.Contains(ProjectStatus.Completed), "terminar el trabajo es el avance normal.");
            Assert.False(offered.Contains(ProjectStatus.Quote), "volver a presupuesto va por «Cancelar trabajo».");
            Assert.False(offered.Contains(ProjectStatus.Rejected), "un trabajo aprobado no se rechaza.");
        });

        run("UI: ClientsView + ViewModel", () =>
        {
            var viewModel = new ClientsViewModel(() => { });
            viewModel.Load();
            LoadView(() => new ClientsView(), viewModel);
        });

        run("UI: la revisión de duplicados propone el par y recuerda el «son distintos»", () =>
        {
            // La fixture siembra «Cliente de prueba» y «Cliente de prueba h.»: pueden ser
            // padre e hijo, así que la app propone y el carpintero decide.
            var viewModel = new ClientsViewModel(() => { });
            viewModel.Load();

            viewModel.ToggleDuplicatesCommand.Execute(null);
            Assert.True(viewModel.IsReviewingDuplicates, "tendría que entrar en modo revisión.");
            Assert.True(viewModel.HasDuplicates, "el par sembrado tendría que aparecer.");

            var pair = viewModel.Duplicates.First();
            viewModel.DismissPairCommand.Execute(pair);

            Assert.False(
                viewModel.Duplicates.Any(d => d.PairKey == pair.PairKey),
                "el par descartado tendría que desaparecer de la lista.");

            // Y sigue descartado al volver a entrar: si la revisión repite lo que ya se
            // marcó, se termina ignorando entera.
            var again = new ClientsViewModel(() => { });
            again.Load();
            again.ToggleDuplicatesCommand.Execute(null);

            Assert.False(
                again.Duplicates.Any(d => d.PairKey == pair.PairKey),
                "el descarte tendría que recordarse entre sesiones.");
        });

        run("UI: al cotizar se ofrecen las fichas que ya existen, sin obligar a elegir", () =>
        {
            // El campo sigue siendo texto libre: llega alguien, se le pasa un precio, y
            // recién si acepta importa quién es.
            AppHost.ClientService.Create("Mueblería Los Álamos", "3777-333444");

            var viewModel = new QuotesViewModel(() => { });
            viewModel.Load();
            viewModel.NewQuoteCommand.Execute(null);

            viewModel.FormClientName = "muebleria los";
            Assert.True(viewModel.HasClientSuggestions, "tendría que ofrecer la ficha que ya existe.");

            var suggestion = viewModel.ClientSuggestions.First();
            Assert.Equal(suggestion.Name, "Mueblería Los Álamos", "sugerencia encontrada sin acentos");

            viewModel.PickClientCommand.Execute(suggestion);
            Assert.Equal(viewModel.FormClientName, "Mueblería Los Álamos", "nombre completado");
            Assert.False(viewModel.HasClientSuggestions, "elegida una, no quedan sugerencias.");
        });

        run("UI: guardar un presupuesto con un cliente nuevo le crea la ficha", () =>
        {
            var viewModel = new QuotesViewModel(() => { });
            viewModel.Load();

            viewModel.NewQuoteCommand.Execute(null);
            viewModel.FormTitle = "Alacena";
            viewModel.FormClientName = "Doña Rosa";
            viewModel.SaveQuoteCommand.Execute(null);

            var client = AppHost.ClientService.GetClients(search: "Doña Rosa").SingleOrDefault();
            Assert.NotNull(client, "la ficha tendría que haberse creado sola");

            // Y el presupuesto quedó enganchado a ella.
            Assert.Equal(viewModel.Detail!.ClientId ?? 0, client!.Id, "ficha vinculada al presupuesto");
            Assert.Equal(viewModel.Detail.ClientName, "Doña Rosa", "nombre impreso");
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

        run("UI: con IVA pactado, todos los carteles muestran el mismo total", () =>
        {
            // El presupuesto sembrado lleva 10% de descuento y 21% de IVA. Tener el precio
            // calculado en un cartel y el total en otro es la forma más fácil de que
            // alguien lea el número equivocado en voz alta con el cliente enfrente.
            var viewModel = new QuotesViewModel(() => { });
            viewModel.Load();
            viewModel.SelectedQuote = viewModel.Quotes.First(q => q.Id == fixture.QuoteId);

            var total = viewModel.Detail!.Commercial!.TotalDisplay;

            Assert.True(
                viewModel.Detail.Commercial.Total != viewModel.Breakdown!.FinalPrice,
                "la prueba necesita condiciones pactadas para tener sentido.");

            Assert.Equal(viewModel.FinalPriceOrPlaceholder, total, "barra fija de precio final");
            Assert.Equal(viewModel.PriceStepSummary, total, "resumen del paso de precio");
            Assert.Equal(viewModel.Detail.BudgetDisplay, total, "precio guardado");
            Assert.False(viewModel.ShowManualAdjustNotice, "aplicar condiciones no es un ajuste a mano.");
        });

        run("UI: «Volver al calculado» no tira a la basura el IVA pactado", () =>
        {
            var viewModel = new QuotesViewModel(() => { });
            viewModel.Load();
            viewModel.SelectedQuote = viewModel.Quotes.First(q => q.Id == fixture.QuoteId);

            var withTerms = viewModel.Detail!.Commercial!.Total;

            // Se redondea a mano para cerrar la venta…
            viewModel.AdjustedPrice = "190000";
            viewModel.ApplyAdjustedPriceCommand.Execute(null);
            Assert.True(viewModel.ShowManualAdjustNotice, "tendría que quedar marcado como ajustado.");

            // …y al volver atrás se vuelve al total CON condiciones, no al precio pelado.
            viewModel.RestoreCalculatedPriceCommand.Execute(null);

            Assert.Equal(viewModel.Detail!.Budget ?? 0m, withTerms, "precio tras volver al calculado");
            Assert.False(viewModel.ShowManualAdjustNotice, "ya no habría que avisar de un ajuste.");
        });

        run("UI: buscar en la lista no pisa lo tipeado en la calculadora", () =>
        {
            // El ciclo era: buscar → recargar la lista → la grilla reemite la selección →
            // se relee el detalle → los campos vuelven a los valores guardados. Con un
            // presupuesto abierto, tipear en el buscador borraba lo que se estaba cargando.
            var viewModel = new QuotesViewModel(() => { });
            viewModel.Load();
            viewModel.SelectedQuote = viewModel.Quotes.First(q => q.Id == fixture.QuoteId);

            // Un valor todavía sin confirmar: en la vista los campos guardan al perder el
            // foco, así que esto es exactamente lo que hay tipeado a mitad de una carga.
            viewModel.CalcDailyRate = "44444";

            viewModel.SearchText = "Mesada";
            Assert.Equal(viewModel.CalcDailyRate, "44444", "jornal tras buscar");

            viewModel.SearchText = string.Empty;
            Assert.Equal(viewModel.CalcDailyRate, "44444", "jornal tras limpiar la búsqueda");

            // Y el presupuesto abierto sigue siendo el mismo.
            Assert.Equal(viewModel.Detail!.Id, fixture.QuoteId, "presupuesto abierto");
        });

        run("UI: reelegir el mismo presupuesto no recarga el formulario", () =>
        {
            // La grilla entrega instancias nuevas del mismo presupuesto cada vez que se
            // refresca la lista. Comparadas por referencia, cada una parecía un cambio de
            // selección y disparaba la recarga.
            var viewModel = new QuotesViewModel(() => { });
            viewModel.Load();
            viewModel.SelectedQuote = viewModel.Quotes.First(q => q.Id == fixture.QuoteId);

            viewModel.CalcDays = "9";

            var otherInstance = AppHost.QuoteService.GetListItem(fixture.QuoteId)!;
            Assert.False(
                ReferenceEquals(otherInstance, viewModel.SelectedQuote),
                "la prueba necesita una instancia distinta para tener sentido.");

            viewModel.SelectedQuote = otherInstance;

            Assert.Equal(viewModel.CalcDays, "9", "días tras reelegir el mismo presupuesto");
        });

        run("UI: pasar de un presupuesto incompleto a uno completo limpia el cartel de qué falta", () =>
        {
            // El aviso quedaba pegado: se abría uno sin calcular, se elegía otro que sí
            // tenía precio, y el cartel seguía diciendo «Falta el valor del jornal».
            var pendingId = AppHost.QuoteService.CreateQuote("Sin calcular aún", "Cliente pendiente", null).Id;

            var viewModel = new QuotesViewModel(() => { });
            viewModel.Load();

            viewModel.SelectedQuote = viewModel.Quotes.First(q => q.Id == pendingId);
            Assert.True(viewModel.HasMissingData, "un presupuesto sin calcular tendría que decir qué le falta.");

            viewModel.SelectedQuote = viewModel.Quotes.First(q => q.Id == fixture.QuoteId);

            Assert.False(viewModel.HasMissingData, $"quedó pegado: «{viewModel.MissingDataMessage}»");
            Assert.Equal(viewModel.PriceStepSummary, viewModel.FinalPriceOrPlaceholder, "resumen del paso");
        });

        run("UI: cambiar de presupuesto sí recarga el formulario", () =>
        {
            // La contracara: el atajo no puede dejar el detalle pegado al anterior.
            var otherId = AppHost.QuoteService.CreateQuote("Otro trabajo", "Otro cliente", null).Id;

            var viewModel = new QuotesViewModel(() => { });
            viewModel.Load();
            viewModel.SelectedQuote = viewModel.Quotes.First(q => q.Id == fixture.QuoteId);
            viewModel.SelectedQuote = viewModel.Quotes.First(q => q.Id == otherId);

            Assert.Equal(viewModel.Detail!.Id, otherId, "presupuesto abierto tras cambiar");
            Assert.Equal(viewModel.Detail.Title, "Otro trabajo", "título del detalle");
        });

        run("UI: el motivo por el que no se puede borrar se calcula sin consultar en cada tecla", () =>
        {
            // El predicado del comando estaba enganchado al barrido global de WPF, que
            // dispara con cada tecla y cada clic: eran dos consultas a SQLite sincrónicas
            // sobre el hilo de la interfaz, decenas de veces por segundo mientras se tipea.
            var viewModel = new InventoryViewModel(() => { });
            viewModel.LoadProducts();
            viewModel.SelectedProduct = viewModel.Products.First(p => p.Id == fixture.BoardProductId);

            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (var i = 0; i < 1000; i++)
            {
                _ = viewModel.CanDeleteSelected;
            }

            watch.Stop();

            Assert.True(
                watch.ElapsedMilliseconds < 50,
                $"1000 lecturas tardaron {watch.ElapsedMilliseconds} ms: el predicado sigue consultando la base.");

            // La tabla de roble está en el presupuesto sembrado, así que no se puede borrar
            // y el tooltip tiene que decir por qué.
            Assert.False(viewModel.CanDeleteSelected, "el producto está en un presupuesto.");
            Assert.True(
                viewModel.DeleteBlockTooltip.Contains("presupuesto", StringComparison.OrdinalIgnoreCase),
                $"el tooltip no explica el motivo: «{viewModel.DeleteBlockTooltip}»");
        });

        run("UI: una ráfaga de tecleo dispara una sola búsqueda", () =>
        {
            // El Debouncer se prueba solo, con retardo real: en la suite el default está
            // en cero para que las aserciones sobre listas filtradas no dependan del reloj.
            var runs = 0;
            var debouncer = new Debouncer(TimeSpan.FromMilliseconds(40));

            foreach (var _ in "mesada")
            {
                debouncer.Run(() => runs++);
            }

            Assert.Equal(runs, 0, "ejecuciones antes de que se calme la ráfaga");

            // Se bombea el bucle de mensajes hasta que el temporizador llegue a disparar.
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (runs == 0 && DateTime.UtcNow < deadline)
            {
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
                Thread.Sleep(10);
            }

            Assert.Equal(runs, 1, "seis teclas tendrían que dejar una sola búsqueda");
        });

        run("UI: con un presupuesto listo, los botones de entrega quedan habilitados", () =>
        {
            // Protege el endurecimiento de PrintClientCommand: pasarse de estricto deja al
            // taller sin poder imprimir un presupuesto que está perfecto, y eso se nota
            // recién con el cliente enfrente.
            var viewModel = new QuotesViewModel(() => { });
            viewModel.Load();
            viewModel.SelectedQuote = viewModel.Quotes.First(q => q.Id == fixture.QuoteId);

            var view = new QuotesView { DataContext = viewModel };
            view.Measure(new Size(1200, 800));
            view.Arrange(new Rect(0, 0, 1200, 800));
            view.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);

            foreach (var label in new[] { "Imprimir para el cliente", "Hoja de costos", "Aprobar", "Rechazar" })
            {
                var button = FindButton(view, label)
                    ?? throw new InvalidOperationException($"No se encontró el botón «{label}».");

                Assert.True(button.IsEnabled, $"«{label}» tendría que estar habilitado.");
            }
        });

        run("UI: cada sección con lista tiene marcado su buscador para Ctrl+F", () =>
        {
            // El atajo es global pero el buscador es de cada pantalla: sin la marca, el
            // shell no tiene forma de saber dónde poner el foco y Ctrl+F no hace nada.
            (Func<FrameworkElement> Create, object Model, string Name)[] sections =
            [
                (() => new InventoryView(), new InventoryViewModel(() => { }), "Inventario"),
                (() => new QuotesView(), new QuotesViewModel(() => { }), "Presupuestos"),
                (() => new ClientsView(), new ClientsViewModel(() => { }), "Clientes"),
                (() => new ProjectsView(), new ProjectsViewModel(() => { }), "Proyectos"),
                (() => new StaffView(), new StaffViewModel(() => { }), "Personal")
            ];

            foreach (var (create, model, name) in sections)
            {
                var view = create();
                view.DataContext = model;
                view.Measure(new Size(1100, 700));
                view.Arrange(new Rect(0, 0, 1100, 700));
                view.UpdateLayout();

                Assert.NotNull(
                    MetroCarpinteria.App.Controls.Ui.FindSectionSearchBox(view),
                    $"{name} no tiene marcado su buscador con Ui.IsSectionSearchBox");
            }
        });

        run("UI: la chuleta de atajos se abre y se cierra", () =>
        {
            var window = new MetroCarpinteria.App.MainWindow();
            var main = (MainViewModel)window.DataContext!;

            Assert.False(main.AreShortcutsVisible, "arranca cerrada.");
            Assert.True(main.Shortcuts.Count > 0, "tendría que listar algún atajo.");

            main.ToggleShortcutsCommand.Execute(null);
            Assert.True(main.AreShortcutsVisible, "Ctrl+/ tendría que abrirla.");

            main.CloseOverlaysCommand.Execute(null);
            Assert.False(main.AreShortcutsVisible, "Esc tendría que cerrarla.");
        });

        run("UI: Ctrl+N crea lo que corresponde según la sección", () =>
        {
            // El mismo atajo hace cosas distintas según dónde esté parado el usuario, que
            // es justamente lo que lo hace fácil de recordar.
            var window = new MetroCarpinteria.App.MainWindow();
            var main = (MainViewModel)window.DataContext!;

            main.NavigateCommand.Execute(NavigationSection.Clients);
            main.NewInSectionCommand.Execute(null);
            Assert.True(((ClientsViewModel)main.CurrentViewModel).IsFormOpen, "en Clientes abre el alta de cliente.");

            main.NavigateCommand.Execute(NavigationSection.Quotes);
            main.NewInSectionCommand.Execute(null);
            Assert.True(((QuotesViewModel)main.CurrentViewModel).IsFormOpen, "en Presupuestos abre el alta de presupuesto.");

            // En Inicio no hay nada que crear, y no tiene que romper nada.
            main.NavigateCommand.Execute(NavigationSection.Home);
            main.NewInSectionCommand.Execute(null);
        });

        run("UI: los primeros pasos se descartan y se pueden volver a ver", () =>
        {
            var home = new HomeViewModel();
            Assert.True(home.ShowOnboarding, "en una instalación nueva tendría que aparecer.");
            Assert.Equal(home.OnboardingSteps.Count, 4, "pasos de la guía");

            home.DismissOnboardingCommand.Execute(null);
            Assert.False(home.ShowOnboarding, "descartada, no vuelve.");

            // Ni en un arranque posterior.
            Assert.False(new HomeViewModel().ShowOnboarding, "el descarte tendría que quedar guardado.");

            // Pero se recupera desde Configuración: quien la salteó de apurado no tiene
            // otra forma de volver a verla.
            new SettingsViewModel().ReplayOnboardingCommand.Execute(null);
            Assert.True(new HomeViewModel().ShowOnboarding, "tendría que volver a aparecer.");
        });

        run("UI: las preferencias del presupuesto se guardan desde la pantalla", () =>
        {
            // Hasta acá solo se cambiaban editando settings.json a mano.
            var settings = new SettingsViewModel
            {
                QuoteValidityDays = "30",
                DefaultDailyRate = "45000"
            };

            settings.SaveSettingsCommand.Execute(null);

            Assert.Equal(AppHost.Settings.DefaultQuoteValidityDays, 30, "días de vigencia guardados");
            Assert.Equal(AppHost.Settings.DefaultDailyRate ?? 0m, 45000m, "jornal guardado");

            // Y un valor imposible se rechaza con un mensaje, no se guarda callado.
            settings.QuoteValidityDays = "mil";
            settings.SaveSettingsCommand.Execute(null);

            Assert.True(settings.IsStatusError, "un valor inválido tendría que avisar.");
            Assert.Equal(AppHost.Settings.DefaultQuoteValidityDays, 30, "no se pisa lo que estaba bien");
        });

        run("UI: los desplegables muestran la etiqueta, no el nombre del tipo", () =>
        {
            // Todos los ComboBox de la app usan DisplayMemberPath. Si el ControlTemplate
            // no reenvía el template del ítem seleccionado, la caja cerrada cae al
            // ToString() del objeto y muestra «MetroCarpinteria.App.Models.…».
            var combo = new System.Windows.Controls.ComboBox
            {
                ItemsSource = ProjectStatusHelper.GetEditOptions(),
                DisplayMemberPath = "Label",
                SelectedIndex = 0
            };

            var host = new System.Windows.Controls.Border { Child = combo, Width = 240, Height = 48 };
            host.Measure(new Size(240, 48));
            host.Arrange(new Rect(0, 0, 240, 48));
            host.UpdateLayout();

            var shown = FindTexts(combo).ToList();

            Assert.True(
                shown.Any(t => t == "Presupuesto"),
                $"la caja cerrada tendría que decir «Presupuesto», dice: {string.Join(" | ", shown)}");
        });

        ThemeTests.Run(run);
        ThemeTests.RunRepaintCheck(run);

        run("UI: las 10 vistas se dibujan en los 6 combos de tema y escala", () =>
        {
            // Es la red que protege el sistema de temas. Un color declarado en claro y
            // olvidado en oscuro, o un Height fijo que recorta con letra grande, no falla
            // al compilar: aparece como un panel vacío recién cuando alguien cambia el tema.
            var theme = AppHost.ThemeService;
            var originalTheme = theme.Theme;
            var originalScale = theme.Scale;

            Func<FrameworkElement>[] views =
            [
                () => new HomeView(),
                () => new InventoryView(),
                () => new CashRegisterView(),
                () => new QuotesView(),
                () => new ProjectsView(),
                () => new StaffView(),
                () => new ReportsView(),
                () => new SettingsView(),
                () => new AboutView()
            ];

            try
            {
                foreach (var mode in new[] { AppTheme.Light, AppTheme.Dark })
                {
                    foreach (var scale in new[] { FontScale.Small, FontScale.Normal, FontScale.Large })
                    {
                        theme.Apply(mode, scale, persist: false);

                        // La ventana no reporta tamaño sin Show(), que necesitaría sesión
                        // interactiva y no la hay en CI. Alcanza con que dibujar el shell
                        // completo no tire: ahí es donde saltaría una clave de tema faltante.
                        var window = new MetroCarpinteria.App.MainWindow();
                        window.Measure(new Size(1280, 800));
                        window.Arrange(new Rect(0, 0, 1280, 800));
                        window.UpdateLayout();

                        foreach (var create in views)
                        {
                            var view = create();
                            view.DataContext = ((MainViewModel)window.DataContext!).CurrentViewModel;
                            view.Measure(new Size(1000, 700));
                            view.Arrange(new Rect(0, 0, 1000, 700));
                            view.UpdateLayout();

                            Assert.True(
                                view.ActualHeight > 0,
                                $"{view.GetType().Name} no midió nada en {mode}/{scale}.");
                        }
                    }
                }
            }
            finally
            {
                theme.Apply(originalTheme, originalScale, persist: false);
            }
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
            AssertNoInternalNumbers(text);

            foreach (var expected in new[] { "PRESUPUESTO", "Cliente de prueba", "Mesa de prueba", "TOTAL" })
            {
                if (!text.Contains(expected, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Faltaba «{expected}» en el documento del cliente.");
                }
            }
        });

        run("PDF: con descuento e IVA, el cliente ve el pie comercial y ni una cifra interna", () =>
        {
            var commercial = BuildSampleQuote(new CommercialTerms
            {
                DiscountMode = DiscountMode.Percentage,
                DiscountValue = 15m,
                VatPercent = 21m
            });

            var text = ToText(service.BuildClientQuote(commercial, includeMaterialDetail: true));

            foreach (var expected in new[] { "Subtotal", "Descuento", "Neto gravado", "IVA", "TOTAL" })
            {
                if (!text.Contains(expected, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Faltaba «{expected}» en el pie comercial.");
                }
            }

            // Y el margen efectivo es justamente lo que no puede salir del taller.
            AssertNoInternalNumbers(text);
        });

        run("PDF: sin condiciones pactadas no aparece un pie comercial vacío", () =>
        {
            // Un «Descuento $ 0,00» o un subtotal repetido solo agregan ruido al papel.
            var text = ToText(service.BuildClientQuote(quote, includeMaterialDetail: false));

            if (text.Contains("Neto gravado", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Descuento", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Sin nada pactado no debía imprimirse el bloque de condiciones comerciales.");
            }
        });

        run("PDF: con una seña, el cliente ve cuánto le queda por pagar", () =>
        {
            var withDeposit = BuildSampleQuote(payments:
            [
                new ProjectPaymentItem
                {
                    Id = 1,
                    Kind = PaymentKind.Deposit,
                    Amount = 100000m,
                    Method = PaymentMethod.Cash,
                    CreatedAtLocal = DateTime.Today
                }
            ]);

            var text = ToText(service.BuildClientQuote(withDeposit, includeMaterialDetail: false));

            foreach (var expected in new[] { "Entregado a cuenta", "SALDO A PAGAR" })
            {
                if (!text.Contains(expected, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Faltaba «{expected}» en el documento con seña.");
                }
            }

            AssertNoInternalNumbers(text);
        });

        run("PDF: un presupuesto normal entra en una sola hoja A4", () =>
        {
            // La hoja de costos completa se pasaba de A4 por seis píxeles, y esos seis
            // píxeles costaban una hoja entera impresa con nada más que el pie de página.
            // Solo se ve imprimiendo, así que se mide acá.
            var full = BuildSampleQuote(
                new CommercialTerms
                {
                    DiscountMode = DiscountMode.Percentage,
                    DiscountValue = 15m,
                    VatPercent = 21m
                },
                payments:
                [
                    new ProjectPaymentItem
                    {
                        Id = 1,
                        Kind = PaymentKind.Deposit,
                        Amount = 100000m,
                        Method = PaymentMethod.Cash,
                        CreatedAtLocal = DateTime.Today
                    }
                ]);

            (FlowDocument Document, string Name)[] documents =
            [
                (service.BuildClientQuote(full, includeMaterialDetail: true), "presupuesto del cliente"),
                (service.BuildCostSheet(full), "hoja de costos")
            ];

            foreach (var (document, name) in documents)
            {
                var pages = CountA4Pages(document);

                Assert.Equal(pages, 1, $"el {name} con todo cargado tendría que entrar en una hoja");
            }
        });

        run("PDF: la hoja de costos muestra el margen efectivo", () =>
        {
            // Un descuento del 15% sobre este presupuesto deja el margen en negativo, y eso
            // hay que verlo antes de firmar.
            var discounted = BuildSampleQuote(new CommercialTerms
            {
                DiscountMode = DiscountMode.Percentage,
                DiscountValue = 15m
            });

            var text = ToText(service.BuildCostSheet(discounted));

            if (!text.Contains("Margen efectivo", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("La hoja de costos debía traer el margen efectivo.");
            }

            if (!text.Contains("a pérdida", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Con la ganancia comida por el descuento, la hoja tenía que avisarlo.");
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

    /// <summary>
    /// Lo que el papel del cliente no puede contener bajo ninguna circunstancia. La lista
    /// crece con cada concepto interno que se agrega al cálculo: enseñarle el margen al
    /// cliente es un problema comercial que no se arregla después.
    /// </summary>
    private static void AssertNoInternalNumbers(string text)
    {
        string[] forbidden =
        [
            "Ganancia", "Desperdicio", "Desgaste", "Gastos adicionales",
            "Margen efectivo", "a pérdida", "30%", "16%"
        ];

        foreach (var word in forbidden)
        {
            if (text.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"El documento del cliente no debía contener «{word}».");
            }
        }
    }

    private static QuoteDetail BuildSampleQuote(
        CommercialTerms? terms = null,
        IReadOnlyList<ProjectPaymentItem>? payments = null)
    {
        var breakdown = BudgetCalculatorService.Calculate(new BudgetInput
        {
            MaterialsCost = 100000m,
            Days = 3m,
            DailyRate = 30000m,
            Rates = BudgetRates.Defaults()
        });

        terms ??= CommercialTerms.None();
        var commercial = CommercialTermsService.Apply(breakdown.FinalPrice, terms);

        return new QuoteDetail
        {
            Id = 42,
            Title = "Mesa de prueba",
            ClientName = "Cliente de prueba",
            Description = "Roble macizo",
            Status = ProjectStatus.Quote,
            Budget = commercial.Total,
            Terms = terms,
            Commercial = commercial,
            Payments = payments ?? [],
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

    private static System.Windows.Controls.Button? FindButton(
        System.Windows.DependencyObject root,
        string content)
    {
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);

        for (var i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);

            if (child is System.Windows.Controls.Button button
                && button.Content is string text
                && text == content)
            {
                return button;
            }

            if (FindButton(child, content) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>Textos visibles dentro de un control ya dibujado.</summary>
    private static IEnumerable<string> FindTexts(System.Windows.DependencyObject root)
    {
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);

        for (var i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);

            if (child is System.Windows.Controls.TextBlock { Text.Length: > 0 } text)
            {
                yield return text.Text;
            }

            foreach (var nested in FindTexts(child))
            {
                yield return nested;
            }
        }
    }

    /// <summary>En cuántas hojas A4 sale el documento. A4 a 96 ppp: 794 × 1123.</summary>
    private static int CountA4Pages(FlowDocument document)
    {
        QuoteDocumentService.LayOut(document, 794, 1123);

        var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator;
        paginator.ComputePageCount();
        return paginator.PageCount;
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
