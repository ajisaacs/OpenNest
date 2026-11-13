using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Collections;
using OpenNest.CommonLines;

namespace OpenNest
{
    public partial class Plate
    {
        /// <summary>
        /// Groups of parts placed one kerf apart to share common-line cuts (see
        /// docs/common-line-cutting.md). A part removed from <see cref="Parts"/> leaves its group.
        /// </summary>
        public ObservableList<CommonLineGroup> CommonLines { get; }

        /// <summary>The common-line group holding <paramref name="part"/>, or null.</summary>
        public CommonLineGroup CommonLineGroupOf(Part part) =>
            CommonLines.FirstOrDefault(group => group.Contains(part));

        /// <summary>
        /// Puts <paramref name="parts"/> into one common-line group at <paramref name="kerf"/>,
        /// merging any groups they already belong to. Every part must be on this plate.
        /// </summary>
        public CommonLineGroup JoinCommonLine(IEnumerable<Part> parts, double kerf)
        {
            ArgumentNullException.ThrowIfNull(parts);
            var joined = parts.ToList();
            if (joined.Any(part => part == null || !Parts.Contains(part)))
                throw new ArgumentException("Every part in a common line must be on the plate.", nameof(parts));
            var merged = CommonLines.Where(group => joined.Any(group.Contains)).ToList();
            var members = merged.SelectMany(group => group.Members).Concat(joined);
            var result = new CommonLineGroup(members, kerf);
            foreach (var group in merged)
                CommonLines.Remove(group);
            CommonLines.Add(result);
            return result;
        }

        /// <summary>
        /// Cuts every common line twice, as slice 1 did: each member cuts its whole perimeter and
        /// the second pass runs in kerf the first opened. Off by default: Plan Cutting cuts each
        /// shared line once and each member's remaining edges after it (see
        /// docs/common-line-cutting.md). Choose this for a post that cannot write single-pass cuts.
        /// </summary>
        public bool CutCommonLinesTwice { get; set; }

        /// <summary>
        /// True when the plate's common lines are cut once: it has groups and does not cut them twice.
        /// </summary>
        public bool CutsCommonLinesOnce => CommonLines.Count > 0 && !CutCommonLinesTwice;

        /// <summary>Removes the common-line group holding <paramref name="part"/>; true when there was one.</summary>
        public bool RemoveCommonLine(Part part) =>
            CommonLineGroupOf(part) is { } group && CommonLines.Remove(group);
    }
}
