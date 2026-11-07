using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Shapes
{
    public class LShape : ShapeDefinition
    {
        public double Width { get; set; }
        public double Height { get; set; }
        public double LegWidth { get; set; }
        public double LegHeight { get; set; }

        public override string GenerateName() => $"L {Dim(Width)}x{Dim(Height)}";

        public override void SetPreviewDefaults()
        {
            Width = 8;
            Height = 10;
            LegWidth = 3;
            LegHeight = 3;
        }

        private double EffectiveLegWidth => LegWidth > 0 ? LegWidth : Width / 2.0;

        private double EffectiveLegHeight => LegHeight > 0 ? LegHeight : Height / 2.0;

        public override IReadOnlyList<ShapeDimension> GetDimensions() =>
            new[]
            {
                ShapeDimension.Linear(nameof(Width), new Vector(0, 0), new Vector(Width, 0), ShapeDimension.Down),
                ShapeDimension.Linear(nameof(Height), new Vector(0, 0), new Vector(0, Height), ShapeDimension.Left),
                ShapeDimension.Linear(nameof(LegWidth), new Vector(0, Height), new Vector(EffectiveLegWidth, Height), ShapeDimension.Up),
                ShapeDimension.Linear(nameof(LegHeight), new Vector(Width, 0), new Vector(Width, EffectiveLegHeight), ShapeDimension.Right),
            };

        public override Drawing GetDrawing()
        {
            var lw = EffectiveLegWidth;
            var lh = EffectiveLegHeight;

            var entities = new List<Entity>
            {
                new Line(0, 0, Width, 0),
                new Line(Width, 0, Width, lh),
                new Line(Width, lh, lw, lh),
                new Line(lw, lh, lw, Height),
                new Line(lw, Height, 0, Height),
                new Line(0, Height, 0, 0),
            };

            return CreateDrawing(entities);
        }
    }
}
