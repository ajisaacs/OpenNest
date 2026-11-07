using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Shapes
{
    public class RectangleShape : ShapeDefinition
    {
        public double Length { get; set; }
        public double Width { get; set; }

        public override string GenerateName() => $"Rectangle {Dim(Length)}x{Dim(Width)}";

        public override void SetPreviewDefaults()
        {
            Length = 12;
            Width = 6;
        }

        public override IReadOnlyList<ShapeDimension> GetDimensions() =>
            new[]
            {
                ShapeDimension.Linear(nameof(Length), new Vector(0, 0), new Vector(Length, 0), ShapeDimension.Down),
                ShapeDimension.Linear(nameof(Width), new Vector(Length, 0), new Vector(Length, Width), ShapeDimension.Right),
            };

        public override Drawing GetDrawing()
        {
            var entities = new List<Entity>
            {
                new Line(0, 0, Length, 0),
                new Line(Length, 0, Length, Width),
                new Line(Length, Width, 0, Width),
                new Line(0, Width, 0, 0),
            };

            return CreateDrawing(entities);
        }
    }
}
