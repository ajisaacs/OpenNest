using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Bending;
using OpenNest.Geometry;

namespace OpenNest.Shapes
{
    /// <summary>
    /// Flat blanks and sections for the sheet metal shapes: parts bent or rolled along their
    /// length, cut as a rectangle <c>length</c> along X by the developed width along Y.
    /// </summary>
    internal static class SheetMetal
    {
        public const double BendAngle = 90;

        public static void Validate(double thickness, double length, double kFactor)
        {
            if (!(thickness > 0))
                throw new InvalidOperationException("The thickness must be more than zero.");
            if (!(length > 0))
                throw new InvalidOperationException("The length must be more than zero.");
            if (!(kFactor >= 0 && kFactor <= 1))
                throw new InvalidOperationException("The K-factor must be from 0 to 1.");
        }

        public static double BendAllowance(double insideRadius, double thickness, double kFactor) =>
            BendMath.Allowance(Math.Angle.ToRadians(BendAngle), insideRadius, thickness, kFactor);

        /// <summary>The blank's outline, with a 90° up bend across it, etched, at each bend line.</summary>
        /// <param name="bendLines">Each bend line's distance from the blank's bottom edge.</param>
        public static List<Entity> Entities(double length, double width, IReadOnlyList<double> bendLines, double insideRadius, out List<Bend> bends)
        {
            var entities = new List<Entity>
            {
                new Line(0, 0, length, 0),
                new Line(length, 0, length, width),
                new Line(length, width, 0, width),
                new Line(0, width, 0, 0),
            };

            bends = bendLines
                .Select(y => new Bend
                {
                    StartPoint = new Vector(0, y),
                    EndPoint = new Vector(length, y),
                    Direction = BendDirection.Up,
                    Angle = BendAngle,
                    Radius = insideRadius,
                })
                .ToList();
            foreach (var bend in bends)
                entities.AddRange(bend.GetEtchEntities());

            return entities;
        }

        /// <summary>
        /// The section of a part bent up 90° at its right end (an angle, "_|") or at both ends
        /// (a channel, "U"): the bottom on Y = 0 from X = 0 to <paramref name="outsideWidth"/>,
        /// the sides up to <paramref name="outsideHeight"/>, every bend at
        /// <paramref name="insideRadius"/>. Dimensioned with the outside sizes, the thickness
        /// across the right side's top and the inside radius of its bend.
        /// </summary>
        public static ShapeSideView Section(
            bool bothSides, double outsideWidth, double outsideHeight, double thickness, double insideRadius,
            string widthProperty, string heightProperty, string thicknessProperty, string radiusProperty)
        {
            const double quarter = System.Math.PI / 2;
            var outsideRadius = insideRadius + thickness;
            var right = new Vector(outsideWidth - outsideRadius, outsideRadius);
            var left = new Vector(outsideRadius, outsideRadius);
            var w = outsideWidth;
            var h = outsideHeight;
            var t = thickness;

            // Round the outside, counterclockwise from the bottom's left end, then back inside
            // (a reversed arc runs clockwise from its start angle to its end angle).
            var entities = new List<Entity>();
            if (bothSides)
            {
                entities.Add(new Line(0, h, 0, outsideRadius));
                entities.Add(new Arc(left, outsideRadius, 2 * quarter, 3 * quarter));
                entities.Add(new Line(outsideRadius, 0, right.X, 0));
            }
            else
            {
                entities.Add(new Line(0, 0, right.X, 0));
            }
            entities.Add(new Arc(right, outsideRadius, 3 * quarter, 4 * quarter));
            entities.Add(new Line(w, outsideRadius, w, h));
            entities.Add(new Line(w, h, w - t, h));
            entities.Add(new Line(w - t, h, w - t, outsideRadius));
            entities.Add(new Arc(right, insideRadius, 0, 3 * quarter, true));
            if (bothSides)
            {
                entities.Add(new Line(right.X, t, outsideRadius, t));
                entities.Add(new Arc(left, insideRadius, 3 * quarter, 2 * quarter, true));
                entities.Add(new Line(t, outsideRadius, t, h));
                entities.Add(new Line(t, h, 0, h));
            }
            else
            {
                entities.Add(new Line(right.X, t, 0, t));
                entities.Add(new Line(0, t, 0, 0));
            }

            var dimensions = new[]
            {
                ShapeDimension.Linear(widthProperty, new Vector(0, 0), new Vector(w, 0), ShapeDimension.Down),
                ShapeDimension.Linear(heightProperty, new Vector(w, 0), new Vector(w, h), ShapeDimension.Right),
                ShapeDimension.Linear(thicknessProperty, new Vector(w - t, h), new Vector(w, h), ShapeDimension.Up),
                ShapeDimension.ArcLeader(radiusProperty, right, insideRadius, 3 * quarter, 4 * quarter, "R"),
            };

            return new ShapeSideView(entities, dimensions);
        }
    }
}
