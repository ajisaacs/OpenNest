using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Shapes
{
    public class TrapezoidShape : ShapeDefinition
    {
        public double TopWidth { get; set; }
        public double BottomWidth { get; set; }
        public double Height { get; set; }

        public override string GenerateName() =>
            $"Trapezoid {Dim(TopWidth)}x{Dim(BottomWidth)}x{Dim(Height)}";

        public override void SetPreviewDefaults()
        {
            TopWidth = 6;
            BottomWidth = 10;
            Height = 6;
        }

        public override IReadOnlyList<ShapeDimension> GetDimensions() =>
            new[]
            {
                ShapeDimension.Linear(nameof(BottomWidth), new Vector(0, 0), new Vector(BottomWidth, 0), ShapeDimension.Down),
                ShapeDimension.Linear(nameof(TopWidth), new Vector((BottomWidth - TopWidth) / 2.0, Height),
                    new Vector((BottomWidth + TopWidth) / 2.0, Height), ShapeDimension.Up),
                ShapeDimension.Linear(nameof(Height), new Vector(BottomWidth, 0), new Vector((BottomWidth + TopWidth) / 2.0, Height),
                    ShapeDimension.Right),
            };

        public override Drawing GetDrawing()
        {
            var offset = (BottomWidth - TopWidth) / 2.0;

            var entities = new List<Entity>
            {
                new Line(0, 0, BottomWidth, 0),
                new Line(BottomWidth, 0, offset + TopWidth, Height),
                new Line(offset + TopWidth, Height, offset, Height),
                new Line(offset, Height, 0, 0),
            };

            return CreateDrawing(entities);
        }
    }
}
