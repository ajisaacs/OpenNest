using System.Collections.Generic;
using OpenNest.Diagnostics;

namespace OpenNest.CNC.CuttingPlanning;

/// <summary>
/// Remembers whether a lead curve stays clear of another part's material, so a search that
/// re-checks the same emitted leads (every prefix of a part repeats its earlier contours' leads,
/// and backtracking revisits them) runs each native query set once. A hit returns exactly what
/// <see cref="LeadPathValidator.MaterialContact"/> returned for that curve and material and charges
/// the same native query budget. Curves match by exact bits and materials by reference; the
/// materials must not change while the memo is in use. Scans that threw are not remembered and
/// run again. One instance belongs to one single-threaded search; replay must not use one.
/// </summary>
internal sealed class LeadContactMemo
{
    private readonly Dictionary<(PostVerificationGeometry.CurveKey Curve, LeadMaterialSnapshot Material), Scan> scans = new();

    /// <summary>Remembered native queries answered without running (Debug cost tests).</summary>
    internal long Hits { get; private set; }

    internal bool TryGet(PostVerificationGeometry.Curve curve, LeadMaterialSnapshot material, out Scan scan)
    {
        if (!scans.TryGetValue((curve.Key, material), out scan))
            return false;
        Hits++;
        return true;
    }

    internal void Add(PostVerificationGeometry.Curve curve, LeadMaterialSnapshot material, Scan scan) =>
        scans[(curve.Key, material)] = scan;

    /// <summary>A completed scan: whether it found contact and how many boundary queries it charged.</summary>
    internal readonly record struct Scan(bool Contact, int Queries);
}
