using System;
using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingStrategy
{
    /// <summary>
    /// Remembers <see cref="ContourCuttingStrategy.ResolvePierceZone"/> for one preparation's
    /// contours. The resolution reads only the cutout's own geometry, the entry, the configured
    /// lead-in and the zone, so for a given contour it is a pure function of those, yet the
    /// search emits every contour of a part's chain again on every expansion, and on a cutout
    /// whose configured pierce is inside the zone each resolution flattens the cutout, finds its
    /// pole and erodes it. One memo serves every emission of one <c>PreparedContours</c> and
    /// its copies, which share the contours by ordinal. A lead-in a memo returns is shared and
    /// must not be mutated.
    /// </summary>
    internal sealed class PierceZoneMemo
    {
        private readonly Dictionary<Key, LeadIn> resolved = new();
        private readonly object gate = new();

        /// <summary>How many resolutions the memo answered without computing; for tests.</summary>
        internal int Hits { get; private set; }

        internal LeadIn Resolve(int contour, Shape hole, Vector point, LeadIn leadIn, double normal,
            double pierceRadius, RotationType turn)
        {
            if (KeyOf(leadIn) is not { } lead)
                return ContourCuttingStrategy.ResolvePierceZone(hole, point, leadIn, normal, pierceRadius, turn);
            var key = new Key(contour, point.X, point.Y, normal, pierceRadius, turn, lead);
            lock (gate)
            {
                if (resolved.TryGetValue(key, out var known))
                {
                    Hits++;
                    return known;
                }
            }
            var result = ContourCuttingStrategy.ResolvePierceZone(hole, point, leadIn, normal, pierceRadius, turn);
            lock (gate)
            {
                resolved[key] = result;
            }
            return result;
        }

        // The configured lead-in by value: an equal lead-in resolves the same way whichever
        // instance carries it. A lead-in type this does not know is resolved every time.
        private static LeadKey? KeyOf(LeadIn leadIn) => leadIn switch
        {
            NoLeadIn => new(0, 0, 0, 0, 0),
            LineLeadIn line => new(1, line.Length, line.ApproachAngle, 0, 0),
            ArcLeadIn arc => new(2, arc.Radius, arc.SweepAngle, 0, 0),
            LineArcLeadIn lineArc => new(3, lineArc.LineLength, lineArc.ApproachAngle, lineArc.ArcRadius, 0),
            CleanHoleLeadIn cleanHole => new(4, cleanHole.LineLength, cleanHole.ArcRadius, cleanHole.Kerf, 0),
            LineLineLeadIn lineLine => new(5, lineLine.Length1, lineLine.ApproachAngle1, lineLine.Length2, lineLine.ApproachAngle2),
            _ => null,
        };

        private readonly record struct LeadKey(int Kind, double A, double B, double C, double D);

        private readonly record struct Key(int Contour, double X, double Y, double Normal, double PierceRadius,
            RotationType Turn, LeadKey Lead);
    }
}
