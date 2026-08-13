using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using MetroCarpinteria.App.Helpers;
using MetroCarpinteria.App.Models;
using MetroCarpinteria.App.Services;

namespace MetroCarpinteria.App.ViewModels;

public class StaffViewModel : ViewModelBase
{
    private readonly Action _onDataChanged;

    /// <summary>Agrupa el tecleo del buscador para no consultar la base letra por letra.</summary>
    private readonly Debouncer _searchDebouncer = new();

    private EmployeeListItem? _selectedEmployee;
    private string _searchText = string.Empty;
    private bool _showArchived;
    private bool _isFormOpen;
    private bool _isCreating;
    private string _formFullName = string.Empty;
    private string _formPhone = string.Empty;
    private string _formRole = string.Empty;
    private string _formDailyRate = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isStatusError;

    /// <summary>
    /// Por qué no se puede borrar el empleado elegido, o null si se puede. Se calcula una
    /// vez por selección y no dentro del predicado del comando, que corría con cada tecla.
    /// </summary>
    private string? _deleteBlockReason;

    public StaffViewModel(Action onDataChanged)
    {
        _onDataChanged = onDataChanged;
        Employees = new ObservableCollection<EmployeeListItem>();
        Assignments = new ObservableCollection<ProjectAssignmentItem>();

        LoadCommand = new RelayCommand(_ => Load());
        NewEmployeeCommand = new RelayCommand(_ => StartNew());
        EditEmployeeCommand = new RelayCommand(_ => StartEdit(), _ => SelectedEmployee is not null);
        SaveEmployeeCommand = new RelayCommand(_ => SaveEmployee());
        CancelFormCommand = new RelayCommand(_ => CloseForm());
        ArchiveEmployeeCommand = new AsyncRelayCommand(ArchiveSelectedAsync, () => CanArchiveSelected);
        RestoreEmployeeCommand = new RelayCommand(_ => RestoreSelected(), _ => CanRestoreSelected);
        DeleteEmployeeCommand = new AsyncRelayCommand(
            DeleteSelectedAsync, () => CanDeleteSelected, observeRequery: false);
    }

    public ObservableCollection<EmployeeListItem> Employees { get; }
    public ObservableCollection<ProjectAssignmentItem> Assignments { get; }

    public EmployeeListItem? SelectedEmployee
    {
        get => _selectedEmployee;
        set
        {
            if (!SetProperty(ref _selectedEmployee, value))
            {
                return;
            }

            RefreshDeleteBlockReason();
            LoadAssignments();
            OnPropertyChanged(nameof(CanArchiveSelected));
            OnPropertyChanged(nameof(CanRestoreSelected));
        }
    }

