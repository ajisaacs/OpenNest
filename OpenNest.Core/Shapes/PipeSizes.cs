using System.Collections.Generic;
using System.Linq;

namespace OpenNest.Shapes
{
    public static class PipeSizes
    {
        public readonly record struct Entry(string Label, double OuterDiameter);

        public static IReadOnlyList<Entry> All { get; } = new List<Entry>
        {
            new Entry("1/8",    0.405),
            new Entry("1/4",    0.540),
            new Entry("3/8",    0.675),
            new Entry("1/2",    0.840),
            new Entry("3/4",    1.050),
            new Entry("1",      1.315),
            new Entry("1 1/4",  1.660),
            new Entry("1 1/2",  1.900),
            new Entry("2",      2.375),
            new Entry("2 1/2",  2.875),
            new Entry("3",      3.500),
            new Entry("3 1/2",  4.000),
            new Entry("4",      4.500),
            new Entry("4 1/2",  5.000),
            new Entry("5",      5.563),
            new Entry("6",      6.625),
            new Entry("7",      7.625),
            new Entry("8",      8.625),
            new Entry("9",      9.625),
            new Entry("10",    10.750),
            new Entry("11",    11.750),
            new Entry("12",    12.750),
            new Entry("14",    14.000),
            new Entry("16",    16.000),
            new Entry("18",    18.000),
            new Entry("20",    20.000),
            new Entry("24",    24.000),
            new Entry("26",    26.000),
            new Entry("28",    28.000),
            new Entry("30",    30.000),
            new Entry("32",    32.000),
            new Entry("34",    34.000),
            new Entry("36",    36.000),
            new Entry("42",    42.000),
            new Entry("48",    48.000),
        };

        public static bool TryGetOD(string label, out double outerDiameter)
        {
            foreach (var entry in All)
            {
                if (entry.Label == label)
                {
                    outerDiameter = entry.OuterDiameter;
                    return true;
                }
            }

            outerDiameter = 0;
            return false;
        }

        public static IEnumerable<Entry> GetFittingSizes(double maxOD)
        {
            return All.Where(e => e.OuterDiameter <= maxOD);
        }
    }
}
