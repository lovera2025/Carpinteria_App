using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MetroCarpinteria.App.Data;
using MetroCarpinteria.App.Data.Entities;
using MetroCarpinteria.App.Models;
using Microsoft.EntityFrameworkCore;

namespace MetroCarpinteria.App.Services;

/// <summary>
/// Fotos de referencia de un presupuesto. Copia y comprime al adjuntar; la base solo
/// guarda el nombre. No toca stock ni el cálculo.
/// </summary>
public sealed class QuoteImageService
{
    public const int MaxImages = 4;
    public const int MaxLongSide = 1600;
    public const int JpegQuality = 80;
    public const int MaxCaptionLength = 200;
    public const long MaxSourceBytes = 25L * 1024 * 1024;

    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp" };

    private readonly DatabaseService _databaseService;
    private readonly AppPaths _paths;

    public QuoteImageService(DatabaseService databaseService, AppPaths paths)
    {
        _databaseService = databaseService;
        _paths = paths;
    }

    public IReadOnlyList<QuoteImageItem> List(int projectId)
    {
        using var context = _databaseService.CreateContext();

        var rows = context.ProjectQuoteImages
            .AsNoTracking()
            .Where(i => i.ProjectId == projectId)
            .OrderBy(i => i.SortOrder)
            .ThenBy(i => i.Id)
            .ToList();

        return rows.Select(Map).ToList();
    }

