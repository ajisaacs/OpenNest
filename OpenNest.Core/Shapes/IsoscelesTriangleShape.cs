using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Shapes
{
    public class IsoscelesTriangleShape : ShapeDefinition
    {
        public double Base { get; set; }
        public double Height { get; set; }

        public override string GenerateName() => $"Isosceles Triangle {Dim(Base)}x{Dim(Height)}";

        public override void SetPreviewDefaults()
        {
            Base = 8;
            Height = 10;
        }

        public override IReadOnlyList<ShapeDimension> GetDimensions() =>
            new[]
            {
                ShapeDimension.Linear(nameof(Base), new Vector(0, 0), new Vector(Base, 0), ShapeDimension.Down),
                ShapeDimension.Linear(nameof(Height), new Vector(Base, 0), new Vector(Base / 2.0, Height), ShapeDimension.Right),
            };

        public override Drawing GetDrawing()
        {
            var midX = Base / 2.0;

            var entities = new List<Entity>
            {
                new Line(0, 0, Base, 0),
                new Line(Base, 0, midX, Height),
                new Line(midX, Height, 0, 0),
            };

            return CreateDrawing(entities);
        }
    }
}
