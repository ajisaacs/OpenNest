using System.Collections.Generic;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Shapes
{
    public class PipeFlangeShape : ShapeDefinition
    {
        public double OD { get; set; }
        public double HoleDiameter { get; set; }
        public double HolePatternDiameter { get; set; }
        public int HoleCount { get; set; }
        public string PipeSize { get; set; }
        public double PipeClearance { get; set; }
        public bool Blind { get; set; }

        public override string GenerateName()
        {
            var name = $"Pipe Flange {Dim(OD)} OD";
            if (!string.IsNullOrEmpty(PipeSize))
                name += $" {PipeSize} Pipe";
            return name;
        }

        public override void SetPreviewDefaults()
        {
            OD = 7.5;
            HoleDiameter = 0.875;
            HolePatternDiameter = 5.5;
            HoleCount = 8;
            PipeSize = "2";
            PipeClearance = 0.0625;
            Blind = false;
        }

        public override IReadOnlyList<ShapeDimension> GetDimensions()
        {
            // The dotted bolt circle through the hole centers is called out by a leader from
            // outside, down and to the left, landing between holes rather than on one.
            var boltAngle = 1.25 * System.Math.PI;
            if (HoleCount > 0)
            {
                var holeStep = 2.0 * System.Math.PI / HoleCount;
                boltAngle = (System.Math.Floor(boltAngle / holeStep) + 0.5) * holeStep;
            }
            var dimensions = new List<ShapeDimension>
            {
                ShapeDimension.Linear(nameof(OD), new Vector(-OD / 2, 0), new Vector(OD / 2, 0), ShapeDimension.Up, "Ø"),
                ShapeDimension.CircleLeader(nameof(HolePatternDiameter), Vector.Zero, HolePatternDiameter / 2,
                    new Vector(PortableMath.Cos(boltAngle), PortableMath.Sin(boltAngle)), "Ø"),
            };

            // The leader crosses the hole nearest 45 degrees through its center, out that way.
            if (HoleCount > 0)
            {
                var step = 2.0 * System.Math.PI / HoleCount;
                var angle = System.Math.Round(System.Math.PI / 4 / step) * step;
                var out45 = new Vector(System.Math.Sqrt(0.5), System.Math.Sqrt(0.5));
                var center = new Vector(HolePatternDiameter / 2 * PortableMath.Cos(angle), HolePatternDiameter / 2 * PortableMath.Sin(angle));
                dimensions.Add(ShapeDimension.HoleLeader(nameof(HoleDiameter), center, HoleDiameter / 2, out45, "Ø")
                    .WithCount(nameof(HoleCount), "holes"));
            }

            return dimensions;
        }

        public override Drawing GetDrawing()
        {
            var entities = new List<Entity>();

            entities.Add(new Circle(0, 0, OD / 2.0));

            var boltCircleRadius = HolePatternDiameter / 2.0;
            var holeRadius = HoleDiameter / 2.0;
            var angleStep = 2.0 * System.Math.PI / HoleCount;

            for (var i = 0; i < HoleCount; i++)
            {
                var angle = i * angleStep;
                var cx = boltCircleRadius * PortableMath.Cos(angle);
                var cy = boltCircleRadius * PortableMath.Sin(angle);
                entities.Add(new Circle(cx, cy, holeRadius));
            }

            if (
                !Blind
                && !string.IsNullOrEmpty(PipeSize)
                && PipeSizes.TryGetOD(PipeSize, out var pipeOD)
            )
            {
                var boreDiameter = pipeOD + PipeClearance;
                entities.Add(new Circle(0, 0, boreDiameter / 2.0));
            }

            return CreateDrawing(entities);
        }
    }
}
