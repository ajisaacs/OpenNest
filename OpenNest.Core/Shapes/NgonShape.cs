using OpenNest.Geometry;
using System.Collections.Generic;

namespace OpenNest.Shapes
{
    public class NgonShape : ShapeDefinition
    {
        public int Sides { get; set; }
        public double Width { get; set; }

        public override string GenerateName() => $"{Sides}-Sided Polygon {Dim(Width)}";

        public override void SetPreviewDefaults()
        {
            Sides = 8;
            Width = 8;
        }

        public override Drawing GetDrawing()
        {
            var n = Sides < 3 ? 3 : Sides;
            var center = Width / 2.0;
            var circumRadius = Width / (2.0 * System.Math.Cos(System.Math.PI / n));
            var step = 2.0 * System.Math.PI / n;
            var start = System.Math.PI / n;

            var vertices = new Vector[n];
            for (var i = 0; i < n; i++)
            {
                var angle = start + i * step;
                vertices[i] = new Vector(
                    center + circumRadius * System.Math.Cos(angle),
                    center + circumRadius * System.Math.Sin(angle));
            }

            var entities = new List<Entity>();
            for (var i = 0; i < n; i++)
            {
                var next = (i + 1) % n;
                entities.Add(new Line(vertices[i], vertices[next]));
            }

            return CreateDrawing(entities);
        }
    }
}