    private void RefreshDeleteBlockReason()
    {
        _deleteBlockReason = SelectedEmployee is null
            ? "Elegí a alguien de la lista."
            : AppHost.EmployeeService.DescribeDeleteBlock(SelectedEmployee.Id);

        OnPropertyChanged(nameof(CanDeleteSelected));
        OnPropertyChanged(nameof(DeleteBlockTooltip));
        (DeleteEmployeeCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                _searchDebouncer.Run(LoadEmployees);
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
                LoadEmployees();
            }
        }
    }

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

    public string FormHeader => IsCreating ? "Nuevo empleado" : "Editar empleado";

    public string FormFullName
    {
        get => _formFullName;
        set => SetProperty(ref _formFullName, value);
    }

    public string FormPhone
    {
        get => _formPhone;
        set => SetProperty(ref _formPhone, value);
    }

    public string FormRole
    {
        get => _formRole;
        set => SetProperty(ref _formRole, value);
    }

    /// <summary>
    /// Lo que cobra por día. Vacío es válido: se puede dar de alta a alguien sin haber
    /// arreglado el jornal todavía, y se escribe a mano al cotizarlo.
    /// </summary>
    public string FormDailyRate
    {
        get => _formDailyRate;
        set => SetProperty(ref _formDailyRate, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsStatusError
    {
        get => _isStatusError;
        private set => SetProperty(ref _isStatusError, value);
    }

    public bool CanArchiveSelected => SelectedEmployee is { IsArchived: false };
    public bool CanRestoreSelected => SelectedEmployee is { IsArchived: true };
    public bool CanDeleteSelected => SelectedEmployee is not null && _deleteBlockReason is null;

    /// <summary>Lo que dice «Eliminar» al pasarle el mouse, sobre todo cuando está gris.</summary>
    public string DeleteBlockTooltip =>
        _deleteBlockReason ?? "Elimina al empleado definitivamente.";

    public ICommand LoadCommand { get; }
    public ICommand NewEmployeeCommand { get; }
    public ICommand EditEmployeeCommand { get; }
    public ICommand SaveEmployeeCommand { get; }
    public ICommand CancelFormCommand { get; }
    public ICommand ArchiveEmployeeCommand { get; }
    public ICommand RestoreEmployeeCommand { get; }
    public ICommand DeleteEmployeeCommand { get; }

    public void Load() => SafeLoad(
        () =>
        {
            LoadEmployees();
            _onDataChanged();
        },
        "Personal");

    private void LoadEmployees()
    {
        var items = AppHost.EmployeeService.GetEmployees(ShowArchived, SearchText);
        var selectedId = SelectedEmployee?.Id;

        Employees.Clear();
        foreach (var item in items)
        {
            Employees.Add(item);
        }

        SelectedEmployee = selectedId.HasValue
            ? Employees.FirstOrDefault(e => e.Id == selectedId.Value)
            : Employees.FirstOrDefault();

        // Explícito: si la selección quedó igual, el setter no corre y el motivo del
        // bloqueo quedaría con lo que valía antes de recargar.
        RefreshDeleteBlockReason();
        CommandManager.InvalidateRequerySuggested();
    }

    private void LoadAssignments()
    {
        Assignments.Clear();
        if (SelectedEmployee is null)
        {
            return;
        }

        foreach (var assignment in AppHost.EmployeeService.GetEmployeeAssignments(SelectedEmployee.Id))
        {
            Assignments.Add(assignment);
        }
    }

    private void StartNew()
    {
        FormFullName = string.Empty;
        FormPhone = string.Empty;
        FormRole = string.Empty;
        FormDailyRate = string.Empty;
        IsCreating = true;
        IsFormOpen = true;
        ClearStatus();
    }

    private void StartEdit()
    {
        if (SelectedEmployee is null)
        {
            return;
        }

        FormFullName = SelectedEmployee.FullName;
        FormPhone = SelectedEmployee.Phone ?? string.Empty;
        FormRole = SelectedEmployee.Role ?? string.Empty;
        FormDailyRate = SelectedEmployee.DailyRate.HasValue
            ? NumberInput.Format(SelectedEmployee.DailyRate.Value)
            : string.Empty;
        IsCreating = false;
        IsFormOpen = true;
        ClearStatus();
    }

    private void SaveEmployee()
    {
        try
        {
            var dailyRate = ReadDailyRate();

            if (IsCreating)
            {
                var employee = AppHost.EmployeeService.Create(FormFullName, FormPhone, FormRole, dailyRate);
                SetStatus($"Empleado «{employee.FullName}» creado.", isError: false);
            }
            else if (SelectedEmployee is not null)
            {
                AppHost.EmployeeService.Update(
                    SelectedEmployee.Id, FormFullName, FormPhone, FormRole, dailyRate);
                SetStatus($"Empleado «{FormFullName.Trim()}» actualizado.", isError: false);
            }

            CloseForm();
            Load();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    /// <summary>El jornal tipeado, o null si está vacío. Un valor ilegible sí es un error.</summary>
    private decimal? ReadDailyRate()
    {
        if (string.IsNullOrWhiteSpace(FormDailyRate))
        {
            return null;
        }

        return NumberInput.ParseMoneyOrThrow(FormDailyRate, "Jornal por día");
    }

    private void CloseForm() => IsFormOpen = false;

    private async Task ArchiveSelectedAsync()
    {
        if (SelectedEmployee is null)
        {
            return;
        }

        var name = SelectedEmployee.FullName;

        var confirmed = await AppHost.DialogService.ConfirmAsync(
            "Archivar a esta persona",
            $"«{name}» va a dejar de aparecer para asignar a proyectos.\n\n" +
            "Las asignaciones que ya tiene se conservan.",
            confirmText: "Archivar");

        if (!confirmed)
        {
            return;
        }

        try
        {
            AppHost.EmployeeService.Archive(SelectedEmployee.Id);
            AppHost.NotificationService.Success($"«{name}» quedó archivado.");
            Load();
        }
        catch (Exception ex)
        {
            AppHost.NotificationService.Error(ex.Message, ex);
        }
    }

    private void RestoreSelected()
    {
        if (SelectedEmployee is null)
        {
            return;
        }

        try
        {
            AppHost.EmployeeService.Restore(SelectedEmployee.Id);
            SetStatus("Empleado restaurado.", isError: false);
            Load();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private async Task DeleteSelectedAsync()
    {
        if (SelectedEmployee is null)
        {
            return;
        }

        var name = SelectedEmployee.FullName;

        var confirmed = await AppHost.DialogService.ConfirmAsync(
            "Eliminar a esta persona",
            $"Se va a borrar «{name}» del listado de personal.\n\n" +
            "Esto no se puede deshacer. Si solo dejó de trabajar acá, conviene archivarlo.",
            confirmText: "Eliminar",
            isDestructive: true);

        if (!confirmed)
        {
            return;
        }

        try
        {
            AppHost.EmployeeService.Delete(SelectedEmployee.Id);
            SelectedEmployee = null;
            AppHost.NotificationService.Success($"«{name}» se eliminó del listado.");
            Load();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    /// <summary>
    /// Publica el resultado de una acción.
    /// <para>
    /// Antes esto llenaba una barra fija arriba de la pantalla que <b>nunca se borraba</b>:
    /// un "Producto creado." quedaba ahí para siempre, y al rato no se sabía si
    /// correspondía a lo de recién o a algo de veinte minutos antes. Ahora va al aviso
    /// flotante, que se descarta solo.
    /// </para>
    /// <para>
    /// StatusMessage se sigue actualizando porque los tests lo leen para verificar qué
    /// pasó tras una acción.
    /// </para>
    /// </summary>
    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        IsStatusError = isError;

        if (string.IsNullOrWhiteSpace(message) || !AppHost.IsReady)
        {
            return;
        }

        if (isError)
        {
            AppHost.NotificationService.Warning(message);
        }
        else
        {
            AppHost.NotificationService.Success(message);
        }
    }

    private void ClearStatus()
    {
        StatusMessage = string.Empty;
        IsStatusError = false;
    }
}
