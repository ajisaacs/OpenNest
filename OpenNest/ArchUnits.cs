using OpenNest.IO.Bom;
using System;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace OpenNest
{
    public static class ArchUnits
    {
        private static readonly Regex UnitRegex =
            new Regex("^(?<Feet>\\d+\\.?\\d*\\s*')?\\s*(?<Inches>\\d+\\.?\\d*\\s*\")?$");

        public static double ParseToInches(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return 0;

            var sb = new StringBuilder(input.Trim().ToLower());

            sb.Replace("ft", "'");
            sb.Replace("feet", "'");
            sb.Replace("foot", "'");
            sb.Replace("inches", "\"");
            sb.Replace("inch", "\"");
            sb.Replace("in", "\"");

            input = Fraction.ReplaceFractionsWithDecimals(sb.ToString());

            var match = UnitRegex.Match(input);

            if (!match.Success)
            {
                if (!input.Contains("'") && !input.Contains("\""))
                {
                    if (double.TryParse(input.Trim(), out var plainInches))
                        return System.Math.Round(plainInches, 8);
                }

                throw new FormatException("Input is not in a valid format.");
            }

            var feet = match.Groups["Feet"];
            var inches = match.Groups["Inches"];
            var totalInches = 0.0;

            if (feet.Success)
            {
                var x = double.Parse(feet.Value.Remove(feet.Length - 1));
                totalInches += x * 12;
            }

            if (inches.Success)
            {
                var x = double.Parse(inches.Value.Remove(inches.Length - 1));
                totalInches += x;
            }

            return System.Math.Round(totalInches, 8);
        }

        public static double GetLengthInches(TextBox tb)
        {
            try
            {
                if (double.TryParse(tb.Text, out var d))
                {
                    tb.ForeColor = SystemColors.WindowText;
                    return d;
                }

                var x = ParseToInches(tb.Text);
                tb.ForeColor = SystemColors.WindowText;
                return x;
            }
            catch
            {
                tb.ForeColor = Color.Red;
                return double.NaN;
            }
        }
    }
}
