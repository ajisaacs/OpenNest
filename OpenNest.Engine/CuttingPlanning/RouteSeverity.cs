using System;
using System.Collections.Generic;
using OpenNest.Diagnostics;

namespace OpenNest.Engine.CuttingPlanning;

/// <summary>
/// How unsafe a route is, compared most severe first: motions into material (leads through a
/// part, cut-order or nested-lead conflicts, incomplete checks), then rapids across cut
/// contours, then everything else. The default is a route that breaks no check.
/// </summary>
internal readonly record struct RouteSeverity(int Severe, int Rapids, int Minor) : IComparable<RouteSeverity>
{
    internal bool IsClear => Severe == 0 && Rapids == 0 && Minor == 0;

    internal int Count => Severe + Rapids + Minor;

    public int CompareTo(RouteSeverity other) =>
        Severe != other.Severe ? Severe.CompareTo(other.Severe)
        : Rapids != other.Rapids ? Rapids.CompareTo(other.Rapids)
        : Minor.CompareTo(other.Minor);

    public static bool operator <(RouteSeverity left, RouteSeverity right) => left.CompareTo(right) < 0;

    public static bool operator >(RouteSeverity left, RouteSeverity right) => left.CompareTo(right) > 0;

    /// <summary>A finding without a kind is a refused lead path (into material or a pierce zone).</summary>
    internal static RouteSeverity Of(IEnumerable<PostVerificationKind?> kinds)
    {
        var severe = 0;
        var rapids = 0;
        var minor = 0;
        foreach (var kind in kinds)
        {
            switch (kind)
            {
                case null or PostVerificationKind.Overlap or PostVerificationKind.NestedCutOrder
                    or PostVerificationKind.NestedLead or PostVerificationKind.Incomplete:
                    severe++;
                    break;
                case PostVerificationKind.RapidCrossing:
                    rapids++;
                    break;
                default:
                    minor++;
                    break;
            }
        }
        return new(severe, rapids, minor);
    }
}
