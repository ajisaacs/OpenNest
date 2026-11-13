using System.Drawing;
using System.Linq;
using System.Threading;

namespace OpenNest
{
    /// <summary>
    /// A colour scheme's colours: the plate view, its parts and the library previews. Pure data
    /// shared by both desktop front ends; each draws with its own cache of pens or paints, which
    /// it rebuilds when <see cref="Version"/> changes.
    /// </summary>
    public sealed class ColorScheme
    {
        private int version;
        private Color backgroundColor;
        private Color layoutOutlineColor;
        private Color layoutFillColor;
        private Color boundingBoxColor;
        private Color rapidColor;
        private Color originColor;
        private Color edgeSpacingColor;
        private Color previewPartColor;
        private Color[] partColors = PartPalette.Classic.Colors.ToArray();
        private bool useGoldenAngleColors;
        private Color partOutlineColor = Color.Empty;
        private Color selectedPartColor = Color.Empty;
        private Color etchColor = Color.Lime;
        private Color libraryBackgroundColor = Color.White;
        private Color libraryFillColor = Color.FromArgb(214, 219, 226);
        private Color libraryOutlineColor = Color.FromArgb(64, 70, 80);
        private Color libraryEtchColor = Color.FromArgb(0, 150, 60);
        private Color libraryBendColor = Color.FromArgb(40, 100, 210);

        public string Name { get; set; } = "Unnamed";

        /// <summary>
        /// Changes whenever a colour does, so a front end's pen or paint cache knows to rebuild.
        /// </summary>
        public int Version => Volatile.Read(ref version);

        public Color[] PartColors
        {
            get => partColors;
            set => Set(ref partColors, value);
        }

        public bool UseGoldenAngleColors
        {
            get => useGoldenAngleColors;
            set => Set(ref useGoldenAngleColors, value);
        }

        // Empty preserves the legacy outline derived from each part's fill.
        public Color PartOutlineColor
        {
            get => partOutlineColor;
            set => Set(ref partOutlineColor, value);
        }

        // Empty preserves the legacy translucent selection fill.
        public Color SelectedPartColor
        {
            get => selectedPartColor;
            set => Set(ref selectedPartColor, value);
        }

        public Color EtchColor
        {
            get => etchColor;
            set => Set(ref etchColor, value);
        }

        // Library previews (Shape Library, drawing library, drawing set preview) show drawings
        // that are not in the nest yet, so they share one style instead of nest part colors.
        public Color LibraryBackgroundColor
        {
            get => libraryBackgroundColor;
            set => Set(ref libraryBackgroundColor, value);
        }

        public Color LibraryFillColor
        {
            get => libraryFillColor;
            set => Set(ref libraryFillColor, value);
        }

        public Color LibraryOutlineColor
        {
            get => libraryOutlineColor;
            set => Set(ref libraryOutlineColor, value);
        }

        public Color LibraryEtchColor
        {
            get => libraryEtchColor;
            set => Set(ref libraryEtchColor, value);
        }

        public Color LibraryBendColor
        {
            get => libraryBendColor;
            set => Set(ref libraryBendColor, value);
        }

        /// <summary>
        /// The scheme's part outline or, when it has none, the legacy outline: the fill darkened
        /// as WinForms' <c>ControlPaint.Dark</c> does.
        /// </summary>
        public Color GetPartOutlineColor(Color fill) =>
            PartOutlineColor.IsEmpty ? ColorShade.Dark(fill) : PartOutlineColor;

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

        public Color BackgroundColor
        {
            get => backgroundColor;
            set => Set(ref backgroundColor, value);
        }

        public Color LayoutOutlineColor
        {
            get => layoutOutlineColor;
            set => Set(ref layoutOutlineColor, value);
        }

        public Color LayoutFillColor
        {
            get => layoutFillColor;
            set => Set(ref layoutFillColor, value);
        }

        public Color BoundingBoxColor
        {
            get => boundingBoxColor;
            set => Set(ref boundingBoxColor, value);
        }

        public Color RapidColor
        {
            get => rapidColor;
            set => Set(ref rapidColor, value);
        }

        public Color OriginColor
        {
            get => originColor;
            set => Set(ref originColor, value);
        }

        public Color EdgeSpacingColor
        {
            get => edgeSpacingColor;
            set => Set(ref edgeSpacingColor, value);
        }

        public Color PreviewPartColor
        {
            get => previewPartColor;
            set => Set(ref previewPartColor, value);
        }

        private void Set<T>(ref T field, T value)
        {
            field = value;
            Interlocked.Increment(ref version);
        }
    }
}
