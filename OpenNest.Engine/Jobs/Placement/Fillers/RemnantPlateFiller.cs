using System;
using System.Collections.Generic;
using OpenNest.Engine;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.Jobs.Placement.Fillers;

internal class RemnantPlateFiller : DefaultPlateFiller
{
    private readonly RemnantFillPolicy policy;

    internal RemnantPlateFiller(Plate plate, RemnantFillPolicy policy)
        : base(plate)
    {
        this.policy = policy;
    }

    protected override IFillComparer CreateComparer() => policy.CreateComparer();

    public override NestDirection? PreferredDirection => policy.PreferredDirection;

    public override ShrinkAxis TrimAxis => policy.TrimAxis;

    public override List<double> BuildAngles(
        NestItem item,
        ClassificationResult classification,
        Box workArea
    ) => policy.BuildAngles(item, classification);
}

internal sealed class RemnantFillPolicy
{
    private readonly Func<IFillComparer> comparerFactory;
    private readonly Func<NestItem, double, double> angleExtent;

    private RemnantFillPolicy(
        Func<IFillComparer> comparerFactory,
        NestDirection preferredDirection,
        ShrinkAxis trimAxis,
        Func<NestItem, double, double> angleExtent
    )
    {
        this.comparerFactory = comparerFactory;
        PreferredDirection = preferredDirection;
        TrimAxis = trimAxis;
        this.angleExtent = angleExtent;
    }

    internal static RemnantFillPolicy Vertical { get; } = new(
        () => new VerticalRemnantComparer(),
        NestDirection.Horizontal,
        ShrinkAxis.Width,
        RotatedWidth
    );

    internal static RemnantFillPolicy Horizontal { get; } = new(
        () => new HorizontalRemnantComparer(),
        NestDirection.Vertical,
        ShrinkAxis.Length,
        RotatedHeight
    );

    internal NestDirection PreferredDirection { get; }

    internal ShrinkAxis TrimAxis { get; }

    internal IFillComparer CreateComparer() => comparerFactory();

    internal List<double> BuildAngles(NestItem item, ClassificationResult classification)
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
