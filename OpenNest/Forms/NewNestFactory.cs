using System;
using System.Windows.Forms;
using OpenNest.Properties;

namespace OpenNest.Forms
{
    /// <summary>
    /// Creates File > New nests: machine defaults and clamps, then the next name in the
    /// day's sequence (<see cref="Settings.NestNumber"/> restarts at 1 each day).
    /// </summary>
    internal static class NewNestFactory
    {
        private const string Base30Digits = "2345679ACDEFGHJKLMNPQRSTUVWXYZ";

        public static Nest Create(IWin32Window owner)
        {
            var nest = CreateDefaultNest(owner);
            var now = DateTime.Now;

            nest.DateCreated = now;
            nest.DateLastModified = now;

            if (now.Date != Settings.Default.LastNestCreatedDate.Date)
            {
                Settings.Default.NestNumber = 1;
                Settings.Default.LastNestCreatedDate = now.Date;
            }

            nest.Name = GetNestName(now, Settings.Default.NestNumber++);
            return nest;
        }

        private static Nest CreateDefaultNest(IWin32Window owner)
        {
            var nest = new Nest();
            MachineSettings.LoadNestDefaults().ApplyTo(nest);
            // After the defaults so the rail is converted to the new nest's units.
            MachineSettings.LoadClampSettings(owner).ApplyToNewNest(nest);
            return nest;
        }

        /// <summary>
        /// "N" + two-digit year + the sequence in an unambiguous base-30 alphabet, padded to
        /// three digits with its zero digit ('2').
        /// </summary>
        public static string GetNestName(DateTime date, int id)
        {
            var year = (date.Year % 100).ToString("D2");
            var seq = ToBase30(id).PadLeft(3, Base30Digits[0]);

            return $"N{year}-{seq}";
        }

        private static string ToBase30(int value)
        {
            if (value == 0)
                return Base30Digits[0].ToString();

            var result = "";
            while (value > 0)
            {
                result = Base30Digits[value % Base30Digits.Length] + result;
                value /= Base30Digits.Length;
            }
            return result;
        }
    }
}
