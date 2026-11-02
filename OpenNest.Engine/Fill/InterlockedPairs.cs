using System.Collections.Generic;
using System.Linq;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.Fill
{
    /// <summary>
    /// Finds the interlocked pairs in a fill pattern. Separate parts keep spacing apart, so only
    /// an interlocked pair's bounding boxes overlap; each part pairs with the one it overlaps most.
    /// </summary>
    internal static class InterlockedPairs
    {
        /// <returns>Each part's partner index, or -1 for a part outside any pair.</returns>
        internal static int[] Partners(IReadOnlyList<Box> boxes)
        {
            var partner = Enumerable.Repeat(-1, boxes.Count).ToArray();
            var overlaps = new List<(double Area, int A, int B)>();
            // Sweep by left edge: once a box starts past this one's right edge, so do the rest.
            var byLeft = Enumerable.Range(0, boxes.Count).OrderBy(i => boxes[i].Left).ThenBy(i => i).ToArray();
            for (var m = 0; m < byLeft.Length; m++)
                for (var n = m + 1; n < byLeft.Length && boxes[byLeft[n]].Left < boxes[byLeft[m]].Right; n++)
                {
                    var i = System.Math.Min(byLeft[m], byLeft[n]);
                    var j = System.Math.Max(byLeft[m], byLeft[n]);
                    var a = boxes[i];
                    var b = boxes[j];
                    var length = System.Math.Min(a.Right, b.Right) - System.Math.Max(a.Left, b.Left);
                    var width = System.Math.Min(a.Top, b.Top) - System.Math.Max(a.Bottom, b.Bottom);
                    if (length > Tolerance.Epsilon && width > Tolerance.Epsilon)
                        overlaps.Add((length * width, i, j));
                }
            foreach (var (_, a, b) in overlaps.OrderByDescending(o => o.Area).ThenBy(o => o.A).ThenBy(o => o.B))
                if (partner[a] < 0 && partner[b] < 0)
                {
                    partner[a] = b;
                    partner[b] = a;
                }
            return partner;
        }
    }
}
