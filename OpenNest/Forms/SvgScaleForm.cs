using System;
using System.IO;
using System.Windows.Forms;
using OpenNest.IO;

namespace OpenNest.Forms
{
    /// <summary>
    /// Asks what an SVG's numbers mean when the file does not state its real-world size,
    /// showing the size each choice gives so a wrong one is obvious before importing.
    /// </summary>
    public partial class SvgScaleForm : Form
    {
        private const double CssPixelsPerInch = 96;
        private const double IllustratorPointsPerInch = 72;
        private const double UnitsPerInchAtOneMillimeter = 25.4;
        private const double UnitsPerInchAtOneInch = 1;

        private readonly double fileWidth;
        private readonly double fileHeight;
        private readonly Units units;

        public SvgScaleForm(string fileName, double width, double height, Units units)
        {
            InitializeComponent();
            fileWidth = width;
            fileHeight = height;
            this.units = units;
            labelMessage.Text = $"\"{fileName}\" does not state its real-world size. What is one of its units?";
            UpdateSize(this, EventArgs.Empty);
        }

        public double UserUnitsPerInch
        {
            get
            {
                if (radioPoints.Checked)
                    return IllustratorPointsPerInch;
                if (radioMillimeters.Checked)
                    return UnitsPerInchAtOneMillimeter;
                if (radioInches.Checked)
                    return UnitsPerInchAtOneInch;
                return CssPixelsPerInch;
            }
        }

        /// <summary>
        /// Fills in <see cref="CadImportOptions.SvgUserUnitsPerInch"/> when
        /// <paramref name="path"/> needs it; false when the user cancels. Anything else
        /// imports unchanged, and an unreadable file fails in the import that follows.
        /// </summary>
        public static bool TryResolve(IWin32Window owner, string path, CadImportOptions options)
        {
            if (!Svg.IsSvg(path))
                return true;

            (double Width, double Height)? size;
            try
            {
                size = Svg.UndeclaredSize(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return true;
            }

            if (size == null)
                return true;

            using var form = new SvgScaleForm(
                Path.GetFileName(path),
                size.Value.Width,
                size.Value.Height,
                options.DrawingUnits
            );
            if (form.ShowDialog(owner) != DialogResult.OK)
                return false;

            options.SvgUserUnitsPerInch = form.UserUnitsPerInch;
            return true;
        }

        private void UpdateSize(object sender, EventArgs e)
        {
            var perInch = units == Units.Millimeters ? 25.4 : 1;
            var scale = perInch / UserUnitsPerInch;
            var format = units == Units.Millimeters ? "F1" : "F3";
            var unit = UnitsHelper.GetShortString(units);
            labelSize.Text =
                $"Imported size: {(fileWidth * scale).ToString(format)} x {(fileHeight * scale).ToString(format)} {unit}";
        }
    }
}
