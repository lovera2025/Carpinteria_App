using MetroCarpinteria.App.Services;

namespace MetroCarpinteria.App.Models;

public class AppSettings
{
    public bool BackupOnExit { get; set; } = true;
    public int MaxBackupFiles { get; set; } = 30;
    public DateTime? LastBackupUtc { get; set; }

    /// <summary>Porcentajes por defecto de la calculadora de presupuestos.</summary>
    public BudgetRates BudgetRates { get; set; } = new();

    /// <summary>Días de vigencia con los que se autocompleta un presupuesto nuevo.</summary>
    public int DefaultQuoteValidityDays { get; set; } = 15;

    /// <summary>Valor del jornal usado para prellenar la calculadora.</summary>
    public decimal? DefaultDailyRate { get; set; }

    /// <summary>Buscar versiones nuevas al abrir la app, si hay internet.</summary>
    public bool CheckUpdatesOnStartup { get; set; } = true;

    /// <summary>Cuándo se buscaron actualizaciones por última vez.</summary>
    public DateTime? LastUpdateCheckUtc { get; set; }

    /// <summary>
    /// Pares de fichas de cliente que alguien ya revisó y marcó como distintas.
    /// </summary>
    /// <remarks>
    /// Va en la configuración y no en la base porque no es un dato del taller sino una
    /// decisión sobre qué mostrar. Sin esto, la revisión de duplicados vuelve a proponer
    /// «Juan Pérez» y «Juan Pérez h.» cada vez que se abre, y se termina ignorando entera.
    /// </remarks>
    public List<string> DismissedClientPairs { get; set; } = [];

    /// <summary>
    /// Versión de la guía de arranque que el usuario ya vio.
    /// </summary>
    /// <remarks>
    /// Versionada y no un simple booleano: cuando la app cambie lo suficiente como para
    /// que la guía valga la pena de nuevo, se sube el número y vuelve a aparecer una vez.
    /// </remarks>
    public int OnboardingCompletedVersion { get; set; }

    // --- Apariencia ---------------------------------------------------------

    /// <summary>
    /// Claro, oscuro, o lo que tenga Windows. El taller cambia de luz durante el día
    /// y la app se usa tanto de mañana como de noche.
    /// </summary>
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>Tamaño de letra. Chica, normal o grande.</summary>
    public FontScale FontScale { get; set; } = FontScale.Normal;
}
