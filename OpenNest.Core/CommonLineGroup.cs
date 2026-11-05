using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenNest
{
    /// <summary>
    /// Parts placed exactly one kerf apart along collinear straight edges, so one centerline cut
    /// makes both edges (see docs/common-line-cutting.md). Membership is by reference; the shared
    /// spans are derived from the parts' placed geometry (<see cref="CommonLineGeometry"/>), never
    /// stored, so a moved member breaks the group rather than leaving a stale span behind.
    /// </summary>
    public sealed class CommonLineGroup
    {
        private readonly List<Part> members;

        /// <exception cref="ArgumentOutOfRangeException">The kerf is not finite and positive.</exception>
        /// <exception cref="ArgumentException">Fewer than two distinct parts, a null part or a cut-off.</exception>
        public CommonLineGroup(IEnumerable<Part> members, double kerf)
        {
            ArgumentNullException.ThrowIfNull(members);
            if (!double.IsFinite(kerf) || kerf <= 0)
                throw new ArgumentOutOfRangeException(nameof(kerf), kerf, "A common line needs a finite, positive kerf.");
            var distinct = new List<Part>();
            foreach (var part in members)
            {
                if (part == null)
                    throw new ArgumentException("A common-line group cannot hold a null part.", nameof(members));
                if (part.BaseDrawing?.IsCutOff == true)
                    throw new ArgumentException("A cut-off cannot share a common line.", nameof(members));
                if (!distinct.Any(existing => ReferenceEquals(existing, part)))
                    distinct.Add(part);
            }
            if (distinct.Count < 2)
                throw new ArgumentException("A common-line group needs at least two parts.", nameof(members));
            this.members = distinct;
            Kerf = kerf;
        }

        /// <summary>The parts in the group, in the order they were joined.</summary>
        public IReadOnlyList<Part> Members => members;

        /// <summary>The gap the members were placed at: the kerf width when the group was made.</summary>
        public double Kerf { get; }

        public bool Contains(Part part) => members.Any(member => ReferenceEquals(member, part));

        /// <summary>Drops a member (a part removed from the plate); true when it was one.</summary>
        internal bool Remove(Part part) => members.RemoveAll(member => ReferenceEquals(member, part)) > 0;
    }
}
