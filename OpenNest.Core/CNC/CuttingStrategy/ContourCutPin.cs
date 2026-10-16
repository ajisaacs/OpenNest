using System;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Diagnostics;
using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingStrategy
{
    /// <summary>
    /// How one contour of a drawing must be cut: where it is entered and the lead-in,
    /// lead-out and optional tab used there. <see cref="Point"/> is in the drawing's source
    /// frame (the frame of <see cref="Drawing.SourceEntities"/>), which does not move when the
    /// program is re-origined; <see cref="CutPinResolver"/> maps it to a placed contour.
    /// Immutable: only the exact built-in lead and tab types are accepted, they are copied
    /// in, and every property returns a fresh copy.
    /// </summary>
    public sealed class ContourCutPin
    {
        private readonly LeadIn leadIn;
        private readonly LeadOut leadOut;
        private readonly Tab tab;

        /// <exception cref="ArgumentException">A missing or invalid lead, a nonfinite point, or a tab with a lead-out.</exception>
        /// <exception cref="NotSupportedException">A lead or tab that is not an exact built-in type.</exception>
        public ContourCutPin(Vector point, LeadIn leadIn, LeadOut leadOut, Tab tab = null)
        {
            PostVerificationGeometry.Validate(point);
            var ownedLeadIn = OwnedCuttingParameters.CopyLeadIn(leadIn);
            var ownedLeadOut = OwnedCuttingParameters.CopyLeadOut(leadOut);
            var ownedTab = tab == null ? null : OwnedCuttingParameters.CopyTab(tab);
            // A tab leaves the contour open at its end; a lead-out after an open contour is
            // refused by the planner, so the pin cannot ask for both.
            if (ownedTab != null && ownedLeadOut.GetType() != typeof(NoLeadOut))
                throw new ArgumentException("A pin with a tab cannot have a lead-out.", nameof(leadOut));

            Point = point;
            this.leadIn = ownedLeadIn;
            this.leadOut = ownedLeadOut;
            this.tab = ownedTab;
        }

        /// <summary>The contour entry in the drawing's source frame.</summary>
        public Vector Point { get; }

        /// <summary>A copy of the pinned lead-in.</summary>
        public LeadIn LeadIn => OwnedCuttingParameters.CopyLeadIn(leadIn);

        /// <summary>A copy of the pinned lead-out; always <see cref="NoLeadOut"/> when <see cref="HasTab"/>.</summary>
        public LeadOut LeadOut => OwnedCuttingParameters.CopyLeadOut(leadOut);

        /// <summary>A copy of the pinned tab, or null for a closed contour.</summary>
        public Tab Tab => tab == null ? null : OwnedCuttingParameters.CopyTab(tab);

        public bool HasTab => tab != null;
    }
}
