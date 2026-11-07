using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Shapes
{
    public class CircleShape : ShapeDefinition
    {
        public double Diameter { get; set; }

        public override string GenerateName() => $"Circle {Dim(Diameter)} Dia";

        public override void SetPreviewDefaults()
        {
            Diameter = 8;
        }

        public override IReadOnlyList<ShapeDimension> GetDimensions() =>
            new[]
            {
                ShapeDimension.Linear(nameof(Diameter), new Vector(-Diameter / 2, 0), new Vector(Diameter / 2, 0), ShapeDimension.Up, "Ø"),
            };

        public override Drawing GetDrawing()
        {
            var entities = new List<Entity> { new Circle(0, 0, Diameter / 2.0) };

            return CreateDrawing(entities);
        }
    }
}
