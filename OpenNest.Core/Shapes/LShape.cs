using OpenNest.Geometry;
using System.Collections.Generic;

namespace OpenNest.Shapes
{
    public class LShape : ShapeDefinition
    {
        public double Width { get; set; }
        public double Height { get; set; }
        public double LegWidth { get; set; }
        public double LegHeight { get; set; }

        public override Drawing GetDrawing()
        {
            var lw = LegWidth > 0 ? LegWidth : Width / 2.0;
            var lh = LegHeight > 0 ? LegHeight : Height / 2.0;

            var entities = new List<Entity>
            {
                new Line(0, 0, Width, 0),
                new Line(Width, 0, Width, lh),
                new Line(Width, lh, lw, lh),
                new Line(lw, lh, lw, Height),
                new Line(lw, Height, 0, Height),
                new Line(0, Height, 0, 0)
            };

            return CreateDrawing(entities);
        }
    }
}
