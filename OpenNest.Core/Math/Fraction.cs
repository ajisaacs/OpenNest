using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenNest.Math
{
    public static class Fraction
    {
        public static readonly Regex FractionRegex = new Regex(
            @"((?<WholeNum>\d+)(\ |-))?(?<Fraction>\d+\/\d+)",
            RegexOptions.None,
            TimeSpan.FromMilliseconds(250)
        );

        public static bool IsValid(string s)
        {
            return FractionRegex.IsMatch(s);
        }

        public static double Parse(string s)
        {
            var match = FractionRegex.Match(s);

            if (!match.Success)
                throw new FormatException("Invalid fraction format.");

            var value = 0.0;

            var wholeNumGroup = match.Groups["WholeNum"];
            var fractionGroup = match.Groups["Fraction"];

            if (wholeNumGroup.Success)
                value = double.Parse(wholeNumGroup.Value);

            if (fractionGroup.Success)
            {
                var parts = fractionGroup.Value.Split('/');
                var numerator = double.Parse(parts[0]);
                var denominator = double.Parse(parts[1]);
                value += System.Math.Round(numerator / denominator, 8);
            }

            return value;
        }

        public static bool TryParse(string s, out double fraction)
        {
            try
            {
                fraction = Parse(s);
                return true;
            }
            catch
            {
                fraction = 0;
                return false;
            }
        }

        /// <summary>
        /// <paramref name="value"/> as a whole number and a reduced fraction, such as "5 1/2",
        /// when it is a multiple of 1/<paramref name="maxDenominator"/>; otherwise as a decimal
        /// with up to four places. <see cref="ReplaceFractionsWithDecimals"/> reads either back.
        /// </summary>
        public static string Format(double value, int maxDenominator = 64)
        {
            var scaled = value * maxDenominator;
            var rounded = System.Math.Round(scaled);
            if (!double.IsFinite(value) || System.Math.Abs(scaled - rounded) > 1e-6 || System.Math.Abs(rounded) > long.MaxValue / 2)
                return value.ToString("0.####", System.Globalization.CultureInfo.CurrentCulture);

            var sign = rounded < 0 ? "-" : "";
            var units = (long)System.Math.Abs(rounded);
            var whole = units / maxDenominator;
            var numerator = units % maxDenominator;
            if (numerator == 0)
                return sign + whole;

            var denominator = (long)maxDenominator;
            var divisor = Gcd(numerator, denominator);
            numerator /= divisor;
            denominator /= divisor;
            return whole == 0 ? $"{sign}{numerator}/{denominator}" : $"{sign}{whole} {numerator}/{denominator}";
        }

        private static long Gcd(long a, long b)
        {
            while (b != 0)
                (a, b) = (b, a % b);
            return a;
        }

        public static string ReplaceFractionsWithDecimals(string input)
        {
            var sb = new StringBuilder(input);

            var fractionMatches = FractionRegex
                .Matches(sb.ToString())
                .Cast<Match>()
                .OrderByDescending(m => m.Index);

            foreach (var fractionMatch in fractionMatches)
            {
                var decimalValue = Parse(fractionMatch.Value);
                sb.Remove(fractionMatch.Index, fractionMatch.Length);
                sb.Insert(fractionMatch.Index, decimalValue);
            }

            return sb.ToString();
        }
    }
}
