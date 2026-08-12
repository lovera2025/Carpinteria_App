using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using MetroCarpinteria.App.Data.Entities;
using MetroCarpinteria.App.Helpers;
using MetroCarpinteria.App.Models;
using MetroCarpinteria.App.Services;

namespace MetroCarpinteria.App.ViewModels;

/// <summary>Filtros, buscador y la grilla de presupuestos.</summary>
public partial class QuotesViewModel
{
    // --- Lista ---------------------------------------------------------------

    /// <summary>
    /// El presupuesto abierto en el panel de la derecha.
    /// </summary>
    /// <remarks>
    /// Solo recarga el detalle cuando cambia de presupuesto <b>de verdad</b>. La grilla
    /// emite selecciones por muchos motivos que no son el usuario eligiendo otra fila —al
    /// refrescar la lista llegaba una instancia nueva del mismo presupuesto—, y cada una de
    /// esas recargas pisaba los campos de la calculadora con los valores guardados. El
    /// resultado era que salir del campo "Días" reiniciaba el formulario entero.
    /// </remarks>
    public QuoteListItem? SelectedQuote
    {
        get => _selectedQuote;
        set
        {
            var sameQuote = value?.Id == _selectedQuote?.Id;

            if (!SetProperty(ref _selectedQuote, value))
            {
                return;
            }

            if (_isRefreshingList || sameQuote)
            {
                return;
            }

            LoadDetail();
        }
    }

    public QuoteFilterOption SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (SetProperty(ref _selectedFilter, value))
            {
                LoadQuotes();
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                _searchDebouncer.Run(LoadQuotes);
            }
        }
    }

    public bool ShowApproved
    {
        get => _showApproved;
        set
        {
            if (SetProperty(ref _showApproved, value))
            {
                LoadQuotes();
            }
        }
    }

    /// <summary>
    /// Rearma la lista entera. Es la carga "de verdad": filtros, buscador, y cualquier
    /// acción que cambie el estado del presupuesto.
    /// </summary>
    /// <remarks>
    /// Vaciar la colección hace que la grilla reemita la selección, así que la recarga del
    /// detalle se hace acá al final y no desde el setter de <see cref="SelectedQuote"/>.
    /// El flujo de edición no pasa por acá: usa <see cref="RefreshRow"/>.
    /// </remarks>
    private void LoadQuotes()
    {
        var items = AppHost.QuoteService.GetQuotes(SelectedFilter.Filter, SearchText, ShowApproved);
        var previousId = SelectedQuote?.Id;
        QuoteListItem? next;

        _isRefreshingList = true;
        try
        {
            Quotes.Clear();
            foreach (var item in items)
            {
                Quotes.Add(item);
            }

            next = previousId.HasValue
                ? Quotes.FirstOrDefault(q => q.Id == previousId.Value) ?? Quotes.FirstOrDefault()
                : Quotes.FirstOrDefault();

            SetProperty(ref _selectedQuote, next, nameof(SelectedQuote));
        }
        finally
        {
            _isRefreshingList = false;
        }

        // El detalle se recarga solo si quedó abierto otro presupuesto. Si sigue siendo el
        // mismo, lo tipeado en la calculadora se respeta: buscar en la lista no puede
        // borrar un valor que el usuario todavía no terminó de cargar.
        if (next?.Id != previousId)
        {
            LoadDetail();
        }

        _onDataChanged();

        // El barrido global se queda: ningún comando de esta pantalla avisa por su cuenta,
        // y ahora corre una vez por carga de lista y no una vez por tecla.
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>
    /// Para las acciones que cambian el presupuesto abierto (aprobar, rechazar, reabrir,
    /// editar la cabecera): la lista y el detalle tienen que reflejarlo los dos.
    /// </summary>
    private void ReloadListAndDetail()
    {
        LoadQuotes();
        LoadDetail();
    }

    /// <summary>
    /// Actualiza el renglón de un presupuesto sin tocar el resto de la lista ni el
    /// formulario que se está editando.
    /// </summary>
    /// <remarks>
    /// Es la mitad que faltaba para cortar el ciclo. Guardar el cálculo recargaba la lista,
    /// la grilla reemitía la selección y el detalle se releía encima de los campos abiertos:
    /// salir del campo "Días" devolvía la calculadora a los valores guardados.
    /// </remarks>
    private void RefreshRow(int quoteId)
    {
        var index = -1;
        for (var i = 0; i < Quotes.Count; i++)
        {
            if (Quotes[i].Id == quoteId)
            {
                index = i;
                break;
            }
        }

        var fresh = index < 0 ? null : AppHost.QuoteService.GetListItem(quoteId);

        if (fresh is null)
        {
            // No está en la lista visible: no hay renglón que actualizar. No se recarga
            // desde acá a propósito — las acciones que cambian el estado del presupuesto
            // llaman a LoadQuotes ellas mismas, y hacerlo también acá abriría un ciclo.
            _onDataChanged();
            return;
        }

        _isRefreshingList = true;
        try
        {
            Quotes[index] = fresh;

            if (_selectedQuote?.Id == quoteId)
            {
                // Directo al campo: pasar por el setter recargaría el detalle y estaríamos
                // de vuelta en el ciclo que esto viene a cortar.
                _selectedQuote = fresh;
                OnPropertyChanged(nameof(SelectedQuote));
            }
        }
        finally
        {
            _isRefreshingList = false;
        }

        _onDataChanged();
    }
}
