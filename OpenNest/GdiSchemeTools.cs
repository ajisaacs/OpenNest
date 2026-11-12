using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using System.Threading;

namespace OpenNest
{
    /// <summary>
    /// The GDI+ pens and brushes the WinForms views draw a <see cref="ColorScheme"/> with.
    /// </summary>
    /// <remarks>
    /// GDI+ objects are not thread-safe, and views on different UI threads share a scheme
    /// (<see cref="ColorScheme.Default"/>), so each thread draws with its own set, rebuilt after
    /// a colour change. Two views painting at once otherwise fail with "Object is currently in
    /// use elsewhere".
    /// </remarks>
    public sealed class GdiSchemeTools
    {
        private static readonly ConditionalWeakTable<ColorScheme, ThreadLocal<GdiSchemeTools>> Cache = new();

        private GdiSchemeTools(ColorScheme scheme)
        {
            Version = scheme.Version;
            LayoutOutlinePen = new Pen(scheme.LayoutOutlineColor);
            LayoutFillBrush = new SolidBrush(scheme.LayoutFillColor);
            BoundingBoxPen = new Pen(scheme.BoundingBoxColor);
            RapidPen = new Pen(scheme.RapidColor)
            {
                DashPattern = new float[] { 10, 10 },
                DashCap = DashCap.Flat,
            };
            OriginPen = new Pen(scheme.OriginColor);
            EdgeSpacingPen = new Pen(scheme.EdgeSpacingColor)
            {
                DashPattern = new float[] { 3, 3 },
                DashCap = DashCap.Flat,
            };
            PreviewPartPen = new Pen(scheme.PreviewPartColor, 1);
            PreviewPartBrush = new SolidBrush(Color.FromArgb(60, scheme.PreviewPartColor));
            ActivePreviewPartPen = new Pen(Color.FromArgb(128, scheme.PreviewPartColor), 1);
            ActivePreviewPartBrush = new SolidBrush(Color.FromArgb(30, scheme.PreviewPartColor));
        }

        /// <summary>This thread's pens and brushes for <paramref name="scheme"/>, current with its colours.</summary>
        public static GdiSchemeTools For(ColorScheme scheme)
        {
            var perThread = Cache.GetValue(scheme, _ => new ThreadLocal<GdiSchemeTools>());
            var current = perThread.Value;
            if (current == null || current.Version != scheme.Version)
            {
                current?.Release();
                current = new GdiSchemeTools(scheme);
                perThread.Value = current;
            }

            return current;
        }

        private int Version { get; }

        public Pen LayoutOutlinePen { get; }

        public Brush LayoutFillBrush { get; }

        public Pen BoundingBoxPen { get; }

        public Pen RapidPen { get; }

        public Pen OriginPen { get; }

        public Pen EdgeSpacingPen { get; }

        public Pen PreviewPartPen { get; }

        public Brush PreviewPartBrush { get; }

        public Pen ActivePreviewPartPen { get; }

        public Brush ActivePreviewPartBrush { get; }

        // Only the cache that made them releases them, once this thread has built newer ones.
        private void Release()
        {
            LayoutOutlinePen.Dispose();
            LayoutFillBrush.Dispose();
            BoundingBoxPen.Dispose();
            RapidPen.Dispose();
            OriginPen.Dispose();
            EdgeSpacingPen.Dispose();
            PreviewPartPen.Dispose();
            PreviewPartBrush.Dispose();
            ActivePreviewPartPen.Dispose();
            ActivePreviewPartBrush.Dispose();
        }
    }

    public static class GdiSchemeToolsExtensions
    {
        /// <summary>This thread's GDI+ pens and brushes for the scheme.</summary>
        public static GdiSchemeTools Gdi(this ColorScheme scheme) => GdiSchemeTools.For(scheme);
    }
}
