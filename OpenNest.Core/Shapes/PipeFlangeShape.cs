using OpenNest.Geometry;
using System.Collections.Generic;

namespace OpenNest.Shapes
{
    public class PipeFlangeShape : ShapeDefinition
    {
        public double NominalPipeSize { get; set; }
        public double OD { get; set; }
        public double HoleDiameter { get; set; }
        public double HolePatternDiameter { get; set; }
        public int HoleCount { get; set; }

        public override void SetPreviewDefaults()
        {
            NominalPipeSize = 2;
            OD = 7.5;
            HoleDiameter = 0.875;
            HolePatternDiameter = 5.5;
            HoleCount = 8;
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

            return CreateDrawing(entities);
        }
    }
}
