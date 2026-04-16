using OpenNest.Geometry;
using System.Collections.Generic;

namespace OpenNest.Shapes
{
    public class TrapezoidShape : ShapeDefinition
    {
        public double TopWidth { get; set; }
        public double BottomWidth { get; set; }
        public double Height { get; set; }

        public override string GenerateName() => $"Trapezoid {Dim(TopWidth)}x{Dim(BottomWidth)}x{Dim(Height)}";

        public override void SetPreviewDefaults()
        {
            TopWidth = 6;
            BottomWidth = 10;
            Height = 6;
        }

        public override Drawing GetDrawing()
        {
            var offset = (BottomWidth - TopWidth) / 2.0;

            var entities = new List<Entity>
            {
                new Line(0, 0, BottomWidth, 0),
                new Line(BottomWidth, 0, offset + TopWidth, Height),
                new Line(offset + TopWidth, Height, offset, Height),
                new Line(offset, Height, 0, 0)
            };

            return CreateDrawing(entities);
        }
    }
}
