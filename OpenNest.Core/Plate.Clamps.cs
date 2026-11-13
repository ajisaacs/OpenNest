using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using OpenNest.Clamps;
using OpenNest.Collections;
using OpenNest.Geometry;

namespace OpenNest
{
    public partial class Plate
    {
        /// <summary>
        /// Fixed rectangular machine-clamp keep-outs in plate coordinates
        /// (<see cref="ClampZone"/>). Never parts: excluded from quantity, utilization,
        /// cut sequence and CNC output; placement rules and display consume them. Mutate
        /// through this list; <see cref="ClampZonesChanged"/> fires for every change.
        /// </summary>
        public ObservableList<ClampZone> ClampZones { get; }

        /// <summary>
        /// Raised after <see cref="ClampZones"/> content changes so views can refresh
        /// the zone overlay. Carries no geometry arguments.
        /// </summary>
        public event EventHandler ClampZonesChanged;

        private void OnClampZonesChanged() => ClampZonesChanged?.Invoke(this, EventArgs.Empty);

        /// <summary>
        /// The machine clamp rail this plate follows, or null when its
        /// <see cref="ClampZones"/> are explicit (engine sheets, files without a rail). While
        /// set, the zones are derived: re-resolved whenever <see cref="Size"/> or
        /// <see cref="Quadrant"/> changes, because the clamps are fixed to the machine.
        /// </summary>
        public ClampRail ClampRail { get; private set; }

        /// <summary>
        /// The gap part material keeps from every clamp zone: the rail's own
        /// <see cref="OpenNest.Clamps.ClampRail.Clearance"/>, else the bottom edge spacing (D6), never
        /// negative. Part spacing never applies to clamps.
        /// </summary>
        public double ClampClearance =>
            System.Math.Max(0, ClampRail?.Clearance ?? EdgeSpacing.Bottom);

        private static readonly IReadOnlySet<string> NoRemovedClamps = ImmutableHashSet<string>.Empty;

        /// <summary>
        /// Rail clamp ids taken off this sheet, by Auto Nest as a last resort (D8) so a part
        /// that could not be nested anywhere with every clamp in place still fits, or by hand
        /// (<see cref="SetClampRemoved"/>). Empty unless
        /// <see cref="ClampRail"/> is set; <see cref="ClampZones"/> already excludes them, so
        /// nothing else needs to consult this list to avoid them. Ids stay listed across a
        /// resize/rotate even when the matching clamp is temporarily beyond the sheet, so it
        /// re-appears removed if a later resize brings it back onto the sheet (C13).
        /// </summary>
        public IReadOnlySet<string> RemovedClamps { get; private set; } = NoRemovedClamps;

        /// <summary>
        /// Minutes this sheet's cut-time estimate gains from <see cref="RemovedClamps"/> (D9):
        /// each listed clamp the rail places on this sheet adds
        /// <see cref="OpenNest.Clamps.ClampRail.RemovalMinutes"/> once; a listed clamp that is beyond
        /// the sheet (or unknown to the rail) is not pulled and costs nothing. Zero when there
        /// is no rail (removal is meaningless without one to remove clamps from).
        /// </summary>
        public double ClampRemovalMinutes =>
            ClampRail == null || RemovedClamps.Count == 0
                ? 0
                : ClampRail.ResolveFor(Size, Quadrant).Count(zone => RemovedClamps.Contains(zone.Id))
                    * ClampRail.RemovalMinutes;

        /// <summary>
        /// Follows <paramref name="rail"/> and replaces the zones with its resolution for
        /// this sheet; null detaches and removes every zone. Unchanged zones raise no events.
        /// </summary>
        public void SetClampRail(ClampRail rail) => SetClampRail(rail, null);

        /// <summary>
        /// Follows <paramref name="rail"/> with the clamps named in <paramref name="removedClamps"/>
        /// taken off this sheet (D8): zones become the rail's resolution for this sheet minus
        /// those ids. Null <paramref name="rail"/> detaches, clears every zone and forgets the
        /// removed set (removal only means something while following a rail).
        /// </summary>
        public void SetClampRail(ClampRail rail, IEnumerable<string> removedClamps)
        {
            ClampRail = rail;
            RemovedClamps = rail == null || removedClamps == null
                ? NoRemovedClamps
                : removedClamps.ToImmutableHashSet(StringComparer.Ordinal);
            ReplaceClampZones(ResolveRailZones());
        }

        /// <summary>
        /// The rail clamp this sheet places at <paramref name="point"/> (the parts' placement
        /// frame, see <see cref="ClampZoneFrame"/>), whether in place or removed, grown by
        /// <paramref name="tolerance"/> on every side; null without a rail or a clamp there.
        /// Returned bounds are in the placement frame.
        /// </summary>
        public ClampZone RailClampAt(Vector point, double tolerance = 0)
        {
            if (ClampRail == null)
                return null;
            var left = Quadrant is 2 or 3 ? -Size.Length : 0;
            var bottom = Quadrant is 3 or 4 ? -Size.Width : 0;
            foreach (var zone in ClampRail.ResolveFor(Size, Quadrant))
            {
                var box = zone.Bounds.Translate(left, bottom);
                if (box.Offset(tolerance).Contains(point))
                    return new ClampZone(zone.Id, box);
            }
            return null;
        }

        /// <summary>
        /// Takes the rail clamp <paramref name="id"/> off this sheet, or puts it back, as a
        /// manual counterpart to Auto Nest's last-resort removal (D8): the zones and
        /// <see cref="ClampRemovalMinutes"/> follow. False when nothing changed: no rail, a clamp
        /// the rail does not place on this sheet, or one already in the requested state.
        /// </summary>
        public bool SetClampRemoved(string id, bool removed)
        {
            if (ClampRail == null || id == null || RemovedClamps.Contains(id) == removed)
                return false;
            if (removed && !ClampRail.ResolveFor(Size, Quadrant).Any(zone => zone.Id == id))
                return false;
            var ids = removed ? RemovedClamps.Append(id) : RemovedClamps.Where(x => x != id);
            SetClampRail(ClampRail, ids);
            return true;
        }

        private void ResolveClampRail()
        {
            // Called from the constructor's Size assignment before the zone list exists.
            if (ClampRail != null)
                ReplaceClampZones(ResolveRailZones());
        }

        private IReadOnlyList<ClampZone> ResolveRailZones()
        {
            if (ClampRail == null)
                return Array.Empty<ClampZone>();
            var resolved = ClampRail.ResolveFor(Size, Quadrant);
            if (RemovedClamps.Count == 0)
                return resolved;
            var kept = new List<ClampZone>(resolved.Count);
            foreach (var zone in resolved)
                if (!RemovedClamps.Contains(zone.Id))
                    kept.Add(zone);
            return kept;
        }

        private void ReplaceClampZones(IReadOnlyList<ClampZone> zones)
        {
            if (ClampRail.SameZones(ClampZones, zones))
                return;
            ClampZones.Clear();
            foreach (var zone in zones)
                ClampZones.Add(zone);
        }
    }
}
