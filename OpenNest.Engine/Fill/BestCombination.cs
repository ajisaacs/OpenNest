using System.Collections.Generic;
using OpenNest.Math;

namespace OpenNest.Engine.Fill
{
    internal record CombinationResult(bool Found, int Count1, int Count2);

    internal static class BestCombination
    {
        /// <summary>
        /// Every count of <paramref name="length1"/> that fits, each with as many of
        /// <paramref name="length2"/> as fit beside it. Spiral blocks need these: the combination
        /// with the least waste is not always one whose blocks can share the sheet.
        /// </summary>
        public static IEnumerable<CombinationResult> AllFrom2(
            double length1,
            double length2,
            double overallLength
        )
        {
            overallLength += Tolerance.Epsilon;
            var maxCount1 = (int)System.Math.Floor(overallLength / length1);

            for (var c1 = 0; c1 <= maxCount1; c1++)
            {
                var c2 = (int)System.Math.Floor((overallLength - c1 * length1) / length2);
                if (c1 > 0 || c2 > 0)
                    yield return new CombinationResult(true, c1, c2);
            }
        }

        public static CombinationResult FindFrom2(
            double length1,
            double length2,
            double overallLength
        )
        {
            overallLength += Tolerance.Epsilon;
            var count1 = 0;
            var count2 = 0;

            var maxCount1 = (int)System.Math.Floor(overallLength / length1);
            var bestRemnant = overallLength + 1;

            for (var c1 = 0; c1 <= maxCount1; c1++)
            {
                var remaining = overallLength - c1 * length1;
                var c2 = (int)System.Math.Floor(remaining / length2);
                var remnant = remaining - c2 * length2;

                if (!(remnant < bestRemnant))
                    continue;

                count1 = c1;
                count2 = c2;
                bestRemnant = remnant;

                if (remnant.IsEqualTo(0))
                    break;
            }

            return new CombinationResult(count1 > 0 || count2 > 0, count1, count2);
        }
    }
}
