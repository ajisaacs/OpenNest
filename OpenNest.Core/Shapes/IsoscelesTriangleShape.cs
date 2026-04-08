using OpenNest.Geometry;
using System.Collections.Generic;

namespace OpenNest.Shapes
{
    public class IsoscelesTriangleShape : ShapeDefinition
    {
        public double Base { get; set; }
        public double Height { get; set; }

        public override void SetPreviewDefaults()
        {
            Base = 8;
            Height = 10;
        }

        public override Drawing GetDrawing()
        {
            var midX = Base / 2.0;

            var entities = new List<Entity>
            {
                new Line(0, 0, Base, 0),
                new Line(Base, 0, midX, Height),
                new Line(midX, Height, 0, 0)
            };

            return CreateDrawing(entities);
        }
    }
}
