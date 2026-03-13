using System.Drawing;
using System.Drawing.Drawing2D;

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

        public static readonly Color[] PartColors = new Color[]
        {
            Color.FromArgb(205, 92, 92),    // Indian Red
            Color.FromArgb(148, 103, 189),  // Medium Purple
            Color.FromArgb(75, 180, 175),   // Teal
            Color.FromArgb(210, 190, 75),   // Goldenrod
            Color.FromArgb(190, 85, 175),   // Orchid
            Color.FromArgb(185, 115, 85),   // Sienna
            Color.FromArgb(120, 100, 190),  // Slate Blue
            Color.FromArgb(200, 100, 140),  // Rose
            Color.FromArgb(80, 175, 155),   // Sea Green
            Color.FromArgb(195, 160, 85),   // Dark Khaki
            Color.FromArgb(175, 95, 160),   // Plum
            Color.FromArgb(215, 130, 130),  // Light Coral
        };

        public static readonly ColorScheme Default = new ColorScheme
        {
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

        public Pen LayoutOutlinePen { get; private set; }

        public Brush LayoutFillBrush { get; private set; }

        public Pen BoundingBoxPen { get; private set; }

        public Pen RapidPen { get; private set; }

        public Pen OriginPen { get; private set; }

        public Pen EdgeSpacingPen { get; private set; }

        public Pen PreviewPartPen { get; private set; }

        public Brush PreviewPartBrush { get; private set; }

        #endregion Pens/Brushes

        #region Colors

        public Color BackgroundColor { get; set; }

        public Color LayoutOutlineColor
        {
            get { return layoutOutlineColor; }
            set
            {
                layoutOutlineColor = value;

                if (LayoutOutlinePen != null)
                    LayoutOutlinePen.Dispose();

                LayoutOutlinePen = new Pen(value);
            }
        }

        public Color LayoutFillColor
        {
            get { return layoutFillColor; }
            set
            {
                layoutFillColor = value;

                if (LayoutFillBrush != null)
                    LayoutFillBrush.Dispose();

                LayoutFillBrush = new SolidBrush(value);
            }
        }

        public Color BoundingBoxColor
        {
            get { return boundingBoxColor; }
            set
            {
                boundingBoxColor = value;

                if (BoundingBoxPen != null)
                    BoundingBoxPen.Dispose();

                BoundingBoxPen = new Pen(value);
            }
        }

        public Color RapidColor
        {
            get { return rapidColor; }
            set
            {
                rapidColor = value;

                if (RapidPen != null)
                    RapidPen.Dispose();

                RapidPen = new Pen(value)
                {
                    DashPattern = new float[] { 10, 10 },
                    DashCap = DashCap.Flat
                };
            }
        }

        public Color OriginColor
        {
            get { return originColor; }
            set
            {
                originColor = value;

                if (OriginPen != null)
                    OriginPen.Dispose();

                OriginPen = new Pen(value);
            }
        }

        public Color EdgeSpacingColor
        {
            get { return edgeSpacingColor; }
            set
            {
                edgeSpacingColor = value;

                if (EdgeSpacingPen != null)
                    EdgeSpacingPen.Dispose();

                EdgeSpacingPen = new Pen(value)
                {
                    DashPattern = new float[] { 3, 3 },
                    DashCap = DashCap.Flat
                };
            }
        }

        public Color PreviewPartColor
        {
            get { return previewPartColor; }
            set
            {
                previewPartColor = value;

                if (PreviewPartPen != null)
                    PreviewPartPen.Dispose();

                if (PreviewPartBrush != null)
                    PreviewPartBrush.Dispose();

                PreviewPartPen = new Pen(value, 1);
                PreviewPartBrush = new SolidBrush(Color.FromArgb(60, value));
            }
        }

        #endregion Colors
    }
}
