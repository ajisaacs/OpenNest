using System.Linq;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest
{
    public partial class Plate
    {
        /// <summary>
        /// The smallest box that contains the plate.
        /// </summary>
        /// <param name="includeParts"></param>
        /// <returns></returns>
        public Box BoundingBox(bool includeParts = true)
        {
            var plateBox = new Box();

            // Width = Y axis (vertical), Length = X axis (horizontal)
            switch (Quadrant)
            {
                case 1:
                    plateBox.X = 0;
                    plateBox.Y = 0;
                    break;

                case 2:
                    plateBox.X = (float)-Size.Length;
                    plateBox.Y = 0;
                    break;

                case 3:
                    plateBox.X = (float)-Size.Length;
                    plateBox.Y = (float)-Size.Width;
                    break;

                case 4:
                    plateBox.X = 0;
                    plateBox.Y = (float)-Size.Width;
                    break;

                default:
                    return new Box();
            }

            plateBox.Width = Size.Width;
            plateBox.Length = Size.Length;

            if (!includeParts)
                return plateBox;

            var boundingBox = new Box();
            var partsBox = Parts.GetBoundingBox();

            boundingBox.X = partsBox.Left < plateBox.Left ? partsBox.Left : plateBox.Left;

            boundingBox.Y = partsBox.Bottom < plateBox.Bottom ? partsBox.Bottom : plateBox.Bottom;

            boundingBox.Length =
                partsBox.Right > plateBox.Right
                    ? partsBox.Right - boundingBox.X
                    : plateBox.Right - boundingBox.X;

            boundingBox.Width =
                partsBox.Top > plateBox.Top
                    ? partsBox.Top - boundingBox.Y
                    : plateBox.Top - boundingBox.Y;

            return boundingBox;
        }

        /// <summary>
        /// The area within the edge spacing.
        /// </summary>
        /// <returns></returns>
        public Box WorkArea()
        {
            var box = BoundingBox(false);

            box.X += EdgeSpacing.Left;
            box.Y += EdgeSpacing.Bottom;
            box.Length -= EdgeSpacing.Left + EdgeSpacing.Right;
            box.Width -= EdgeSpacing.Top + EdgeSpacing.Bottom;

            return box;
        }

        /// <summary>
        /// The offset that centers the parts' combined bounds in the <see cref="WorkArea"/>,
        /// so uneven edge spacing (such as a wider clamp-side margin) shifts them with it.
        /// Zero when the plate has no parts.
        /// </summary>
        public Vector OffsetToCenterPartsInWorkArea() =>
            Parts.Count == 0 ? new Vector() : WorkArea().Center - Parts.GetBoundingBox().Center;

        /// <summary>
        /// Automatically sizes the plate to fit the parts.
        /// </summary>
        /// <param name="roundingFactor">The factor to round the actual size up to.</param>
        /// <example>
        /// AutoSize 9.7 x 10.1
        /// * roundingFactor=1.0   new Size=10 x 11
        /// * roundingFactor=0.5   new Size=10 x 10.5
        /// * roundingFactor=0.25  new Size=9.75 x 10.25
        /// * roundingFactor=0.0   new Size=9.7 x 10.1
        /// </example>
        public void AutoSize(double roundingFactor = 1.0)
        {
            // An outlined sheet is a particular piece of material; it cannot be resized to fit.
            if (Parts.Count == 0 || Outline != null)
                return;

            var bounds = Parts.GetBoundingBox();

            // Convention: Length = X axis, Width = Y axis
            double xExtent;
            double yExtent;

            switch (Quadrant)
            {
                case 1:
                    xExtent = System.Math.Abs(bounds.Right) + EdgeSpacing.Right;
                    yExtent = System.Math.Abs(bounds.Top) + EdgeSpacing.Top;
                    break;

                case 2:
                    xExtent = System.Math.Abs(bounds.Left) + EdgeSpacing.Left;
                    yExtent = System.Math.Abs(bounds.Top) + EdgeSpacing.Top;
                    break;

                case 3:
                    xExtent = System.Math.Abs(bounds.Left) + EdgeSpacing.Left;
                    yExtent = System.Math.Abs(bounds.Bottom) + EdgeSpacing.Bottom;
                    break;

                case 4:
                    xExtent = System.Math.Abs(bounds.Right) + EdgeSpacing.Right;
                    yExtent = System.Math.Abs(bounds.Bottom) + EdgeSpacing.Bottom;
                    break;

                default:
                    return;
            }

            Size = new Size(
                Rounding.RoundUpToNearest(yExtent, roundingFactor),
                Rounding.RoundUpToNearest(xExtent, roundingFactor)
            );
        }

        /// <summary>
        /// Gets the area of the top surface of the plate: the material inside its
        /// <see cref="Outline"/> when it has one, otherwise the whole rectangle.
        /// </summary>
        /// <returns></returns>
        public double Area()
        {
            return Outline?.Area ?? Size.Width * Size.Length;
        }

        /// <summary>
        /// Gets the volume of the plate.
        /// </summary>
        public double Volume(double thickness)
        {
            return Area() * thickness;
        }

        /// <summary>
        /// Gets the weight of the plate.
        /// </summary>
        public double Weight(double thickness, double density)
        {
            return Volume(thickness) * density;
        }

        /// <summary>
        /// Percentage of the material used.
        /// </summary>
        /// <returns>Returns a number between 0.0 and 1.0</returns>
        public double Utilization()
        {
            return Parts.Where(p => !p.BaseDrawing.IsCutOff).Sum(part => part.BaseDrawing.Area)
                / Area();
        }
    }
}
