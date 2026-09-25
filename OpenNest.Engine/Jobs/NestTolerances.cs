using System;
using OpenNest.Geometry;

namespace OpenNest.Engine.Jobs;

/// <summary>Shared numerical contract for layout validation.
/// Only material contours (SpecialLayers.IsMaterial) count for bounds, clearance and scoring.
/// Scribe/etch marks never affect these checks or salvage envelopes, even outside the sheet.</summary>
public static class NestTolerances
{
    /// <summary>Arc chord tolerance used by both validators. The layout check circumscribes
    /// perimeter arcs and inscribes cutouts; the placement validator uses inscribed arcs.</summary>
    public const double ValidationOutline = 0.001;

    /// <summary>How far under the part spacing a gap may fall and still pass both validators.
    /// Layouts placed exactly at the spacing land up to ~1e-4 short once rotated, rounded
    /// coordinates (e.g. PEP's 4-decimal exports) meet the Clipper grid; 0.0005 covers that
    /// with margin and is far below anything a cutting machine can resolve.</summary>
    public const double SpacingSlack = 0.0005;

    /// <summary>Clipper decimal precision (a 1e-4 coordinate grid).</summary>
    public const int ClipperPrecision = ClipperBridge.Precision;

    /// <summary>Extra pair clearance for an engine whose outline error is bounded by t.
    /// Each of two boundaries contributes ValidationOutline from validator flattening,
    /// one Clipper grid unit from rounding, and t from engine flattening: therefore
    /// 2 * ValidationOutline + 2 * 10^(-ClipperPrecision) + 2 * t.
    /// This budget assumes valid material geometry and bounded chord error on both sides.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Tolerance is negative or non-finite.</exception>
    public static double SafeClearanceMargin(double engineChordTolerance)
    {
        if (!double.IsFinite(engineChordTolerance) || engineChordTolerance < 0)
            throw new ArgumentOutOfRangeException(nameof(engineChordTolerance));
        return 2 * ValidationOutline + 2 * System.Math.Pow(10, -ClipperPrecision)
            + 2 * engineChordTolerance;
    }
}
