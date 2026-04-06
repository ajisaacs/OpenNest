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

        public static Color[] PartColors => Drawing.PartColors;

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

        public Pen ActivePreviewPartPen { get; private set; }

        public Brush ActivePreviewPartBrush { get; private set; }

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

                if (ActivePreviewPartPen != null)
                    ActivePreviewPartPen.Dispose();

                if (ActivePreviewPartBrush != null)
                    ActivePreviewPartBrush.Dispose();

                PreviewPartPen = new Pen(value, 1);
                PreviewPartBrush = new SolidBrush(Color.FromArgb(60, value));
                ActivePreviewPartPen = new Pen(Color.FromArgb(128, value), 1);
                ActivePreviewPartBrush = new SolidBrush(Color.FromArgb(30, value));
            }
        }

        #endregion Colors
    }
}
