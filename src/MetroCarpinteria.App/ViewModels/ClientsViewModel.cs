using System.Collections.ObjectModel;
using System.Windows.Input;
using MetroCarpinteria.App.Helpers;
using MetroCarpinteria.App.Models;
using MetroCarpinteria.App.Services;

namespace MetroCarpinteria.App.ViewModels;

/// <summary>
/// La agenda de clientes: ficha, historial y revisión de duplicados.
/// </summary>
/// <remarks>
/// La revisión de duplicados vive acá adentro y no en una pantalla aparte: se decide
/// mirando el historial de las dos fichas, que es justo lo que esta pantalla ya muestra.
/// Mandar al usuario a otro lado para volver a mostrarle lo mismo no ayuda.
/// </remarks>
public class ClientsViewModel : ViewModelBase
{
    private readonly Action _onDataChanged;
    private readonly Debouncer _searchDebouncer = new();

    private ClientListItem? _selectedClient;
    private string _searchText = string.Empty;
    private bool _showArchived;
    private bool _isReviewingDuplicates;

    private bool _isFormOpen;
    private bool _isCreating;
    private string _formName = string.Empty;
    private string _formPhone = string.Empty;
    private string _formEmail = string.Empty;
    private string _formTaxId = string.Empty;
    private string _formAddress = string.Empty;
    private string _formNotes = string.Empty;

    private string? _deleteBlockReason;

    public ClientsViewModel(Action onDataChanged)
    {
        _onDataChanged = onDataChanged;

        LoadCommand = new RelayCommand(_ => Load());
        NewClientCommand = new RelayCommand(_ => StartNew());
        EditClientCommand = new RelayCommand(_ => StartEdit(), _ => SelectedClient is not null);
        SaveClientCommand = new RelayCommand(_ => SaveClient());
        CancelFormCommand = new RelayCommand(_ => IsFormOpen = false);

        ArchiveClientCommand = new AsyncRelayCommand(ArchiveSelectedAsync, () => CanArchiveSelected);
        RestoreClientCommand = new RelayCommand(_ => RestoreSelected(), _ => CanRestoreSelected);
        DeleteClientCommand = new AsyncRelayCommand(
            DeleteSelectedAsync, () => CanDeleteSelected, observeRequery: false);

        ToggleDuplicatesCommand = new RelayCommand(_ => ToggleDuplicates());
        MergeCommand = new AsyncRelayCommand(MergeAsync);
        DismissPairCommand = new RelayCommand(DismissPair);
    }

    public ObservableCollection<ClientListItem> Clients { get; } = [];
    public ObservableCollection<ClientProjectItem> History { get; } = [];
    public ObservableCollection<ClientDuplicateCandidate> Duplicates { get; } = [];

    // --- Lista ---------------------------------------------------------------

