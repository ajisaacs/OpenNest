using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace OpenNest.Controls
{
    /// <summary>
    /// Numeric box whose <see cref="Suffix"/> is painted beside the value rather than
    /// being part of the editable text. Length boxes ("in"/"mm") also accept a value
    /// typed in another unit, such as 10mm, 3/8", 1 1/2 in or 2ft, and convert it.
    /// </summary>
    public class NumericUpDown : System.Windows.Forms.NumericUpDown
    {
        private const double MillimetersPerInch = 25.4;

        private static readonly Regex InputPattern = new Regex(
            @"^(?<number>[-+]?(?:\d+\s+\d+/\d+|\d+/\d+|[\d.,]+))\s*(?<unit>[a-zA-Z""']*)$",
            RegexOptions.CultureInvariant);

        private readonly TextBox editor;
        private SuffixPainter painter;
        private string suffix;

        public NumericUpDown()
        {
            suffix = string.Empty;
            editor = Controls.OfType<TextBox>().FirstOrDefault();

            if (editor != null)
            {
                editor.HandleCreated += (_, _) => AttachPainter();
                editor.HandleDestroyed += (_, _) => painter?.ReleaseHandle();
                editor.TextChanged += (_, _) => painter?.Paint();
            }
        }

        public string Suffix
        {
            get { return suffix; }
            set
            {
                suffix = value ?? string.Empty;
                painter?.ApplyMargin();
                editor?.Invalidate();
            }
        }

        protected override void OnEnter(EventArgs e)
        {
            base.OnEnter(e);
            this.Select(0, Text.Length);
        }

        protected override void ValidateEditText()
        {
            // Cleared first: the base Value setter would otherwise re-parse Text with
            // decimal.Parse, which rejects typed units.
            var parsed = TryParseInput(Text, Suffix, out var value);
            UserEdit = false;

            if (parsed)
                Value = System.Math.Clamp(value, Minimum, Maximum);

            UpdateEditText();
        }

        /// <summary>
        /// Parses typed text into the box's units. A bare number or one followed by the
        /// box's own suffix is taken as-is; when the suffix is a length unit, any other
        /// length unit is converted to it.
        /// </summary>
        public static bool TryParseInput(string text, string suffix, out decimal value)
        {
            value = 0;
            text = (text ?? string.Empty).Trim();
            var ownUnit = (suffix ?? string.Empty).Trim();

            if (ownUnit.Length > 0 && text.EndsWith(ownUnit, StringComparison.OrdinalIgnoreCase))
            {
                var number = text.Substring(0, text.Length - ownUnit.Length).Trim();
                if (TryParseNumber(number, out value))
                    return true;
            }

            var match = InputPattern.Match(text);
            if (!match.Success || !TryParseNumber(match.Groups["number"].Value, out var amount))
                return false;

            var unit = match.Groups["unit"].Value;
            if (unit.Length == 0)
            {
                value = amount;
                return true;
            }

            var from = MillimetersPer(unit);
            var to = MillimetersPer(ownUnit);
            if (from == null || to == null)
                return false;

            try
            {
                value = (decimal)((double)amount * from.Value / to.Value);
                return true;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private static bool TryParseNumber(string text, out decimal value)
        {
            value = 0;
            text = text.Trim();

            var slash = text.IndexOf('/');
            if (slash < 0)
                return decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value);

            var negative = text.StartsWith("-");
            text = text.TrimStart('-', '+').Trim();

            var whole = 0m;
            var space = text.IndexOf(' ');
            if (space > 0)
            {
                if (!decimal.TryParse(text.Substring(0, space), NumberStyles.None, CultureInfo.InvariantCulture, out whole))
                    return false;
                text = text.Substring(space + 1).Trim();
                slash = text.IndexOf('/');
            }

            if (!decimal.TryParse(text.Substring(0, slash), NumberStyles.None, CultureInfo.InvariantCulture, out var numerator)
                || !decimal.TryParse(text.Substring(slash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var denominator)
                || denominator == 0)
                return false;

            value = whole + numerator / denominator;
            if (negative)
                value = -value;
            return true;
        }

        private static double? MillimetersPer(string unit)
        {
            switch (unit.ToLowerInvariant())
            {
                case "\"":
                case "in":
                case "inch":
                case "inches":
                    return MillimetersPerInch;

                case "'":
                case "ft":
                case "foot":
                case "feet":
                    return MillimetersPerInch * 12;

                case "mm":
                    return 1;

                case "cm":
                    return 10;

                case "m":
                    return 1000;

                default:
                    return null;
            }
        }

        private void AttachPainter()
        {
            painter?.ReleaseHandle();
            painter = new SuffixPainter(this, editor);
            painter.ApplyMargin();
        }

        /// <summary>
        /// Paints the suffix in a right margin reserved on the inner edit box, so the
        /// caret and typed text never run under it.
        /// </summary>
        private sealed class SuffixPainter : NativeWindow
        {
            private const int WM_SETFONT = 0x0030;
            private const int WM_PAINT = 0x000F;
            private const int EM_SETMARGINS = 0x00D3;
            private const int EC_RIGHTMARGIN = 0x0002;
            private const int Gap = 2;

            private readonly NumericUpDown owner;
            private readonly TextBox editor;

            public SuffixPainter(NumericUpDown owner, TextBox editor)
            {
                this.owner = owner;
                this.editor = editor;
                AssignHandle(editor.Handle);
            }

            private string Text => owner.Suffix.Trim();

            public void ApplyMargin()
            {
                if (Handle == IntPtr.Zero)
                    return;

                var width = Text.Length == 0
                    ? 0
                    : TextRenderer.MeasureText(Text, editor.Font, Size.Empty, TextFormatFlags.NoPadding).Width + Gap * 2;

                var margins = new IntPtr(width << 16);
                NativeMethods.SendMessage(Handle, EM_SETMARGINS, new IntPtr(EC_RIGHTMARGIN), margins);
                editor.Invalidate();
            }

            public void Paint()
            {
                if (Handle == IntPtr.Zero || Text.Length == 0)
                    return;

                using var graphics = Graphics.FromHwnd(Handle);
                var bounds = editor.ClientRectangle;
                bounds.Width -= Gap;
                TextRenderer.DrawText(graphics, Text, editor.Font, bounds, SystemColors.GrayText, editor.BackColor,
                    TextFormatFlags.Right | TextFormatFlags.Top | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            }

            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);

                if (m.Msg == WM_PAINT)
                    Paint();
                else if (m.Msg == WM_SETFONT)
                    ApplyMargin();
            }
        }

        private static class NativeMethods
        {
            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        }
    }
}
