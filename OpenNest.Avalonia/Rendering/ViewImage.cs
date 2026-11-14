using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using OpenNest.Avalonia.Controls;
using SkiaSharp;

namespace OpenNest.Avalonia.Rendering
{
    /// <summary>
    /// File &gt; Export's "image as displayed": the plate view drawn as it is shown, at the
    /// screen's pixel density, by the same Skia drawing that paints it on screen. A .jpg or .jpeg
    /// file is written as JPEG (as the WinForms export writes), any other as PNG.
    /// </summary>
    internal static class ViewImage
    {
        private const int JpegQuality = 90;

        public static void Save(PlateCanvas canvas, string path)
        {
            ArgumentNullException.ThrowIfNull(canvas);
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            if (canvas.Bounds.Width < 1 || canvas.Bounds.Height < 1)
                throw new InvalidOperationException("The plate view is not shown, so there is no image to save.");

            var scaling = TopLevel.GetTopLevel(canvas)?.RenderScaling ?? 1;
            var size = new PixelSize(
                System.Math.Max(1, (int)System.Math.Ceiling(canvas.Bounds.Width * scaling)),
                System.Math.Max(1, (int)System.Math.Ceiling(canvas.Bounds.Height * scaling)));
            using var bitmap = new RenderTargetBitmap(size, new Vector(96 * scaling, 96 * scaling));
            // The canvas draws itself (it has no child controls) at the origin: RenderTargetBitmap
            // .Render would place it where it sits in its parent, shifting it out of the image.
            using (var context = bitmap.CreateDrawingContext())
                canvas.Render(context);

            if (!IsJpeg(path))
            {
                bitmap.Save(path);
                return;
            }

            // Avalonia saves PNG only; Skia re-encodes it as JPEG.
            using var png = new MemoryStream();
            bitmap.Save(png);
            png.Position = 0;
            using var decoded = SKBitmap.Decode(png)
                ?? throw new InvalidOperationException("The plate view image could not be encoded.");
            using var image = SKImage.FromBitmap(decoded);
            using var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality)
                ?? throw new InvalidOperationException("The plate view image could not be encoded as JPEG.");
            using var file = File.Create(path);
            jpeg.SaveTo(file);
        }

        private static bool IsJpeg(string path) =>
            Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg";
    }
}
