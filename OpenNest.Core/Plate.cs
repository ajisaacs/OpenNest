using OpenNest.Collections;
using OpenNest.Geometry;
using OpenNest.Math;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenNest
{
    public class Plate
    {
        private int quadrant;

        public event EventHandler<ItemAddedEventArgs<Part>> PartAdded
        {
            add { Parts.ItemAdded += value; }
            remove { Parts.ItemAdded -= value; }
        }

        public event EventHandler<ItemRemovedEventArgs<Part>> PartRemoved
        {
            add { Parts.ItemRemoved += value; }
            remove { Parts.ItemRemoved -= value; }
        }

        public event EventHandler<ItemChangedEventArgs<Part>> PartChanged
        {
            add { Parts.ItemChanged += value; }
            remove { Parts.ItemChanged -= value; }
        }

        public Plate()
            : this(60, 120)
        {
        }

        public Plate(double width, double length)
            : this(new Size(width, length))
        {
        }

        public Plate(Size size)
        {
            EdgeSpacing = new Spacing();
            Size = size;
            Material = new Material();
            Parts = new ObservableList<Part>();
            Parts.ItemAdded += Parts_PartAdded;
            Parts.ItemRemoved += Parts_PartRemoved;
            Quadrant = 1;
        }

        private void Parts_PartAdded(object sender, ItemAddedEventArgs<Part> e)
        {
            if (!e.Item.BaseDrawing.IsCutOff)
                e.Item.BaseDrawing.Quantity.Nested += Quantity;
        }

        private void Parts_PartRemoved(object sender, ItemRemovedEventArgs<Part> e)
        {
            if (!e.Item.BaseDrawing.IsCutOff)
                e.Item.BaseDrawing.Quantity.Nested -= Quantity;
        }

        /// <summary>
        /// Thickness of the plate.
        /// </summary>
        public double Thickness { get; set; }

        /// <summary>
        /// The spacing between parts.
        /// </summary>
        public double PartSpacing { get; set; }

        /// <summary>
        /// The spacing along the edges of the plate.
        /// </summary>
        public Spacing EdgeSpacing;

        /// <summary>
        /// The size of the plate.
        /// </summary>
        public Size Size { get; set; }

        /// <summary>
        /// Material the plate is made out of.
        /// </summary>
        public Material Material { get; set; }

        /// <summary>
        /// The parts that the plate contains.
        /// </summary>
        public ObservableList<Part> Parts { get; set; }

        /// <summary>
        /// The number of times to cut the plate.
        /// </summary>
        public int Quantity { get; set; }

        /// <summary>
        /// The quadrant the plate is located in.
        /// 1 = TopRight
        /// 2 = TopLeft
        /// 3 = BottomLeft
        /// 4 = BottomRight
        /// </summary>
        public int Quadrant
        {
            get { return quadrant; }
            set { quadrant = value <= 4 && value > 0 ? value : 1; }
        }

        /// <summary>
        /// Rotates the plate clockwise or counter-clockwise along with all parts.
        /// </summary>
        /// <param name="rotationDirection"></param>
        /// <param name="keepSameQuadrant"></param>
        public void Rotate90(RotationType rotationDirection, bool keepSameQuadrant = true)
        {
            const double oneAndHalfPI = System.Math.PI * 1.5;

            Size = new Size(Size.Length, Size.Width);

            // After Size swap above, new Size.Width = old Length (old X extent),
            // new Size.Length = old Width (old Y extent).
            // Convention: Length = X axis, Width = Y axis.
            if (rotationDirection == RotationType.CW)
            {
                Rotate(oneAndHalfPI);

                if (keepSameQuadrant)
                {
                    switch (Quadrant)
                    {
                        case 1:
                            Offset(0, Size.Width);
                            break;

                        case 2:
                            Offset(-Size.Length, 0);
                            break;

                        case 3:
                            Offset(0, -Size.Width);
                            break;

                        case 4:
                            Offset(Size.Length, 0);
                            break;

                        default:
                            return;
                    }
                }
                else
                {
                    Quadrant = Quadrant < 2 ? 4 : Quadrant - 1;
                }
            }
            else
            {
                Rotate(Angle.HalfPI);

                if (keepSameQuadrant)
                {
                    switch (Quadrant)
                    {
                        case 1:
                            Offset(Size.Length, 0);
                            break;

                        case 2:
                            Offset(0, Size.Width);
                            break;

                        case 3:
                            Offset(-Size.Length, 0);
                            break;

                        case 4:
                            Offset(0, -Size.Width);
                            break;

                        default:
                            return;
                    }
                }
                else
                {
                    Quadrant = Quadrant > 3 ? 1 : Quadrant + 1;
                }
            }
        }

        /// <summary>
        /// Rotates the plate 180 degrees along with all parts.
        /// </summary>
        /// <param name="keepSameQuadrant"></param>
        public void Rotate180(bool keepSameQuadrant = true)
        {
            if (keepSameQuadrant)
            {
                Vector centerpt;

                switch (Quadrant)
                {
                    case 1:
                        centerpt = new Vector(Size.Length * 0.5, Size.Width * 0.5);
                        break;

                    case 2:
                        centerpt = new Vector(-Size.Length * 0.5, Size.Width * 0.5);
                        break;

                    case 3:
                        centerpt = new Vector(-Size.Length * 0.5, -Size.Width * 0.5);
                        break;

                    case 4:
                        centerpt = new Vector(Size.Length * 0.5, -Size.Width * 0.5);
                        break;

                    default:
                        return;
                }

                Rotate(System.Math.PI, centerpt);
            }
            else
            {
                Rotate(System.Math.PI);
                Quadrant = (Quadrant + 2) % 4;

                if (Quadrant == 0)
                    Quadrant = 4;
            }
        }

        /// <summary>
        /// Rotates the parts on the plate.
        /// </summary>
        /// <param name="angle"></param>
        public void Rotate(double angle)
        {
            for (int i = 0; i < Parts.Count; ++i)
            {
                var part = Parts[i];
                part.Rotate(angle);
            }
        }

        /// <summary>
        /// Rotates the parts on the plate around the specified origin.
        /// </summary>
        /// <param name="angle"></param>
        /// <param name="origin"></param>
        public void Rotate(double angle, Vector origin)
        {
            for (int i = 0; i < Parts.Count; ++i)
            {
                var part = Parts[i];
                part.Rotate(angle, origin);
            }
        }

        /// <summary>
        /// Offsets the parts on the plate.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        public void Offset(double x, double y)
        {
            for (int i = 0; i < Parts.Count; ++i)
            {
                var part = Parts[i];
                part.Offset(x, y);
            }
        }

        /// <summary>
        /// Offsets the parts on the plate.
        /// </summary>
        /// <param name="voffset"></param>
        public void Offset(Vector voffset)
        {
            for (int i = 0; i < Parts.Count; ++i)
            {
                var part = Parts[i];
                part.Offset(voffset);
            }
        }

        /// <summary>
        /// The smallest box that contains the plate.
        /// </summary>
        /// <param name="includeParts"></param>
        /// <returns></returns>
        public Box BoundingBox(bool includeParts = true)
        {
            var plateBox = new Box();

            // Convention: Size.Length = X axis (horizontal), Size.Width = Y axis (vertical)
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

            plateBox.Width = Size.Length;
            plateBox.Length = Size.Width;

            if (!includeParts)
                return plateBox;

            var boundingBox = new Box();
            var partsBox = Parts.GetBoundingBox();

            boundingBox.X = partsBox.Left < plateBox.Left
                ? partsBox.Left
                : plateBox.Left;

            boundingBox.Y = partsBox.Bottom < plateBox.Bottom
                ? partsBox.Bottom
                : plateBox.Bottom;

            boundingBox.Width = partsBox.Right > plateBox.Right
                ? partsBox.Right - boundingBox.X
                : plateBox.Right - boundingBox.X;

            boundingBox.Length = partsBox.Top > plateBox.Top
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
            box.Width -= EdgeSpacing.Left + EdgeSpacing.Right;
            box.Length -= EdgeSpacing.Top + EdgeSpacing.Bottom;

            return box;
        }

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
            if (Parts.Count == 0)
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
                Rounding.RoundUpToNearest(xExtent, roundingFactor));
        }

        /// <summary>
        /// Gets the area of the top surface of the plate.
        /// </summary>
        /// <returns></returns>
        public double Area()
        {
            return Size.Width * Size.Length;
        }

        /// <summary>
        /// Gets the volume of the plate.
        /// </summary>
        /// <returns></returns>
        public double Volume()
        {
            return Area() * Thickness;
        }

        /// <summary>
        /// Gets the weight of the plate.
        /// </summary>
        /// <returns></returns>
        public double Weight()
        {
            return Volume() * Material.Density;
        }

        /// <summary>
        /// Percentage of the material used.
        /// </summary>
        /// <returns>Returns a number between 0.0 and 1.0</returns>
        public double Utilization()
        {
            return Parts.Where(p => !p.BaseDrawing.IsCutOff).Sum(part => part.BaseDrawing.Area) / Area();
        }

        public bool HasOverlappingParts(out List<Vector> pts)
        {
            pts = new List<Vector>();
            var realParts = Parts.Where(p => !p.BaseDrawing.IsCutOff).ToList();

            for (var i = 0; i < realParts.Count; i++)
            {
                var part1 = realParts[i];

                for (var j = i + 1; j < realParts.Count; j++)
                {
                    var part2 = realParts[j];

                    if (part1.Intersects(part2, out var pts2))
                        pts.AddRange(pts2);
                }
            }

            return pts.Count > 0;
        }

    }
}
