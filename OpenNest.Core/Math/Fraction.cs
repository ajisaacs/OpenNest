using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenNest.Math
{
    public static class Fraction
    {
        public static readonly Regex FractionRegex =
            new Regex(@"((?<WholeNum>\d+)(\ |-))?(?<Fraction>\d+\/\d+)");

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

        public static string ReplaceFractionsWithDecimals(string input)
        {
            var sb = new StringBuilder(input);

            var fractionMatches = FractionRegex.Matches(sb.ToString())
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