    public QuoteImageItem AddFromFile(int projectId, string sourcePath, string? caption = null)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            throw new InvalidOperationException("No se encontró el archivo de la foto.");
        }

        var extension = Path.GetExtension(sourcePath);
        if (!AllowedExtensions.Contains(extension))
        {
            throw new InvalidOperationException(
                "Ese tipo de archivo no se puede adjuntar. Probá con una foto JPG, PNG o BMP.");
        }

        var info = new FileInfo(sourcePath);
        if (info.Length <= 0)
        {
            throw new InvalidOperationException("El archivo de la foto está vacío.");
        }

        if (info.Length > MaxSourceBytes)
        {
            throw new InvalidOperationException(
                "La foto pesa demasiado (más de 25 MB). Probá con otra más chica.");
        }

        BitmapSource source;
        try
        {
            source = LoadBitmap(sourcePath);
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or IOException)
        {
            throw new InvalidOperationException("No se pudo leer la imagen. Probá con otra foto.", ex);
        }

        return AddFromBitmap(projectId, source, caption);
    }

    public QuoteImageItem AddFromBitmap(int projectId, BitmapSource source, string? caption = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        using var context = _databaseService.CreateContext();
        var project = RequireEditable(context, projectId);

        var count = context.ProjectQuoteImages.Count(i => i.ProjectId == projectId);
        if (count >= MaxImages)
        {
            throw new InvalidOperationException(
                $"Un presupuesto puede tener hasta {MaxImages} fotos. Quitá una para agregar otra.");
        }

        var fileName = $"{Guid.NewGuid():N}.jpg";
        var folder = _paths.QuoteImageFolder(projectId);
        Directory.CreateDirectory(folder);
        var destination = Path.Combine(folder, fileName);

        try
        {
            SaveJpeg(source, destination);
        }
        catch (Exception ex)
        {
            TryDeleteFile(destination);
            throw new InvalidOperationException("No se pudo guardar la foto comprimida.", ex);
        }

        var now = DateTime.UtcNow;
        var row = new ProjectQuoteImage
        {
            ProjectId = project.Id,
            FileName = fileName,
            Caption = NormalizeCaption(caption),
            SortOrder = count,
            CreatedAtUtc = now
        };

        try
        {
            context.ProjectQuoteImages.Add(row);
            project.UpdatedAtUtc = now;
            context.SaveChanges();
        }
        catch
        {
            TryDeleteFile(destination);
            throw;
        }

        return Map(row);
    }

    public void UpdateCaption(int imageId, string? caption)
    {
        using var context = _databaseService.CreateContext();
        var row = context.ProjectQuoteImages.FirstOrDefault(i => i.Id == imageId)
            ?? throw new InvalidOperationException("No se encontró la foto.");

        RequireEditable(context, row.ProjectId);
        row.Caption = NormalizeCaption(caption);
        context.SaveChanges();
    }

    public void Remove(int imageId)
    {
        using var context = _databaseService.CreateContext();
        var row = context.ProjectQuoteImages.FirstOrDefault(i => i.Id == imageId)
            ?? throw new InvalidOperationException("No se encontró la foto.");

        RequireEditable(context, row.ProjectId);

        var path = SafePathOrNull(row.ProjectId, row.FileName);
        var projectId = row.ProjectId;

        context.ProjectQuoteImages.Remove(row);
        context.SaveChanges();

        if (path is not null)
        {
            TryDeleteFile(path);
        }

        TryDeleteFolderIfEmpty(projectId);
    }

    /// <summary>
    /// Copia las fotos a otro presupuesto (duplicar). Los archivos son copias nuevas:
    /// borrar una no toca la otra.
    /// </summary>
    public void CopyTo(int sourceProjectId, int destinationProjectId)
    {
        if (sourceProjectId == destinationProjectId)
        {
            return;
        }

        using var context = _databaseService.CreateContext();
        RequireProject(context, destinationProjectId);

        var sourceRows = context.ProjectQuoteImages
            .AsNoTracking()
            .Where(i => i.ProjectId == sourceProjectId)
            .OrderBy(i => i.SortOrder)
            .ThenBy(i => i.Id)
            .ToList();

        if (sourceRows.Count == 0)
        {
            return;
        }

        var destFolder = _paths.QuoteImageFolder(destinationProjectId);
        Directory.CreateDirectory(destFolder);

        var now = DateTime.UtcNow;
        var copied = 0;

        foreach (var source in sourceRows)
        {
            if (copied >= MaxImages)
            {
                break;
            }

            var sourcePath = SafePathOrNull(source.ProjectId, source.FileName);
            if (sourcePath is null || !File.Exists(sourcePath))
            {
                continue;
            }

            var fileName = $"{Guid.NewGuid():N}.jpg";
            var destPath = Path.Combine(destFolder, fileName);

            try
            {
                File.Copy(sourcePath, destPath, overwrite: false);
            }
            catch
            {
                TryDeleteFile(destPath);
                continue;
            }

            context.ProjectQuoteImages.Add(new ProjectQuoteImage
            {
                ProjectId = destinationProjectId,
                FileName = fileName,
                Caption = source.Caption,
                SortOrder = copied,
                CreatedAtUtc = now
            });
            copied++;
        }

        context.SaveChanges();
    }

    /// <summary>Borra la carpeta de fotos de un presupuesto. Lo usa el borrado del proyecto.</summary>
    public void DeleteFilesForProject(int projectId)
    {
        var folder = _paths.QuoteImageFolder(projectId);
        if (!Directory.Exists(folder))
        {
            return;
        }

        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch
        {
            // Best effort: la fila de la base ya no va a existir.
        }
    }

    /// <summary>Vacía todas las fotos de la instalación. Lo usa restaurar un respaldo viejo.</summary>
    public static void ClearAllFiles(AppPaths paths)
    {
        if (!Directory.Exists(paths.QuoteImagesDirectory))
        {
            Directory.CreateDirectory(paths.QuoteImagesDirectory);
            return;
        }

        try
        {
            Directory.Delete(paths.QuoteImagesDirectory, recursive: true);
        }
        catch
        {
            // Si no se pudo borrar entera, se vacía archivo por archivo más abajo.
        }

        Directory.CreateDirectory(paths.QuoteImagesDirectory);

        if (!Directory.Exists(paths.QuoteImagesDirectory))
        {
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(paths.QuoteImagesDirectory))
        {
            try
            {
                if (Directory.Exists(entry))
                {
                    Directory.Delete(entry, recursive: true);
                }
                else
                {
                    File.Delete(entry);
                }
            }
            catch
            {
                // Best effort.
            }
        }
    }

    public static void CopyImageTree(string sourceDirectory, string destinationDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            return;
        }

        Directory.CreateDirectory(destinationDirectory);

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, file);
            if (relative.Contains("..", StringComparison.Ordinal))
            {
                continue;
            }

            var dest = Path.Combine(destinationDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }
    }

    private QuoteImageItem Map(ProjectQuoteImage row)
    {
        var safe = AppPaths.IsSafeImageFileName(row.FileName);
        var fullPath = safe ? _paths.QuoteImagePath(row.ProjectId, row.FileName) : string.Empty;
        var missing = !safe || string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath);

        return new QuoteImageItem
        {
            Id = row.Id,
            ProjectId = row.ProjectId,
            FileName = row.FileName,
            Caption = row.Caption ?? string.Empty,
            SortOrder = row.SortOrder,
            FullPath = missing ? string.Empty : fullPath,
            IsMissing = missing
        };
    }

    private string? SafePathOrNull(int projectId, string fileName)
    {
        if (!AppPaths.IsSafeImageFileName(fileName))
        {
            return null;
        }

        return _paths.QuoteImagePath(projectId, fileName);
    }

    private static Project RequireEditable(AppDbContext context, int projectId)
    {
        var project = RequireProject(context, projectId);
        if (project.IsArchived)
        {
            throw new InvalidOperationException(
                "Este trabajo está archivado. Restauralo para cambiar las fotos.");
        }

        return project;
    }

    private static Project RequireProject(AppDbContext context, int projectId) =>
        context.Projects.FirstOrDefault(p => p.Id == projectId)
        ?? throw new InvalidOperationException("Presupuesto no encontrado.");

    private static string? NormalizeCaption(string? caption)
    {
        if (string.IsNullOrWhiteSpace(caption))
        {
            return null;
        }

        var trimmed = caption.Trim();
        return trimmed.Length <= MaxCaptionLength
            ? trimmed
            : trimmed[..MaxCaptionLength];
    }

    private static BitmapSource LoadBitmap(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.IgnoreColorProfile,
            BitmapCacheOption.OnLoad);

        var frame = decoder.Frames[0]
            ?? throw new FileFormatException("La imagen no tiene fotogramas.");

        frame.Freeze();
        return frame;
    }

    private static void SaveJpeg(BitmapSource source, string destination)
    {
        var prepared = PrepareForJpeg(source);
        var encoder = new JpegBitmapEncoder { QualityLevel = JpegQuality };
        encoder.Frames.Add(BitmapFrame.Create(prepared));

        var temp = destination + ".tmp";
        try
        {
            using (var stream = File.Create(temp))
            {
                encoder.Save(stream);
            }

            File.Move(temp, destination, overwrite: true);
        }
        finally
        {
            TryDeleteFile(temp);
        }
    }

    /// <summary>
    /// Achica si hace falta y pinta fondo blanco: el JPEG no tiene transparencia, y un
    /// PNG con alfa salía negro en el papel.
    /// </summary>
    private static BitmapSource PrepareForJpeg(BitmapSource source)
    {
        var width = source.PixelWidth;
        var height = source.PixelHeight;
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("La imagen no tiene tamaño.");
        }

        var longSide = Math.Max(width, height);
        var scale = longSide > MaxLongSide ? (double)MaxLongSide / longSide : 1.0;
        var destWidth = Math.Max(1, (int)Math.Round(width * scale));
        var destHeight = Math.Max(1, (int)Math.Round(height * scale));

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, destWidth, destHeight));
            context.DrawImage(source, new Rect(0, 0, destWidth, destHeight));
        }

        var bitmap = new RenderTargetBitmap(destWidth, destHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort.
        }
    }

    private void TryDeleteFolderIfEmpty(int projectId)
    {
        var folder = _paths.QuoteImageFolder(projectId);
        try
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
            }
        }
        catch
        {
            // Best effort.
        }
    }
}
