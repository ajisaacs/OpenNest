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

        public override Drawing GetDrawing()
        {
            var sw = StemWidth > 0 ? StemWidth : Width / 3.0;
            var bh = BarHeight > 0 ? BarHeight : Height / 3.0;
            var stemLeft = (Width - sw) / 2.0;
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
                new Line(stemLeft, stemTop, stemLeft, 0)
            };

            return CreateDrawing(entities);
        }
    }
}
