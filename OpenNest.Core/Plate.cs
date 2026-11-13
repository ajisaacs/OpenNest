using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Clamps;
using OpenNest.Collections;
using OpenNest.CommonLines;
using OpenNest.Geometry;
using OpenNest.Support;

namespace OpenNest
{
    public partial class Plate
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

        /// <summary>
        /// Raised once after the part order changes without adding or removing parts, including
        /// a committed cutting plan that also installed planned programs. Refresh order- and
        /// program-derived views; quantity accounting is unchanged.
        /// </summary>
        public event EventHandler PartsReordered
        {
            add { Parts.ItemsReordered += value; }
            remove { Parts.ItemsReordered -= value; }
        }

        public Plate()
            : this(60, 120) { }

        public Plate(double width, double length)
            : this(new Size(width, length)) { }

        public Plate(Size size)
        {
            EdgeSpacing = new Spacing();
            Size = size;
            Parts = new ObservableList<Part>();
            Parts.ItemAdded += Parts_PartAdded;
            Parts.ItemRemoved += Parts_PartRemoved;
            CutOffs = new ObservableList<CutOff>();
            CommonLines = new ObservableList<CommonLineGroup>();
            ClampZones = new ObservableList<ClampZone>();
            ClampZones.ItemAdded += (_, _) => OnClampZonesChanged();
            ClampZones.ItemRemoved += (_, _) => OnClampZonesChanged();
            ClampZones.ItemChanged += (_, _) => OnClampZonesChanged();
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
            // A removed part leaves its common-line group; a group left with one part is gone.
            for (var i = CommonLines.Count - 1; i >= 0; i--)
                if (CommonLines[i].Remove(e.Item) && CommonLines[i].Members.Count < 2)
                    CommonLines.RemoveAt(i);
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
        /// The size of the plate. A plate following a <see cref="ClampRail"/> re-resolves
        /// its clamp zones for the new size.
        /// </summary>
        public Size Size
        {
            get { return size; }
            set
            {
                // A turned sheet is the same sheet; any other size is no longer the coded item.
                if (stockItemCodes.Count > 0 && !SameSheet(size, value))
                    stockItemCodes = Array.Empty<string>();
                // An outline is a particular sheet; any other size is a rectangle again.
                if (outline != null && (outline.Size.Width != value.Width || outline.Size.Length != value.Length))
                    outline = null;
                size = value;
                ResolveClampRail();
            }
        }

        private Size size;

        /// <summary>
        /// The sheet's material when it is not a rectangle (see <see cref="SheetOutline"/>), in
        /// plate coordinates; null for a rectangular sheet. Setting one makes <see cref="Size"/> its
        /// bounding box, and setting any other size drops it. Only the Irregular whole-job engine
        /// nests on an outlined sheet; cut-offs are trimmed to it, automatic cut-offs refuse it, and
        /// <see cref="Area"/> is its material area.
        /// </summary>
        public SheetOutline Outline
        {
            get { return outline; }
            set
            {
                outline = value;
                if (value != null)
                    Size = value.Size;
            }
        }

        private SheetOutline outline;

        private static bool SameSheet(Size a, Size b) =>
            a.Width == b.Width && a.Length == b.Length || a.Width == b.Length && a.Length == b.Width;

        /// <summary>
        /// The inventory item each physical sheet of this plate is cut from: entry i names sheet
        /// i of <see cref="Quantity"/>, and sheets past the end are uncoded. Empty when no sheet
        /// is coded. A resize (other than turning the sheet) clears it, lowering
        /// <see cref="Quantity"/> truncates it, and raising it leaves the extra sheets uncoded.
        /// Display and hand-off data only: quantity accounting never reads it.
        /// </summary>
        public IReadOnlyList<string> StockItemCodes
        {
            get { return stockItemCodes; }
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                if (value.Any(string.IsNullOrWhiteSpace))
                    throw new ArgumentException("Stock item codes must not be blank.", nameof(value));
                stockItemCodes = value.Take(System.Math.Max(quantity, 0)).ToArray();
            }
        }

