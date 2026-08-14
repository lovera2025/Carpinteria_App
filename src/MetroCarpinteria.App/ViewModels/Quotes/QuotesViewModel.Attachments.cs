using System.Windows.Input;
using MetroCarpinteria.App.Helpers;
using MetroCarpinteria.App.Models;
using MetroCarpinteria.App.Services;

namespace MetroCarpinteria.App.ViewModels;

/// <summary>
/// Presupuestos colgados del abierto: otros trabajos del mismo cliente, sin mezclar totales.
/// </summary>
public partial class QuotesViewModel
{
    public bool CanManageAttachments => Detail is { IsArchived: false };

    public bool HasAttachments => Attachments.Count > 0;

    public string AttachmentsSummary
    {
        get
        {
            if (Detail is null)
            {
                return string.Empty;
            }

            if (Attachments.Count == 0)
            {
                return "Ningún presupuesto adjunto";
            }

            return Phrases.Count(Attachments.Count, "trabajo adjunto", "trabajos adjuntos") +
                   " · no entran en el total de éste";
        }
    }

    public bool IsAttachmentPickerOpen
    {
        get => _isAttachmentPickerOpen;
        private set
        {
            if (SetProperty(ref _isAttachmentPickerOpen, value))
            {
                OnPropertyChanged(nameof(HasAttachableQuotes));
            }
        }
    }

    public bool HasAttachableQuotes => AttachableQuotes.Count > 0;

    public bool IsSiblingFormOpen
    {
        get => _isSiblingFormOpen;
        private set => SetProperty(ref _isSiblingFormOpen, value);
    }

    public string SiblingTitle
    {
        get => _siblingTitle;
        set
        {
            if (SetProperty(ref _siblingTitle, value))
            {
                OnPropertyChanged(nameof(CanCreateSibling));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool CanCreateSibling =>
        CanManageAttachments && !string.IsNullOrWhiteSpace(SiblingTitle);

    private void ReloadAttachments()
    {
        Attachments.Clear();
        CloseAttachmentPicker();
        CloseSiblingForm();

        if (Detail is null)
        {
            OnPropertyChanged(nameof(HasAttachments));
            OnPropertyChanged(nameof(AttachmentsSummary));
            return;
        }

        foreach (var item in Detail.Attachments)
        {
            Attachments.Add(item);
        }

        OnPropertyChanged(nameof(HasAttachments));
        OnPropertyChanged(nameof(AttachmentsSummary));
    }

    private void OpenAttachmentPicker()
    {
        if (Detail is null)
        {
            return;
        }

        try
        {
            AttachableQuotes.Clear();
            foreach (var item in AppHost.QuoteService.GetAttachableQuotes(Detail.Id))
            {
                AttachableQuotes.Add(item);
            }

            IsAttachmentPickerOpen = true;
            OnPropertyChanged(nameof(HasAttachableQuotes));

            if (AttachableQuotes.Count == 0)
            {
                SetStatus(
                    "No hay otros presupuestos de este cliente para adjuntar. Creá otro trabajo o guardá la ficha del cliente.",
                    isError: false);
            }
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private void CloseAttachmentPicker()
    {
        IsAttachmentPickerOpen = false;
        AttachableQuotes.Clear();
        OnPropertyChanged(nameof(HasAttachableQuotes));
    }

    private void AttachQuote(object? parameter)
    {
        if (Detail is null || parameter is not QuoteListItem item)
        {
            return;
        }

        try
        {
            AppHost.QuoteService.AttachQuote(Detail.Id, item.Id);
            CloseAttachmentPicker();
            ReloadListAndDetail();
            SetStatus($"«{item.Title}» quedó adjunto a este presupuesto.", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private void DetachQuote(object? parameter)
    {
        if (Detail is null || parameter is not QuoteAttachmentItem item)
        {
            return;
        }

        try
        {
            AppHost.QuoteService.DetachQuote(Detail.Id, item.AttachmentId);
            ReloadListAndDetail();
            SetStatus($"Se quitó «{item.Title}» de los adjuntos.", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private void OpenSiblingForm()
    {
        SiblingTitle = string.Empty;
        IsSiblingFormOpen = true;
        CloseAttachmentPicker();
    }

    private void CloseSiblingForm()
    {
        IsSiblingFormOpen = false;
        SiblingTitle = string.Empty;
    }

    private void CreateSiblingQuote()
    {
        if (Detail is null)
        {
            return;
        }

        try
        {
            var id = AppHost.QuoteService.CreateSiblingQuote(Detail.Id, SiblingTitle);
            CloseSiblingForm();
            LoadQuotes();
            SelectedQuote = Quotes.FirstOrDefault(q => q.Id == id);
            SetStatus("Presupuesto creado y adjunto al anterior. Cargá materiales y precio.", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }
}
