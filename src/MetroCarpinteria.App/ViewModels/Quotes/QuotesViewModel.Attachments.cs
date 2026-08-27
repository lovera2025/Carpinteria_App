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
                   (IncludeAttachmentsInTotal
                       ? " · suman al total de éste"
                       : " · no entran en el total de éste");
        }
    }

    /// <summary>
    /// Si los adjuntos suman al TOTAL del papel del cliente.
    /// </summary>
    /// <remarks>
    /// Apagado por defecto: adjuntar es agrupar los trabajos de un cliente en una sola hoja,
    /// que no es lo mismo que cobrarlos juntos. Prenderlo cambia el número grande que el
    /// cliente lee, así que es una decisión aparte y explícita.
    /// </remarks>
    public bool IncludeAttachmentsInTotal
    {
        get => Detail?.IncludeAttachmentsInTotal ?? false;
        set
        {
            if (Detail is not null && Detail.IncludeAttachmentsInTotal != value)
            {
                try
                {
                    AppHost.QuoteService.SaveIncludeAttachmentsInTotal(Detail.Id, value);
                    LoadDetail();
                }
                catch (Exception ex)
                {
                    SetStatus(ex.Message, isError: true);
                }
            }

            // Siempre, incluso cuando no se guardó nada: la vista ya movió el tilde por su
            // cuenta y hay que mandarla a releer el valor real. Sin esto quedaba marcado en
            // pantalla mientras la base decía que no, y el PDF salía con el total viejo.
            NotifyAttachmentsTotalChanged();
        }
    }

    /// <summary>
    /// El tilde solo se puede tocar mientras el presupuesto sea editable.
    /// </summary>
    /// <remarks>
    /// Va aparte de <see cref="CanManageAttachments"/>: adjuntar y desadjuntar siguen
    /// andando sobre un aprobado, pero el servicio no deja cambiar el número que se
    /// entrega —«una vez aprobado, el papel que firmó el cliente no se retoca»—. El
    /// checkbox se colgaba de CanManageAttachments y quedaba habilitado para tirar un error.
    /// </remarks>
    public bool CanIncludeAttachmentsInTotal => Detail is { IsEditable: true };

    private void NotifyAttachmentsTotalChanged()
    {
        OnPropertyChanged(nameof(IncludeAttachmentsInTotal));
        OnPropertyChanged(nameof(AttachmentsSummary));
        OnPropertyChanged(nameof(AttachmentsTotalHint));
    }

    /// <summary>Lo que el papel va a decir, para que el tilde no se pruebe imprimiendo.</summary>
    public string AttachmentsTotalHint
    {
        get
        {
            if (Detail is null || Attachments.Count == 0)
            {
                return string.Empty;
            }

            return IncludeAttachmentsInTotal
                ? $"El TOTAL del PDF va a decir {Detail.PrintedTotalDisplay}, sumando este trabajo y los adjuntos."
                : $"El TOTAL del PDF va a decir {Detail.BudgetDisplay}, solo este trabajo.";
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
        OnPropertyChanged(nameof(IncludeAttachmentsInTotal));
        OnPropertyChanged(nameof(AttachmentsTotalHint));
        OnPropertyChanged(nameof(CanIncludeAttachmentsInTotal));
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
            SelectEnsuringVisible(id);
            SetStatus("Presupuesto creado y adjunto al anterior. Cargá materiales y precio.", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }
}
