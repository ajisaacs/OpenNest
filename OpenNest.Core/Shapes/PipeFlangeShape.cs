using OpenNest.Geometry;
using System.Collections.Generic;

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

            if (!Blind && !string.IsNullOrEmpty(PipeSize) && PipeSizes.TryGetOD(PipeSize, out var pipeOD))
            {
                var boreDiameter = pipeOD + PipeClearance;
                entities.Add(new Circle(0, 0, boreDiameter / 2.0));
            }

            return CreateDrawing(entities);
        }
    }
}
