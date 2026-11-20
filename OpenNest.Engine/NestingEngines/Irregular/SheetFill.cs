#nullable enable
using System.Collections.Generic;
using OpenNest.Engine.Jobs;

namespace OpenNest.Engine.NestingEngines.Irregular;

/// <summary>A part on a sheet: its orientation with the reference point at (X, Y).</summary>
internal sealed record Placed(Orientation Orientation, double X, double Y)
{
    public double Left => X + Orientation.MinX;
    public double Right => X + Orientation.MaxX;
    public double Bottom => Y + Orientation.MinY;
    public double Top => Y + Orientation.MaxY;
}

/// <summary>One sheet of a stock with the parts packed on it, in placement order, and their area.</summary>
internal sealed record SheetFill(NestPlateStock Stock, IReadOnlyList<Placed> Parts, double PartArea);
