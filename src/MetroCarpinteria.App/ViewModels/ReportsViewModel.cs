using System.Collections.ObjectModel;
using System.Windows.Input;
using MetroCarpinteria.App.Helpers;
using MetroCarpinteria.App.Models;
using MetroCarpinteria.App.Services;

namespace MetroCarpinteria.App.ViewModels;

public class ReportsViewModel : ObservableObject
{
    public ReportsViewModel()
    {
        Sections = new ObservableCollection<ReportSection>();
        LoadCommand = new RelayCommand(_ => Load());
    }

    public ObservableCollection<ReportSection> Sections { get; }

    public string GeneratedAt { get; private set; } = string.Empty;

    public ICommand LoadCommand { get; }

    public void Load()
    {
        Sections.Clear();
        foreach (var section in AppHost.ReportService.BuildSummary())
        {
            Sections.Add(section);
        }

        GeneratedAt = DateTime.Now.ToString(
            "dd/MM/yyyy HH:mm",
            new System.Globalization.CultureInfo("es-AR"));
        OnPropertyChanged(nameof(GeneratedAt));
    }
}
