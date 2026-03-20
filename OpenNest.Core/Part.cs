using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Geometry;
using System.Collections.Generic;
using System.Linq;

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
        private bool ownsProgram;

        public readonly Drawing BaseDrawing;

        public Part(Drawing baseDrawing)
            : this(baseDrawing, new Vector())
        {
        }

        public Part(Drawing baseDrawing, Vector location)
        {
            BaseDrawing = baseDrawing;
            Program = baseDrawing.Program.Clone() as Program;
            ownsProgram = true;
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

        public bool HasManualLeadIns { get; set; }

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
            EnsureOwnedProgram();
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
            EnsureOwnedProgram();
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
        /// Creates a part normalized to the origin with optional rotation.
        /// </summary>
        public static Part CreateAtOrigin(Drawing drawing, double rotation = 0)
        {
            var part = new Part(drawing);

            if (!Math.Tolerance.IsEqualTo(rotation, 0))
                part.Rotate(rotation);

            var bbox = part.Program.BoundingBox();
            part.Offset(-bbox.Location.X, -bbox.Location.Y);
            part.UpdateBounds();

            return part;
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
                .Where(e => e.Layer != SpecialLayers.Rapid)
                .ToList();
            var entities2 = ConvertProgram.ToGeometry(part.Program)
                .Where(e => e.Layer != SpecialLayers.Rapid)
                .ToList();

            if (entities1.Count == 0 || entities2.Count == 0)
                return false;

            var perimeter1 = new ShapeProfile(entities1).Perimeter;
            var perimeter2 = new ShapeProfile(entities2).Perimeter;

            if (perimeter1 == null || perimeter2 == null)
                return false;

            perimeter1.Offset(Location);
            perimeter2.Offset(part.Location);

            return perimeter1.Intersects(perimeter2, out pts);
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

        /// <summary>
        /// Creates an offset copy of the part. Clones from the already-rotated
        /// program (skips re-rotation) and computes the bounding box arithmetically
        /// (skips Program.BoundingBox walk).
        /// </summary>
        public Part CloneAtOffset(Vector offset)
        {
            // Share the Program instance — offset-only copies don't modify the program codes.
            // This is a major performance win for tiling large patterns.
            var part = new Part(BaseDrawing, Program,
                location + offset,
                new Box(BoundingBox.X + offset.X, BoundingBox.Y + offset.Y,
                    BoundingBox.Width, BoundingBox.Length));

            return part;
        }

        private void EnsureOwnedProgram()
        {
            if (!ownsProgram)
            {
                Program = Program.Clone() as Program;
                ownsProgram = true;
            }
        }

        private Part(Drawing baseDrawing, Program program, Vector location, Box boundingBox)
        {
            BaseDrawing = baseDrawing;
            Program = program;
            this.location = location;
            BoundingBox = boundingBox;
        }
    }
}
