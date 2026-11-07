using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Shapes
{
    public class TShape : ShapeDefinition
    {
        public double Width { get; set; }
        public double Height { get; set; }
        public double StemWidth { get; set; }
        public double BarHeight { get; set; }

        public override string GenerateName() => $"T {Dim(Width)}x{Dim(Height)}";

        public override void SetPreviewDefaults()
        {
            Width = 10;
            Height = 8;
            StemWidth = 3;
            BarHeight = 3;
        }

        private double EffectiveStemWidth => StemWidth > 0 ? StemWidth : Width / 3.0;

        private double EffectiveBarHeight => BarHeight > 0 ? BarHeight : Height / 3.0;

        private double StemLeft => (Width - EffectiveStemWidth) / 2.0;

        public override IReadOnlyList<ShapeDimension> GetDimensions() =>
            new[]
            {
                ShapeDimension.Linear(nameof(Width), new Vector(0, Height), new Vector(Width, Height), ShapeDimension.Up),
                ShapeDimension.Linear(nameof(Height), new Vector(StemLeft, 0), new Vector(0, Height), ShapeDimension.Left),
                ShapeDimension.Linear(nameof(StemWidth), new Vector(StemLeft, 0), new Vector(StemLeft + EffectiveStemWidth, 0),
                    ShapeDimension.Down),
                ShapeDimension.Linear(nameof(BarHeight), new Vector(Width, Height - EffectiveBarHeight), new Vector(Width, Height),
                    ShapeDimension.Right),
            };

        public override Drawing GetDrawing()
        {
            var sw = EffectiveStemWidth;
            var bh = EffectiveBarHeight;
            var stemLeft = StemLeft;
            var stemRight = stemLeft + sw;
            var stemTop = Height - bh;

            var entities = new List<Entity>
            {
                new Line(stemLeft, 0, stemRight, 0),
                new Line(stemRight, 0, stemRight, stemTop),
                new Line(stemRight, stemTop, Width, stemTop),
                new Line(Width, stemTop, Width, Height),
                new Line(Width, Height, 0, Height),
                new Line(0, Height, 0, stemTop),
                new Line(0, stemTop, stemLeft, stemTop),
                new Line(stemLeft, stemTop, stemLeft, 0),
            };

            return CreateDrawing(entities);
        }
    }
}
