using OpenNest.Geometry;
using System.Collections.Generic;

namespace OpenNest.Shapes
{
    public class RectangleShape : ShapeDefinition
    {
        public double Length { get; set; }
        public double Width { get; set; }

        public override Drawing GetDrawing()
        {
            var entities = new List<Entity>
            {
                new Line(0, 0, Length, 0),
                new Line(Length, 0, Length, Width),
                new Line(Length, Width, 0, Width),
                new Line(0, Width, 0, 0)
            };

            return CreateDrawing(entities);
        }
    }
}
