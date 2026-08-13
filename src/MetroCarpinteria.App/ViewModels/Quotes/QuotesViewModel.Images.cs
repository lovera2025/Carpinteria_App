using System.Windows;
using System.Windows.Media.Imaging;
using MetroCarpinteria.App.Helpers;
using MetroCarpinteria.App.Models;
using MetroCarpinteria.App.Services;
using Microsoft.Win32;

namespace MetroCarpinteria.App.ViewModels;

/// <summary>Fotos de referencia del presupuesto abierto.</summary>
public partial class QuotesViewModel
{
    /// <summary>
    /// Se pueden cambiar si el trabajo no está archivado. No usa <see cref="CanEditSelected"/>
    /// a propósito: un aprobado se sigue reimprimiendo desde Proyectos, y olvidarse una
    /// foto no puede quedar bloqueado por el descuento de stock.
    /// </summary>
    public bool CanEditImages => Detail is { IsArchived: false };

    public bool CanAddImages =>
        CanEditImages && Images.Count < QuoteImageService.MaxImages;

    public string ImagesSummary
    {
        get
        {
            if (Detail is null)
            {
                return string.Empty;
            }

            if (Images.Count == 0)
            {
                return "Ninguna foto todavía";
            }

            var missing = Images.Count(i => i.IsMissing);
            var label = Images.Count == 1 ? "1 foto" : $"{Images.Count} fotos";
            return missing > 0
                ? $"{label} · {missing} no se encontraron en disco"
                : $"{label} · salen en el presupuesto del cliente";
        }
    }

    private void ReloadImages()
    {
        Images.Clear();

        if (Detail is null)
        {
            NotifyImageState();
            return;
        }

        foreach (var item in Detail.Images)
        {
            Images.Add(QuoteImageRow.From(item));
        }

        NotifyImageState();
    }

    private void AddImages()
    {
        if (Detail is null || !CanAddImages)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Elegir fotos para el presupuesto",
            Filter = "Imágenes|*.jpg;*.jpeg;*.png;*.bmp|Todos los archivos|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() != true || dialog.FileNames.Length == 0)
        {
            return;
        }

        var added = 0;
        string? lastError = null;

        foreach (var path in dialog.FileNames)
        {
            if (Images.Count >= QuoteImageService.MaxImages)
            {
                lastError = $"Se agregaron {added}. El máximo es {QuoteImageService.MaxImages} fotos.";
                break;
            }

            try
            {
                AppHost.QuoteImageService.AddFromFile(Detail.Id, path);
                added++;
                RefreshDetailImages();
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
            }
        }

        if (added > 0)
        {
            SetStatus(
                added == 1 ? "Foto agregada." : $"{added} fotos agregadas.",
                isError: false);
        }

        if (lastError is not null)
        {
            SetStatus(lastError, isError: true);
        }
    }

    private void PasteImage()
    {
        if (Detail is null || !CanAddImages)
        {
            return;
        }

        if (!Clipboard.ContainsImage())
        {
            SetStatus("No hay una imagen en el portapapeles. Copiá una foto y volvé a pegar.", isError: true);
            return;
        }

        var image = Clipboard.GetImage();
        if (image is null)
        {
            SetStatus("No se pudo leer la imagen del portapapeles.", isError: true);
            return;
        }

        try
        {
            AppHost.QuoteImageService.AddFromBitmap(Detail.Id, image);
            RefreshDetailImages();
            SetStatus("Foto pegada.", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private void RemoveImage(object? parameter)
    {
        if (parameter is not QuoteImageRow row || Detail is null || !CanEditImages)
        {
            return;
        }

        try
        {
            AppHost.QuoteImageService.Remove(row.Id);
            RefreshDetailImages();
            SetStatus("Foto quitada.", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private void SaveImageCaption(object? parameter)
    {
        if (parameter is not QuoteImageRow row || Detail is null || !CanEditImages)
        {
            return;
        }

        try
        {
            AppHost.QuoteImageService.UpdateCaption(row.Id, row.Caption);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private void RefreshDetailImages()
    {
        if (Detail is null)
        {
            Images.Clear();
            NotifyImageState();
            return;
        }

        var typed = Images.ToDictionary(i => i.Id, i => i.Caption);
        Detail = AppHost.QuoteService.GetDetail(Detail.Id);
        ReloadImages();

        foreach (var row in Images)
        {
            if (typed.TryGetValue(row.Id, out var caption) && caption != row.Caption)
            {
                row.Caption = caption;
            }
        }
    }

    private void NotifyImageState()
    {
        OnPropertyChanged(nameof(CanEditImages));
        OnPropertyChanged(nameof(CanAddImages));
        OnPropertyChanged(nameof(ImagesSummary));
        (AddImagesCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (PasteImageCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }
}

/// <summary>Una foto en la lista del editor, con miniatura ya cargada.</summary>
public sealed class QuoteImageRow : ObservableObject
{
    private string _caption = string.Empty;

    public int Id { get; init; }
    public bool IsMissing { get; init; }
    public BitmapImage? Thumbnail { get; init; }

    public string Caption
    {
        get => _caption;
        set => SetProperty(ref _caption, value);
    }

    public static QuoteImageRow From(QuoteImageItem item) => new()
    {
        Id = item.Id,
        Caption = item.Caption,
        IsMissing = item.IsMissing,
        Thumbnail = item.IsMissing ? null : LoadThumbnail(item.FullPath)
    };

    private static BitmapImage? LoadThumbnail(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.DecodePixelWidth = 240;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }
}
