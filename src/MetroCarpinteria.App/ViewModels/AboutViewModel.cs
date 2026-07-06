using System.Reflection;
using MetroCarpinteria.App.Helpers;
using MetroCarpinteria.App.Services;

namespace MetroCarpinteria.App.ViewModels;

public class AboutViewModel : ObservableObject
{
    public string BrandName => "Metro Carpintería";
    public string BrandNameUpper => "METRO CARPINTERÍA";
    public string Tagline => "Diseños a medida";
    public string Phone => "3777-412207";
    public string ContactLine => $"Diseños a medida | {Phone}";
    public string DeveloperCredit => "Desarrollado por L.M";
    public string Year => DateTime.Now.Year.ToString();
    public string DeveloperLine => $"Desarrollado por L.M · {Year}";

    public string AppVersion
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version is null ? "1.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    public string DatabasePath => AppHost.Paths.DatabasePath;
}
