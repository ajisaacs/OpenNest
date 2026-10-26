using System;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;

namespace OpenNest.Support
{
    /// <summary>
    /// Which parts <see cref="CuttingParameters.SupportTabs"/> tabs (docs/support-tabs.md): the
    /// parts the plate's support bed cannot hold level or, on a plate with no bed, the parts
    /// whose width is in the settings' no-bed range. Also the cutting settings for a lead-in
    /// placed by hand: the tool cuts where it is clicked, so the part is tabbed there. Plan
    /// Cutting chooses the tab point itself.
    /// </summary>
    public static class SupportTabSettings
    {
        /// <summary>
        /// The analysis that tabs a part, or null when support tabs are off or the part is
        /// outside the rule: with a bed, the bed's analysis, held by its tab alone
        /// (<see cref="SupportAnalyzer.HeldByTab"/>) when the bed cannot hold it level and its
        /// length is within <see cref="CuttingParameters.TabHoldsAt"/>; without one, a no-bed analysis
        /// (<see cref="SupportAnalyzer.AnalyzeWithoutBed"/>) of a part whose width is in
        /// <see cref="CuttingParameters.TabsWithoutBedAt"/>'s range. The part needs a tab when
        /// the result is complete and not supported; an incomplete result could not be read.
        /// </summary>
        /// <param name="insideCornerClearance">
        /// How far a tab point keeps from an inside corner; 0 keeps it off the corner only.
        /// </param>
        public static PartSupport Analyze(CuttingParameters parameters, SupportBed bed, int quadrant,
            SupportMaterial material, double insideCornerClearance = 0)
        {
            ArgumentNullException.ThrowIfNull(material);
            if (parameters?.SupportTabs != true)
                return null;
            if (bed != null)
            {
                var support = SupportAnalyzer.Analyze(bed, quadrant, material, insideCornerClearance);
                return support is { IsComplete: true, IsSupported: false }
                    && parameters.TabHoldsAt(SupportAnalyzer.Length(material))
                    ? SupportAnalyzer.HeldByTab(support, material)
                    : support;
            }
            if (!parameters.HasNoBedTabRange())
                return null;
            if (!material.IsComplete)
                return SupportAnalyzer.AnalyzeWithoutBed(material, insideCornerClearance);
            return parameters.TabsWithoutBedAt(SupportAnalyzer.Width(material))
                ? SupportAnalyzer.AnalyzeWithoutBed(material, insideCornerClearance)
                : null;
        }

        /// <summary>
        /// Whether support tabs are on but can tab nothing: the plate has no support bed and the
        /// settings set no no-bed width range.
        /// </summary>
        public static bool SkipsWithoutBed(CuttingParameters parameters, SupportBed bed) =>
            parameters?.SupportTabs == true && bed == null && !parameters.HasNoBedTabRange();

        /// <summary>
        /// <paramref name="parameters"/> itself, or a copy with tabs enabled when support tabs are
        /// on, a tab size is set and <paramref name="part"/> needs a tab (see <see cref="Analyze"/>).
        /// The caller's settings are never changed.
        /// </summary>
        public static CuttingParameters ForManualLeadIn(CuttingParameters parameters, Plate plate, Part part)
        {
            ArgumentNullException.ThrowIfNull(parameters);
            if (!parameters.SupportTabs || parameters.TabsEnabled || parameters.TabConfig == null
                || plate == null || part == null
                || (plate.SupportBed == null && !parameters.HasNoBedTabRange()))
                return parameters;
            var support = Analyze(parameters, plate.SupportBed, plate.Quadrant, SupportMaterial.FromPart(part));
            if (support is not { IsComplete: true, IsSupported: false })
                return parameters;
            var tabbed = OwnedCuttingParameters.Copy(parameters);
            tabbed.TabsEnabled = true;
            return tabbed;
        }
    }
}
