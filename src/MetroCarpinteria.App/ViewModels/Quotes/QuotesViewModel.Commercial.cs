using System.Collections.ObjectModel;
using MetroCarpinteria.App.Data.Entities;
using MetroCarpinteria.App.Helpers;
using MetroCarpinteria.App.Models;
using MetroCarpinteria.App.Services;

namespace MetroCarpinteria.App.ViewModels;

/// <summary>
/// Condiciones comerciales: IVA y descuento pactados con el cliente.
/// </summary>
/// <remarks>
/// Los campos van en este archivo y no en el núcleo, a diferencia del resto: son de esta
/// función y de ninguna otra, y tenerlos al lado de su lógica hace que se pueda leer el
/// bloque entero sin saltar de archivo.
/// </remarks>
public partial class QuotesViewModel
{
    private bool _isCommercialOpen;
    private VatOption _selectedVat;
    private string _customVatPercent = string.Empty;
    private DiscountModeOption _selectedDiscountMode;
    private string _discountValue = string.Empty;
    private CommercialBreakdown? _commercial;

    /// <summary>Una alícuota del desplegable. <c>Percent</c> null es «la tipea el usuario».</summary>
    public sealed record VatOption(decimal? Percent, string Label);

    public sealed record DiscountModeOption(DiscountMode Mode, string Label);

    public IReadOnlyList<VatOption> VatOptions { get; } =
    [
        new(0m, "Sin IVA"),
        new(CommercialTerms.ToPercent(VatRate.Reduced), "IVA 10,5%"),
        new(CommercialTerms.ToPercent(VatRate.Standard), "IVA 21%"),
        new(null, "Otra alícuota…")
    ];

    public IReadOnlyList<DiscountModeOption> DiscountModeOptions { get; } =
    [
        new(DiscountMode.None, "Sin descuento"),
        new(DiscountMode.Percentage, "Porcentaje"),
        new(DiscountMode.Amount, "Importe fijo")
    ];

    public ObservableCollection<BudgetBreakdownLine> CommercialLines { get; } = [];

    /// <summary>
    /// El panel arranca plegado: la mayoría de los presupuestos del taller no llevan IVA
    /// discriminado, y abrirlo siempre agrega ruido al 90% de los casos.
    /// </summary>
    public bool IsCommercialOpen
    {
        get => _isCommercialOpen;
        set => SetProperty(ref _isCommercialOpen, value);
    }

    public VatOption SelectedVat
    {
        get => _selectedVat;
        set
        {
            if (SetProperty(ref _selectedVat, value))
            {
                OnPropertyChanged(nameof(IsCustomVat));
                ApplyCommercialTerms();
            }
        }
    }

    public bool IsCustomVat => SelectedVat?.Percent is null;

    /// <summary>Alícuota tipeada a mano, por si mañana cambia y no está en la lista.</summary>
    public string CustomVatPercent
    {
        get => _customVatPercent;
        set
        {
            if (SetProperty(ref _customVatPercent, value))
            {
                ApplyCommercialTerms();
            }
        }
    }

    public DiscountModeOption SelectedDiscountMode
    {
        get => _selectedDiscountMode;
        set
        {
            if (SetProperty(ref _selectedDiscountMode, value))
            {
                OnPropertyChanged(nameof(HasDiscountMode));
                OnPropertyChanged(nameof(DiscountValueLabel));
                ApplyCommercialTerms();
            }
        }
    }

    public bool HasDiscountMode => SelectedDiscountMode?.Mode is not DiscountMode.None;

    public string DiscountValueLabel => SelectedDiscountMode?.Mode == DiscountMode.Percentage
        ? "Porcentaje de descuento"
        : "Importe a descontar";

    public string DiscountValue
    {
        get => _discountValue;
        set
        {
            if (SetProperty(ref _discountValue, value))
            {
                ApplyCommercialTerms();
            }
        }
    }

    public CommercialBreakdown? Commercial
    {
        get => _commercial;
        private set
        {
            if (SetProperty(ref _commercial, value))
            {
                OnPropertyChanged(nameof(HasCommercialTerms));
                OnPropertyChanged(nameof(CommercialSummary));
            }
        }
    }

    public bool HasCommercialTerms => Commercial is { IsPlain: false };

    /// <summary>
    /// El encabezado del panel plegado dice qué se pactó, para no tener que abrirlo.
    /// </summary>
    public string CommercialSummary
    {
        get
        {
            if (Commercial is not { IsPlain: false } commercial)
            {
                return "Condiciones comerciales  —  sin IVA ni descuento";
            }

            var parts = new List<string>();

            if (commercial.HasDiscount)
            {
                parts.Add($"descuento {commercial.DiscountDisplay}");
            }

            if (commercial.HasVat)
            {
                parts.Add($"IVA {AppCulture.Percent(commercial.VatPercent!.Value)}");
            }

            return $"Condiciones comerciales  —  {string.Join(" · ", parts)}";
        }
    }

    /// <summary>Solo se pactan condiciones sobre un presupuesto abierto y ya calculado.</summary>
    public bool CanEditCommercialTerms => Detail is { IsEditable: true } && HasResult;

    // --- Carga y guardado -----------------------------------------------------

