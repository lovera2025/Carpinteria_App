using System.Collections.ObjectModel;
using System.Windows.Input;
using MetroCarpinteria.App.Data.Entities;
using MetroCarpinteria.App.Helpers;
using MetroCarpinteria.App.Models;
using MetroCarpinteria.App.Services;

namespace MetroCarpinteria.App.ViewModels;

/// <summary>
/// Señas y pagos a cuenta de un trabajo, con su saldo.
/// </summary>
/// <remarks>
/// <para>
/// Es un ViewModel aparte y no un <c>partial</c> de la pantalla porque lo usan dos:
/// Presupuestos, donde se toma la seña, y Proyectos, porque un trabajo en curso también
/// cobra. Acá sí conviene el sub-ViewModel —a diferencia de la calculadora, donde habría
/// obligado a reescribir doscientas rutas de binding—: es un bloque chico, autocontenido,
/// y su panel se monta con un <c>DataContext</c> propio.
/// </para>
/// <para>
/// No consulta la base por su cuenta: la pantalla que lo contiene le pasa el detalle ya
/// cargado y le avisa cuando cambió.
/// </para>
/// </remarks>
public sealed class PaymentsSectionViewModel : ObservableObject
{
    private readonly Action _onChanged;

    // Los asigna el constructor desde las listas de opciones: el desplegable compara por
    // referencia, así que tienen que ser la misma instancia que está en la lista o la
    // caja aparece en blanco.
    private PaymentKindOption _paymentKind;
    private PaymentMethodOption _paymentMethod;

    private QuoteDetail? _detail;
    private bool _isFormOpen;
    private string _amount = string.Empty;
    private string _notes = string.Empty;

    /// <param name="onChanged">
    /// Recarga la pantalla contenedora después de cobrar o anular: el precio, el saldo y
    /// el renglón de la lista dependen de esto.
    /// </param>
    public PaymentsSectionViewModel(Action onChanged)
    {
        _onChanged = onChanged;

        _paymentKind = PaymentKinds[0];
        _paymentMethod = PaymentMethods[0];

        OpenFormCommand = new RelayCommand(_ => OpenForm(), _ => CanRegisterPayment);
        CancelFormCommand = new RelayCommand(_ => IsFormOpen = false);
        ConfirmCommand = new AsyncRelayCommand(ConfirmAsync, () => CanRegisterPayment);
        RemoveCommand = new AsyncRelayCommand(RemoveAsync);
        PrintReceiptCommand = new RelayCommand(PrintReceipt, _ => HasSelection);
    }

    /// <summary>
    /// Los desplegables van con la etiqueta al lado del valor, y no con el enum pelado:
    /// si no, el usuario elige entre «Deposit» y «Partial».
    /// </summary>
    public sealed record PaymentKindOption(PaymentKind Kind, string Label);

    public sealed record PaymentMethodOption(PaymentMethod Method, string Label);

    public ObservableCollection<ProjectPaymentItem> Payments { get; } = [];

    public IReadOnlyList<PaymentKindOption> PaymentKinds { get; } = PaymentRules.Kinds
        .Select(k => new PaymentKindOption(k, PaymentRules.GetKindLabel(k)))
        .ToList();

    public IReadOnlyList<PaymentMethodOption> PaymentMethods { get; } = PaymentRules.Methods
        .Select(m => new PaymentMethodOption(m, PaymentRules.GetMethodLabel(m)))
        .ToList();

    public ICommand OpenFormCommand { get; }
    public ICommand CancelFormCommand { get; }
    public ICommand ConfirmCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand PrintReceiptCommand { get; }

    // --- Estado ---------------------------------------------------------------

    public bool HasSelection => _detail is not null;
    public bool HasPayments => Payments.Count > 0;

    /// <summary>Se cobra sobre un trabajo con precio, esté aprobado o todavía en presupuesto.</summary>
    public bool CanRegisterPayment =>
        _detail is { Budget: > 0, IsArchived: false } && !_detail.IsFullyPaid;

