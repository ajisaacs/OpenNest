using System.Collections.Generic;
using System.Linq;
using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.Math;

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
        /// <summary>
        /// Chord tolerance used to polygonize arcs/circles for overlap testing in
        /// <see cref="Intersects"/>. Matches the tolerance used elsewhere for
        /// geometry-sensitive checks (e.g. <see cref="PartGeometry"/>).
        /// </summary>
        private const double IntersectsChordTolerance = 0.001;

        private Vector location;
        private bool ownsProgram;
        private double preLeadInRotation;

        public readonly Drawing BaseDrawing;

        public Part(Drawing baseDrawing)
            : this(baseDrawing, new Vector()) { }

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

        public bool LeadInsLocked { get; set; }

        public CNC.CuttingStrategy.CuttingParameters CuttingParameters { get; set; }

        public void ApplyLeadIns(
            CNC.CuttingStrategy.CuttingParameters parameters,
            Vector approachPoint
        )
        {
            ApplyLeadIns(parameters, approachPoint, Geometry.Vector.Invalid);
        }

        public void ApplyLeadIns(
            CNC.CuttingStrategy.CuttingParameters parameters,
            Vector approachPoint,
            Vector nextPartStart
        )
        {
            preLeadInRotation = Rotation;
            var strategy = new CNC.CuttingStrategy.ContourCuttingStrategy
            {
                Parameters = parameters,
            };
            var result = strategy.Apply(Program, approachPoint, nextPartStart);
            Program = result.Program;
            CuttingParameters = parameters;
            HasManualLeadIns = true;
            UpdateBounds();
        }

        public void ApplySingleLeadIn(
            CNC.CuttingStrategy.CuttingParameters parameters,
            Geometry.Vector point,
            Geometry.Entity entity,
            CNC.CuttingStrategy.ContourType contourType
        )
        {
            preLeadInRotation = Rotation;
            var strategy = new CNC.CuttingStrategy.ContourCuttingStrategy
            {
                Parameters = parameters,
            };
            var result = strategy.ApplySingle(Program, point, entity, contourType);
            Program = result.Program;
            CuttingParameters = parameters;
            HasManualLeadIns = true;
            UpdateBounds();
        }

        /// <summary>
        /// Installs an owned, already-rotated saved cutting program without rotating it again.
        /// The current pose remains the clean-drawing pose used by RemoveLeadIns.
        /// </summary>
        public bool RestoreLeadInProgram(Program program, bool locked)
        {
            if (program == null || !program.Codes.Any(code => code is Motion
                || code is SubProgramCall call && call.Program != null
                    && call.Program.Codes.Any(subCode => subCode is Motion)))
                return false;

            // Compute before changing state, so a malformed program cannot half-install.
            var bounds = program.BoundingBox();
            bounds.Offset(Location);
            preLeadInRotation = Rotation;
            Program = program;
            ownsProgram = true;
            HasManualLeadIns = true;
            LeadInsLocked = locked;
            CuttingParameters = null;
            BoundingBox = bounds;
            return true;
        }

        /// <summary>Exact cutting-state record for freshness and rollback; references are not copied.</summary>
        internal CNC.CuttingPlanning.PartCuttingState CaptureCuttingState() =>
            new(Program, ownsProgram, preLeadInRotation, HasManualLeadIns, LeadInsLocked,
                CuttingParameters, location, BoundingBox);

        /// <summary>Reinstates a state captured from this part, field for field.</summary>
        internal void RestoreCuttingState(CNC.CuttingPlanning.PartCuttingState state)
        {
            Program = state.Program;
            ownsProgram = state.OwnsProgram;
            preLeadInRotation = state.PreLeadInRotation;
            HasManualLeadIns = state.HasManualLeadIns;
            LeadInsLocked = state.LeadInsLocked;
            CuttingParameters = state.CuttingParameters;
            location = state.Location;
            BoundingBox = state.BoundingBox;
        }

        /// <summary>
        /// Installs an owned, prevalidated planned program and its precomputed placed bounds.
        /// Pose and lock are unchanged; nothing is regenerated or rotated here.
        /// </summary>
        internal void InstallPlannedProgram(Program program, Box bounds,
            CNC.CuttingStrategy.CuttingParameters parameters)
        {
            preLeadInRotation = Rotation;
            Program = program;
            ownsProgram = true;
            CuttingParameters = parameters;
            HasManualLeadIns = true;
            BoundingBox = bounds;
        }

        public void RemoveLeadIns()
        {
            var rotation = preLeadInRotation;
            var location = Location;
            Program = BaseDrawing.Program.Clone() as Program;
            ownsProgram = true;

            if (!Math.Tolerance.IsEqualTo(rotation, 0))
                Program.Rotate(rotation);

            Location = location;
            HasManualLeadIns = false;
            LeadInsLocked = false;
            CuttingParameters = null;
            UpdateBounds();
        }

        /// <summary>
        /// Gets the rotation of the part in radians.
        /// </summary>
        public double Rotation
        {
            get { return HasManualLeadIns ? preLeadInRotation : Program.Rotation; }
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
            TrackRotation(angle);
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
            TrackRotation(angle);
            UpdateBounds();
        }

        /// <summary>
        /// Records the part's rotation after it turned by <paramref name="angle"/>. A lead-in
        /// program is rebuilt by the cutting strategy and starts over at zero program
        /// rotation, so for those parts the rotation is accumulated rather than read back.
        /// </summary>
        private void TrackRotation(double angle)
        {
            preLeadInRotation = HasManualLeadIns
                ? Angle.NormalizeRad(preLeadInRotation + angle)
                : Program.Rotation;
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
            PerfCounters.CountPartBoundsUpdate();
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

            if (!Math.Tolerance.IsEqualTo(rotation, 0))
                Program.Rotate(rotation);

            HasManualLeadIns = false;
            LeadInsLocked = false;
            CuttingParameters = null;
            UpdateBounds();
        }

        /// <summary>
        /// The smallest box that contains the part.
        /// </summary>
        public Box BoundingBox { get; protected set; }

        public bool Intersects(Part part, out List<Vector> pts)
        {
            PerfCounters.CountPartIntersects();
            pts = new List<Vector>();

            var entities1 = MaterialEntities(Program);
            var entities2 = MaterialEntities(part.Program);

            if (entities1.Count == 0 || entities2.Count == 0)
                return false;

            var perimeter1 = new ShapeProfile(entities1).Perimeter;
            var perimeter2 = new ShapeProfile(entities2).Perimeter;

            if (perimeter1 == null || perimeter2 == null)
                return false;

            var polygon1 = BuildOverlapPolygon(perimeter1);
            var polygon2 = BuildOverlapPolygon(perimeter2);

            if (polygon1 == null || polygon2 == null)
                return false;

            polygon1.Offset(Location);
            polygon2.Offset(part.Location);

            var result = Geometry.Collision.Check(polygon1, polygon2);
            pts = result.IntersectionPoints.ToList();
            return result.Overlaps;
        }

        /// <summary>
        /// Material (cut) entities of <paramref name="program"/> in the program's local frame: the
        /// first stage of overlap preparation, shared by <see cref="Intersects"/> and
        /// <see cref="PartOverlapChecker"/>.
        /// </summary>
        internal static List<Entity> MaterialEntities(CNC.Program program)
        {
            PerfCounters.CountOverlapPolygonPreparation();
            return ConvertProgram
                .ToGeometry(program)
                .Where(e => SpecialLayers.IsMaterial(e.Layer))
                .ToList();
        }

        /// <summary>
        /// Local-frame overlap polygon of a material perimeter: the last stage of overlap
        /// preparation, shared by <see cref="Intersects"/> and <see cref="PartOverlapChecker"/>.
        /// </summary>
        internal static Polygon BuildOverlapPolygon(Shape perimeter) =>
            perimeter.ToPolygonWithTolerance(IntersectsChordTolerance);

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
            // Clone the current Program directly rather than rebuilding from BaseDrawing and
            // re-rotating by the absolute Rotation: when BaseDrawing.Program.Rotation is nonzero
            // (e.g. a canonical-frame copy used internally during fill), `new Part(BaseDrawing)`
            // already carries that baked rotation, so re-applying the full absolute Rotation on
            // top of it double-counts the baseline and corrupts the clone's orientation.
            var part = new Part(
                BaseDrawing,
                (Program)Program.Clone(),
                Location,
                new Box(BoundingBox.X, BoundingBox.Y, BoundingBox.Length, BoundingBox.Width)
            );
            part.ownsProgram = true;
            part.CopyLeadInStateFrom(this);

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
            var part = new Part(
                BaseDrawing,
                Program,
                location + offset,
                new Box(
                    BoundingBox.X + offset.X,
                    BoundingBox.Y + offset.Y,
                    BoundingBox.Length,
                    BoundingBox.Width
                )
            );
            part.CopyLeadInStateFrom(this);

            return part;
        }

        /// <summary>
        /// Copies the lead-in state that goes with a copied program. Without it a copy of a
        /// lead-in part reads its rotation from the rebuilt program (zero), and lead-in
        /// assignment does not know to strip the copied lead-ins before adding new ones.
        /// </summary>
        private void CopyLeadInStateFrom(Part source)
        {
            HasManualLeadIns = source.HasManualLeadIns;
            LeadInsLocked = source.LeadInsLocked;
            CuttingParameters = source.CuttingParameters;
            preLeadInRotation = source.preLeadInRotation;
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
