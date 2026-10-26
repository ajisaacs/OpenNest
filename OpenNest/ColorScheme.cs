using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;

namespace OpenNest
{
    public sealed class ColorScheme
    {
        private Color layoutOutlineColor;
        private Color layoutFillColor;
        private Color boundingBoxColor;
        private Color rapidColor;
        private Color originColor;
        private Color edgeSpacingColor;
        private Color previewPartColor;

        public string Name { get; set; } = "Unnamed";

        public Color[] PartColors { get; set; } = Drawing.PartColors;

        public bool UseGoldenAngleColors { get; set; }

        // Empty preserves the legacy outline derived from each part's fill.
        public Color PartOutlineColor { get; set; } = Color.Empty;

        // Empty preserves the legacy translucent selection fill.
        public Color SelectedPartColor { get; set; } = Color.Empty;

        public Color EtchColor { get; set; } = Color.Lime;

        // Library previews (Shape Library, drawing library, drawing set preview) show drawings
        // that are not in the nest yet, so they share one style instead of nest part colors.
        public Color LibraryBackgroundColor { get; set; } = Color.White;

        public Color LibraryFillColor { get; set; } = Color.FromArgb(214, 219, 226);

        public Color LibraryOutlineColor { get; set; } = Color.FromArgb(64, 70, 80);

        public Color LibraryEtchColor { get; set; } = Color.FromArgb(0, 150, 60);

        public Color LibraryBendColor { get; set; } = Color.FromArgb(40, 100, 210);

        public Color GetPartOutlineColor(Color fill) =>
            PartOutlineColor.IsEmpty ? System.Windows.Forms.ControlPaint.Dark(fill) : PartOutlineColor;

        public static readonly ColorScheme Default = new ColorScheme
        {
            Name = "Classic",
            BackgroundColor = Color.DarkGray,
            LayoutOutlineColor = Color.Gray,
            LayoutFillColor = Color.WhiteSmoke,
            BoundingBoxColor = Color.FromArgb(128, 128, 255),
            RapidColor = Color.DodgerBlue,
            OriginColor = Color.Gray,
            EdgeSpacingColor = Color.FromArgb(180, 180, 180),
            PreviewPartColor = Color.FromArgb(255, 140, 0),
        };

        #region Pens/Brushes

        // GDI+ objects are not thread-safe, and views on different UI threads share a
        // scheme (Default), so each thread draws with its own set, rebuilt after a color
        // change. Two views painting at once otherwise fail with "Object is currently in
        // use elsewhere".
        private readonly ThreadLocal<DrawingTools> tools = new();
        private int toolsVersion;

        private DrawingTools Tools
        {
            get
            {
                var current = tools.Value;
                if (current == null || current.Version != toolsVersion)
                {
                    current?.Dispose();
                    current = new DrawingTools(this, toolsVersion);
                    tools.Value = current;
                }

                return current;
            }
        }

        public Pen LayoutOutlinePen => Tools.LayoutOutlinePen;

        public Brush LayoutFillBrush => Tools.LayoutFillBrush;

        public Pen BoundingBoxPen => Tools.BoundingBoxPen;

        public Pen RapidPen => Tools.RapidPen;

        public Pen OriginPen => Tools.OriginPen;

        public Pen EdgeSpacingPen => Tools.EdgeSpacingPen;

        public Pen PreviewPartPen => Tools.PreviewPartPen;

        public Brush PreviewPartBrush => Tools.PreviewPartBrush;

        public Pen ActivePreviewPartPen => Tools.ActivePreviewPartPen;

        public Brush ActivePreviewPartBrush => Tools.ActivePreviewPartBrush;

        private sealed class DrawingTools : IDisposable
        {
            public DrawingTools(ColorScheme scheme, int version)
            {
                Version = version;
                LayoutOutlinePen = new Pen(scheme.layoutOutlineColor);
                LayoutFillBrush = new SolidBrush(scheme.layoutFillColor);
                BoundingBoxPen = new Pen(scheme.boundingBoxColor);
                RapidPen = new Pen(scheme.rapidColor)
                {
                    DashPattern = new float[] { 10, 10 },
                    DashCap = DashCap.Flat,
                };
                OriginPen = new Pen(scheme.originColor);
                EdgeSpacingPen = new Pen(scheme.edgeSpacingColor)
                {
                    DashPattern = new float[] { 3, 3 },
                    DashCap = DashCap.Flat,
                };
                PreviewPartPen = new Pen(scheme.previewPartColor, 1);
                PreviewPartBrush = new SolidBrush(Color.FromArgb(60, scheme.previewPartColor));
                ActivePreviewPartPen = new Pen(Color.FromArgb(128, scheme.previewPartColor), 1);
                ActivePreviewPartBrush = new SolidBrush(Color.FromArgb(30, scheme.previewPartColor));
            }

            public int Version { get; }

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

            public void Dispose()
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

        #endregion Pens/Brushes

        #region Colors

        public Color BackgroundColor { get; set; }

        public Color LayoutOutlineColor
        {
            get { return layoutOutlineColor; }
            set { layoutOutlineColor = value; Interlocked.Increment(ref toolsVersion); }
        }

        public Color LayoutFillColor
        {
            get { return layoutFillColor; }
            set { layoutFillColor = value; Interlocked.Increment(ref toolsVersion); }
        }

        public Color BoundingBoxColor
        {
            get { return boundingBoxColor; }
            set { boundingBoxColor = value; Interlocked.Increment(ref toolsVersion); }
        }

        public Color RapidColor
        {
            get { return rapidColor; }
            set { rapidColor = value; Interlocked.Increment(ref toolsVersion); }
        }

        public Color OriginColor
        {
            get { return originColor; }
            set { originColor = value; Interlocked.Increment(ref toolsVersion); }
        }

        public Color EdgeSpacingColor
        {
            get { return edgeSpacingColor; }
            set { edgeSpacingColor = value; Interlocked.Increment(ref toolsVersion); }
        }

        public Color PreviewPartColor
        {
            get { return previewPartColor; }
            set { previewPartColor = value; Interlocked.Increment(ref toolsVersion); }
        }

        #endregion Colors
    }
}