    public string PaidTotalDisplay => _detail?.PaidTotalDisplay ?? AppCulture.Money(0m);
    public string BalanceDisplay => _detail?.BalanceDisplay ?? AppCulture.Money(0m);

    public string Summary
    {
        get
        {
            if (_detail is not { Budget: > 0 })
            {
                return "Falta el precio del trabajo";
            }

            if (!HasPayments)
            {
                return "Todavía no se cobró nada";
            }

            return _detail.IsFullyPaid
                ? "Cobrado por completo"
                : $"{Phrases.Count(Payments.Count, "cobro", "cobros")} · " +
                  $"{_detail.PaidTotalDisplay} de {_detail.BudgetDisplay}";
        }
    }

    public bool IsFormOpen
    {
        get => _isFormOpen;
        private set => SetProperty(ref _isFormOpen, value);
    }

    public string Amount
    {
        get => _amount;
        set => SetProperty(ref _amount, value);
    }

    public PaymentKindOption PaymentKind
    {
        get => _paymentKind;
        set => SetProperty(ref _paymentKind, value);
    }

    public PaymentMethodOption PaymentMethod
    {
        get => _paymentMethod;
        set
        {
            if (SetProperty(ref _paymentMethod, value))
            {
                OnPropertyChanged(nameof(NeedsOpenRegister));
            }
        }
    }

    /// <summary>Avisa antes de intentar, para no hacer tipear todo y después rebotar.</summary>
    public bool NeedsOpenRegister =>
        PaymentMethod?.Method == Data.Entities.PaymentMethod.Cash
        && AppHost.IsReady
        && !AppHost.CashRegisterService.HasOpenSession();

    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    // --- Carga ----------------------------------------------------------------

    public void Load(QuoteDetail? detail)
    {
        _detail = detail;

        Payments.Clear();
        foreach (var payment in detail?.Payments ?? [])
        {
            Payments.Add(payment);
        }

        if (detail is null)
        {
            IsFormOpen = false;
        }

        NotifyChanged();
    }

