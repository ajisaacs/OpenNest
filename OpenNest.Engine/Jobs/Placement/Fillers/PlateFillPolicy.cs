using System;
using System.Collections.Generic;
using OpenNest.Engine;
using OpenNest.Engine.Fill;
using OpenNest.Math;

namespace OpenNest.Engine.Jobs.Placement.Fillers;

/// <summary>
/// Filler-only settings that distinguish Fill from the remnant fills: how candidates are ranked,
/// the preferred direction, the axis surplus parts are trimmed from, and how angles are chosen.
/// Strategies see only the <see cref="Strategies.FillPolicy"/> the filler builds from it.
/// </summary>
internal sealed class PlateFillPolicy
{
    private readonly Func<IFillComparer> comparerFactory;
    private readonly Func<NestItem, double, double> angleExtent;

    private PlateFillPolicy(
        Func<IFillComparer> comparerFactory,
        NestDirection? preferredDirection,
        ShrinkAxis trimAxis,
        Func<NestItem, double, double> angleExtent
    )
    {
        this.comparerFactory = comparerFactory;
        PreferredDirection = preferredDirection;
        TrimAxis = trimAxis;
        this.angleExtent = angleExtent;
    }

    /// <summary>Plain fill: default ranking and the filler's learned angle candidates.</summary>
    internal static PlateFillPolicy Fill { get; } = new(
        () => new DefaultFillComparer(),
        null,
        ShrinkAxis.Width,
        null
    );

    /// <summary>Vertical remnant: minimize X-extent, prefer horizontal placement.</summary>
    internal static PlateFillPolicy VerticalRemnant { get; } = new(
        () => new VerticalRemnantComparer(),
        NestDirection.Horizontal,
        ShrinkAxis.Width,
        RotatedWidth
    );

    /// <summary>Horizontal remnant: minimize Y-extent, prefer vertical placement.</summary>
    internal static PlateFillPolicy HorizontalRemnant { get; } = new(
        () => new HorizontalRemnantComparer(),
        NestDirection.Vertical,
        ShrinkAxis.Length,
        RotatedHeight
    );

    internal NestDirection? PreferredDirection { get; }

    internal ShrinkAxis TrimAxis { get; }

    /// <summary>
    /// True when the filler's <see cref="AngleCandidateBuilder"/> chooses the angles; false when
    /// <see cref="BuildFixedAngles"/> replaces it entirely.
    /// </summary>
    internal bool UsesAngleBuilder => angleExtent == null;

    internal IFillComparer CreateComparer() => comparerFactory();

    /// <summary>
    /// The remnant angles: the primary angle and its quarter turn, narrowest extent first. No
    /// circle case, sweep or learned angles, and the item's rotation range is ignored.
    /// </summary>
    internal List<double> BuildFixedAngles(NestItem item, ClassificationResult classification)
    {
        var baseAngles = new List<double>
        {
            classification.PrimaryAngle,
            classification.PrimaryAngle + Angle.HalfPI,
        };
        baseAngles.Sort((left, right) => angleExtent(item, left).CompareTo(angleExtent(item, right)));
        return baseAngles;
    }

    private static double RotatedWidth(NestItem item, double angle)
    {
        var boundingBox = item.Drawing.Program.BoundingBox();
        var cos = System.Math.Abs(System.Math.Cos(angle));
        var sin = System.Math.Abs(System.Math.Sin(angle));
        return boundingBox.Length * cos + boundingBox.Width * sin;
    }

    private static double RotatedHeight(NestItem item, double angle)
    {
        var boundingBox = item.Drawing.Program.BoundingBox();
        var cos = System.Math.Abs(System.Math.Cos(angle));
        var sin = System.Math.Abs(System.Math.Sin(angle));
        return boundingBox.Width * cos + boundingBox.Length * sin;
    }
}
