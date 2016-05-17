using System;

namespace OpenNest
{
    internal static class BestCombination
    {
        public static bool FindFrom2(double length1, double length2, double overallLength, out int count1, out int count2)
        {
            overallLength += Tolerance.Epsilon;

            if (length1 > overallLength)
            {
                if (length2 > overallLength)
                {
                    count1 = 0;
                    count2 = 0;
                    return false;
                }

                count1 = 0;
                count2 = (int)Math.Floor(overallLength / length2);
                return true;
            }

            if (length2 > overallLength)
            {
                count1 = (int)Math.Floor(overallLength / length1);
                count2 = 0;
                return true;
            }

            var maxCountLength1 = (int)Math.Floor(overallLength / length1);

            count1 = maxCountLength1;
            count2 = 0;

            var remnant = overallLength - maxCountLength1 * length1;

            if (remnant.IsEqualTo(0))
                return true;

            for (int countLength1 = 0; countLength1 <= maxCountLength1; ++countLength1)
            {
                var remnant1 = overallLength - countLength1 * length1;

                if (remnant1 >= length2)
                {
                    var countLength2 = (int)Math.Floor(remnant1 / length2);
                    var remnant2 = remnant1 - length2 * countLength2;

                    if (!(remnant2 < remnant))
                        continue;

                    count1 = countLength1;
                    count2 = countLength2;

                    if (remnant2.IsEqualTo(0))
                        break;

                    remnant = remnant2;
                }
                else
                {
                    if (!(remnant1 < remnant))
                        continue;

                    count1 = countLength1;
                    count2 = 0;

                    if (remnant1.IsEqualTo(0))
                        break;

                    remnant = remnant1;
                }
            }

            return true;
        }
    }
}
