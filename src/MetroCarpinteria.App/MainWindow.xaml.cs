using MetroCarpinteria.App.Services;
using MetroCarpinteria.App.ViewModels;
using System.Windows;
using System.Windows.Input;

namespace MetroCarpinteria.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();

        // Estos dos no entran en InputBindings: uno tiene que buscar un control en el
        // árbol visual y el otro tiene que ganarle al foco de los campos de texto.
        PreviewKeyDown += OnPreviewKeyDown;

        // Mientras la máquina duerme los temporizadores no corren, así que volver a la
        // ventana es el momento en que más probable es que el día haya cambiado sin que
        // nadie se enterara.
        Activated += (_, _) => AppHost.ClockService.CheckForDayChange();

        AppHost.ClockService.Start();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel main)
        {
            return;
        }

        // Ctrl+F: cada sección tiene su propio buscador, así que hay que encontrar el de
        // la que está a la vista. Lo marca Ui.IsSectionSearchBox; Inicio y Acerca de no
        // tienen ninguno y ahí el atajo simplemente no hace nada.
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (Controls.Ui.FindSectionSearchBox(PageContent) is { } search)
            {
                search.Focus();
                search.SelectAll();
                e.Handled = true;
            }

            return;
        }

        // Esc cierra lo que haya abierto. Va en PreviewKeyDown porque un TextBox con el
        // foco se queda con la tecla antes de que llegue a la ventana.
        if (e.Key == Key.Escape && main.AreShortcutsVisible)
        {
            main.CloseOverlaysCommand.Execute(null);
            e.Handled = true;
        }
    }
}
