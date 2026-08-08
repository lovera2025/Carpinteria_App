using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using MetroCarpinteria.App.Data.Entities;
using MetroCarpinteria.App.Helpers;
using MetroCarpinteria.App.Models;
using MetroCarpinteria.App.Services;

namespace MetroCarpinteria.App.ViewModels;

public class ProjectsViewModel : ObservableObject
{
    private readonly Action _onDataChanged;
    private ProjectListItem? _selectedProject;
    private string _searchText = string.Empty;
    private bool _showArchived;
    private ProjectStatusOption? _selectedStatusFilter;
    private bool _isFormOpen;
    private bool _isCreating;
    private string _formTitle = string.Empty;
    private string _formClientName = string.Empty;
    private string _formDescription = string.Empty;
    private string _formBudget = string.Empty;
    private ProjectStatusOption? _formStatus;
    private ProductListItem? _selectedProduct;
    private string _materialQuantity = string.Empty;
    private EmployeeListItem? _selectedEmployee;
    private string _assignmentNotes = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isStatusError;
    private QuoteDetail? _quote;

    public ProjectsViewModel(Action onDataChanged)
    {
        _onDataChanged = onDataChanged;
        Projects = new ObservableCollection<ProjectListItem>();
        Materials = new ObservableCollection<ProjectMaterialItem>();
        Assignments = new ObservableCollection<ProjectAssignmentItem>();
        QuoteLines = new ObservableCollection<BudgetBreakdownLine>();
        AvailableProducts = new ObservableCollection<ProductListItem>();
        AvailableEmployees = new ObservableCollection<EmployeeListItem>();
        StatusFilterOptions = ProjectStatusHelper.GetFilterOptions();
        FormStatusOptions = ProjectStatusHelper.GetEditOptions();
        _selectedStatusFilter = StatusFilterOptions[0];
        _formStatus = FormStatusOptions[0];

        LoadCommand = new RelayCommand(_ => Load());
        NewProjectCommand = new RelayCommand(_ => StartNew());
        EditProjectCommand = new RelayCommand(_ => StartEdit(), _ => SelectedProject is not null);
        SaveProjectCommand = new RelayCommand(_ => SaveProject());
        CancelFormCommand = new RelayCommand(_ => CloseForm());
        ArchiveProjectCommand = new RelayCommand(_ => ArchiveSelected(), _ => CanArchiveSelected);
        RestoreProjectCommand = new RelayCommand(_ => RestoreSelected(), _ => CanRestoreSelected);
        DeleteProjectCommand = new RelayCommand(_ => DeleteSelected(), _ => CanDeleteSelected);
        AssignMaterialCommand = new RelayCommand(_ => AssignMaterial(), _ => CanAssignToProject);
        AssignEmployeeCommand = new RelayCommand(_ => AssignEmployee(), _ => CanAssignToProject);
        RemoveMaterialCommand = new RelayCommand(p => RemoveMaterial(p), _ => CanAssignToProject);
        RemoveAssignmentCommand = new RelayCommand(p => RemoveAssignment(p), _ => SelectedProject is not null);
        PrintQuoteCommand = new RelayCommand(_ => PrintQuote(), _ => HasQuote);
    }

    public ObservableCollection<ProjectListItem> Projects { get; }
    public ObservableCollection<ProjectMaterialItem> Materials { get; }
    public ObservableCollection<ProjectAssignmentItem> Assignments { get; }
    public ObservableCollection<BudgetBreakdownLine> QuoteLines { get; }
    public ObservableCollection<ProductListItem> AvailableProducts { get; }
    public ObservableCollection<EmployeeListItem> AvailableEmployees { get; }
    public IReadOnlyList<ProjectStatusOption> StatusFilterOptions { get; }
    public IReadOnlyList<ProjectStatusOption> FormStatusOptions { get; }

