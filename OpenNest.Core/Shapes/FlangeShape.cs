using System;
using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Shapes
{
    public class FlangeShape : ShapeDefinition
    {
        public double NominalPipeSize { get; set; }
        public double OD { get; set; }
        public double HoleDiameter { get; set; }
        public double HolePatternDiameter { get; set; }
        public int HoleCount { get; set; }

        public override Drawing GetDrawing()
        {
            var entities = new List<Entity>();

            // Outer circle
            entities.Add(new Circle(0, 0, OD / 2.0));

            // Bolt holes evenly spaced on the bolt circle
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
