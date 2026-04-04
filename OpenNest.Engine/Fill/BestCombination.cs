using OpenNest.Math;

namespace OpenNest
{
    internal record CombinationResult(bool Found, int Count1, int Count2);

    internal static class BestCombination
    {
        public static CombinationResult FindFrom2(double length1, double length2, double overallLength)
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