    public ProjectListItem? SelectedProject
    {
        get => _selectedProject;
        set
        {
            if (!SetProperty(ref _selectedProject, value))
            {
                return;
            }

            LoadProjectDetails();
            OnPropertyChanged(nameof(CanArchiveSelected));
            OnPropertyChanged(nameof(CanRestoreSelected));
            OnPropertyChanged(nameof(CanDeleteSelected));
            OnPropertyChanged(nameof(CanAssignToProject));
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                LoadProjects();
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
                LoadProjects();
            }
        }
    }

    public ProjectStatusOption? SelectedStatusFilter
    {
        get => _selectedStatusFilter;
        set
        {
            if (SetProperty(ref _selectedStatusFilter, value))
            {
                LoadProjects();
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

    public string FormHeader => IsCreating ? "Nuevo proyecto" : "Editar proyecto";

    public string FormTitle
    {
        get => _formTitle;
        set => SetProperty(ref _formTitle, value);
    }

    public string FormClientName
    {
        get => _formClientName;
        set => SetProperty(ref _formClientName, value);
    }

    public string FormDescription
    {
        get => _formDescription;
        set => SetProperty(ref _formDescription, value);
    }

    public string FormBudget
    {
        get => _formBudget;
        set => SetProperty(ref _formBudget, value);
    }

    public ProjectStatusOption? FormStatus
    {
        get => _formStatus;
        set => SetProperty(ref _formStatus, value);
    }

    public ProductListItem? SelectedProduct
    {
        get => _selectedProduct;
        set => SetProperty(ref _selectedProduct, value);
    }

    public string MaterialQuantity
    {
        get => _materialQuantity;
        set => SetProperty(ref _materialQuantity, value);
    }

    public EmployeeListItem? SelectedEmployee
    {
        get => _selectedEmployee;
        set => SetProperty(ref _selectedEmployee, value);
    }

    public string AssignmentNotes
    {
        get => _assignmentNotes;
        set => SetProperty(ref _assignmentNotes, value);
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

    public bool CanArchiveSelected => SelectedProject is { IsArchived: false };
    public bool CanRestoreSelected => SelectedProject is { IsArchived: true };
    public bool CanDeleteSelected => SelectedProject is not null
        && !AppHost.ProjectService.HasDependencies(SelectedProject.Id);
    public bool CanAssignToProject => SelectedProject is { IsArchived: false };

    public ICommand LoadCommand { get; }
    public ICommand NewProjectCommand { get; }
    public ICommand EditProjectCommand { get; }
    public ICommand SaveProjectCommand { get; }
    public ICommand CancelFormCommand { get; }
    public ICommand ArchiveProjectCommand { get; }
    public ICommand RestoreProjectCommand { get; }
    public ICommand DeleteProjectCommand { get; }
    public ICommand AssignMaterialCommand { get; }
    public ICommand AssignEmployeeCommand { get; }
    public ICommand RemoveMaterialCommand { get; }
    public ICommand RemoveAssignmentCommand { get; }
    public ICommand PrintQuoteCommand { get; }

    /// <summary>Presupuesto que originó el proyecto. Solo lectura: acá ya no se edita.</summary>
    public QuoteDetail? Quote
    {
        get => _quote;
        private set
        {
            if (SetProperty(ref _quote, value))
            {
                OnPropertyChanged(nameof(HasQuote));
                OnPropertyChanged(nameof(QuoteMaterialsDisplay));
                OnPropertyChanged(nameof(QuoteFinalPriceDisplay));
                OnPropertyChanged(nameof(QuoteIssuedDisplay));
            }
        }
    }

    public bool HasQuote => Quote is not null && (Quote.Lines.Count > 0 || Quote.Breakdown is not null);
    public string QuoteMaterialsDisplay => Quote?.MaterialsTotalDisplay ?? "—";
    public string QuoteFinalPriceDisplay => Quote?.BudgetDisplay ?? "—";

    public string QuoteIssuedDisplay => Quote?.QuotedAtLocal is null
        ? string.Empty
        : $"Cotizado el {AppCulture.ShortDate(Quote.QuotedAtLocal.Value)}";

    public void Load()
    {
        LoadPickers();
        LoadProjects();
    }

    private void LoadPickers()
    {
        AvailableProducts.Clear();
        foreach (var product in AppHost.InventoryService.GetProducts(false, false, null))
        {
            AvailableProducts.Add(product);
        }

        AvailableEmployees.Clear();
        foreach (var employee in AppHost.EmployeeService.GetEmployees(false, null))
        {
            AvailableEmployees.Add(employee);
        }

        SelectedProduct = AvailableProducts.FirstOrDefault();
        SelectedEmployee = AvailableEmployees.FirstOrDefault();
    }

    private void LoadProjects()
    {
        var items = AppHost.ProjectService.GetProjects(
            ShowArchived,
            SelectedStatusFilter?.Status,
            SearchText);

        var selectedId = SelectedProject?.Id;
        Projects.Clear();
        foreach (var item in items)
        {
            Projects.Add(item);
        }

        SelectedProject = selectedId.HasValue
            ? Projects.FirstOrDefault(p => p.Id == selectedId.Value)
            : Projects.FirstOrDefault();

        _onDataChanged();
        CommandManager.InvalidateRequerySuggested();
    }

    private void LoadProjectDetails()
    {
        Materials.Clear();
        Assignments.Clear();
        QuoteLines.Clear();

        if (SelectedProject is null)
        {
            Quote = null;
            return;
        }

        Quote = AppHost.QuoteService.GetDetail(SelectedProject.Id);

        if (Quote?.Breakdown is not null)
        {
            foreach (var line in Quote.Breakdown.Lines.Where(l => !l.IsTotal))
            {
                QuoteLines.Add(line);
            }
        }

        foreach (var material in AppHost.ProjectService.GetProjectMaterials(SelectedProject.Id))
        {
            Materials.Add(material);
        }

        foreach (var assignment in AppHost.ProjectService.GetProjectAssignments(SelectedProject.Id))
        {
            Assignments.Add(assignment);
        }
    }

    private void StartNew()
    {
        FormTitle = string.Empty;
        FormClientName = string.Empty;
        FormDescription = string.Empty;
        FormBudget = string.Empty;
        FormStatus = FormStatusOptions.First(o => o.Status == ProjectStatus.Quote);
        IsCreating = true;
        IsFormOpen = true;
        ClearStatus();
    }

    private void StartEdit()
    {
        if (SelectedProject is null)
        {
            return;
        }

        FormTitle = SelectedProject.Title;
        FormClientName = SelectedProject.ClientName;
        FormDescription = SelectedProject.Description ?? string.Empty;
        FormBudget = SelectedProject.Budget?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
        FormStatus = FormStatusOptions.First(o => o.Status == SelectedProject.Status);
        IsCreating = false;
        IsFormOpen = true;
        ClearStatus();
    }

    private void SaveProject()
    {
        try
        {
            decimal? budget = null;
            if (!string.IsNullOrWhiteSpace(FormBudget))
            {
                if (!NumberInput.TryParseDecimal(FormBudget, out var parsedBudget))
                {
                    throw new InvalidOperationException("Presupuesto inválido.");
                }

                if (parsedBudget < 0)
                {
                    throw new InvalidOperationException("El presupuesto no puede ser negativo.");
                }

                budget = parsedBudget;
            }

            var status = FormStatus?.Status ?? ProjectStatus.Quote;

            if (IsCreating)
            {
                var project = AppHost.ProjectService.Create(
                    FormTitle, FormClientName, FormDescription, budget, status);
                SetStatus($"Proyecto «{project.Title}» creado.", isError: false);
            }
            else if (SelectedProject is not null)
            {
                AppHost.ProjectService.Update(
                    SelectedProject.Id, FormTitle, FormClientName, FormDescription, budget, status);
                SetStatus($"Proyecto «{FormTitle.Trim()}» actualizado.", isError: false);
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
        if (SelectedProject is null)
        {
            return;
        }

        if (MessageBox.Show(
                $"¿Archivar «{SelectedProject.Title}»?",
                "Confirmar",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            AppHost.ProjectService.Archive(SelectedProject.Id);
            SetStatus("Proyecto archivado.", isError: false);
            Load();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private void RestoreSelected()
    {
        if (SelectedProject is null)
        {
            return;
        }

        try
        {
            AppHost.ProjectService.Restore(SelectedProject.Id);
            SetStatus("Proyecto restaurado.", isError: false);
            Load();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private void DeleteSelected()
    {
        if (SelectedProject is null)
        {
            return;
        }

        if (MessageBox.Show(
                $"¿Eliminar «{SelectedProject.Title}» permanentemente?",
                "Confirmar",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            AppHost.ProjectService.Delete(SelectedProject.Id);
            SelectedProject = null;
            SetStatus("Proyecto eliminado.", isError: false);
            Load();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private void AssignMaterial()
    {
        if (SelectedProject is null || SelectedProduct is null)
        {
            return;
        }

        try
        {
            if (!NumberInput.TryParseDecimal(MaterialQuantity, out var quantity))
            {
                throw new InvalidOperationException("Cantidad inválida.");
            }

            AppHost.ProjectService.AssignMaterial(SelectedProject.Id, SelectedProduct.Id, quantity);
            MaterialQuantity = string.Empty;
            SetStatus("Material asignado y stock descontado.", isError: false);
            Load();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private void AssignEmployee()
    {
        if (SelectedProject is null || SelectedEmployee is null)
        {
            return;
        }

        try
        {
            AppHost.ProjectService.AssignEmployee(
                SelectedProject.Id, SelectedEmployee.Id, AssignmentNotes);
            AssignmentNotes = string.Empty;
            SetStatus($"{SelectedEmployee.FullName} asignado al proyecto.", isError: false);
            Load();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private void RemoveMaterial(object? parameter)
    {
        if (parameter is not ProjectMaterialItem material)
        {
            return;
        }

        if (MessageBox.Show(
                $"¿Quitar «{material.ProductName}» ({material.QuantityDisplay}) del proyecto?\n\nSe devolverá al inventario.",
                "Confirmar",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            AppHost.ProjectService.RemoveMaterial(material.Id);
            SetStatus("Material quitado y stock devuelto al inventario.", isError: false);
            Load();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private void RemoveAssignment(object? parameter)
    {
        if (parameter is not ProjectAssignmentItem assignment)
        {
            return;
        }

        if (MessageBox.Show(
                $"¿Quitar a {assignment.EmployeeName} del proyecto?",
                "Confirmar",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            AppHost.ProjectService.RemoveAssignment(assignment.Id);
            SetStatus("Asignación eliminada.", isError: false);
            Load();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private void PrintQuote()
    {
        if (Quote is null)
        {
            return;
        }

        try
        {
            var document = AppHost.QuoteDocumentService.BuildClientQuote(Quote, includeMaterialDetail: true);

            if (AppHost.QuoteDocumentService.Print(document, $"Presupuesto {Quote.Id:0000}"))
            {
                SetStatus("Presupuesto enviado a la impresora.", isError: false);
            }
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
