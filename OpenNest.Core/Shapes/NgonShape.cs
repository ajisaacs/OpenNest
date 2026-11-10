using System.Collections.Generic;
using OpenNest.Geometry;
using OpenNest.Math;

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

        public override IReadOnlyList<ShapeDimension> GetDimensions() =>
            new[]
            {
                // Across the flats: the flat at angle 0 and the one opposite it.
                ShapeDimension.Linear(nameof(Width), new Vector(0, Width / 2.0), new Vector(Width, Width / 2.0), ShapeDimension.Down),
            };

        public override Drawing GetDrawing()
        {
            var n = Sides < 3 ? 3 : Sides;
            var center = Width / 2.0;
            var circumRadius = Width / (2.0 * PortableMath.Cos(System.Math.PI / n));
            var step = 2.0 * System.Math.PI / n;
            var start = System.Math.PI / n;

            var vertices = new Vector[n];
            for (var i = 0; i < n; i++)
            {
                var angle = start + i * step;
                vertices[i] = new Vector(
                    center + circumRadius * PortableMath.Cos(angle),
                    center + circumRadius * PortableMath.Sin(angle)
                );
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
