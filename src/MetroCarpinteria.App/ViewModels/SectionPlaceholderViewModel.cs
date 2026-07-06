using MetroCarpinteria.App.Helpers;

namespace MetroCarpinteria.App.ViewModels;

public class SectionPlaceholderViewModel : ObservableObject
{
    public SectionPlaceholderViewModel(string title, string subtitle, string icon, string comingSoonPart)
    {
        Title = title;
        Subtitle = subtitle;
        Icon = icon;
        ComingSoonPart = comingSoonPart;
    }

    public string Title { get; }
    public string Subtitle { get; }
    public string Icon { get; }
    public string ComingSoonPart { get; }
}
