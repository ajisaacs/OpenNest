namespace OpenNest
{
    /// <summary>
    /// Whether new cut-offs lock the head height and overcut the far sheet edge, kept between
    /// sessions with the overcut in inches and converted to each nest's units.
    /// </summary>
    internal static class CutOffHeadLockDefaults
    {
        private const double DefaultOvercutInches = 1.5;
        private const double MillimetersPerInch = 25.4;

        internal static void Load(CutOffSettings settings, Units units)
        {
            var inches = Properties.Settings.Default.CutOffHeadLockOvercutInches;
            if (!double.IsFinite(inches) || inches <= 0)
                inches = DefaultOvercutInches;
            settings.LockHeadHeight = Properties.Settings.Default.CutOffLockHeadHeight;
            settings.HeadLockOvercut = units == Units.Millimeters ? inches * MillimetersPerInch : inches;
        }

        internal static void Save(bool lockHeadHeight, double overcut, Units units)
        {
            Properties.Settings.Default.CutOffLockHeadHeight = lockHeadHeight;
            Properties.Settings.Default.CutOffHeadLockOvercutInches =
                units == Units.Millimeters ? overcut / MillimetersPerInch : overcut;
            Properties.Settings.Default.Save();
        }
    }
}
