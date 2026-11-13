using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using OpenNest.Bending;
using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Geometry;

namespace OpenNest
{
    public class Drawing
    {
        private static int nextId;
        private static int nextColorIndex;
        private Program program;

        /// <summary>The active palette's color for a drawing at <paramref name="index"/>.</summary>
        public static Color GetPartColor(int index) => PartColorPalette.Current.ColorAt(index);

        public static Color GetNextColor() =>
            GetPartColor(unchecked(Interlocked.Increment(ref nextColorIndex) - 1) & int.MaxValue);

        public Drawing()
            : this(string.Empty, new Program()) { }

        public Drawing(string name)
            : this(name, new Program()) { }

        public Drawing(string name, Program pgm)
        {
            Id = Interlocked.Increment(ref nextId);
            Name = name;
            Material = new Material();
            Constraints = new NestConstraints();
            Source = new SourceInfo();
            Program = pgm;
        }

        public int Id { get; }

        public string Name { get; set; }

        public string Customer { get; set; }

        /// <summary>Nesting order: higher numbers nest first. Zero is the default.</summary>
        public int Priority { get; set; }

        public DwgQty Quantity;

        public Material Material { get; set; }

        public Program Program
        {
            get { return program; }
            set
            {
                program = value;
                ProgramChanged();
            }
        }

        /// <summary>
        /// Refreshes everything derived from <see cref="Program"/>: <see cref="Area"/> and the
        /// canonical angle in <see cref="SourceInfo.Angle"/>. Assigning <see cref="Program"/>
        /// calls it; callers that mutate the program in place (rather than reassigning it) must
        /// call it afterwards.
        /// </summary>
        public void ProgramChanged()
        {
            UpdateArea();
            RecomputeCanonicalAngle();
        }

        // Cut-off drawings are left with Angle=0.
        private void RecomputeCanonicalAngle()
        {
            if (Source == null)
                Source = new SourceInfo();

            if (program == null || IsCutOff)
            {
                Source.Angle = 0.0;
                return;
            }

            Source.Angle = CanonicalAngle.Compute(this);
        }

        public Color Color { get; set; }

        /// <summary>
        /// True when someone chose <see cref="Color"/> on purpose. Otherwise the color comes
        /// from the active scheme's palette and is reassigned when the scheme changes or the
        /// nest is opened.
        /// </summary>
        public bool HasCustomColor { get; set; }

        public bool IsCutOff { get; set; }

        /// <summary>
        /// Cut this drawing's parts before every other part when automatic sequencing runs,
        /// e.g. a test coupon checked before the rest of the sheet is cut.
        /// </summary>
        public bool CutFirst { get; set; }

        /// <summary>
        /// Lets automatic nesting place this drawing's parts one kerf apart so they share a cut
        /// (see docs/common-line-cutting.md), when the nest's common-line usage allows it. Off by
        /// default: a shared edge is cut once from the centerline, which some customers'
        /// edge-quality requirements do not accept.
        /// </summary>
        public bool AllowCommonLine { get; set; }

        public NestConstraints Constraints { get; set; }

        public SourceInfo Source { get; set; }

        public List<Bend> Bends { get; set; } = new List<Bend>();

        /// <summary>
        /// The library revision this drawing was copied from, or null when it did not come
        /// from a drawing library. Editing the drawing in a nest keeps the link as provenance.
        /// </summary>
        public DrawingLibraryLink Library { get; set; }

        /// <summary>
        /// Contours this drawing says must be cut a fixed way, in the source frame. At most one
        /// pin per contour is used; <see cref="CNC.CuttingPlanning.CutPinResolver"/> maps them to
        /// a placement. Pins do not follow geometry into new drawings (split pieces).
        /// </summary>
        public List<CNC.CuttingStrategy.ContourCutPin> CutPins { get; set; } = new List<CNC.CuttingStrategy.ContourCutPin>();

