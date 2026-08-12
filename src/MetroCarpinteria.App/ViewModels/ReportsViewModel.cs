using System.Collections.ObjectModel;
using System.Windows.Input;
using MetroCarpinteria.App.Helpers;
using MetroCarpinteria.App.Models;
using MetroCarpinteria.App.Services;

namespace MetroCarpinteria.App.ViewModels;

public class ReportsViewModel : ViewModelBase
{
    public ReportsViewModel()
    {
        Sections = new ObservableCollection<ReportSection>();
        LoadCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
    }

    public ObservableCollection<ReportSection> Sections { get; }

    public string GeneratedAt { get; private set; } = string.Empty;

    public ICommand LoadCommand { get; }

    /// <summary>
    /// Carga al entrar a la sección. Sincrónica a propósito: la navegación ya espera a que
    /// la pantalla esté armada, y mostrarla vacía por un instante para llenarla después se
    /// ve como un parpadeo.
    /// </summary>
    public void Load() => SafeLoad(() => Publish(AppHost.ReportService.BuildSummary()), "Reportes");

    /// <summary>
    /// El botón «Actualizar». Acá sí va en segundo plano: son varias consultas agregadas
    /// sobre todo el historial, y es el único momento en que el usuario está esperando a
    /// propósito a que se recalculen.
    /// </summary>
    private async Task RefreshAsync()
    {
        try
        {
            var sections = await RunBusyAsync(
                () => AppHost.ReportService.BuildSummary(),
                "Recalculando los números…");

            Publish(sections);
        }
        catch (Exception ex)
        {
            ReportLoadFailure(ex, "Reportes");
        }
    }

    private void Publish(IReadOnlyList<ReportSection> sections)
    {
        Sections.Clear();
        foreach (var section in sections)
        {
            Sections.Add(section);
        }

        GeneratedAt = AppCulture.DateTimeShort(DateTime.Now);
        OnPropertyChanged(nameof(GeneratedAt));
    }
}
