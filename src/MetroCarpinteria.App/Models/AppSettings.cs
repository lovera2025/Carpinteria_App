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

    /// <summary>
    /// Cuándo el taller confirmó el saldo de la caja fuerte. Null es «todavía no».
    /// </summary>
    /// <remarks>
    /// La conversión de las cajas viejas a un saldo único suma aperturas y diferencias de
    /// arqueo que antes no tenían renglón. El número resultante tiene que cuadrar con lo
    /// que hay de verdad, y eso solo lo sabe él: hasta que lo confirme, la pantalla le
    /// muestra el saldo abierto en de dónde sale. Va en la configuración y no en la base
    /// porque no es un dato del taller sino una decisión sobre qué mostrar.
    /// </remarks>
    public DateTime? CashSafeReviewedAtUtc { get; set; }

    // --- Apariencia ---------------------------------------------------------

    /// <summary>
    /// Claro, oscuro, o lo que tenga Windows. El taller cambia de luz durante el día
    /// y la app se usa tanto de mañana como de noche.
    /// </summary>
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>Tamaño de letra. Chica, normal o grande.</summary>
    public FontScale FontScale { get; set; } = FontScale.Normal;

    /// <summary>
    /// Tapar los importes de la pantalla. Se prende con el botón de la barra de arriba o
    /// con Ctrl+H.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se guarda a propósito. Lo que se quiere tapar es que alguien vea la plata del
    /// taller, y el momento más descuidado es justo el de abrir la app con el cliente ya
    /// parado al lado: si el modo se apagara solo al cerrar, ese momento quedaría
    /// destapado, que es el único que importa.
    /// </para>
    /// <para>
    /// Se puede guardar sin riesgo porque lo tapado <b>se lee como tapado</b>: es
    /// difuminado, no un número cambiado. Encontrarse la caja borrosa al día siguiente se
    /// entiende; encontrársela en cero, no.
    /// </para>
    /// </remarks>
    public bool HideSensitiveNumbers { get; set; }
}
