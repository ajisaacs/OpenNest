using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using SkiaSharp;
using DrawingColor = System.Drawing.Color;

namespace OpenNest.Avalonia.Rendering
{
    /// <summary>
    /// A colour scheme's colours as Skia colours, for the plate canvas. Immutable, so the render
    /// thread may read one while the UI thread changes the scheme; <see cref="For"/> returns the
    /// cached palette until the scheme's colours change.
    /// </summary>
    internal sealed class SkiaSchemePalette
    {
        // The WinForms view's translucent selection fill, used when the scheme sets none.
        private static readonly DrawingColor LegacySelectedColor = DrawingColor.FromArgb(90, 150, 200, 255);

        private static readonly ConditionalWeakTable<ColorScheme, SkiaSchemePalette> Cache = new();

        private readonly DrawingColor partOutline;
        private readonly ConcurrentDictionary<SKColor, SKColor> outlines = new();

        private SkiaSchemePalette(ColorScheme scheme)
        {
            partOutline = scheme.PartOutlineColor;
            Version = scheme.Version;
            Background = ToSk(scheme.BackgroundColor);
            PlateFill = ToSk(scheme.LayoutFillColor);
            PlateOutline = ToSk(scheme.LayoutOutlineColor);
            EdgeSpacing = ToSk(scheme.EdgeSpacingColor);
            Etch = ToSk(scheme.EtchColor);
            var selected = scheme.SelectedPartColor.IsEmpty ? LegacySelectedColor : scheme.SelectedPartColor;
            SelectedFill = ToSk(selected);
            // As WinForms: the scheme's outline, else the legacy selection fill darkened.
            SelectedOutline = ToSk(scheme.PartOutlineColor.IsEmpty
                ? ColorShade.Dark(LegacySelectedColor)
                : scheme.PartOutlineColor);
        }

        private int Version { get; }

        public SKColor Background { get; }

        public SKColor PlateFill { get; }

        public SKColor PlateOutline { get; }

        public SKColor EdgeSpacing { get; }

        public SKColor Etch { get; }

        public SKColor SelectedFill { get; }

        public SKColor SelectedOutline { get; }

        /// <summary>The palette for <paramref name="scheme"/>'s current colours.</summary>
        public static SkiaSchemePalette For(ColorScheme scheme)
        {
            lock (Cache)
            {
                if (!Cache.TryGetValue(scheme, out var palette) || palette.Version != scheme.Version)
                {
                    palette = new SkiaSchemePalette(scheme);
                    Cache.AddOrUpdate(scheme, palette);
                }

                return palette;
            }
        }

        /// <summary>A part's outline for its fill: the scheme's outline colour, or the fill darkened.</summary>
        public SKColor PartOutline(SKColor fill) =>
            outlines.GetOrAdd(fill, f => ToSk(partOutline.IsEmpty
                ? ColorShade.Dark(DrawingColor.FromArgb(f.Alpha, f.Red, f.Green, f.Blue))
                : partOutline));

        public static SKColor ToSk(DrawingColor color) => new(color.R, color.G, color.B, color.A);
    }
}
