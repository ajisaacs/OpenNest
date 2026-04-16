using OpenNest.Geometry;
using System.Collections.Generic;

namespace OpenNest.Shapes
{
    public class RightTriangleShape : ShapeDefinition
    {
        public double Width { get; set; }
        public double Height { get; set; }

        public override string GenerateName() => $"Right Triangle {Dim(Width)}x{Dim(Height)}";

        public override void SetPreviewDefaults()
        {
            Width = 8;
            Height = 6;
        }

        public override Drawing GetDrawing()
        {
            var entities = new List<Entity>
            {
                new Line(0, 0, Width, 0),
                new Line(Width, 0, 0, Height),
                new Line(0, Height, 0, 0)
            };

            return CreateDrawing(entities);
        }
    }
}
