using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
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

        run("UI: rechazar con un filtro puesto avisa por el presupuesto que se rechazó", () =>
        {
            // Con cualquier filtro que no sea «Rechazados», el que se acaba de rechazar sale
            // de la lista y la selección cae en otro. El aviso se armaba después de recargar,
            // así que nombraba a ese otro: parecía que se había rechazado el que no era.
            var victimId = AppHost.QuoteService.CreateQuote("Ropero de dos puertas", "Cliente que dijo que no", null).Id;
            AppHost.QuoteService.CreateQuote("Mesa que sigue viva", "Otro cliente", null);

            var viewModel = new QuotesViewModel(() => { });
            viewModel.Load();

            // «Vigentes» es el que usa el taller. Los rechazados no entran, que es lo que
            // hace caer la selección en otro renglón.
            viewModel.SelectedFilter = viewModel.FilterOptions.First(o => o.Filter == QuoteFilter.Current);
            viewModel.SelectedQuote = viewModel.Quotes.First(q => q.Id == victimId);

            AppHost.NotificationService.Clear();
            AppHost.DialogService.HasHost = true;

            try
            {
                viewModel.RejectCommand.Execute(null);

                // Sin capa visual real, la confirmación se responde acá.
                Assert.NotNull(AppHost.DialogService.Current, "tendría que haber pedido confirmación");
                AppHost.DialogService.Complete(true);

                // El await vuelve por el Dispatcher: hay que bombear para que corra.
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            }
            finally
            {
                AppHost.DialogService.HasHost = false;
            }

            Assert.Equal(
                AppHost.QuoteService.GetDetail(victimId)!.Status,
                ProjectStatus.Rejected,
                "el presupuesto elegido tendría que haber quedado rechazado");

            var message = AppHost.NotificationService.Items.LastOrDefault()?.Message ?? string.Empty;

            Assert.True(
                message.Contains("Ropero de dos puertas", StringComparison.Ordinal),
                $"el aviso tendría que nombrar al que se rechazó: «{message}»");
            Assert.False(
                message.Contains("Mesa que sigue viva", StringComparison.Ordinal),
                $"el aviso nombró a otro presupuesto: «{message}»");
        });

        run("UI: con el buscador escrito, el presupuesto adjunto recién creado queda a la vista", () =>
        {
            // Guardar y duplicar ya se aseguraban de esto; crear un adjunto se quedó sin el
            // respaldo. Con algo escrito en el buscador el nuevo no entra en la lista, y la
            // pantalla se vaciaba justo después de decir «cargá materiales y precio».
            var parentId = AppHost.QuoteService.CreateQuote("Cocina completa", "Cliente de dos trabajos", null).Id;

            var viewModel = new QuotesViewModel(() => { });
            viewModel.Load();

            viewModel.SearchText = "Cocina completa";
            viewModel.SelectedQuote = viewModel.Quotes.First(q => q.Id == parentId);

            viewModel.OpenSiblingFormCommand.Execute(null);
            viewModel.SiblingTitle = "Mesada de granito";
            viewModel.CreateSiblingQuoteCommand.Execute(null);

            Assert.NotNull(viewModel.Detail, "el adjunto recién creado tendría que quedar abierto.");
            Assert.Equal(viewModel.Detail!.Title, "Mesada de granito", "presupuesto abierto tras crear el adjunto");
            Assert.True(
                viewModel.Quotes.Any(q => q.Id == viewModel.Detail.Id),
                "y tendría que verse en la lista, no solo estar seleccionado.");
        });

        run("UI: con un filtro de estado puesto, el proyecto recién anotado queda a la vista", () =>
        {
            // El alta recargaba conservando la selección anterior, así que el trabajo recién
            // cargado no quedaba abierto — y con un filtro puesto ni siquiera aparecía.
            var viewModel = new ProjectsViewModel(() => { });
            viewModel.Load();

            // Un alta entra como «Presupuesto», así que este filtro la deja fuera.
            viewModel.SelectedStatusFilter = viewModel.StatusFilterOptions
                .First(o => o.Status == ProjectStatus.Completed);

            viewModel.NewProjectCommand.Execute(null);
            viewModel.FormTitle = "Banco de taller";
            viewModel.FormClientName = "Cliente del banco";
            viewModel.SaveProjectCommand.Execute(null);

            Assert.NotNull(viewModel.SelectedProject, "el proyecto recién creado tendría que quedar seleccionado.");
            Assert.Equal(viewModel.SelectedProject!.Title, "Banco de taller", "proyecto abierto tras el alta");
        });

        run("UI: sobre un aprobado, el tilde de sumar los adjuntos no puede quedar mintiendo", () =>
        {
            // El checkbox se colgaba de CanManageAttachments, que solo mira si está
            // archivado, pero guardar exige que el presupuesto sea editable. Sobre un
            // aprobado tiraba error, el setter salía sin avisar, y el tilde quedaba marcado
            // en pantalla mientras la base decía que no: el PDF salía con el total viejo.
            var parentId = NewPricedQuote("Placard con anexo", "Cliente con anexos");
            var childId = NewPricedQuote("Zócalos del placard", "Cliente con anexos");

            AppHost.QuoteService.AttachQuote(parentId, childId);

            var viewModel = new QuotesViewModel(() => { });
            viewModel.Load();
            viewModel.SelectedQuote = viewModel.Quotes.First(q => q.Id == parentId);

            Assert.True(viewModel.CanIncludeAttachmentsInTotal, "sin aprobar tendría que poder tocarse.");

            AppHost.QuoteService.ApproveQuote(parentId);
            viewModel.Load();
            viewModel.ShowApproved = true;
            viewModel.SelectedQuote = viewModel.Quotes.First(q => q.Id == parentId);

            Assert.False(
                viewModel.CanIncludeAttachmentsInTotal,
                "aprobado, el tilde no tendría que poder tocarse.");

            // Y si igual llega una escritura, el valor que se muestra sigue siendo el real.
            viewModel.IncludeAttachmentsInTotal = true;

            Assert.False(
                viewModel.IncludeAttachmentsInTotal,
                "el tilde no puede quedar marcado si no se guardó.");
            Assert.False(
                AppHost.QuoteService.GetDetail(parentId)!.IncludeAttachmentsInTotal,
                "y en la base tampoco tendría que haberse guardado.");

            // Adjuntar sigue andando sobre un aprobado: eso no se toca.
            Assert.True(viewModel.CanManageAttachments, "adjuntar tendría que seguir disponible.");
        });

        run("UI: con «solo bajo stock» puesto, el producto recién creado queda a la vista", () =>
        {
            // Acá el filtro muerde más que en otras pantallas: un producto nuevo cargado con
            // existencias no entra en «solo bajo stock», así que se acababa de crear y no
            // aparecía por ningún lado.
            var viewModel = new InventoryViewModel(() => { });
            viewModel.LoadProducts();
            viewModel.LowStockOnly = true;

            viewModel.NewProductCommand.Execute(null);
            viewModel.FormName = "Bisagra recién cargada";
            viewModel.FormInitialStock = "50";
            viewModel.FormMinimumStock = "5";
            viewModel.SaveProductCommand.Execute(null);

            Assert.NotNull(viewModel.SelectedProduct, "el producto recién creado tendría que quedar seleccionado.");
            Assert.Equal(viewModel.SelectedProduct!.Name, "Bisagra recién cargada", "producto abierto tras el alta");
        });

        run("UI: dar de alta un empleado con el buscador escrito lo deja a la vista", () =>
        {
            var viewModel = new StaffViewModel(() => { });
            viewModel.Load();
            viewModel.SearchText = "zzz-no-existe";

            viewModel.NewEmployeeCommand.Execute(null);
            viewModel.FormFullName = "Ramón Carpintero";
            viewModel.SaveEmployeeCommand.Execute(null);

            Assert.NotNull(viewModel.SelectedEmployee, "el empleado recién creado tendría que quedar seleccionado.");
            Assert.Equal(viewModel.SelectedEmployee!.FullName, "Ramón Carpintero", "empleado abierto tras el alta");
        });

        run("UI: quitar personal pide lo mismo que quitar material", () =>
        {
            // Uno se negaba sobre un proyecto archivado y el otro no, sin ninguna razón.
            var viewModel = new ProjectsViewModel(() => { });
            viewModel.Load();
            viewModel.ShowArchived = true;

            var projectId = AppHost.ProjectService.Create(
                "Trabajo archivado", "Cliente viejo", null, 1000m, ProjectStatus.Completed).Id;
            var employeeId = AppHost.EmployeeService.Create("Peón archivable", null, null).Id;

            AppHost.ProjectService.AssignEmployee(projectId, employeeId, null);
            AppHost.ProjectService.Archive(projectId);

            var assignmentId = AppHost.ProjectService.GetProjectAssignments(projectId).Single().Id;

            Assert.Throws(
                () => AppHost.ProjectService.RemoveAssignment(assignmentId),
                "archivados");

            viewModel.Load();
            viewModel.SelectedProject = viewModel.Projects.First(p => p.Id == projectId);

            Assert.False(
                viewModel.RemoveAssignmentCommand.CanExecute(null),
                "sobre un archivado el botón tendría que estar apagado, igual que el de material.");
            Assert.False(
                viewModel.RemoveMaterialCommand.CanExecute(null),
                "y su hermano sigue apagado, que es de donde sale la regla.");
        });

        run("UI: Esc contesta el diálogo abierto y no toca el formulario de abajo", () =>
        {
            // El PreviewKeyDown de la ventana se queda con la tecla antes de que llegue al
            // botón IsCancel del diálogo, así que Esc no lo cerraba: se iba a cerrar el
            // formulario que había quedado abajo. En Proyectos, Inventario, Personal y
            // Clientes el formulario convive con la barra de acciones, o sea que se podía
            // tener los dos abiertos y perder lo tipeado con el diálogo todavía esperando.
            var window = new MetroCarpinteria.App.MainWindow();
            var main = (MainViewModel)window.DataContext!;

            main.SelectedNavItem = main.NavItems.First(i => i.Section == NavigationSection.Projects);
            var projects = (ProjectsViewModel)main.CurrentViewModel;

            projects.NewProjectCommand.Execute(null);
            projects.FormTitle = "Lo que estaba tipeando";
            Assert.True(projects.IsFormOpen, "la prueba necesita el formulario abierto.");

            AppHost.DialogService.HasHost = true;

            try
            {
                var pendiente = AppHost.DialogService.ConfirmAsync("Archivar proyecto", "¿Seguro?");
                Assert.False(pendiente.IsCompleted, "el diálogo tendría que estar esperando.");

                main.CloseOverlaysCommand.Execute(null);

                Assert.True(pendiente.IsCompleted, "Esc tendría que haber contestado el diálogo.");
                Assert.False(pendiente.Result, "y la respuesta segura es que no.");
                Assert.True(AppHost.DialogService.Current is null, "el diálogo tendría que haberse cerrado.");

                Assert.True(projects.IsFormOpen, "el formulario de abajo no se tiene que tocar.");
                Assert.Equal(projects.FormTitle, "Lo que estaba tipeando", "ni perder lo tipeado");
            }
            finally
            {
                AppHost.DialogService.HasHost = false;
            }

            // Sin diálogo, Esc sigue cerrando el formulario como promete la chuleta.
            main.CloseOverlaysCommand.Execute(null);
            Assert.False(projects.IsFormOpen, "sin diálogo, Esc tendría que cerrar el formulario.");
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

        run("UI: los botones del taller dependen del estado del trabajo", () =>
        {
            // Antes esto era un desplegable que ofrecía los cinco estados siempre, y de ahí
            // salía el salteo del ciclo. Ahora son botones, y cada uno aparece solo cuando
            // su paso corresponde.
            var viewModel = new ProjectsViewModel(() => { });
            viewModel.Load();
            viewModel.SelectedProject = viewModel.Projects.First(p => p.Id == fixture.ActiveProjectId);

            Assert.False(viewModel.CanStartWork, "un trabajo que ya está en taller no se vuelve a iniciar.");
            Assert.True(viewModel.CanMarkReady, "terminar el trabajo es el avance normal desde el taller.");
            Assert.False(viewModel.CanBackToWorkshop, "todavía no está listo, no hay a qué volver.");
            Assert.True(viewModel.CanCancelSelected, "el material sigue devolviéndose desde el taller.");

            // Y editar dejó de poder mover el estado: el selector solo sale en el alta.
            viewModel.EditProjectCommand.Execute(null);
            Assert.False(viewModel.IsCreating, "editar no es un alta.");
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
            Assert.Equal(viewModel.FormClientPhone, "3777-333444", "teléfono prellenado");
            Assert.False(viewModel.HasClientSuggestions, "elegida una, no quedan sugerencias.");
        });

        run("UI: guardar un presupuesto con un cliente nuevo le crea la ficha", () =>
        {
            var viewModel = new QuotesViewModel(() => { });
            viewModel.Load();

            viewModel.NewQuoteCommand.Execute(null);
            viewModel.FormTitle = "Alacena";
            viewModel.FormClientName = "Doña Rosa";
            viewModel.FormClientPhone = "3777-444555";
            viewModel.FormClientEmail = "rosa@ejemplo.com";
            viewModel.SaveQuoteCommand.Execute(null);

            var client = AppHost.ClientService.GetClients(search: "Doña Rosa").SingleOrDefault();
            Assert.NotNull(client, "la ficha tendría que haberse creado sola");

            // Y el presupuesto quedó enganchado a ella.
            Assert.Equal(viewModel.Detail!.ClientId ?? 0, client!.Id, "ficha vinculada al presupuesto");
            Assert.Equal(viewModel.Detail.ClientName, "Doña Rosa", "nombre impreso");
            Assert.Equal(client.Phone, "3777-444555", "teléfono guardado en la ficha");
            Assert.Equal(client.Email, "rosa@ejemplo.com", "email guardado en la ficha");

            viewModel.EditQuoteCommand.Execute(null);
            Assert.Equal(viewModel.FormClientPhone, "3777-444555", "teléfono al reeditar");
            Assert.Equal(viewModel.FormClientEmail, "rosa@ejemplo.com", "email al reeditar");
        });

        run("UI: un segundo presupuesto sin teléfono no le borra el contacto a la ficha", () =>
        {
            AppHost.ClientService.Create("Don Pedro", "3777-777888", "pedro@ejemplo.com");

            var viewModel = new QuotesViewModel(() => { });
            viewModel.Load();
            viewModel.NewQuoteCommand.Execute(null);
            viewModel.FormTitle = "Estante";
            viewModel.FormClientName = "Don Pedro";
            viewModel.SaveQuoteCommand.Execute(null);

            var client = AppHost.ClientService.GetClients(search: "Don Pedro").Single();
            Assert.Equal(client.Phone, "3777-777888", "teléfono conservado");
            Assert.Equal(client.Email, "pedro@ejemplo.com", "email conservado");
        });

        run("UI: editar la cabecera le escribe al presupuesto que se abrió, no al que quedó seleccionado", () =>
        {
            // El que reportó el taller: se abría «Editar datos» sobre uno, se tocaba otra
            // fila de la lista —que cambia el detalle sin cerrar el formulario— y Guardar
            // terminaba renombrando al segundo con los datos del primero.
            var aId = AppHost.QuoteService.CreateQuote("Mesada de A", "Cliente A", null).Id;
            var bId = AppHost.QuoteService.CreateQuote("Placard de B", "Cliente B", null).Id;

            var viewModel = new QuotesViewModel(() => { });
            viewModel.Load();

            viewModel.SelectedQuote = viewModel.Quotes.First(q => q.Id == aId);
            viewModel.EditQuoteCommand.Execute(null);
            viewModel.FormTitle = "Mesada corregida";

            // La lista cambia el presupuesto abierto por debajo del formulario.
            viewModel.SelectedQuote = viewModel.Quotes.First(q => q.Id == bId);
            viewModel.SaveQuoteCommand.Execute(null);

            Assert.Equal(
                AppHost.QuoteService.GetDetail(aId)!.Title, "Mesada corregida", "el que se abrió sí cambia");
            Assert.Equal(
                AppHost.QuoteService.GetDetail(bId)!.Title, "Placard de B", "el otro queda intacto");
        });

        run("UI: con el formulario de cabecera abierto no se toca el presupuesto de abajo", () =>
        {
            // Los pasos 1/2/3 graban solos al salir del foco y siempre contra el
            // presupuesto abierto: mientras se da de alta otro, tienen que estar apagados.
            var viewModel = new QuotesViewModel(() => { });
            viewModel.Load();
            viewModel.SelectedQuote = viewModel.Quotes.First(q => q.Id == fixture.QuoteId);

            Assert.True(viewModel.ShowQuoteBody, "con un presupuesto elegido, los pasos se ven.");
            Assert.True(viewModel.CanEditSelected, "y se pueden tocar.");

            viewModel.NewQuoteCommand.Execute(null);

            Assert.False(viewModel.ShowQuoteBody, "con el alta abierta, los pasos se esconden.");
            Assert.False(viewModel.CanEditSelected, "y dejan de aceptar cambios.");
            Assert.False(viewModel.EditQuoteCommand.CanExecute(null), "«Editar datos» no se puede apretar.");
            Assert.False(viewModel.DuplicateCommand.CanExecute(null), "«Duplicar» tampoco.");

            // Cancelar devuelve el presupuesto anterior intacto.
            viewModel.CancelFormCommand.Execute(null);

            Assert.True(viewModel.ShowQuoteBody, "al cancelar vuelven los pasos.");
            Assert.Equal(viewModel.Detail!.Id, fixture.QuoteId, "y sigue siendo el mismo presupuesto.");
        });

        run("UI: la lista queda deshabilitada mientras el formulario de cabecera está abierto", () =>
        {
            // Los bindings nuevos se verifican sobre el control y no por lo que se ve: en
            // WPF una ruta mal escrita deja la propiedad en su valor por omisión —la lista
            // habilitada— sin romper nada ni avisar.
            var viewModel = new QuotesViewModel(() => { });
            viewModel.Load();
            viewModel.SelectedQuote = viewModel.Quotes.First(q => q.Id == fixture.QuoteId);

            var view = BuildView(() => new QuotesView(), viewModel);
            var grid = FindVisual<System.Windows.Controls.DataGrid>(view);
            Assert.NotNull(grid, "la grilla de presupuestos tendría que estar en la vista");
            Assert.True(grid!.IsEnabled, "sin formulario abierto la lista se toca normalmente.");

            viewModel.NewQuoteCommand.Execute(null);
            view.UpdateLayout();

            Assert.False(grid.IsEnabled, "con el alta abierta la lista no se puede tocar.");

            viewModel.CancelFormCommand.Execute(null);
            view.UpdateLayout();

            Assert.True(grid.IsEnabled, "al cancelar la lista vuelve a habilitarse.");
        });

        run("UI: el presupuesto recién creado aparece aunque haya algo en el buscador", () =>
        {
            // Con un filtro puesto, el nuevo no entraba en la lista y la pantalla quedaba
            // en «Ningún presupuesto seleccionado»: se acababa de crear y parecía perdido.
            var viewModel = new QuotesViewModel(() => { });
            viewModel.Load();
            viewModel.SearchText = "zzz-no-existe-nada-con-esto";

            viewModel.NewQuoteCommand.Execute(null);
            viewModel.FormTitle = "Biblioteca";
            viewModel.FormClientName = "Cliente escondido";
            viewModel.SaveQuoteCommand.Execute(null);

            Assert.NotNull(viewModel.Detail, "el recién creado tendría que quedar abierto");
            Assert.Equal(viewModel.Detail!.Title, "Biblioteca", "título del que quedó abierto");
            Assert.Equal(viewModel.SearchText, string.Empty, "el buscador se limpia para que entre en la lista");
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

        run("UI: fijar el precio sin marcar líneas no toca el desglose", () =>
        {
            var viewModel = new QuotesViewModel(() => { });
            viewModel.Load();
            viewModel.SelectedQuote = viewModel.Quotes.First(q => q.Id == fixture.QuoteId);

            var profitBefore = viewModel.Detail!.UnadjustedBreakdown!.Profit;
            viewModel.AdjustedPrice = "190000";
            viewModel.ApplyAdjustedPriceCommand.Execute(null);

            Assert.Equal(viewModel.Detail!.Budget ?? 0m, 190000m, "precio fijado");
            Assert.Equal(viewModel.Detail.Breakdown!.Profit, profitBefore, "ganancia intacta");
            Assert.Equal(viewModel.Detail.PriceAdjustmentTargets.Count, 0, "sin marcas");

            viewModel.RestoreCalculatedPriceCommand.Execute(null);
        });

        run("UI: marcar ganancia resta de ahí al fijar un precio menor", () =>
        {
            var viewModel = new QuotesViewModel(() => { });
            viewModel.Load();
            viewModel.SelectedQuote = viewModel.Quotes.First(q => q.Id == fixture.QuoteId);

            var profitLine = viewModel.BreakdownLines.First(l => l.Kind == BudgetLineKind.Profit);
            var profitBefore = profitLine.OriginalAmount;

            profitLine.IsSelected = true;
            viewModel.AdjustedPrice = "190000";
            viewModel.ApplyAdjustedPriceCommand.Execute(null);

            Assert.True(viewModel.Detail!.PriceAdjustmentTargets.Contains(BudgetLineKind.Profit), "quedó ganancia marcada");
            Assert.True(viewModel.Detail.Breakdown!.Profit < profitBefore, "la ganancia tenía que bajar");
            Assert.True(
                viewModel.BreakdownLines.First(l => l.Kind == BudgetLineKind.Profit).IsSelected,
                "el tilde se restaura al reabrir");

            viewModel.RestoreCalculatedPriceCommand.Execute(null);
            Assert.Equal(
                viewModel.Detail!.Breakdown!.Profit,
                profitBefore,
                "volver al calculado restaura la ganancia");
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

        run("UI: con el modo privado puesto, Caja no deja ningún importe a la vista", () =>
        {
            // El modo cambia cada importe por puntos con un converter, así que la forma de
            // que falle es olvidarse de ponerlo en un binding: ese importe queda a la
            // vista, la app no se rompe, y nadie se entera hasta que hay alguien mirando
            // por encima del hombro. Este test dibuja las dos pantallas de Caja con el
            // modo puesto y busca plata sin tapar.
            var wasOn = MetroCarpinteria.App.Controls.Ui.IsPrivacyOn;

            try
            {
                MetroCarpinteria.App.Controls.Ui.SetPrivacy(true);

                // Inicio entra porque su tarjeta de Caja es el mismo saldo: taparlo en
                // Caja y dejarlo en la pantalla que se abre primero no taparía nada.
                (Func<FrameworkElement> View, Func<object> Model)[] pairs =
                [
                    (() => new CashRegisterView(), () => new CashRegisterViewModel(() => { })),
                    (() => new SettlementsView(), () => new SettlementsViewModel(() => { })),
                    (() => new HomeView(), () => new HomeViewModel())
                ];

                var expuestos = new List<string>();

                foreach (var (createView, createModel) in pairs)
                {
                    var view = BuildView(createView, createModel());
                    var name = view.GetType().Name;

                    foreach (var text in FindAllVisual<System.Windows.Controls.TextBlock>(view))
                    {
                        if (!PrivacyMaskConverter.HasMoney(text.Text) || !IsOnScreen(text))
                        {
                            continue;
                        }

                        expuestos.Add($"{name}: «{text.Text.Trim()}»");
                    }
                }

                Assert.True(
                    expuestos.Count == 0,
                    "Con el modo privado puesto quedaron importes a la vista; al binding le " +
                    "falta el PrivacyMaskConverter:\n  " +
                    string.Join("\n  ", expuestos.Distinct().Take(10)));
            }
            finally
            {
                MetroCarpinteria.App.Controls.Ui.SetPrivacy(wasOn);
            }
        });

        run("UI: el modo privado tapa la plata y deja el resto de la pantalla", () =>
        {
            // Un modo que tapa todo no se usa: él abre Terminados justamente para mirar
            // qué trabajos cerró y de quién es cada uno. Lo que se va es la plata.
            var wasOn = MetroCarpinteria.App.Controls.Ui.IsPrivacyOn;

            try
            {
                MetroCarpinteria.App.Controls.Ui.SetPrivacy(true);

                var view = BuildView(() => new SettlementsView(), new SettlementsViewModel(() => { }));

                var textos = FindAllVisual<System.Windows.Controls.TextBlock>(view)
                    .Where(t => IsOnScreen(t) && !string.IsNullOrWhiteSpace(t.Text))
                    .Select(t => t.Text)
                    .ToList();

                Assert.True(
                    textos.Any(t => t.Contains("Trabajos terminados", StringComparison.Ordinal)),
                    "el título tiene que seguir estando: se tapa la plata, no la pantalla.");
                Assert.True(
                    textos.Any(t => t.Contains("A QUIÉN LE TOCA", StringComparison.Ordinal)
                        || t.Contains("Elegí a alguien", StringComparison.Ordinal)
                        || t.Contains("Todavía no hay trabajos", StringComparison.Ordinal)),
                    "y el resto de la pantalla también, para poder seguir mirando los trabajos.");
            }
            finally
            {
                MetroCarpinteria.App.Controls.Ui.SetPrivacy(wasOn);
            }
        });

        run("UI: el enmascarado no se lleva puesto un texto sin plata", () =>
        {
            // Es la regla que hace que el modo sirva: «Todo pagado» y «Sin operarios» no
            // son importes y tienen que pasar enteros. Si el converter tapara por las
            // dudas, la pantalla quedaría llena de puntos y él la apagaría.
            Assert.True(PrivacyMaskConverter.HasMoney("$ 25.000,00"), "un importe pelado es plata");
            Assert.True(PrivacyMaskConverter.HasMoney("Falta pagar $ 25.000,00 de mano de obra"), "adentro de una frase también");
            Assert.True(PrivacyMaskConverter.HasMoney("Cobró $ 5.000 de $ 20.000"), "con dos importes también");

            Assert.False(PrivacyMaskConverter.HasMoney("Todo pagado"), "no hay plata acá");
            Assert.False(PrivacyMaskConverter.HasMoney("Sin operarios"), "ni acá");
            Assert.False(PrivacyMaskConverter.HasMoney("Cobró una parte"), "ni acá");
            Assert.False(PrivacyMaskConverter.HasMoney("Le debés plata a 2 personas por 2 trabajos"), "hablar de plata no es mostrarla");
            Assert.False(PrivacyMaskConverter.HasMoney(""), "vacío no es plata");
            Assert.False(PrivacyMaskConverter.HasMoney(null), "null tampoco");
        });

        run("UI: ninguna pantalla queda invisible por olvidar el trigger que la muestra", () =>
        {
            // Las pantallas arrancan con el root en Opacity 0 y una animación de entrada
            // las hace aparecer. Si al editar una vista se pierde ese trigger, la pantalla
            // se dibuja entera y no se ve NADA: no falla al compilar, no tira excepción, no
            // sale en el log, y los tests que solo dibujan la vista pasan igual. Pasó al
            // reescribir Caja, y la única forma de notarlo fue abrir la app.
            Func<FrameworkElement>[] views =
            [
                () => new HomeView(),
                () => new InventoryView(),
                () => new CashRegisterView(),
                () => new QuotesView(),
                () => new ProjectsView(),
                () => new SettlementsView(),
                () => new StaffView(),
                () => new ReportsView(),
                () => new SettingsView(),
                () => new AboutView(),
                () => new ClientsView()
            ];

            foreach (var create in views)
            {
                var view = create();
                var name = view.GetType().Name;

                if (view.FindName("PageRoot") is not UIElement root || root.Opacity > 0d)
                {
                    continue;
                }

                var animatesOpacity = view.Triggers
                    .OfType<EventTrigger>()
                    .Where(t => t.RoutedEvent == FrameworkElement.LoadedEvent)
                    .SelectMany(t => t.Actions.OfType<BeginStoryboard>())
                    .Where(b => b.Storyboard is not null)
                    .SelectMany(b => b.Storyboard.Children)
                    .Any(a => Storyboard.GetTargetProperty(a)?.Path == "Opacity");

                Assert.True(
                    animatesOpacity,
                    $"{name} arranca invisible y no tiene el trigger que la muestra: la pantalla " +
                    "va a quedar en blanco.");
            }
        });

        run("UI: una lista vacía por el filtro no dice que no hay nada cargado", () =>
        {
            // El taller trabaja con los filtros puestos, y quedan puestos de un día para el
            // otro. Con «Solo alertas» tildado y nada bajo el mínimo, Inventario decía
            // «Todavía no hay productos. Cargá el primero»: o sea, le avisaba que se le
            // borró el inventario. Una lista vacía por un filtro no es una lista vacía.
            var inventory = new InventoryViewModel(() => { });

            Assert.True(
                inventory.EmptyTitle.Contains("Todavía no hay", StringComparison.Ordinal),
                $"sin filtros tendría que hablar de cargar el primero, y dice «{inventory.EmptyTitle}».");

            inventory.LowStockOnly = true;

            Assert.True(inventory.IsFiltered, "«Solo alertas» recorta la lista.");
            Assert.True(
                inventory.EmptyTitle.Contains("filtro", StringComparison.Ordinal),
                $"con el filtro puesto tendría que nombrarlo, y dice «{inventory.EmptyTitle}».");
            Assert.True(
                inventory.EmptyMessage.Contains("Solo alertas", StringComparison.Ordinal),
                $"y decir cuál destildar, pero dice «{inventory.EmptyMessage}».");

            inventory.LowStockOnly = false;
            inventory.SearchText = "melamina que no existe";

            Assert.True(
                inventory.EmptyMessage.Contains("buscador", StringComparison.Ordinal),
                $"buscando tendría que mandar a limpiar el buscador, y dice «{inventory.EmptyMessage}».");
        });

        run("UI: una lista con ItemTemplate dibuja su contenido, no un renglón vacío", () =>
        {
            // La tercera forma en que esto falla en silencio, y la que ninguna de las otras
            // dos barandas caza: el estilo de ListViewItem reemplazaba la plantilla por un
            // GridViewRowPresenter, que solo sabe dibujar columnas de un GridView. Una
            // lista con ItemTemplate quedaba con sus renglones y su línea divisoria, y ni
            // una letra adentro. No hay binding roto que rastrear —el binding está bien—,
            // la pantalla no está invisible, y el log no dice nada. Le pasó al historial de
            // Inventario, y se descubrió abriendo la app.
            var list = new System.Windows.Controls.ListView
            {
                ItemsSource = new[] { "Entrada de prueba", "Salida de prueba" },
                Width = 300,
                Height = 120
            };

            var template = new DataTemplate();
            var block = new FrameworkElementFactory(typeof(System.Windows.Controls.TextBlock));
            block.SetBinding(System.Windows.Controls.TextBlock.TextProperty, new System.Windows.Data.Binding("."));
            template.VisualTree = block;
            list.ItemTemplate = template;

            var host = new System.Windows.Controls.Border { Child = list, Width = 300, Height = 120 };
            host.Measure(new Size(300, 120));
            host.Arrange(new Rect(0, 0, 300, 120));
            host.UpdateLayout();

            var shown = FindTexts(list).ToList();

            Assert.True(
                shown.Any(t => t == "Entrada de prueba"),
                "una lista con ItemTemplate tendría que mostrar su contenido; se dibujó: " +
                $"{(shown.Count == 0 ? "NADA" : string.Join(" | ", shown))}");
        });

        run("UI: ninguna pantalla tiene bindings rotos", () =>
        {
            // La otra forma en que WPF falla en silencio, y la más peligrosa: un binding a
            // una propiedad que no existe no rompe nada. La propiedad se queda en su valor
            // por omisión, la pantalla se dibuja igual, y el número o el texto simplemente
            // no aparecen. Al renombrar o borrar una propiedad del ViewModel, el compilador
            // no ayuda: el XAML la nombra por texto.
            //
            // WPF sí lo reporta, pero por un canal de trazas que nadie mira. Esto lo
            // escucha y lo convierte en una prueba que falla.
            var errors = new List<string>();
            var listener = new BindingErrorListener(errors);
            var previousLevel = PresentationTraceSources.DataBindingSource.Switch.Level;

            PresentationTraceSources.Refresh();
            PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;

            try
            {
                // Cada vista con SU ViewModel: emparejarlas mal generaría errores de
                // binding legítimos y la prueba no distinguiría los de verdad.
                (Func<FrameworkElement> View, Func<object> Model)[] pairs =
                [
                    (() => new HomeView(), () => new HomeViewModel()),
                    (() => new InventoryView(), () => new InventoryViewModel(() => { })),
                    (() => new CashRegisterView(), () => new CashRegisterViewModel(() => { })),
                    (() => new QuotesView(), () => new QuotesViewModel(() => { })),
                    (() => new ProjectsView(), () => new ProjectsViewModel(() => { })),
                    (() => new SettlementsView(), () => new SettlementsViewModel(() => { })),
                    (() => new ClientsView(), () => new ClientsViewModel(() => { })),
                    (() => new StaffView(), () => new StaffViewModel(() => { })),
                    (() => new ReportsView(), () => new ReportsViewModel()),
                    (() => new SettingsView(), () => new SettingsViewModel()),
                    (() => new AboutView(), () => new AboutViewModel())
                ];

                foreach (var (createView, createModel) in pairs)
                {
                    LoadView(createView, createModel());
                }
            }
            finally
            {
                PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
                PresentationTraceSources.DataBindingSource.Switch.Level = previousLevel;
            }

            Assert.True(
                errors.Count == 0,
                $"Hay {errors.Count} binding(s) rotos; la pantalla los muestra vacíos sin avisar:\n  " +
                string.Join("\n  ", errors.Distinct().Take(10)));
        });

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
                () => new SettlementsView(),
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
            var text = ToText(service.BuildClientQuote(quote));
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

            var text = ToText(service.BuildClientQuote(commercial));

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
            var text = ToText(service.BuildClientQuote(quote));

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

            var text = ToText(service.BuildClientQuote(withDeposit));

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
                (service.BuildClientQuote(full), "presupuesto del cliente"),
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
            var text = ToText(service.BuildClientQuote(quote));

            if (!text.Contains("Válido hasta", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("El presupuesto debía indicar hasta cuándo vale el precio.");
            }
        });

        run("PDF: el presupuesto del cliente no lleva número de referencia", () =>
        {
            var text = ToText(service.BuildClientQuote(quote));

            if (text.Contains("N.º", StringComparison.Ordinal) || text.Contains("N.o", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("El título no debía llevar el N.º interno.");
            }
        });

        run("PDF: el aviso de seña solo sale si está prendido", () =>
        {
            var plain = ToText(service.BuildClientQuote(quote));
            if (plain.Contains("Entregando", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Sin el tilde no debía imprimirse el aviso de seña.");
            }

            var withNote = BuildSampleQuote(showCommitment: true, commitmentAmount: 100000m);
            var text = ToText(service.BuildClientQuote(withNote));

            if (!text.Contains("Entregando", StringComparison.OrdinalIgnoreCase)
                || !text.Contains("100.000", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Con el aviso prendido tenía que salir el importe debajo del TOTAL.");
            }

            AssertNoInternalNumbers(text);
        });

        run("PDF: con el tilde apagado los adjuntos se listan y no cambian el TOTAL", () =>
        {
            var withAttachments = BuildSampleQuote(attachments: [SampleAttachment()]);
            var text = ToText(service.BuildClientQuote(withAttachments));

            // Con más de un trabajo la lista se numera, y el principal es el 1.
            foreach (var expected in new[]
                { "TRABAJOS", "Trabajo 1", "Trabajo 2", "Mesa de prueba", "Placard de dormitorio", "185.000" })
            {
                if (!text.Contains(expected, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Faltaba «{expected}» en la lista de trabajos.");
                }
            }

            if (!text.Contains("no está incluido", StringComparison.OrdinalIgnoreCase)
                && !text.Contains("no están incluidos", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Tenía que aclarar que el anexo no entra en este total.");
            }

            // El TOTAL del principal sigue siendo el de la mesa de prueba (287.000).
            if (!text.Contains("287.000", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("El TOTAL del principal no tenía que cambiar al adjuntar.");
            }

            if (text.Contains("472.000", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Con el tilde apagado el TOTAL no tenía que sumar el adjunto.");
            }

            AssertNoInternalNumbers(text);
        });

        run("PDF: con el tilde prendido el TOTAL suma los adjuntos", () =>
        {
            var summed = BuildSampleQuote(
                attachments: [SampleAttachment()], includeAttachmentsInTotal: true);

            var text = ToText(service.BuildClientQuote(summed));

            // 287.000 de la mesa más 185.000 del placard.
            if (!text.Contains("472.000", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("El TOTAL tenía que sumar el adjunto.");
            }

            // Y la aclaración de que no entra sería justo lo contrario de lo que muestra.
            if (text.Contains("no está incluido", StringComparison.OrdinalIgnoreCase)
                || text.Contains("no están incluidos", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Con el tilde prendido el papel no puede decir que no entra.");
            }

            AssertNoInternalNumbers(text);
        });

        run("PDF: con el tilde prendido el saldo resta también las señas de los adjuntos", () =>
        {
            var summed = BuildSampleQuote(
                payments:
                [
                    new ProjectPaymentItem
                    {
                        Id = 1,
                        Kind = PaymentKind.Deposit,
                        Amount = 87000m,
                        Method = PaymentMethod.Cash,
                        CreatedAtLocal = DateTime.Today
                    }
                ],
                attachments: [SampleAttachment(paidTotal: 85000m)],
                includeAttachmentsInTotal: true);

            var text = ToText(service.BuildClientQuote(summed));

            // 472.000 de total, menos 87.000 del principal y 85.000 del adjunto.
            if (!text.Contains("300.000", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Si el total suma los adjuntos, el saldo tiene que restar sus señas.");
            }
        });

        run("PDF: el presupuesto de siempre sale sin compactar", () =>
        {
            // Lo primero que tiene que garantizar el ajuste: el papel de una mesa sola no
            // cambia de aspecto. Sólo se aprieta lo que hace falta apretar.
            var document = service.BuildClientQuote(BuildSampleQuote());

            Assert.Equal(document.FontSize, 12d, "cuerpo del papel de siempre");
            Assert.Equal(CountA4Pages(document), 1, "hojas del presupuesto simple");
        });

        run("PDF: un presupuesto cargado se compacta solo para entrar en una hoja", () =>
        {
            var loaded = BuildSampleQuote(attachments: ManyAttachments(6));
            var document = service.BuildClientQuote(loaded);

            Assert.Equal(CountA4Pages(document), 1, "hojas del presupuesto cargado");
            Assert.True(
                document.FontSize < 12d,
                "con seis adjuntos y descripciones largas tendría que haberse apretado para entrar.");
        });

        run("PDF: las observaciones sobreviven a la compactación", () =>
        {
            // Es lo último que se recorta —de tres renglones a uno— pero nunca desaparece:
            // el presupuesto se termina de cerrar de palabra en el taller.
            var text = ToText(service.BuildClientQuote(BuildSampleQuote(attachments: ManyAttachments(6))));

            if (!text.Contains("Observaciones", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Apretado también tiene que quedar dónde escribir.");
            }
        });

        run("PDF: el cierre queda encadenado para no partirse entre hojas", () =>
        {
            // Encadenar sólo funciona con párrafos: si el TOTAL vuelve a ser una tabla,
            // corta la cadena y puede quedar solo al pie de una hoja con las observaciones
            // y la vigencia en la siguiente.
            var document = service.BuildClientQuote(BuildSampleQuote(payments:
            [
                new ProjectPaymentItem
                {
                    Id = 1,
                    Kind = PaymentKind.Deposit,
                    Amount = 100000m,
                    Method = PaymentMethod.Cash,
                    CreatedAtLocal = DateTime.Today
                }
            ]));

            // Total, entregado, saldo, observaciones, vigencia y pie.
            var closing = document.Blocks.ToList().TakeLast(6).ToList();

            foreach (var block in closing.Take(closing.Count - 1))
            {
                if (block is not System.Windows.Documents.Paragraph { KeepWithNext: true })
                {
                    throw new InvalidOperationException(
                        $"Un bloque del cierre no quedó atado al siguiente: {block.GetType().Name}.");
                }
            }
        });

        run("PDF: la vista previa dibuja una imagen por hoja, del tamaño de una A4", () =>
        {
            // Es el mismo render que usa el runner de documentos: lo que se ve en la vista
            // previa es lo que se verifica, no una maqueta aparte.
            var document = service.BuildClientQuote(BuildSampleQuote());
            var pages = QuoteDocumentService.RenderPages(document);

            Assert.Equal(pages.Count, CountA4Pages(document), "hojas dibujadas");
            Assert.Equal(pages[0].PixelWidth, 794, "ancho de la hoja");
            Assert.Equal(pages[0].PixelHeight, 1123, "alto de la hoja");
        });

        run("PDF: la ventana de vista previa se arma con las hojas montadas", () =>
        {
            // Sin abrirla: alcanza con que el XAML cargue y las hojas queden puestas. Lo
            // que se rompe en una ventana nueva es el enlace, no el dibujo.
            var document = service.BuildClientQuote(BuildSampleQuote());
            var window = new MetroCarpinteria.App.Views.QuotePreviewWindow(document, "Presupuesto 0042");

            Assert.True(window.PagesList.ItemsSource is not null, "las hojas tendrían que estar puestas.");
            Assert.False(window.Printed, "recién abierta todavía no imprimió nada.");
        });

        run("PDF: con un solo trabajo la lista no se numera", () =>
        {
            var text = ToText(service.BuildClientQuote(BuildSampleQuote()));

            if (!text.Contains("TRABAJO", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Faltaba el título de la lista de trabajos.");
            }

            if (text.Contains("Trabajo 1", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Numerar un único renglón es ruido.");
            }
        });

        run("PDF: la caja del cliente ya no repite el trabajo", () =>
        {
            // El título y la descripción bajaron a la lista de trabajos, donde cada uno va
            // con su precio al lado.
            var text = ToText(service.BuildClientQuote(BuildSampleQuote()));

            if (text.Contains("Trabajo:", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("La caja del cliente no tenía que repetir el trabajo.");
            }

            if (!text.Contains("Cliente:", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("La caja del cliente sí lleva el nombre.");
            }
        });

        run("PDF: el papel cierra con el TOTAL y un lugar para escribir a mano", () =>
        {
            var text = ToText(service.BuildClientQuote(BuildSampleQuote()));

            if (!text.Contains("Observaciones", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Faltaba el bloque de observaciones.");
            }

            // El TOTAL va al final, después de las condiciones: el cliente termina de leer
            // en el número.
            var total = text.LastIndexOf("TOTAL", StringComparison.Ordinal);
            var observations = text.IndexOf("Observaciones", StringComparison.OrdinalIgnoreCase);

            if (total < 0 || observations < 0 || total > observations)
            {
                throw new InvalidOperationException("Las observaciones tienen que ir debajo del TOTAL.");
            }
        });

        run("PDF: el recibo muestra el cobro y no el margen", () =>
        {
            var payment = new ProjectPaymentItem
            {
                Id = 1,
                Kind = PaymentKind.Deposit,
                Amount = 100000m,
                Method = PaymentMethod.Cash,
                CreatedAtLocal = DateTime.Today
            };

            var withDeposit = BuildSampleQuote(payments: [payment]);
            var text = ToText(service.BuildReceipt(withDeposit, payment));

            foreach (var expected in new[] { "RECIBO", "Seña", "RECIBIDO", "SALDO A PAGAR" })
            {
                if (!text.Contains(expected, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Faltaba «{expected}» en el recibo.");
                }
            }

            if (text.Contains("N.º", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("El recibo no debía llevar N.º.");
            }

            AssertNoInternalNumbers(text);
        });

        run("PDF: el papel del cliente ya no lleva firmas, pero sí la descripción", () =>
        {
            var text = ToText(service.BuildClientQuote(quote));

            foreach (var gone in new[] { "Firma del cliente", "Tabla de roble" })
            {
                if (text.Contains(gone, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"El documento del cliente no debía contener «{gone}».");
                }
            }

            // Sacado el desglose, esto es lo único que le dice al cliente qué está comprando.
            if (!text.Contains("Roble macizo", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Faltaba la descripción del trabajo.");
            }
        });

        run("PDF: la descripción respeta los renglones que escribió el jefe", () =>
        {
            var multiline = BuildSampleQuote(
                description: "Melamina blanca 18 mm.\nIncluye colocación y zócalo.\n\nEntrega en 3 semanas.");

            var text = ToText(service.BuildClientQuote(multiline));

            foreach (var line in new[] { "Melamina blanca 18 mm.", "Incluye colocación y zócalo.", "Entrega en 3 semanas." })
            {
                if (!text.Contains(line, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Faltaba el renglón «{line}» de la descripción.");
                }
            }

            // Si los saltos se aplastaran, los renglones quedarían pegados sin separación.
            if (text.Contains("18 mm.Incluye", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Los renglones de la descripción salieron pegados en un párrafo corrido.");
            }
        });

        run("PDF: la hoja de costos desglosa la mano de obra por persona", () =>
        {
            var text = ToText(service.BuildCostSheet(BuildQuoteWithWorkers()));

            foreach (var expected in new[]
                     {
                         "Mano de obra por persona", "Pesa en el precio",
                         "Jefe", "Cristian Gómez", "Diego Ruiz"
                     })
            {
                if (!text.Contains(expected, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Faltaba «{expected}» en la hoja de costos.");
                }
            }
        });

        run("PDF: sin operarios la hoja de costos no arma la tabla por persona", () =>
        {
            // Con el jefe solo no hay nada que desglosar, y el desglose de arriba ya lo dice.
            var text = ToText(service.BuildCostSheet(quote));

            if (text.Contains("Mano de obra por persona", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Con una sola persona no debía imprimirse la tabla por persona.");
            }
        });

        run("PDF: con tres operarios la hoja de costos sigue entrando en una A4", () =>
        {
            // Cada operario suma un renglón, y la hoja completa ya venía pasando justo.
            var crowded = BuildQuoteWithWorkers(
                ("Cristian Gómez", 5m, 25000m),
                ("Diego Ruiz", 3m, 22000m),
                ("Marcela Ríos", 4m, 28000m),
                ("Ayudante de obra", 2m, 18000m));

            Assert.Equal(
                CountA4Pages(service.BuildCostSheet(crowded)),
                1,
                "la hoja de costos con cuatro operarios tendría que entrar en una hoja");
        });

        run("PDF: se escribe un archivo que abre como PDF", () =>
        {
            var exporter = new PdfExportService();
            var path = Path.Combine(Path.GetTempPath(), $"metro-pdf-{Guid.NewGuid():N}.pdf");

            try
            {
                exporter.Export(service.BuildClientQuote(quote), path);

                var bytes = File.ReadAllBytes(path);

                if (bytes.Length < 1024)
                {
                    throw new InvalidOperationException($"El PDF salió sospechosamente chico ({bytes.Length} bytes).");
                }

                var head = Encoding.ASCII.GetString(bytes, 0, 8);
                if (!head.StartsWith("%PDF-1.", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"El archivo no arranca con la firma de un PDF: «{head}».");
                }

                var tail = Encoding.ASCII.GetString(bytes, bytes.Length - 32, 32);
                if (!tail.Contains("%%EOF", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Al PDF le faltaba el cierre %%EOF.");
                }
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        });

        run("PDF: el nombre sugerido no lleva caracteres que Windows rechace", () =>
        {
            var name = PdfExportService.SuggestFileName("Presupuesto", 42, "Juan / Pérez: taller");

            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new InvalidOperationException($"El nombre «{name}» todavía tiene caracteres inválidos.");
            }

            if (!name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) || !name.Contains("0042"))
            {
                throw new InvalidOperationException($"El nombre «{name}» no tiene la forma esperada.");
            }
        });
    }

    /// <summary>Un presupuesto con el jefe más los operarios que se le pasen.</summary>
    /// <summary>
    /// Un presupuesto con material y precio, listo para aprobar. Aprobar exige las dos
    /// cosas, así que sin esto no se puede probar nada del otro lado de la aprobación.
    /// </summary>
    private static int NewPricedQuote(string title, string client)
    {
        var productId = AppHost.InventoryService.CreateProduct($"Material {title}", 100m, 0m, "Metro", 500m).Id;
        var id = AppHost.QuoteService.CreateQuote(title, client, null).Id;

        AppHost.QuoteService.AddInventoryLine(id, productId, 4m);
        AppHost.QuoteService.SaveCalculation(id, 2000m, 2m, 25000m, BudgetRates.Defaults());

        return id;
    }

    private static QuoteDetail BuildQuoteWithWorkers(
        params (string Name, decimal Days, decimal Rate)[] workers)
    {
        if (workers.Length == 0)
        {
            workers = [("Cristian Gómez", 5m, 25000m), ("Diego Ruiz", 3m, 22000m)];
        }

        var lines = workers
            .Select((w, i) => new QuoteLaborLineItem
            {
                Id = i + 1,
                Description = w.Name,
                Days = w.Days,
                DailyRate = w.Rate,
                SortOrder = i + 1
            })
            .ToList();

        var breakdown = BudgetCalculatorService.Calculate(new BudgetInput
        {
            MaterialsCost = 100000m,
            Days = 5m,
            DailyRate = 40000m,
            LaborLines = lines
                .Select(l => new LaborLineInput
                {
                    Description = l.Description,
                    Days = l.Days,
                    DailyRate = l.DailyRate
                })
                .ToList(),
            Rates = BudgetRates.Defaults()
        });

        var quote = BuildSampleQuote();

        return new QuoteDetail
        {
            Id = quote.Id,
            Title = quote.Title,
            ClientName = quote.ClientName,
            Description = quote.Description,
            Status = quote.Status,
            Budget = breakdown.FinalPrice,
            Terms = quote.Terms,
            Commercial = CommercialTermsService.Apply(breakdown.FinalPrice, quote.Terms),
            Payments = quote.Payments,
            QuotedAtLocal = quote.QuotedAtLocal,
            ValidUntilLocal = quote.ValidUntilLocal,
            QuotedMaterialsCost = 100000m,
            EstimatedDays = 5m,
            DailyRate = 40000m,
            Rates = BudgetRates.Defaults(),
            Breakdown = breakdown,
            Lines = quote.Lines,
            LaborLines = lines
        };
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
            "Margen efectivo", "a pérdida", "30%", "16%",

            // El presupuesto del cliente muestra el TOTAL y nada más. Ni el resumen que
            // partía el precio en materiales y mano de obra, ni la lista de materiales, ni
            // el reparto por persona, que además deja ver la ganancia.
            "Detalle de materiales", "Resumen", "Mano de obra", "Pesa en el precio"
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

    /// <summary>
    /// Varios adjuntos con descripciones largas: el caso que no entra en una A4 con el aire
    /// de siempre y obliga al documento a compactarse.
    /// </summary>
    private static IReadOnlyList<QuoteAttachmentItem> ManyAttachments(int count) =>
        Enumerable.Range(1, count)
            .Select(i => new QuoteAttachmentItem
            {
                AttachmentId = i,
                ProjectId = 100 + i,
                Title = $"Mueble a medida {i}",
                Description =
                    "Melamina blanca de 18 mm con cantos de PVC\n" +
                    "Herrajes con cierre suave y correderas telescópicas\n" +
                    "Zócalo regulable y fondo de 3 mm\n" +
                    "Incluye colocación y ajuste en obra",
                Budget = 120000m + (i * 1000m)
            })
            .ToList();

    /// <summary>Un presupuesto colgado del de prueba, para los casos con adjuntos.</summary>
    private static QuoteAttachmentItem SampleAttachment(decimal paidTotal = 0m) => new()
    {
        AttachmentId = 1,
        ProjectId = 99,
        Title = "Placard de dormitorio",
        Description = "Frentes de 2,40 m",
        Budget = 185000m,
        PaidTotal = paidTotal
    };

    private static QuoteDetail BuildSampleQuote(
        CommercialTerms? terms = null,
        IReadOnlyList<ProjectPaymentItem>? payments = null,
        string? description = null,
        IReadOnlyList<QuoteAttachmentItem>? attachments = null,
        bool showCommitment = false,
        decimal? commitmentAmount = null,
        bool includeAttachmentsInTotal = false)
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
            Description = description ?? "Roble macizo",
            Status = ProjectStatus.Quote,
            Budget = commercial.Total,
            Terms = terms,
            Commercial = commercial,
            Payments = payments ?? [],
            Attachments = attachments ?? [],
            IncludeAttachmentsInTotal = includeAttachmentsInTotal,
            ShowCommitmentNote = showCommitment,
            CommitmentAmount = commitmentAmount,
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

    /// <summary>
    /// Junta los errores de binding que WPF escribe en sus trazas.
    /// </summary>
    /// <remarks>
    /// WPF arma el mensaje en varias llamadas y recién lo cierra con <c>WriteLine</c>, así
    /// que hay que acumular los <c>Write</c> sueltos hasta que llegue el final.
    /// </remarks>
    private sealed class BindingErrorListener : TraceListener
    {
        private readonly List<string> _errors;
        private readonly StringBuilder _pending = new();

        public BindingErrorListener(List<string> errors) => _errors = errors;

        public override void Write(string? message) => _pending.Append(message);

        public override void WriteLine(string? message)
        {
            _pending.Append(message);
            var line = _pending.ToString().Trim();
            _pending.Clear();

            if (line.Length > 0 && !IsKnownNoise(line))
            {
                _errors.Add(line);
            }
        }

        /// <summary>
        /// El único ruido que se ignora, y por un motivo concreto.
        /// </summary>
        /// <remarks>
        /// <c>(Validation.Errors)[0].ErrorContent</c> es el modismo estándar de WPF para
        /// mostrar el error de un campo: cuando el campo está bien, la colección está
        /// vacía, el índice 0 no existe y WPF lo anota como error de binding. Pasa en todas
        /// las plantillas de validación de la app y no significa nada.
        /// <para>
        /// Se filtra por este patrón exacto a propósito. Ignorar de a categorías enteras
        /// convertiría esta prueba en un cartel decorativo.
        /// </para>
        /// </remarks>
        private static bool IsKnownNoise(string line) =>
            line.Contains("(Validation.Errors)", StringComparison.Ordinal);
    }

    private static void LoadView(Func<FrameworkElement> createView, object dataContext)
    {
        var view = createView();
        view.DataContext = dataContext;
        view.Measure(new Size(900, 600));
        view.Arrange(new Rect(0, 0, 900, 600));
        view.UpdateLayout();
    }

    /// <summary>
    /// Dibuja la vista y la devuelve, para poder mirar los controles de adentro.
    /// </summary>
    /// <remarks>
    /// Un binding mal escrito en WPF no rompe nada: la propiedad se queda en su valor por
    /// omisión y la pantalla se dibuja como si nada. Leer el control es la única forma de
    /// que un test note la diferencia.
    /// </remarks>
    private static FrameworkElement BuildView(Func<FrameworkElement> createView, object dataContext)
    {
        var view = createView();
        view.DataContext = dataContext;
        view.Measure(new Size(900, 600));
        view.Arrange(new Rect(0, 0, 900, 600));
        view.UpdateLayout();
        return view;
    }

    /// <summary>Todos los descendientes de ese tipo, incluida la raíz si corresponde.</summary>
    private static IEnumerable<T> FindAllVisual<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T self)
        {
            yield return self;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var i = 0; i < count; i++)
        {
            foreach (var found in FindAllVisual<T>(VisualTreeHelper.GetChild(root, i)))
            {
                yield return found;
            }
        }
    }

    /// <summary>
    /// Si el texto es un importe. Alcanza con el signo seguido de un dígito: es como los
    /// escribe <c>AppCulture.Money</c>, y lo que buscamos es plata a la vista.
    /// </summary>
    private static bool LooksLikeMoney(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var peso = text.IndexOf('$');

        return peso >= 0
            && text.Skip(peso + 1).SkipWhile(char.IsWhiteSpace).FirstOrDefault() is var next
            && char.IsDigit(next);
    }

    /// <summary>
    /// Si el elemento se ve. Se mira hacia arriba porque lo que se marca son zonas: el
    /// importe cuelga de adentro y desaparece con el contenedor, sin cambiar él mismo.
    /// </summary>
    private static bool IsOnScreen(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is UIElement el && el.Visibility != Visibility.Visible)
            {
                return false;
            }

            element = VisualTreeHelper.GetParent(element);
        }

        return true;
    }

    /// <summary>El primer descendiente de ese tipo en el árbol visual, o null.</summary>
    private static T? FindVisual<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is T match)
            {
                return match;
            }

            if (FindVisual<T>(child) is { } deeper)
            {
                return deeper;
            }
        }

        return null;
    }
}