        /// <summary>
        /// Scrap pieces tabbed to the part, in the source frame. <see cref="Program"/> already has
        /// them applied; they are kept so an editor can change them from <see cref="SourceEntities"/>.
        /// </summary>
        public List<Breakoffs.Breakoff> Breakoffs { get; set; } = new List<Breakoffs.Breakoff>();

        /// <summary>
        /// Holes whose slugs stay attached by tabs, in the source frame. <see cref="Program"/> already
        /// has them applied (cut pieces plus <see cref="CNC.LayerType.Tab"/> spans); they are kept so
        /// an editor can change them from <see cref="SourceEntities"/>.
        /// </summary>
        public List<TabbedCutouts.TabbedCutout> TabbedCutouts { get; set; } = new List<TabbedCutouts.TabbedCutout>();

        /// <summary>
        /// Annotations shown in the CAD Converter. They store placement only, are measured from the
        /// geometry when shown and never affect <see cref="Program"/>.
        /// </summary>
        public List<Dimensions.DrawingDimension> Dimensions { get; set; } = new List<Dimensions.DrawingDimension>();

        /// <summary>
        /// Complete set of source entities with stable GUIDs.
        /// Null when the drawing was created from G-code or an older nest file.
        /// </summary>
        public List<Entity> SourceEntities { get; set; }

        /// <summary>
        /// IDs of entities in <see cref="SourceEntities"/> that are suppressed (hidden).
        /// Suppressed entities are excluded from the active Program but preserved for re-enabling.
        /// </summary>
        public HashSet<Guid> SuppressedEntityIds { get; set; } = new HashSet<Guid>();

        /// <summary>
        /// Role overrides for entities in <see cref="SourceEntities"/>; entities not listed keep the
        /// role their layer implies (see <see cref="Converters.EntityRoles"/>). <see cref="Program"/> already has
        /// them applied; display-only entities are not in it and come from
        /// <see cref="Converters.EntityRoles.DisplayGeometry"/>.
        /// </summary>
        public Dictionary<Guid, EntityRole> EntityRoles { get; set; } = new Dictionary<Guid, EntityRole>();

        public double Area { get; protected set; }

        private void UpdateArea()
        {
            if (program == null)
            {
                Area = 0;
                return;
            }

            var geometry = ConvertProgram
                .ToGeometry(program)
                .Where(entity => SpecialLayers.IsMaterial(entity.Layer));
            var shapes = ShapeBuilder.GetShapes(geometry);

            // No material left (e.g. only marks): a stale area would still count as material.
            if (shapes.Count == 0)
            {
                Area = 0;
                return;
            }

            var areas = new double[shapes.Count];

            for (int i = 0; i < shapes.Count; i++)
            {
                var shape = shapes[i];
                areas[i] = shape.Area();
            }

            int largestAreaIndex = 0;

            for (int i = 1; i < areas.Length; i++)
            {
                var area = areas[i];

                if (area > areas[largestAreaIndex])
                    largestAreaIndex = i;
            }

            var outerArea = areas[largestAreaIndex];

            Area = outerArea - (areas.Sum() - outerArea);
        }

        public override bool Equals(object obj)
        {
            if (obj is Drawing == false)
                return false;

            var dwg = (Drawing)obj;

            return Name == dwg.Name;
        }

        public override int GetHashCode()
        {
            return Name.GetHashCode();
        }
    }

    public class SourceInfo
    {
        /// <summary>
        /// Path to the source file.
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// Offset distances to the original location.
        /// </summary>
        public Vector Offset { get; set; }

        /// <summary>
        /// Rotation (radians) that maps the source program geometry to its canonical
        /// (MBR-axis-aligned) frame. Populated automatically by the <see cref="Drawing.Program"/>
        /// setter via <see cref="CanonicalAngle.Compute"/>. A value of 0 means the drawing is
        /// already canonical or <see cref="Drawing.IsCutOff"/> is true. Callers that mutate
        /// <see cref="Drawing.Program"/> in place must invoke
        /// <see cref="Drawing.ProgramChanged"/> to refresh.
        /// </summary>
        public double Angle { get; set; }
    }
}
