using OpenNest.Geometry;
using OpenNest.Math;
using System.Collections.Generic;

namespace OpenNest.Shapes
{
    public class RoundedRectangleShape : ShapeDefinition
    {
        public double Width { get; set; }
        public double Height { get; set; }
        public double Radius { get; set; }

        public override Drawing GetDrawing()
        {
            var r = Radius;
            var entities = new List<Entity>();

            if (r <= 0)
            {
                entities.Add(new Line(0, 0, Width, 0));
                entities.Add(new Line(Width, 0, Width, Height));
                entities.Add(new Line(Width, Height, 0, Height));
                entities.Add(new Line(0, Height, 0, 0));
            }
            else
            {
                // Bottom edge (left to right, above bottom-left arc to bottom-right arc)
                entities.Add(new Line(r, 0, Width - r, 0));

                // Bottom-right corner arc: center at (Width-r, r), from 270deg to 360deg
                entities.Add(new Arc(Width - r, r, r,
                    Angle.ToRadians(270), Angle.ToRadians(360)));

                // Right edge
                entities.Add(new Line(Width, r, Width, Height - r));

                // Top-right corner arc: center at (Width-r, Height-r), from 0deg to 90deg
                entities.Add(new Arc(Width - r, Height - r, r,
                    Angle.ToRadians(0), Angle.ToRadians(90)));

                // Top edge (right to left)
                entities.Add(new Line(Width - r, Height, r, Height));

                // Top-left corner arc: center at (r, Height-r), from 90deg to 180deg
                entities.Add(new Arc(r, Height - r, r,
                    Angle.ToRadians(90), Angle.ToRadians(180)));

                // Left edge
                entities.Add(new Line(0, Height - r, 0, r));

                // Bottom-left corner arc: center at (r, r), from 180deg to 270deg
                entities.Add(new Arc(r, r, r,
                    Angle.ToRadians(180), Angle.ToRadians(270)));
            }

            return CreateDrawing(entities);
        }
    }
}
