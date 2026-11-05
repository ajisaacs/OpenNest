using System;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;

namespace OpenNest.Support
{
    /// <summary>
    /// Which parts <see cref="CuttingParameters.SupportTabs"/> tabs (docs/support-tabs.md): the
    /// parts the plate's support bed cannot hold level or, by size (<see cref="CuttingParameters.TabBySize"/>,
    /// or a plate with no bed), the parts whose width is in the settings' size range. Also the cutting settings for a lead-in
    /// placed by hand: the tool cuts where it is clicked, so the part is tabbed there. Plan
    /// Cutting chooses the tab point itself.
    /// </summary>
    public static class SupportTabSettings
    {
        /// <summary>
        /// The widest part a migrated "Tab every part" setting tabs by size: wider than any sheet,
        /// so the same parts stay tabbed. It is also the cutting panel's largest range value.
        /// </summary>
        public const double TabEveryPartMaxWidth = 9999;

        /// <summary>
        /// Whether support tabs judge parts by the plate's support bed: they are on, not by size,
        /// and the plate has a bed. Otherwise they go by the size range.
        /// </summary>
        public static bool UsesBed(CuttingParameters parameters, SupportBed bed) =>
            parameters?.SupportTabs == true && !parameters.TabBySize && bed != null;

        /// <summary>
        /// Whether support tabs are on and can tab a part on a plate with <paramref name="bed"/>:
        /// by the bed, or by a usable size range.
        /// </summary>
        public static bool CanTab(CuttingParameters parameters, SupportBed bed) =>
            parameters?.SupportTabs == true && (UsesBed(parameters, bed) || parameters.HasTabSizeRange());

        /// <summary>
        /// The analysis that tabs a part, or null when support tabs are off or the part is
        /// outside the rule: by the bed, the bed's analysis, held by its tab alone
        /// (<see cref="SupportAnalyzer.HeldByTab"/>) when the bed cannot hold it level and its
        /// length is within <see cref="CuttingParameters.TabHoldsAt"/>; by size (no bed, or
        /// <see cref="CuttingParameters.TabBySize"/>), a no-bed analysis
        /// (<see cref="SupportAnalyzer.AnalyzeWithoutBed"/>) of a part whose width is in
        /// <see cref="CuttingParameters.TabsBySizeAt"/>'s range. The part needs a tab when
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
            if (UsesBed(parameters, bed))
            {
                var support = SupportAnalyzer.Analyze(bed, quadrant, material, insideCornerClearance);
                return support is { IsComplete: true, IsSupported: false }
                    && parameters.TabHoldsAt(SupportAnalyzer.Length(material))
                    ? SupportAnalyzer.HeldByTab(support, material)
                    : support;
            }
            if (!parameters.HasTabSizeRange())
                return null;
            if (!material.IsComplete)
                return SupportAnalyzer.AnalyzeWithoutBed(material, insideCornerClearance);
            return parameters.TabsBySizeAt(SupportAnalyzer.Width(material))
                ? SupportAnalyzer.AnalyzeWithoutBed(material, insideCornerClearance)
                : null;
        }

        /// <summary>
        /// Whether support tabs are on but can tab nothing: they go by size (no bed, or
        /// <see cref="CuttingParameters.TabBySize"/>) and the settings set no size range.
        /// </summary>
        public static bool SkipsForNoRange(CuttingParameters parameters, SupportBed bed) =>
            parameters?.SupportTabs == true && !CanTab(parameters, bed);

        /// <summary>
        /// <paramref name="parameters"/> itself, or a copy with tabs enabled when support tabs are
        /// on, a tab size is set and <paramref name="part"/> needs a tab (see <see cref="Analyze"/>).
        /// The caller's settings are never changed.
        /// </summary>
        public static CuttingParameters ForManualLeadIn(CuttingParameters parameters, Plate plate, Part part)
        {
            ArgumentNullException.ThrowIfNull(parameters);
            if (parameters.TabsEnabled || parameters.TabConfig == null || plate == null || part == null
                || !CanTab(parameters, plate.SupportBed))
                return parameters;
            var support = Analyze(parameters, plate.SupportBed, plate.Quadrant, SupportMaterial.FromPart(part));
            if (support is not { IsComplete: true, IsSupported: false })
                return parameters;
            var tabbed = OwnedCuttingParameters.Copy(parameters);
            tabbed.TabsEnabled = true;
            return tabbed;
        }

        /// <summary>
        /// Moves the user's cutting settings off the retired "Tab every part" choice
        /// (<see cref="CuttingParameters.TabsEnabled"/> on with support tabs off): they become tabs
        /// by size from 0 to <see cref="TabEveryPartMaxWidth"/>, which tabs the same parts. Returns
        /// whether it changed <paramref name="parameters"/>. Only for the settings the user edits,
        /// never a plate's recorded parameters or a drawing pin's, which replay as planned.
        /// </summary>
        public static bool MigrateTabEveryPart(CuttingParameters parameters)
        {
            if (parameters is not { TabsEnabled: true, SupportTabs: false })
                return false;
            parameters.TabsEnabled = false;
            parameters.SupportTabs = true;
            parameters.TabBySize = true;
            parameters.TabSizeMinWidth = 0;
            parameters.TabSizeMaxWidth = TabEveryPartMaxWidth;
            return true;
        }
    }
}
