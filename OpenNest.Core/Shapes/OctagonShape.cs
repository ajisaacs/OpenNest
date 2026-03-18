using OpenNest.Geometry;
using System.Collections.Generic;

namespace OpenNest.Shapes
{
    public class OctagonShape : ShapeDefinition
    {
        public double Width { get; set; }

        public override Drawing GetDrawing()
        {
            var center = Width / 2.0;
            var circumRadius = Width / (2.0 * System.Math.Cos(System.Math.PI / 8.0));

            var vertices = new Vector[8];
            for (var i = 0; i < 8; i++)
            {
                var angle = System.Math.PI / 8.0 + i * System.Math.PI / 4.0;
                vertices[i] = new Vector(
                    center + circumRadius * System.Math.Cos(angle),
                    center + circumRadius * System.Math.Sin(angle));
            }

            var entities = new List<Entity>();
            for (var i = 0; i < 8; i++)
            {
                var next = (i + 1) % 8;
                entities.Add(new Line(vertices[i], vertices[next]));
            }

            return CreateDrawing(entities);
        }
    }
}
