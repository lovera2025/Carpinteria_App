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
            var document = AppHost.QuoteDocumentService.BuildClientQuote(Detail, includeMaterialDetail: true);

            if (AppHost.QuoteDocumentService.Print(document, $"Presupuesto {Detail.Id:0000}"))
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

            if (AppHost.QuoteDocumentService.Print(document, $"Hoja de costos {Detail.Id:0000}"))
            {
                SetStatus("Hoja de costos enviada a la impresora.", isError: false);
            }
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

}