        private IReadOnlyList<string> stockItemCodes = Array.Empty<string>();

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
        /// The machine support bed this sheet rests on (its nest's, see
        /// <see cref="Nest.SetSupportBed"/>), or null for none. Display, analysis and support
        /// tabs only: never material, spacing or a keep-out.
        /// </summary>
        public SupportBed SupportBed { get; set; }

        /// <summary>
        /// A new, unowned plate with this plate's sheet and layout: size, outline, spacing,
        /// quadrant, grain, clamps, support bed, parts (programs and lead-in state included),
        /// common-line groups and cut-off definitions, regenerated in their places in the
        /// sequence. It is one sheet (<see cref="Quantity"/> 1) with no stock item codes, since
        /// those name particular physical sheets. Adding the parts counts them as nested at once,
        /// as every plate does, so callers add the copy to a nest.
        /// <see cref="CuttingParameters"/> is shared; callers that need an independent copy
        /// replace it.
        /// </summary>
        public Plate CopyLayout()
        {
            var copy = new Plate(Size)
            {
                Quantity = 1,
                Quadrant = Quadrant,
                PartSpacing = PartSpacing,
                EdgeSpacing = EdgeSpacing,
                GrainAngle = GrainAngle,
                CuttingParameters = CuttingParameters,
                SupportBed = SupportBed,
                CutCommonLinesTwice = CutCommonLinesTwice,
            };
            copy.Outline = Outline;

            if (ClampRail != null)
                copy.SetClampRail(ClampRail, RemovedClamps);
            else
                foreach (var zone in ClampZones)
                    copy.ClampZones.Add(new ClampZone(zone.Id, zone.Bounds));

            var copies = new Dictionary<Part, Part>();
            foreach (var part in Parts.Where(p => !p.BaseDrawing.IsCutOff))
            {
                var clone = (Part)part.Clone();
                copy.Parts.Add(clone);
                copies[part] = clone;
            }

            foreach (var group in CommonLines)
            {
                var members = group.Members.Where(copies.ContainsKey).Select(member => copies[member]).ToList();
                if (members.Count >= 2)
                    copy.CommonLines.Add(new CommonLineGroup(members, group.Kerf));
            }

            if (CutOffs.Count == 0)
                return copy;

            // Cut-off segments keep their places among the parts, as a save and reload does.
            var places = new Dictionary<CutOff, IReadOnlyList<int>>();
            foreach (var cutoff in CutOffs)
            {
                var duplicate = new CutOff(cutoff.Position, cutoff.Axis)
                {
                    StartLimit = cutoff.StartLimit,
                    EndLimit = cutoff.EndLimit,
                    HeadLockOvercut = cutoff.HeadLockOvercut,
                    FarEdgeFirst = cutoff.FarEdgeFirst,
                    Gaps = cutoff.Gaps,
                };
                copy.CutOffs.Add(duplicate);

                var segments = CutOffSegments.Of(this, cutoff)
                    .Select(segment => Parts.IndexOf(segment.Part))
                    .Where(index => index >= 0)
                    .ToArray();
                if (segments.Length > 0)
                    places[duplicate] = segments;
            }

            copy.RegenerateCutOffs(CutOffSettings ?? new CutOffSettings(), places, null);
            return copy;
        }

        /// <summary>
        /// The number of times to cut the plate. Lowering it drops the
        /// <see cref="StockItemCodes"/> of the sheets no longer cut.
        /// </summary>
        public int Quantity
        {
            get { return quantity; }
            set
            {
                quantity = value;
                if (stockItemCodes.Count > System.Math.Max(value, 0))
                    stockItemCodes = stockItemCodes.Take(System.Math.Max(value, 0)).ToArray();
            }
        }

        private int quantity;

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
            set
            {
                quadrant = value <= 4 && value > 0 ? value : 1;
                ResolveClampRail();
            }
        }
    }
}
