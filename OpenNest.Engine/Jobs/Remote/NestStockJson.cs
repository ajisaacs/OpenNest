using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Geometry;

namespace OpenNest.Engine.Jobs.Remote;

/// <summary>
/// Wire form of one <see cref="NestPlateStock"/>: every setting an engine or validator reads,
/// clamps in plate coordinates exactly as given and the machine rail they came from, so a
/// sheet rebuilt by <see cref="ToStock"/> equals the original bit for bit
/// (<see cref="RepeatedPlateGrouping.SameStock"/>).
/// </summary>
public sealed class NestStockJson
{
    public string Id { get; set; }
    public double Width { get; set; }
    public double Length { get; set; }
    public int? Quantity { get; set; }
    public double PartSpacing { get; set; }
    public double EdgeLeft { get; set; }
    public double EdgeBottom { get; set; }
    public double EdgeRight { get; set; }
    public double EdgeTop { get; set; }
    public int Quadrant { get; set; } = 1;
    public double? Cost { get; set; }
    public List<ClampZoneJson> ClampZones { get; set; } = new();
    public ClampRailJson ClampRail { get; set; }
    public OutlineJson Outline { get; set; }

    public static NestStockJson From(NestPlateStock stock)
    {
        ArgumentNullException.ThrowIfNull(stock);
        return new NestStockJson
        {
            Id = stock.Id,
            Width = stock.Size.Width,
            Length = stock.Size.Length,
            Quantity = stock.Quantity,
            PartSpacing = stock.PartSpacing,
            EdgeLeft = stock.EdgeSpacing.Left,
            EdgeBottom = stock.EdgeSpacing.Bottom,
            EdgeRight = stock.EdgeSpacing.Right,
            EdgeTop = stock.EdgeSpacing.Top,
            Quadrant = stock.Quadrant,
            Cost = stock.Cost,
            ClampZones = stock.SheetClampZones.Select(z => new ClampZoneJson
            {
                Id = z.Id,
                X = z.Bounds.X,
                Y = z.Bounds.Y,
                Length = z.Bounds.Length,
                Width = z.Bounds.Width,
            }).ToList(),
            ClampRail = stock.ClampRail is { } rail ? new ClampRailJson
            {
                Width = rail.Width,
                Depth = rail.Depth,
                RemovalMinutes = rail.RemovalMinutes,
                Clearance = rail.Clearance,
                Clamps = rail.Clamps.Select(c => new RailClampJson { Edge = c.Edge, Center = c.Center }).ToList(),
            } : null,
            Outline = stock.Outline is { } outline ? new OutlineJson
            {
                Points = outline.Points.Select(PointJson.From).ToList(),
                Obstacles = outline.Obstacles.Select(o => o.Select(PointJson.From).ToList()).ToList(),
            } : null,
        };
    }

    public NestPlateStock ToStock()
    {
        var size = new Size(Width, Length);
        var zones = (ClampZones ?? new()).Select(z => new ClampZone(z.Id, new Box(z.X, z.Y, z.Length, z.Width))).ToList();
        var outline = Outline is { } o
            ? new SheetOutline(o.Points.Select(p => p.ToVector()),
                (o.Obstacles ?? new()).Select(ob => ob.Select(p => p.ToVector())))
            : null;
        return new NestPlateStock(Id, size, Quantity, PartSpacing,
            new Spacing(EdgeLeft, EdgeBottom, EdgeRight, EdgeTop), Quadrant, Cost, zones, outline)
        {
            ClampRail = ClampRail is { } rail
                ? new ClampRail(rail.Width, rail.Depth,
                    (rail.Clamps ?? new()).Select(c => new RailClamp(c.Edge, c.Center)),
                    rail.RemovalMinutes, rail.Clearance)
                : null,
        };
    }

    public sealed class ClampZoneJson
    {
        public string Id { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Length { get; set; }
        public double Width { get; set; }
    }

    public sealed class ClampRailJson
    {
        public double Width { get; set; }
        public double Depth { get; set; }
        public double RemovalMinutes { get; set; }
        public double? Clearance { get; set; }
        public List<RailClampJson> Clamps { get; set; } = new();
    }

    public sealed class RailClampJson
    {
        public ClampEdge Edge { get; set; }
        public double Center { get; set; }
    }

    public sealed class OutlineJson
    {
        public List<PointJson> Points { get; set; } = new();
        public List<List<PointJson>> Obstacles { get; set; } = new();
    }

    public sealed class PointJson
    {
        public double X { get; set; }
        public double Y { get; set; }

        public static PointJson From(Vector v) => new() { X = v.X, Y = v.Y };

        public Vector ToVector() => new(X, Y);
    }
}
