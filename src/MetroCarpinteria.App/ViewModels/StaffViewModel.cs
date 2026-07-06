using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using MetroCarpinteria.App.Helpers;
using MetroCarpinteria.App.Models;
using MetroCarpinteria.App.Services;

namespace MetroCarpinteria.App.ViewModels;

public class StaffViewModel : ObservableObject
{
    private readonly Action _onDataChanged;
    private EmployeeListItem? _selectedEmployee;
    private string _searchText = string.Empty;
    private bool _showArchived;
    private bool _isFormOpen;
    private bool _isCreating;
    private string _formFullName = string.Empty;
    private string _formPhone = string.Empty;
    private string _formRole = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isStatusError;

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
        ArchiveEmployeeCommand = new RelayCommand(_ => ArchiveSelected(), _ => CanArchiveSelected);
        RestoreEmployeeCommand = new RelayCommand(_ => RestoreSelected(), _ => CanRestoreSelected);
        DeleteEmployeeCommand = new RelayCommand(_ => DeleteSelected(), _ => CanDeleteSelected);
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

            LoadAssignments();
            OnPropertyChanged(nameof(CanArchiveSelected));
            OnPropertyChanged(nameof(CanRestoreSelected));
            OnPropertyChanged(nameof(CanDeleteSelected));
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                LoadEmployees();
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
    public bool CanDeleteSelected => SelectedEmployee is not null
        && !AppHost.EmployeeService.HasAssignments(SelectedEmployee.Id);

    public ICommand LoadCommand { get; }
    public ICommand NewEmployeeCommand { get; }
    public ICommand EditEmployeeCommand { get; }
    public ICommand SaveEmployeeCommand { get; }
    public ICommand CancelFormCommand { get; }
    public ICommand ArchiveEmployeeCommand { get; }
    public ICommand RestoreEmployeeCommand { get; }
    public ICommand DeleteEmployeeCommand { get; }

    public void Load()
    {
        LoadEmployees();
        _onDataChanged();
    }

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
        IsCreating = false;
        IsFormOpen = true;
        ClearStatus();
    }

    private void SaveEmployee()
    {
        try
        {
            if (IsCreating)
            {
                var employee = AppHost.EmployeeService.Create(FormFullName, FormPhone, FormRole);
                SetStatus($"Empleado «{employee.FullName}» creado.", isError: false);
            }
            else if (SelectedEmployee is not null)
            {
                AppHost.EmployeeService.Update(
                    SelectedEmployee.Id, FormFullName, FormPhone, FormRole);
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

    private void CloseForm() => IsFormOpen = false;

    private void ArchiveSelected()
    {
        if (SelectedEmployee is null)
        {
            return;
        }

        if (MessageBox.Show(
                $"¿Archivar a «{SelectedEmployee.FullName}»?",
                "Confirmar",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            AppHost.EmployeeService.Archive(SelectedEmployee.Id);
            SetStatus("Empleado archivado.", isError: false);
            Load();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
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

    private void DeleteSelected()
    {
        if (SelectedEmployee is null)
        {
            return;
        }

        if (MessageBox.Show(
                $"¿Eliminar a «{SelectedEmployee.FullName}» permanentemente?",
                "Confirmar",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            AppHost.EmployeeService.Delete(SelectedEmployee.Id);
            SelectedEmployee = null;
            SetStatus("Empleado eliminado.", isError: false);
            Load();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        IsStatusError = isError;
    }

    private void ClearStatus()
    {
        StatusMessage = string.Empty;
        IsStatusError = false;
    }
}
