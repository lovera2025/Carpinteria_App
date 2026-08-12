using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using MetroCarpinteria.App.Data.Entities;
using MetroCarpinteria.App.Helpers;
using MetroCarpinteria.App.Models;
using MetroCarpinteria.App.Services;

namespace MetroCarpinteria.App.ViewModels;

public class ProjectsViewModel : ViewModelBase
{
    private readonly Action _onDataChanged;

    /// <summary>Agrupa el tecleo del buscador para no consultar la base letra por letra.</summary>
    private readonly Debouncer _searchDebouncer = new();

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
    private IReadOnlyList<ProjectStatusOption> _formStatusOptions;

    /// <summary>
    /// Por qué no se puede borrar el proyecto elegido, o null si se puede. Se calcula una
    /// vez por selección: dentro del predicado del comando eran tres consultas a SQLite
    /// por cada tecla tipeada en el buscador.
    /// </summary>
    private string? _deleteBlockReason;

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
        _formStatusOptions = ProjectStatusHelper.GetEditOptions();
        _selectedStatusFilter = StatusFilterOptions[0];
        _formStatus = _formStatusOptions[0];

        LoadCommand = new RelayCommand(_ => Load());
        NewProjectCommand = new RelayCommand(_ => StartNew());
        EditProjectCommand = new RelayCommand(_ => StartEdit(), _ => SelectedProject is not null);
        SaveProjectCommand = new RelayCommand(_ => SaveProject());
        CancelFormCommand = new RelayCommand(_ => CloseForm());
        ArchiveProjectCommand = new AsyncRelayCommand(ArchiveSelectedAsync, () => CanArchiveSelected);
        RestoreProjectCommand = new RelayCommand(_ => RestoreSelected(), _ => CanRestoreSelected);
        DeleteProjectCommand = new AsyncRelayCommand(
            DeleteSelectedAsync, () => CanDeleteSelected, observeRequery: false);
        AssignMaterialCommand = new RelayCommand(_ => AssignMaterial(), _ => CanAssignToProject);
        AssignEmployeeCommand = new RelayCommand(_ => AssignEmployee(), _ => CanAssignToProject);
        RemoveMaterialCommand = new AsyncRelayCommand(RemoveMaterialAsync, () => CanAssignToProject);
        RemoveAssignmentCommand = new AsyncRelayCommand(RemoveAssignmentAsync, () => SelectedProject is not null);
        PrintQuoteCommand = new RelayCommand(_ => PrintQuote(), _ => CanPrintQuote);
        CancelProjectCommand = new AsyncRelayCommand(CancelSelectedAsync, () => CanCancelSelected);
    }

    public ObservableCollection<ProjectListItem> Projects { get; }
    public ObservableCollection<ProjectMaterialItem> Materials { get; }
    public ObservableCollection<ProjectAssignmentItem> Assignments { get; }
    public ObservableCollection<BudgetBreakdownLine> QuoteLines { get; }
    public ObservableCollection<ProductListItem> AvailableProducts { get; }
    public ObservableCollection<EmployeeListItem> AvailableEmployees { get; }
    public IReadOnlyList<ProjectStatusOption> StatusFilterOptions { get; }

    /// <summary>
    /// Los estados que se pueden elegir en el formulario. Cambia según en cuál esté el
    /// proyecto: ofrecer los cinco siempre era lo que permitía saltearse el ciclo.
    /// </summary>
    public IReadOnlyList<ProjectStatusOption> FormStatusOptions
    {
        get => _formStatusOptions;
        private set => SetProperty(ref _formStatusOptions, value);
    }

    public ProjectListItem? SelectedProject
    {
        get => _selectedProject;
        set
        {
            if (!SetProperty(ref _selectedProject, value))
            {
                return;
            }

            RefreshDeleteBlockReason();
            LoadProjectDetails();
            OnPropertyChanged(nameof(CanArchiveSelected));
            OnPropertyChanged(nameof(CanRestoreSelected));
            OnPropertyChanged(nameof(CanAssignToProject));
            OnPropertyChanged(nameof(CanCancelSelected));
        }
    }

    private void RefreshDeleteBlockReason()
    {
        _deleteBlockReason = SelectedProject is null
            ? "Elegí un proyecto de la lista."
            : AppHost.ProjectService.DescribeDeleteBlock(SelectedProject.Id);

        OnPropertyChanged(nameof(CanDeleteSelected));
        OnPropertyChanged(nameof(DeleteBlockTooltip));
        (DeleteProjectCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                _searchDebouncer.Run(LoadProjects);
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
    public bool CanDeleteSelected => SelectedProject is not null && _deleteBlockReason is null;

    /// <summary>Lo que dice «Eliminar» al pasarle el mouse, sobre todo cuando está gris.</summary>
    public string DeleteBlockTooltip =>
        _deleteBlockReason ?? "Borra el proyecto junto con su presupuesto.";
    public bool CanAssignToProject => SelectedProject is { IsArchived: false };

    /// <summary>
    /// Solo un trabajo en curso se puede cancelar. Terminado o entregado significa que
    /// el material ya se usó, y devolverlo al inventario inventaría existencias.
    /// </summary>
    public bool CanCancelSelected =>
        SelectedProject is { IsArchived: false, Status: ProjectStatus.InProgress };

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
    public ICommand CancelProjectCommand { get; }

    /// <summary>Presupuesto que originó el proyecto. Solo lectura: acá ya no se edita.</summary>
    public QuoteDetail? Quote
    {
        get => _quote;
        private set
        {
            if (SetProperty(ref _quote, value))
            {
                OnPropertyChanged(nameof(HasQuote));
                OnPropertyChanged(nameof(CanPrintQuote));
                OnPropertyChanged(nameof(QuoteMaterialsDisplay));
                OnPropertyChanged(nameof(QuoteFinalPriceDisplay));
                OnPropertyChanged(nameof(QuoteIssuedDisplay));
            }
        }
    }

    public bool HasQuote => Quote is not null && (Quote.Lines.Count > 0 || Quote.Breakdown is not null);

    /// <summary>
    /// La tarjeta del presupuesto se muestra apenas haya materiales cargados, pero
    /// imprimirlo exige precio y desglose: si no, el documento sale con el TOTAL en guión.
    /// </summary>
    public bool CanPrintQuote => Quote is { Budget: > 0, Breakdown: not null };
    public string QuoteMaterialsDisplay => Quote?.MaterialsTotalDisplay ?? "—";
    public string QuoteFinalPriceDisplay => Quote?.BudgetDisplay ?? "—";

    public string QuoteIssuedDisplay => Quote?.QuotedAtLocal is null
        ? string.Empty
        : $"Cotizado el {AppCulture.ShortDate(Quote.QuotedAtLocal.Value)}";

    public void Load() => SafeLoad(
        () =>
        {
            LoadPickers();
            LoadProjects();
        },
        "Proyectos");

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

        // Explícito: si la selección quedó igual, el setter no corre y el motivo del
        // bloqueo quedaría con lo que valía antes de recargar.
        RefreshDeleteBlockReason();
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

        // Un alta no es una transición: se puede anotar un trabajo que ya está en curso
        // o incluso entregado, para cargar historial.
        FormStatusOptions = ProjectStatusHelper.GetEditOptions();
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
        FormBudget = NumberInput.Format(SelectedProject.Budget);

        FormStatusOptions = ProjectStatusPolicy.GetManualOptions(SelectedProject.Status);
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
                if (!NumberInput.TryParseMoney(FormBudget, out var parsedBudget))
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

    private async Task ArchiveSelectedAsync()
    {
        if (SelectedProject is null)
        {
            return;
        }

        var title = SelectedProject.Title;

        var confirmed = await AppHost.DialogService.ConfirmAsync(
            "Archivar proyecto",
            $"«{title}» va a dejar de aparecer en la lista.\n\n" +
            "Los materiales y el personal asignados se conservan.",
            confirmText: "Archivar");

        if (!confirmed)
        {
            return;
        }

        try
        {
            AppHost.ProjectService.Archive(SelectedProject.Id);
            AppHost.NotificationService.Success($"«{title}» quedó archivado.");
            Load();
        }
        catch (Exception ex)
        {
            AppHost.NotificationService.Error(ex.Message, ex);
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

    private async Task DeleteSelectedAsync()
    {
        if (SelectedProject is null)
        {
            return;
        }

        var title = SelectedProject.Title;

        var confirmed = await AppHost.DialogService.ConfirmAsync(
            "Eliminar proyecto",
            $"Se va a borrar «{title}» junto con su presupuesto.\n\n" +
            "Esto no se puede deshacer. Si el trabajo ya terminó, conviene archivarlo.",
            confirmText: "Eliminar",
            isDestructive: true);

        if (!confirmed)
        {
            return;
        }

        try
        {
            AppHost.ProjectService.Delete(SelectedProject.Id);
            SelectedProject = null;
            AppHost.NotificationService.Success($"«{title}» se eliminó.");
            Load();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    /// <summary>
    /// Vuelve el trabajo a presupuesto devolviendo el material al inventario. Es el único
    /// camino de vuelta desde «En curso»: cambiar el estado a mano quedaría con el stock
    /// descontado y nada que lo reponga.
    /// </summary>
    private async Task CancelSelectedAsync()
    {
        if (SelectedProject is null)
        {
            return;
        }

        var title = SelectedProject.Title;
        var materialCount = Materials.Count;

        var detail = materialCount == 0
            ? "No hay materiales entregados que devolver."
            : materialCount == 1
                ? "El material entregado vuelve al inventario."
                : $"Los {materialCount} materiales entregados vuelven al inventario.";

        var confirmed = await AppHost.DialogService.ConfirmAsync(
            "Cancelar el trabajo",
            $"«{title}» vuelve a ser un presupuesto editable.\n\n" +
            $"{detail}\n\n" +
            "El personal asignado se conserva.",
            confirmText: "Cancelar el trabajo",
            isDestructive: true);

        if (!confirmed)
        {
            return;
        }

        try
        {
            AppHost.QuoteService.CancelApproval(SelectedProject.Id);
            AppHost.NotificationService.Success(
                $"«{title}» volvió a presupuesto y el material se devolvió al inventario.");
            Load();
        }
        catch (Exception ex)
        {
            AppHost.NotificationService.Error(ex.Message, ex);
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
            if (!NumberInput.TryParseQuantity(MaterialQuantity, out var quantity))
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

    private async Task RemoveMaterialAsync(object? parameter)
    {
        if (parameter is not ProjectMaterialItem material)
        {
            return;
        }

        var confirmed = await AppHost.DialogService.ConfirmAsync(
            "Quitar material del proyecto",
            $"«{material.ProductName}» ({material.QuantityDisplay}) vuelve al inventario.",
            confirmText: "Quitar y devolver");

        if (!confirmed)
        {
            return;
        }

        try
        {
            AppHost.ProjectService.RemoveMaterial(material.Id);
            AppHost.NotificationService.Success(
                $"«{material.ProductName}» volvió al inventario ({material.QuantityDisplay}).");
            Load();
        }
        catch (Exception ex)
        {
            AppHost.NotificationService.Error(ex.Message, ex);
        }
    }

    private async Task RemoveAssignmentAsync(object? parameter)
    {
        if (parameter is not ProjectAssignmentItem assignment)
        {
            return;
        }

        var confirmed = await AppHost.DialogService.ConfirmAsync(
            "Quitar del proyecto",
            $"{assignment.EmployeeName} deja de estar asignado a este trabajo.",
            confirmText: "Quitar");

        if (!confirmed)
        {
            return;
        }

        try
        {
            AppHost.ProjectService.RemoveAssignment(assignment.Id);
            AppHost.NotificationService.Success($"{assignment.EmployeeName} ya no está asignado.");
            Load();
        }
        catch (Exception ex)
        {
            AppHost.NotificationService.Error(ex.Message, ex);
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
