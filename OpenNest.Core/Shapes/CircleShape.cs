using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Shapes
{
    public class CircleShape : ShapeDefinition
    {
        public double Diameter { get; set; }

        public override Drawing GetDrawing()
        {
            var entities = new List<Entity>
            {
                new Circle(0, 0, Diameter / 2.0)
            };

            return CreateDrawing(entities);
        }
    }
}
