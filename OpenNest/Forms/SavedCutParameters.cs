using OpenNest.Desktop;

namespace OpenNest.Forms
{
    /// <summary>
    /// The cut-time settings last used in Calculate Cut Time or the simulator, shared with
    /// anything else that estimates cut time (such as reports). The rules live in
    /// <see cref="SavedCutTimeSettings"/>; this reads and writes them through the app settings.
    /// </summary>
    internal static class SavedCutParameters
    {
        /// <summary>
        /// The saved feed, hole feed, rapid, pierce and etch choice, plus the saved machine's
        /// acceleration in <paramref name="units"/>. Settings never saved (1, 1, 0) would crawl, so
        /// they fall back to <see cref="CutParameters.Default"/>.
        /// </summary>
        public static CutParameters Load(Units units)
        {
            var saved = Current();
            var machine = SavedCutTimeSettings.ChooseMachine(MachineSettings.LoadMachines(), saved.Machine);
            return saved.ToCutParameters(units, machine);
        }

        public static void Save(CutParameters parameters)
        {
            var saved = Current().With(parameters);
            var settings = Properties.Settings.Default;
            settings.LastFeedrate = saved.Feedrate;
            settings.LastHoleFeedrate = saved.HoleFeedrate;
            settings.LastRapidFeedrate = saved.RapidFeedrate;
            settings.LastPierceTime = saved.PierceSeconds;
            settings.CutTimeSkipEtch = saved.SkipEtch;
            settings.Save();
        }

        private static SavedCutTimeSettings Current()
        {
            var settings = Properties.Settings.Default;
            return new SavedCutTimeSettings(
                settings.LastFeedrate,
                settings.LastHoleFeedrate,
                settings.LastRapidFeedrate,
                settings.LastPierceTime,
                settings.CutTimeSkipEtch,
                settings.CutTimeMachine);
        }
    }
}
