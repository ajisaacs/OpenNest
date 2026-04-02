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
            Parts = new ObservableList<Part>();
            Parts.ItemAdded += Parts_PartAdded;
            Parts.ItemRemoved += Parts_PartRemoved;
            CutOffs = new ObservableList<CutOff>();
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

        public CNC.CuttingStrategy.CuttingParameters CuttingParameters { get; set; }

        /// <summary>
        /// Material grain direction in radians. 0 = horizontal.
        /// </summary>
        public double GrainAngle { get; set; }

        /// <summary>
        /// The parts that the plate contains.
        /// </summary>
        public ObservableList<Part> Parts { get; set; }

        /// <summary>
        /// The cut-off lines defined on this plate.
        /// </summary>
        public ObservableList<CutOff> CutOffs { get; set; }

        /// <summary>
        /// Regenerates all cut-off drawings and materializes them as parts.
        /// Existing cut-off parts are removed first, then each cut-off is
        /// regenerated and added back if it produces any geometry.
        /// </summary>
        public void RegenerateCutOffs(CutOffSettings settings)
        {
            // Remove existing cut-off parts
            for (var i = Parts.Count - 1; i >= 0; i--)
            {
                if (Parts[i].BaseDrawing.IsCutOff)
                    Parts.RemoveAt(i);
            }

            var cache = BuildPerimeterCache(this);

            // Regenerate and materialize each cut-off
            foreach (var cutoff in CutOffs)
            {
                cutoff.Regenerate(this, settings, cache);

                if (cutoff.Drawing.Program.Codes.Count == 0)
                    continue;

                var part = new Part(cutoff.Drawing);
                Parts.Add(part);
            }
        }

        /// <summary>
        /// Builds a dictionary mapping each non-cut-off part to its perimeter entity.
        /// Closed shapes use ShapeProfile; open contours fall back to ConvexHull.
        /// </summary>
        public static Dictionary<Part, Geometry.Entity> BuildPerimeterCache(Plate plate)
        {
            var cache = new Dictionary<Part, Geometry.Entity>();

            foreach (var part in plate.Parts)
            {
                if (part.BaseDrawing.IsCutOff)
                    continue;

                Geometry.Entity perimeter = null;
                try
                {
                    var entities = Converters.ConvertProgram.ToGeometry(part.Program)
                        .Where(e => e.Layer != SpecialLayers.Rapid)
                        .ToList();

                    if (entities.Count > 0)
                    {
                        var profile = new Geometry.ShapeProfile(entities);

                        if (profile.Perimeter.IsClosed())
                        {
                            perimeter = profile.Perimeter;
                            perimeter.Offset(part.Location);
                        }
                        else
                        {
                            var points = entities.CollectPoints();
                            if (points.Count >= 3)
                            {
                                var hull = Geometry.ConvexHull.Compute(points);
                                hull.Offset(part.Location);
                                perimeter = hull;
                            }
                        }
                    }
                }
                catch
                {
                    perimeter = null;
                }

                cache[part] = perimeter;
            }

            return cache;
        }

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
            for (var i = Parts.Count - 1; i >= 0; i--)
            {
                if (Parts[i].BaseDrawing.IsCutOff)
                    Parts.RemoveAt(i);
            }

            for (var i = 0; i < Parts.Count; ++i)
            {
                var part = Parts[i];
                part.Rotate(angle);
            }

            foreach (var cutoff in CutOffs)
                cutoff.Position = cutoff.Position.Rotate(angle);
        }

        /// <summary>
        /// Rotates the parts on the plate around the specified origin.
        /// </summary>
        /// <param name="angle"></param>
        /// <param name="origin"></param>
        public void Rotate(double angle, Vector origin)
        {
            for (var i = Parts.Count - 1; i >= 0; i--)
            {
                if (Parts[i].BaseDrawing.IsCutOff)
                    Parts.RemoveAt(i);
            }

            for (var i = 0; i < Parts.Count; ++i)
            {
                var part = Parts[i];
                part.Rotate(angle, origin);
            }

            foreach (var cutoff in CutOffs)
            {
                var pos = cutoff.Position - origin;
                pos = pos.Rotate(angle);
                cutoff.Position = pos + origin;
            }
        }

        /// <summary>
        /// Offsets the parts on the plate.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        public void Offset(double x, double y)
        {
            // Remove cut-off parts before transforming
            for (var i = Parts.Count - 1; i >= 0; i--)
            {
                if (Parts[i].BaseDrawing.IsCutOff)
                    Parts.RemoveAt(i);
            }

            for (var i = 0; i < Parts.Count; ++i)
            {
                var part = Parts[i];
                part.Offset(x, y);
            }

            // Transform cut-off positions
            foreach (var cutoff in CutOffs)
                cutoff.Position = new Vector(cutoff.Position.X + x, cutoff.Position.Y + y);
        }

        /// <summary>
        /// Offsets the parts on the plate.
        /// </summary>
        /// <param name="voffset"></param>
        public void Offset(Vector voffset)
        {
            for (var i = Parts.Count - 1; i >= 0; i--)
            {
                if (Parts[i].BaseDrawing.IsCutOff)
                    Parts.RemoveAt(i);
            }

            for (var i = 0; i < Parts.Count; ++i)
            {
                var part = Parts[i];
                part.Offset(voffset);
            }

            foreach (var cutoff in CutOffs)
                cutoff.Position = new Vector(cutoff.Position.X + voffset.X, cutoff.Position.Y + voffset.Y);
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
            return Parts.Where(p => !p.BaseDrawing.IsCutOff).Sum(part => part.BaseDrawing.Area) / Area();
        }

        public bool HasOverlappingParts(out List<Vector> pts)
        {
            pts = new List<Vector>();
            var realParts = Parts.Where(p => !p.BaseDrawing.IsCutOff).ToList();

            for (var i = 0; i < realParts.Count; i++)
            {
                var part1 = realParts[i];
                var b1 = part1.BoundingBox;

                for (var j = i + 1; j < realParts.Count; j++)
                {
                    var part2 = realParts[j];
                    var b2 = part2.BoundingBox;

                    // Skip pairs whose bounding boxes don't meaningfully overlap.
                    // Floating-point rounding can produce sub-epsilon overlaps for
                    // parts that are merely edge-touching, so require the overlap
                    // region to exceed Epsilon in both dimensions.
                    var overlapX = System.Math.Min(b1.Right, b2.Right)
                                 - System.Math.Max(b1.Left, b2.Left);
                    var overlapY = System.Math.Min(b1.Top, b2.Top)
                                 - System.Math.Max(b1.Bottom, b2.Bottom);

                    if (overlapX <= Math.Tolerance.Epsilon || overlapY <= Math.Tolerance.Epsilon)
                        continue;

                    if (part1.Intersects(part2, out var pts2))
                        pts.AddRange(pts2);
                }
            }

            return pts.Count > 0;
        }

    }
}
