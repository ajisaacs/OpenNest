using System;
using OpenNest.Data;

namespace OpenNest.Forms
{
    /// <summary>
    /// The cut-time settings last used in Calculate Cut Time or the simulator, shared with
    /// anything else that estimates cut time (such as reports).
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
            var settings = Properties.Settings.Default;
            var unset = settings.LastFeedrate == 1 && settings.LastRapidFeedrate == 1 && settings.LastPierceTime == 0;
            var defaults = CutParameters.Default;
            var feed = unset ? defaults.Feedrate : (double)settings.LastFeedrate;
            var machine = CutTimeMachineBox.SavedMachine(MachineSettings.LoadMachines());

            return new CutParameters
            {
                Feedrate = feed,
                HoleFeedrate = settings.LastHoleFeedrate > 0 ? (double)settings.LastHoleFeedrate : feed,
                RapidTravelRate = unset ? defaults.RapidTravelRate : (double)settings.LastRapidFeedrate,
                PierceTime = unset ? defaults.PierceTime : TimeSpan.FromSeconds((double)settings.LastPierceTime),
                SkipScribe = settings.CutTimeSkipEtch,
                Units = units,
                Motion = machine?.GetMotionOrPlaceholder().ToProfile(machine.Units, units),
            };
        }

        public static void Save(CutParameters parameters)
        {
            var settings = Properties.Settings.Default;
            settings.LastFeedrate = (decimal)parameters.Feedrate;
            settings.LastHoleFeedrate = (decimal)(parameters.HoleFeedrate ?? 0);
            settings.LastRapidFeedrate = (decimal)parameters.RapidTravelRate;
            settings.LastPierceTime = (decimal)parameters.PierceTime.TotalSeconds;
            settings.CutTimeSkipEtch = parameters.SkipScribe;
            settings.Save();
        }
    }
}
