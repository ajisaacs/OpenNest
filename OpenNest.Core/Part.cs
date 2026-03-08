using System.Collections.Generic;
using System.Linq;
using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Geometry;

namespace OpenNest
{
    public interface IPart : IBoundable
    {
        Vector Location { get; set; }
        double Rotation { get; }
        void Rotate(double angle);
        void Rotate(double angle, Vector origin);
        void Offset(double x, double y);
        void Offset(Vector voffset);
        void Update();
    }

    public class Part : IPart, IBoundable
    {
        private Vector location;

        public readonly Drawing BaseDrawing;

        public Part(Drawing baseDrawing)
            : this(baseDrawing, new Vector())
        {
        }

        public Part(Drawing baseDrawing, Vector location)
        {
            BaseDrawing = baseDrawing;
            Program = baseDrawing.Program.Clone() as Program;
            this.location = location;
            UpdateBounds();
        }

        /// <summary>
        /// Location of the part.
        /// </summary>
        public Vector Location
        {
            get { return location; }
            set
            {
                BoundingBox.Offset(value - location);
                location = value;
            }
        }

        public Program Program { get; private set; }

        /// <summary>
        /// Gets the rotation of the part in radians.
        /// </summary>
        public double Rotation
        {
            get { return Program.Rotation; }
        }

        /// <summary>
        /// Rotates the part.
        /// </summary>
        /// <param name="angle">Angle of rotation in radians.</param>
        public void Rotate(double angle)
        {
            Program.Rotate(angle);
            location = Location.Rotate(angle);
            UpdateBounds();
        }

        /// <summary>
        /// Rotates the part around the specified origin.
        /// </summary>
        /// <param name="angle">Angle of rotation in radians.</param>
        /// <param name="origin">The origin to rotate the part around.</param>
        public void Rotate(double angle, Vector origin)
        {
            Program.Rotate(angle);
            location = Location.Rotate(angle, origin);
            UpdateBounds();
        }

        /// <summary>
        /// Offsets the part.
        /// </summary>
        /// <param name="x">The x-axis offset distance.</param>
        /// <param name="y">The y-axis offset distance.</param>
        public void Offset(double x, double y)
        {
            location = new Vector(location.X + x, location.Y + y);
            BoundingBox.Offset(x, y);
        }

        /// <summary>
        /// Offsets the part.
        /// </summary>
        /// <param name="voffset">The vector containing the x-axis & y-axis offset distances.</param>
        public void Offset(Vector voffset)
        {
            location += voffset;
            BoundingBox.Offset(voffset);
        }

        /// <summary>
        /// Updates the bounding box of the part.
        /// </summary>
        public void UpdateBounds()
        {
            BoundingBox = Program.BoundingBox();
            BoundingBox.Offset(Location);
        }

        /// <summary>
        /// Updates the part from the drawing it was derived from.
        /// </summary>
        public void Update()
        {
            var rotation = Rotation;
            Program = BaseDrawing.Program.Clone() as Program;
            Program.Rotate(Program.Rotation - rotation);
        }

        /// <summary>
        /// The smallest box that contains the part.
        /// </summary>
        public Box BoundingBox { get; protected set; }

        public bool Intersects(Part part, out List<Vector> pts)
        {
            pts = new List<Vector>();

            var entities1 = ConvertProgram.ToGeometry(Program)
                .Where(e => e.Layer != SpecialLayers.Rapid);
            var entities2 = ConvertProgram.ToGeometry(part.Program)
                .Where(e => e.Layer != SpecialLayers.Rapid);

            var shapes1 = Helper.GetShapes(entities1);
            var shapes2 = Helper.GetShapes(entities2);

            shapes1.ForEach(shape => shape.Offset(Location));
            shapes2.ForEach(shape => shape.Offset(part.Location));

            for (int i = 0; i < shapes1.Count; i++)
            {
                var shape1 = shapes1[i];

                for (int j = 0; j < shapes2.Count; j++)
                {
                    var shape2 = shapes2[j];
                    List<Vector> pts2;

                    if (shape1.Intersects(shape2, out pts2))
                        pts.AddRange(pts2);
                }
            }

            return pts.Count > 0;
        }

        public double Left
        {
            get { return BoundingBox.Left; }
        }

        public double Right
        {
            get { return BoundingBox.Right; }
        }

        public double Top
        {
            get { return BoundingBox.Top; }
        }

        public double Bottom
        {
            get { return BoundingBox.Bottom; }
        }

        /// <summary>
        /// Gets a deep copy of the part.
        /// </summary>
        /// <returns></returns>
        public object Clone()
        {
            var part = new Part(BaseDrawing);
            part.Rotate(Rotation);
            part.Location = Location;

            return part;
        }
    }
}
