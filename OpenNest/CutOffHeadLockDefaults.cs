using OpenNest.Desktop;

namespace OpenNest
{
    /// <summary>
    /// Whether new cut-offs lock the head height and overcut the far sheet edge, kept between
    /// sessions with the overcut in inches and converted to each nest's units. The rules live in
    /// <see cref="SavedCutOffHeadLock"/>; this reads and writes them through the app settings.
    /// </summary>
    internal static class CutOffHeadLockDefaults
    {
        internal static void Load(CutOffSettings settings, Units units) =>
            new SavedCutOffHeadLock(
                Properties.Settings.Default.CutOffLockHeadHeight,
                Properties.Settings.Default.CutOffHeadLockOvercutInches)
                .ApplyTo(settings, units);

        internal static void Save(bool lockHeadHeight, double overcut, Units units)
        {
            var saved = SavedCutOffHeadLock.From(lockHeadHeight, overcut, units);
            Properties.Settings.Default.CutOffLockHeadHeight = saved.LockHeadHeight;
            Properties.Settings.Default.CutOffHeadLockOvercutInches = saved.OvercutInches;
            Properties.Settings.Default.Save();
        }
    }
}
