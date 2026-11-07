using System.Collections.Generic;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Shapes
{
    public class RoundedRectangleShape : ShapeDefinition
    {
        public double Length { get; set; }
        public double Width { get; set; }
        public double Radius { get; set; }

        public override string GenerateName() =>
            $"Rounded Rectangle {Dim(Length)}x{Dim(Width)} R{Dim(Radius)}";

        public override void SetPreviewDefaults()
        {
            Length = 12;
            Width = 6;
            Radius = 1;
        }

        public override IReadOnlyList<ShapeDimension> GetDimensions() =>
            new[]
            {
                ShapeDimension.Linear(nameof(Length), new Vector(0, 0), new Vector(Length, 0), ShapeDimension.Down),
                ShapeDimension.Linear(nameof(Width), new Vector(Length, 0), new Vector(Length, Width), ShapeDimension.Right),
                // On the top-left corner arc, pointing out along its 45-degree radius, clear of
                // the width dimension on the right.
                ShapeDimension.Leader(nameof(Radius),
                    new Vector(Radius * (1 - System.Math.Sqrt(0.5)), Width - Radius * (1 - System.Math.Sqrt(0.5))),
                    new Vector(-1, 1), "R"),
            };

        public override Drawing GetDrawing()
        {
            var r = Radius;
            var entities = new List<Entity>();

            if (r <= 0)
            {
                entities.Add(new Line(0, 0, Length, 0));
                entities.Add(new Line(Length, 0, Length, Width));
                entities.Add(new Line(Length, Width, 0, Width));
                entities.Add(new Line(0, Width, 0, 0));
            }
            else
            {
                // Bottom edge (left to right, above bottom-left arc to bottom-right arc)
                entities.Add(new Line(r, 0, Length - r, 0));

                // Bottom-right corner arc: center at (Length-r, r), from 270deg to 360deg
                entities.Add(new Arc(Length - r, r, r, Angle.ToRadians(270), Angle.ToRadians(360)));

                // Right edge
                entities.Add(new Line(Length, r, Length, Width - r));

                // Top-right corner arc: center at (Length-r, Width-r), from 0deg to 90deg
                entities.Add(
                    new Arc(Length - r, Width - r, r, Angle.ToRadians(0), Angle.ToRadians(90))
                );

                // Top edge (right to left)
                entities.Add(new Line(Length - r, Width, r, Width));

                // Top-left corner arc: center at (r, Width-r), from 90deg to 180deg
                entities.Add(new Arc(r, Width - r, r, Angle.ToRadians(90), Angle.ToRadians(180)));

                // Left edge
                entities.Add(new Line(0, Width - r, 0, r));

                // Bottom-left corner arc: center at (r, r), from 180deg to 270deg
                entities.Add(new Arc(r, r, r, Angle.ToRadians(180), Angle.ToRadians(270)));
            }

            return CreateDrawing(entities);
        }
    }
}
