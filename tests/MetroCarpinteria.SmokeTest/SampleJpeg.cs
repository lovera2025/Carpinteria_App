using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MetroCarpinteria.SmokeTest;

/// <summary>JPEG de muestra para adjuntar en tests y en la prueba impresa.</summary>
internal static class SampleJpeg
{
    public static string Write(
        string directory,
        string fileName,
        Color fill,
        string label,
        int width = 800,
        int height = 500)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(fill), null, new Rect(0, 0, width, height));

            var text = new FormattedText(
                label,
                CultureInfo.GetCultureInfo("es-AR"),
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                32,
                Brushes.White,
                1.25);

            context.DrawText(text, new Point(28, (height - text.Height) / 2));
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();

        var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }
}