    private void NotifyChanged()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasPayments));
        OnPropertyChanged(nameof(CanRegisterPayment));
        OnPropertyChanged(nameof(PaidTotalDisplay));
        OnPropertyChanged(nameof(BalanceDisplay));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(NeedsOpenRegister));
    }

    // --- Acciones -------------------------------------------------------------

    private void OpenForm()
    {
        if (_detail is null)
        {
            return;
        }

        // Se propone cobrar todo el saldo: es lo más habitual y evita tipear el número.
        Amount = NumberInput.Format(_detail.Balance);

        // El primer cobro casi siempre es la seña; los siguientes, pagos a cuenta.
        PaymentKind = PaymentKinds.First(o => o.Kind == (HasPayments
            ? Data.Entities.PaymentKind.Partial
            : Data.Entities.PaymentKind.Deposit));

        PaymentMethod = PaymentMethods.First(o => o.Method == Data.Entities.PaymentMethod.Cash);
        Notes = string.Empty;

        IsFormOpen = true;
        OnPropertyChanged(nameof(NeedsOpenRegister));
    }

    private async Task ConfirmAsync()
    {
        if (_detail is null)
        {
            return;
        }

        try
        {
            var amount = NumberInput.ParseMoneyOrThrow(Amount, "Importe del cobro");

            var payment = AppHost.PaymentService.RegisterPayment(
                _detail.Id, PaymentKind.Kind, amount, PaymentMethod.Method, Notes);

            IsFormOpen = false;
            _onChanged();

            AppHost.NotificationService.Success(
                $"{PaymentKind.Label} de {AppCulture.Money(amount)} registrada. Saldo: {BalanceDisplay}");

            OfferReceipt(payment);
        }
        catch (CashRegisterClosedException)
        {
            // El único error de cobro que se resuelve con un botón y no corrigiendo lo
            // tipeado: se ofrece abrir la caja sin perder lo que venía cargado.
            await OfferToOpenRegisterAsync();
        }
        catch (Exception ex)
        {
            AppHost.NotificationService.Warning(ex.Message);
        }
    }

    private async Task OfferToOpenRegisterAsync()
    {
        var confirmed = await AppHost.DialogService.ConfirmAsync(
            "No hay una caja abierta",
            "Un cobro en efectivo se asienta en el arqueo del día, así que necesita la caja abierta.\n\n" +
            "Podés abrirla ahora con saldo inicial en cero, o registrar el cobro por otro medio.",
            confirmText: "Abrir caja y cobrar");

        if (!confirmed)
        {
            return;
        }

        try
        {
            AppHost.CashRegisterService.OpenSession(0m, "Apertura para registrar un cobro");
            OnPropertyChanged(nameof(NeedsOpenRegister));

            // Y se reintenta el cobro, que es lo que el usuario venía a hacer.
            await ConfirmAsync();
        }
        catch (Exception ex)
        {
            AppHost.NotificationService.Error(ex.Message, ex);
        }
    }

    private async Task RemoveAsync(object? parameter)
    {
        if (parameter is not ProjectPaymentItem payment)
        {
            return;
        }

        var confirmed = await AppHost.DialogService.ConfirmAsync(
            $"Anular {PaymentRules.GetKindLabel(payment.Kind).ToLowerInvariant()}",
            $"Se anula el cobro de {payment.AmountDisplay} del {payment.DateDisplay}.\n\n" +
            (payment.IsLinkedToCash
                ? "Como entró por Caja, el movimiento no se borra: se asienta una salida que lo " +
                  "compensa, para no descuadrar un arqueo ya cerrado. Necesita la caja abierta."
                : "El saldo del trabajo vuelve a subir."),
            confirmText: "Anular cobro",
            isDestructive: true);

        if (!confirmed)
        {
            return;
        }

        try
        {
            AppHost.PaymentService.CancelPayment(payment.Id, "Anulado a mano");
            _onChanged();
            AppHost.NotificationService.Success("Cobro anulado.");
        }
        catch (Exception ex)
        {
            AppHost.NotificationService.Error(ex.Message, ex);
        }
    }

    private void PrintReceipt(object? parameter)
    {
        if (parameter is not ProjectPaymentItem payment)
        {
            return;
        }

        OfferReceipt(payment);
    }

    /// <summary>
    /// Arma el PDF, lo deja en la carpeta de recibos y lo abre. El cobro ya está
    /// guardado: si esto falla, se avisa y nada más.
    /// </summary>
    private void OfferReceipt(ProjectPaymentItem payment)
    {
        if (_detail is null || !AppHost.IsReady)
        {
            return;
        }

        try
        {
            var document = AppHost.QuoteDocumentService.BuildReceipt(_detail, payment);
            Directory.CreateDirectory(AppHost.Paths.ReceiptsDirectory);

            var suggested = PdfExportService.SuggestFileName(
                $"Recibo {payment.KindLabel}", _detail.Id, _detail.ClientName);
            var path = UniquePath(Path.Combine(AppHost.Paths.ReceiptsDirectory, suggested));

            AppHost.PdfExportService.Export(document, path);

            if (AppHost.DialogService.HasHost)
            {
                PdfExportService.OpenInDefaultApp(path);
            }
        }
        catch (Exception ex)
        {
            AppHost.NotificationService.Warning(
                $"El cobro quedó registrado, pero no se pudo armar el recibo: {ex.Message}");
        }
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? ".";
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var i = 2; i < 100; i++)
        {
            var candidate = Path.Combine(directory, $"{name} ({i}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(directory, $"{name} {Guid.NewGuid():N}{extension}");
    }
}
