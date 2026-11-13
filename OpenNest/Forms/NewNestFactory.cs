using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Data;
using OpenNest.Desktop;
using OpenNest.Properties;

namespace OpenNest.Forms
{
    /// <summary>
    /// The WinForms entry point for File > New nests. The rules live in the shared
    /// <see cref="Desktop.NewNestSource"/>, reading this app's settings; this shows its warning.
    /// </summary>
    internal static class NewNestFactory
    {
        private static readonly Desktop.NewNestSource Factory = new(
            MachineSettings.Source,
            new AppUserSettings(),
            () => NestStorage.Settings.IsDatabaseMode ? NestStorage.CreateRepository() : null);

        /// <param name="release">
        /// The drawing set release group the nest is for: its units, with the saved plate
        /// defaults converted to them, its material and thickness, and the machine's spacing for
        /// those. Null uses the defaults as saved.
        /// </param>
        public static async Task<Nest> CreateAsync(IWin32Window owner, SetReleaseGroup release = null)
        {
            var created = await Factory.CreateAsync(release);
            if (created.Warning != null)
                MessageBox.Show(owner, created.Warning, "Nest Defaults", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return created.Nest;
        }

        /// <summary>See <see cref="Desktop.NewNestSource.DefaultMadeBy"/>.</summary>
        public static string DefaultMadeBy => Desktop.NewNestSource.MadeByOrUserName(Settings.Default.MadeBy);

        /// <summary>The app settings the shared rules read and save.</summary>
        private sealed class AppUserSettings : INewNestUserSettings
        {
            public int NestNumber
            {
                get => Settings.Default.NestNumber;
                set => Settings.Default.NestNumber = value;
            }

            public DateTime LastNestCreatedDate
            {
                get => Settings.Default.LastNestCreatedDate;
                set => Settings.Default.LastNestCreatedDate = value;
            }

            public string MadeBy => Settings.Default.MadeBy;

            public void Save() => Settings.Default.Save();
        }
    }
}
