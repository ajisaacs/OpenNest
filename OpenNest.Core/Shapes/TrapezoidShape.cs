using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Shapes
{
    public class TrapezoidShape : ShapeDefinition
    {
        public double TopWidth { get; set; }
        public double BottomWidth { get; set; }
        public double Height { get; set; }

        public override Drawing GetDrawing()
        {
            var offset = (BottomWidth - TopWidth) / 2.0;

            var entities = new List<Entity>
            {
                new Line(0, 0, BottomWidth, 0),
                new Line(BottomWidth, 0, offset + TopWidth, Height),
                new Line(offset + TopWidth, Height, offset, Height),
                new Line(offset, Height, 0, 0)
            };

            return CreateDrawing(entities);
        }
    }
}
