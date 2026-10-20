using System;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;

namespace OpenNest.Support
{
    /// <summary>
    /// Cutting settings for a lead-in placed by hand under
    /// <see cref="CuttingParameters.SupportTabs"/>: the tool cuts where it is clicked, so the
    /// part is tabbed there when its plate's support bed cannot hold it level. Plan Cutting
    /// chooses the tab point itself (docs/support-tabs.md).
    /// </summary>
    public static class SupportTabSettings
    {
        /// <summary>
        /// <paramref name="parameters"/> itself, or a copy with tabs enabled when support tabs are
        /// on, the plate has a support bed, a tab size is set and the bed cannot hold
        /// <paramref name="part"/> level. The caller's settings are never changed.
        /// </summary>
        public static CuttingParameters ForManualLeadIn(CuttingParameters parameters, Plate plate, Part part)
        {
            ArgumentNullException.ThrowIfNull(parameters);
            if (!parameters.SupportTabs || parameters.TabsEnabled || parameters.TabConfig == null
                || plate?.SupportBed == null || part == null)
                return parameters;
            var support = SupportAnalyzer.Analyze(plate.SupportBed, plate.Quadrant, part);
            if (!support.IsComplete || support.IsSupported)
                return parameters;
            var tabbed = OwnedCuttingParameters.Copy(parameters);
            tabbed.TabsEnabled = true;
            return tabbed;
        }
    }
}
