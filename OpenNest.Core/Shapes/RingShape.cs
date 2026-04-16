using OpenNest.Geometry;
using System.Collections.Generic;

namespace OpenNest.Shapes
{
    public class RingShape : ShapeDefinition
    {
        public double OuterDiameter { get; set; }
        public double InnerDiameter { get; set; }

        public override string GenerateName() => $"Ring {Dim(OuterDiameter)}x{Dim(InnerDiameter)}";

        public override void SetPreviewDefaults()
        {
            OuterDiameter = 10;
            InnerDiameter = 6;
        }

        public override Drawing GetDrawing()
        {
            var entities = new List<Entity>
            {
                new Circle(0, 0, OuterDiameter / 2.0),
                new Circle(0, 0, InnerDiameter / 2.0)
            };

            return CreateDrawing(entities);
        }
    }
}
