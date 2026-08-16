using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using MetroCarpinteria.App.Data.Entities;
using MetroCarpinteria.App.Helpers;
using MetroCarpinteria.App.Models;
using MetroCarpinteria.App.Services;

namespace MetroCarpinteria.App.ViewModels;

/// <summary>Aprobar, rechazar, reabrir, duplicar e imprimir.</summary>
public partial class QuotesViewModel
{
    // --- Ciclo de vida --------------------------------------------------------

    private async Task ApproveAsync()
    {
        if (Detail is null)
        {
            return;
        }

        var confirmed = await AppHost.DialogService.ConfirmAsync(
            "Aprobar el presupuesto",
            $"«{Detail.Title}» pasa a ser un trabajo en curso por {Detail.BudgetDisplay}.\n\n" +
            "Del inventario se descuenta el stock disponible de los materiales cotizados. " +
            "Lo que no alcance queda anotado como pendiente de compra.",
            confirmText: "Aprobar y descontar");

        if (!confirmed)
        {
            return;
        }

        try
        {
            var result = AppHost.QuoteService.ApproveQuote(Detail.Id);

            // Los faltantes se cargan después de recargar: al refrescar el detalle se
            // limpia la lista, así que llenarla antes la borraría sin que se vea.
            ShowApproved = true;
            ReloadListAndDetail();
            ShowShortfalls(result);

            AppHost.NotificationService.Success(result.Summary);
        }
        catch (Exception ex)
        {
            AppHost.NotificationService.Error(ex.Message, ex);
        }
    }

    private void ApplyPending()
    {
        if (Detail is null)
        {
            return;
        }

        try
        {
            var result = AppHost.QuoteService.ApplyPendingStock(Detail.Id);

            ReloadListAndDetail();
            ShowShortfalls(result);

            SetStatus(
                result.HasShortfalls
                    ? "Se descontó lo que había. Todavía faltan materiales."
                    : "Materiales pendientes descontados del inventario.",
                isError: false);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private void ShowShortfalls(QuoteApprovalResult result)
    {
        Shortfalls.Clear();
        foreach (var shortfall in result.Shortfalls)
        {
            Shortfalls.Add(shortfall);
        }
    }

    private async Task RejectAsync()
    {
        if (Detail is null)
        {
            return;
        }

        var confirmed = await AppHost.DialogService.ConfirmAsync(
            "Marcar como rechazado",
            $"«{Detail.Title}» queda guardado en el historial de lo que cotizaste.\n\n" +
            "No se toca el inventario, y lo podés reabrir cuando quieras.",
            confirmText: "Marcar rechazado");

        if (!confirmed)
        {
            return;
        }

        try
        {
            AppHost.QuoteService.RejectQuote(Detail.Id);
            ReloadListAndDetail();
            AppHost.NotificationService.Info($"«{Detail?.Title ?? "El presupuesto"}» quedó como rechazado.");
        }
        catch (Exception ex)
        {
            AppHost.NotificationService.Error(ex.Message, ex);
        }
    }

    /// <summary>
    /// Saca de la lista un presupuesto rechazado que ya no interesa.
    /// </summary>
    /// <remarks>
    /// Es el único borrado destructivo de esta pantalla, así que el diálogo dice qué se
    /// pierde y va marcado como tal. Reabrir sigue siendo la salida para el que puede
    /// volver.
    /// </remarks>
    private async Task DeleteRejectedAsync()
    {
        if (Detail is null)
        {
            return;
        }

        var title = Detail.Title;
        var id = Detail.Id;

        var confirmed = await AppHost.DialogService.ConfirmAsync(
            "Eliminar el presupuesto",
            $"«{title}» se borra junto con su desglose y sus fotos.\n\n" +
            "No se puede deshacer. Si lo querés conservar, archivalo o reabrilo.",
            confirmText: "Eliminar",
            isDestructive: true);

        if (!confirmed)
        {
            return;
        }

        try
        {
            AppHost.QuoteService.DeleteRejected(id);
            ReloadListAndDetail();
            AppHost.NotificationService.Success($"«{title}» se eliminó.");
        }
        catch (Exception ex)
        {
            AppHost.NotificationService.Error(ex.Message, ex);
        }
    }

    private void Reopen()
    {
        if (Detail is null)
        {
            return;
        }

        try
        {
            AppHost.QuoteService.ReopenQuote(Detail.Id);
            ReloadListAndDetail();
            SetStatus("Presupuesto reabierto.", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private void Duplicate()
    {
        if (Detail is null)
        {
            return;
        }

        try
        {
            var copyId = AppHost.QuoteService.DuplicateQuote(Detail.Id);

            SelectedFilter = FilterOptions[0];
            LoadQuotes();
            SelectedQuote = Quotes.FirstOrDefault(q => q.Id == copyId);
            SetStatus("Copia creada con los precios de hoy.", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    // --- Impresión ------------------------------------------------------------

    private void PrintClient()
    {
        if (Detail is null)
        {
            return;
        }

        try
        {
            // Se relee de la base: lo que se le entrega al cliente tiene que ser lo
            // guardado y no lo que quedó en pantalla.
            var fresh = AppHost.QuoteService.GetDetail(Detail.Id) ?? Detail;
            var document = AppHost.QuoteDocumentService.BuildClientQuote(fresh);

            // Por la vista previa y no directo al diálogo de Windows: ese diálogo no puede
            // mostrar el papel, así que sin esto se imprime a ciegas.
            if (Views.QuotePreviewWindow.ShowFor(
                    document, $"Presupuesto {Detail.Id:0000}", SavePdfClient))
            {
                SetStatus("Presupuesto enviado a la impresora.", isError: false);
            }
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private void PrintCostSheet()
    {
        if (Detail is null)
        {
            return;
        }

        try
        {
            var document = AppHost.QuoteDocumentService.BuildCostSheet(Detail);

            if (Views.QuotePreviewWindow.ShowFor(
                    document, $"Hoja de costos {Detail.Id:0000}", SavePdfCostSheet))
            {
                SetStatus("Hoja de costos enviada a la impresora.", isError: false);
            }
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private void SavePdfClient()
    {
        if (Detail is null)
        {
            return;
        }

        // Se relee de la base, igual que al imprimir: lo que se manda por mensaje tiene que
        // ser lo guardado y no lo que quedó en pantalla.
        var fresh = AppHost.QuoteService.GetDetail(Detail.Id) ?? Detail;

        SavePdf(
            () => AppHost.QuoteDocumentService.BuildClientQuote(fresh),
            PdfExportService.SuggestFileName("Presupuesto", fresh.Id, fresh.ClientName),
            "Presupuesto");
    }

    private void SavePdfCostSheet()
    {
        if (Detail is null)
        {
            return;
        }

        var detail = Detail;

        SavePdf(
            () => AppHost.QuoteDocumentService.BuildCostSheet(detail),
            PdfExportService.SuggestFileName("Hoja de costos", detail.Id, detail.ClientName),
            "Hoja de costos");
    }

    /// <param name="build">
    /// Perezoso a propósito: armar el documento puede tirar —un presupuesto sin precio no se
    /// imprime— y ese error tiene que salir por el cartel de estado y no como excepción suelta.
    /// </param>
    private void SavePdf(Func<System.Windows.Documents.FlowDocument> build, string suggestedName, string what)
    {
        try
        {
            var path = AppHost.PdfExportService.SaveAs(build(), suggestedName);

            if (path is null)
            {
                return;
            }

            SetStatus($"{what} guardado en {Path.GetFileName(path)}.", isError: false);
            PdfExportService.OpenInDefaultApp(path);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

}