    /// <summary>Siembra los campos desde lo guardado, sin disparar un guardado de vuelta.</summary>
    private void LoadCommercialTerms(QuoteDetail detail)
    {
        var terms = detail.Terms;

        _selectedVat = VatOptions.FirstOrDefault(o => o.Percent == (terms.VatPercent ?? 0m))
            ?? VatOptions[^1];

        _customVatPercent = _selectedVat.Percent is null && terms.VatPercent is > 0
            ? NumberInput.Format(terms.VatPercent.Value)
            : string.Empty;

        _selectedDiscountMode = DiscountModeOptions.FirstOrDefault(o => o.Mode == terms.DiscountMode)
            ?? DiscountModeOptions[0];

        _discountValue = terms.DiscountValue > 0 ? NumberInput.Format(terms.DiscountValue) : string.Empty;

        OnPropertyChanged(nameof(SelectedVat));
        OnPropertyChanged(nameof(IsCustomVat));
        OnPropertyChanged(nameof(CustomVatPercent));
        OnPropertyChanged(nameof(SelectedDiscountMode));
        OnPropertyChanged(nameof(HasDiscountMode));
        OnPropertyChanged(nameof(DiscountValueLabel));
        OnPropertyChanged(nameof(DiscountValue));
        OnPropertyChanged(nameof(CanEditCommercialTerms));

        ShowCommercial(detail.Commercial);

        // Se abre solo si hay algo pactado: así el que lo usa lo ve, y al que no le
        // interesa no le ocupa media pantalla.
        IsCommercialOpen = !terms.IsEmpty;
    }

    private void ShowCommercial(CommercialBreakdown? commercial)
    {
        Commercial = commercial;
        CommercialLines.Clear();

        if (commercial is null || commercial.IsPlain)
        {
            return;
        }

        foreach (var line in commercial.Lines.Where(l => !l.IsTotal))
        {
            CommercialLines.Add(line);
        }
    }

    /// <summary>Lee los campos y guarda. Corre solo, como el resto de la calculadora.</summary>
    private void ApplyCommercialTerms()
    {
        if (_isLoadingDetail || Detail is not { IsEditable: true })
        {
            return;
        }

        try
        {
            var terms = ReadTermsFromForm();
            var commercial = AppHost.QuoteService.SaveCommercialTerms(Detail.Id, terms);

            ShowCommercial(commercial);

            // Cambió el total: el renglón de la lista y el precio guardado tienen que
            // seguirlo, igual que después de recalcular.
            RefreshRow(Detail.Id);
            RefreshDetailAfterCalculation();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private CommercialTerms ReadTermsFromForm()
    {
        decimal? vat = SelectedVat?.Percent;

        if (SelectedVat?.Percent is null)
        {
            vat = string.IsNullOrWhiteSpace(CustomVatPercent)
                ? null
                : NumberInput.ParseQuantityOrThrow(CustomVatPercent, "Alícuota de IVA");
        }

        var mode = SelectedDiscountMode?.Mode ?? DiscountMode.None;

        var discount = mode == DiscountMode.None || string.IsNullOrWhiteSpace(DiscountValue)
            ? 0m
            : mode == DiscountMode.Percentage
                ? NumberInput.ParseQuantityOrThrow(DiscountValue, "Porcentaje de descuento")
                : NumberInput.ParseMoneyOrThrow(DiscountValue, "Importe del descuento");

        return new CommercialTerms
        {
            VatPercent = vat,
            DiscountMode = mode,
            DiscountValue = discount
        };
    }

    // --- Aviso de seña bajo el TOTAL ------------------------------------------

    public bool ShowCommitmentOnTotal => Detail is { HasCommitmentNote: true };

    public string CommitmentNoteDisplay => Detail?.CommitmentNoteDisplay ?? string.Empty;

    public bool ShowCommitmentNote
    {
        get => _showCommitmentNote;
        set
        {
            if (SetProperty(ref _showCommitmentNote, value))
            {
                SaveCommitmentNote();
            }
        }
    }

    public string CommitmentAmount
    {
        get => _commitmentAmount;
        set => SetProperty(ref _commitmentAmount, value);
    }

    public string CommitmentText
    {
        get => _commitmentText;
        set => SetProperty(ref _commitmentText, value);
    }

    private void LoadCommitmentNote(QuoteDetail detail)
    {
        _showCommitmentNote = detail.ShowCommitmentNote;
        _commitmentAmount = NumberInput.Format(detail.CommitmentAmount);
        _commitmentText = detail.CommitmentText ?? string.Empty;

        OnPropertyChanged(nameof(ShowCommitmentNote));
        OnPropertyChanged(nameof(CommitmentAmount));
        OnPropertyChanged(nameof(CommitmentText));
        OnPropertyChanged(nameof(ShowCommitmentOnTotal));
        OnPropertyChanged(nameof(CommitmentNoteDisplay));
    }

    /// <summary>Guarda el aviso. Corre al marcar el tilde y al salir de los campos.</summary>
    public void SaveCommitmentNote()
    {
        if (_isLoadingDetail || Detail is not { IsEditable: true })
        {
            return;
        }

        try
        {
            decimal? amount = null;
            if (!string.IsNullOrWhiteSpace(CommitmentAmount))
            {
                amount = NumberInput.ParseMoneyOrThrow(CommitmentAmount, "Importe de la seña");
            }

            AppHost.QuoteService.SaveCommitmentNote(
                Detail.Id, ShowCommitmentNote, amount, CommitmentText);

            RefreshDetailAfterCalculation();
            OnPropertyChanged(nameof(ShowCommitmentOnTotal));
            OnPropertyChanged(nameof(CommitmentNoteDisplay));
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }
}
