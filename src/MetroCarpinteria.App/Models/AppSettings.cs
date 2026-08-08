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
}
