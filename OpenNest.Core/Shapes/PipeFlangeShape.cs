using System.Collections.Generic;
using OpenNest.Geometry;

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
            var dimensions = new List<ShapeDimension>
            {
                ShapeDimension.Linear(nameof(OD), new Vector(-OD / 2, 0), new Vector(OD / 2, 0), ShapeDimension.Up, "Ø"),
                ShapeDimension.Linear(nameof(HolePatternDiameter), new Vector(-HolePatternDiameter / 2, 0),
                    new Vector(HolePatternDiameter / 2, 0), ShapeDimension.Down, "Ø"),
            };

            // The leader starts on the hole nearest 45 degrees, at its edge facing out that way.
            if (HoleCount > 0)
            {
                var step = 2.0 * System.Math.PI / HoleCount;
                var angle = System.Math.Round(System.Math.PI / 4 / step) * step;
                var out45 = new Vector(System.Math.Sqrt(0.5), System.Math.Sqrt(0.5));
                var center = new Vector(HolePatternDiameter / 2 * System.Math.Cos(angle), HolePatternDiameter / 2 * System.Math.Sin(angle));
                var edge = new Vector(center.X + out45.X * HoleDiameter / 2, center.Y + out45.Y * HoleDiameter / 2);
                dimensions.Add(ShapeDimension.Leader(nameof(HoleDiameter), edge, out45, "Ø").WithCount(nameof(HoleCount), "holes"));
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
                var cx = boltCircleRadius * System.Math.Cos(angle);
                var cy = boltCircleRadius * System.Math.Sin(angle);
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