    public ClientListItem? SelectedClient
    {
        get => _selectedClient;
        set
        {
            if (!SetProperty(ref _selectedClient, value))
            {
                return;
            }

            RefreshDeleteBlockReason();
            LoadHistory();

            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(CanArchiveSelected));
            OnPropertyChanged(nameof(CanRestoreSelected));
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                _searchDebouncer.Run(LoadClients);
            }
        }
    }

    public bool ShowArchived
    {
        get => _showArchived;
        set
        {
            if (SetProperty(ref _showArchived, value))
            {
                LoadClients();
            }
        }
    }

    public bool HasSelection => SelectedClient is not null;
    public bool CanArchiveSelected => SelectedClient is { IsArchived: false };
    public bool CanRestoreSelected => SelectedClient is { IsArchived: true };
    public bool CanDeleteSelected => SelectedClient is not null && _deleteBlockReason is null;

    public string DeleteBlockTooltip =>
        _deleteBlockReason ?? "Elimina la ficha definitivamente.";

    // --- Formulario ----------------------------------------------------------

    public bool IsFormOpen
    {
        get => _isFormOpen;
        private set => SetProperty(ref _isFormOpen, value);
    }

    public bool IsCreating
    {
        get => _isCreating;
        private set
        {
            if (SetProperty(ref _isCreating, value))
            {
                OnPropertyChanged(nameof(FormHeader));
            }
        }
    }

    public string FormHeader => IsCreating ? "Nuevo cliente" : "Editar cliente";

    public string FormName
    {
        get => _formName;
        set => SetProperty(ref _formName, value);
    }

    public string FormPhone
    {
        get => _formPhone;
        set => SetProperty(ref _formPhone, value);
    }

    public string FormEmail
    {
        get => _formEmail;
        set => SetProperty(ref _formEmail, value);
    }

    public string FormTaxId
    {
        get => _formTaxId;
        set => SetProperty(ref _formTaxId, value);
    }

    public string FormAddress
    {
        get => _formAddress;
        set => SetProperty(ref _formAddress, value);
    }

    public string FormNotes
    {
        get => _formNotes;
        set => SetProperty(ref _formNotes, value);
    }

    // --- Duplicados ----------------------------------------------------------

    public bool IsReviewingDuplicates
    {
        get => _isReviewingDuplicates;
        private set
        {
            if (SetProperty(ref _isReviewingDuplicates, value))
            {
                OnPropertyChanged(nameof(DuplicatesButtonLabel));
            }
        }
    }

    public string DuplicatesButtonLabel => IsReviewingDuplicates
        ? "Volver a la lista"
        : "Revisar duplicados";

    public bool HasDuplicates => Duplicates.Count > 0;

    public string DuplicatesSummary => Duplicates.Count switch
    {
        0 => "No hay fichas parecidas para revisar.",
        _ => $"{Phrases.Count(Duplicates.Count, "par", "pares")} para revisar. " +
             "La app propone; juntar dos fichas mezcla dos historiales y no se deshace con un botón."
    };

    // --- Comandos ------------------------------------------------------------

    public ICommand LoadCommand { get; }
    public ICommand NewClientCommand { get; }
    public ICommand EditClientCommand { get; }
    public ICommand SaveClientCommand { get; }
    public ICommand CancelFormCommand { get; }
    public ICommand ArchiveClientCommand { get; }
    public ICommand RestoreClientCommand { get; }
    public ICommand DeleteClientCommand { get; }
    public ICommand ToggleDuplicatesCommand { get; }
    public ICommand MergeCommand { get; }
    public ICommand DismissPairCommand { get; }

    // --- Carga ---------------------------------------------------------------

    public void Load() => SafeLoad(LoadClients, "Clientes");

    private void LoadClients()
    {
        var items = AppHost.ClientService.GetClients(ShowArchived, SearchText);
        var selectedId = SelectedClient?.Id;

        Clients.Clear();
        foreach (var item in items)
        {
            Clients.Add(item);
        }

        SelectedClient = selectedId.HasValue
            ? Clients.FirstOrDefault(c => c.Id == selectedId.Value) ?? Clients.FirstOrDefault()
            : Clients.FirstOrDefault();

        // Explícito: si la selección quedó igual, el setter no corre y el historial
        // quedaría con lo que valía antes de recargar.
        RefreshDeleteBlockReason();
        LoadHistory();
        LoadDuplicates();

        _onDataChanged();
    }

    private void LoadHistory()
    {
        History.Clear();

        if (SelectedClient is null)
        {
            return;
        }

        foreach (var project in AppHost.ClientService.GetClientProjects(SelectedClient.Id))
        {
            History.Add(project);
        }
    }

    private void LoadDuplicates()
    {
        Duplicates.Clear();

        foreach (var candidate in AppHost.ClientService
            .FindDuplicateCandidates(AppHost.Settings.DismissedClientPairs))
        {
            Duplicates.Add(candidate);
        }

        OnPropertyChanged(nameof(HasDuplicates));
        OnPropertyChanged(nameof(DuplicatesSummary));
    }

    private void RefreshDeleteBlockReason()
    {
        _deleteBlockReason = SelectedClient is null
            ? "Elegí un cliente de la lista."
            : AppHost.ClientService.DescribeDeleteBlock(SelectedClient.Id);

        OnPropertyChanged(nameof(CanDeleteSelected));
        OnPropertyChanged(nameof(DeleteBlockTooltip));
        (DeleteClientCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    // --- Acciones ------------------------------------------------------------

    private void StartNew()
    {
        FormName = string.Empty;
        FormPhone = string.Empty;
        FormEmail = string.Empty;
        FormTaxId = string.Empty;
        FormAddress = string.Empty;
        FormNotes = string.Empty;

        IsCreating = true;
        IsFormOpen = true;
    }

    private void StartEdit()
    {
        if (SelectedClient is null)
        {
            return;
        }

        FormName = SelectedClient.Name;
        FormPhone = SelectedClient.Phone ?? string.Empty;
        FormEmail = SelectedClient.Email ?? string.Empty;
        FormTaxId = SelectedClient.TaxId ?? string.Empty;
        FormAddress = SelectedClient.Address ?? string.Empty;
        FormNotes = SelectedClient.Notes ?? string.Empty;

        IsCreating = false;
        IsFormOpen = true;
    }

    private void SaveClient()
    {
        try
        {
            if (IsCreating)
            {
                var created = AppHost.ClientService.Create(
                    FormName, FormPhone, FormEmail, FormTaxId, FormAddress, FormNotes);

                IsFormOpen = false;
                LoadClients();
                SelectedClient = Clients.FirstOrDefault(c => c.Id == created.Id);
                AppHost.NotificationService.Success($"Cliente «{created.Name}» creado.");
                return;
            }

            if (SelectedClient is null)
            {
                return;
            }

            AppHost.ClientService.Update(
                SelectedClient.Id, FormName, FormPhone, FormEmail, FormTaxId, FormAddress, FormNotes);

            IsFormOpen = false;
            LoadClients();
            AppHost.NotificationService.Success("Ficha actualizada.");
        }
        catch (Exception ex)
        {
            AppHost.NotificationService.Warning(ex.Message);
        }
    }

    private async Task ArchiveSelectedAsync()
    {
        if (SelectedClient is null)
        {
            return;
        }

        var name = SelectedClient.Name;

        var confirmed = await AppHost.DialogService.ConfirmAsync(
            "Archivar cliente",
            $"«{name}» deja de aparecer en la lista y en el selector de presupuestos.\n\n" +
            "El historial de trabajos se conserva entero.",
            confirmText: "Archivar");

        if (!confirmed)
        {
            return;
        }

        try
        {
            AppHost.ClientService.SetArchived(SelectedClient.Id, archived: true);
            AppHost.NotificationService.Success($"«{name}» quedó archivado.");
            LoadClients();
        }
        catch (Exception ex)
        {
            AppHost.NotificationService.Error(ex.Message, ex);
        }
    }

    private void RestoreSelected()
    {
        if (SelectedClient is null)
        {
            return;
        }

        try
        {
            AppHost.ClientService.SetArchived(SelectedClient.Id, archived: false);
            LoadClients();
        }
        catch (Exception ex)
        {
            AppHost.NotificationService.Error(ex.Message, ex);
        }
    }

    private async Task DeleteSelectedAsync()
    {
        if (SelectedClient is null)
        {
            return;
        }

        var name = SelectedClient.Name;

        var confirmed = await AppHost.DialogService.ConfirmAsync(
            "Eliminar cliente",
            $"Se borra la ficha de «{name}». Esto no se puede deshacer.",
            confirmText: "Eliminar",
            isDestructive: true);

        if (!confirmed)
        {
            return;
        }

        try
        {
            AppHost.ClientService.Delete(SelectedClient.Id);
            SelectedClient = null;
            AppHost.NotificationService.Success($"«{name}» se eliminó.");
            LoadClients();
        }
        catch (Exception ex)
        {
            AppHost.NotificationService.Error(ex.Message, ex);
        }
    }

    private void ToggleDuplicates()
    {
        IsReviewingDuplicates = !IsReviewingDuplicates;

        if (IsReviewingDuplicates)
        {
            LoadDuplicates();
        }
    }

    private async Task MergeAsync(object? parameter)
    {
        if (parameter is not ClientDuplicateCandidate candidate)
        {
            return;
        }

        var target = candidate.SuggestedTarget;
        var source = candidate.SuggestedSource;

        var confirmed = await AppHost.DialogService.ConfirmAsync(
            "Fusionar las dos fichas",
            $"Los {Phrases.Count(source.QuoteCount, "trabajo", "trabajos")} de «{source.Name}» " +
            $"pasan a «{target.Name}», que queda como ficha única.\n\n" +
            "El nombre escrito en cada presupuesto ya entregado no cambia. " +
            $"«{source.Name}» queda archivada, no se borra.",
            confirmText: "Fusionar",
            isDestructive: true);

        if (!confirmed)
        {
            return;
        }

        try
        {
            var moved = AppHost.ClientService.Merge(source.Id, target.Id);

            AppHost.NotificationService.Success(
                $"Fichas fusionadas. Se movieron {Phrases.Count(moved, "trabajo", "trabajos")} a «{target.Name}».");

            LoadClients();
        }
        catch (Exception ex)
        {
            AppHost.NotificationService.Error(ex.Message, ex);
        }
    }

    /// <summary>
    /// «Son distintas». Se recuerda para no volver a proponer el mismo par: si la revisión
    /// repite lo que ya se descartó, se termina ignorando entera.
    /// </summary>
    private void DismissPair(object? parameter)
    {
        if (parameter is not ClientDuplicateCandidate candidate)
        {
            return;
        }

        AppHost.SettingsService.Update(settings =>
        {
            if (!settings.DismissedClientPairs.Contains(candidate.PairKey))
            {
                settings.DismissedClientPairs.Add(candidate.PairKey);
            }
        });

        Duplicates.Remove(candidate);
        OnPropertyChanged(nameof(HasDuplicates));
        OnPropertyChanged(nameof(DuplicatesSummary));
    }
}
