using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Data;
using OpenNest.Properties;

namespace OpenNest.Forms
{
    /// <summary>
    /// Creates File > New nests: nest defaults, clamps, the default machine and its support grid, then the year's next name
    /// (<see cref="NestNames"/>). In Database mode the nest server allocates the number so
    /// every PC shares one sequence; when it cannot, <see cref="Settings.NestNumber"/> is the
    /// local fallback, kept above every number the server handed out and reset each year.
    /// </summary>
    internal static class NewNestFactory
    {
        private static readonly TimeSpan ServerTimeout = TimeSpan.FromSeconds(3);

        /// <param name="units">
        /// The new nest's units, with the saved plate defaults converted to them (a drawing set
        /// release group drawn in other units); null uses the defaults as saved.
        /// </param>
        public static async Task<Nest> CreateAsync(IWin32Window owner, Units? units = null)
        {
            var nest = CreateDefaultNest(owner, units);
            var now = DateTime.Now;

            nest.DateCreated = now;
            nest.DateLastModified = now;
            nest.Name = NestNames.Format(now.Year, await AllocateNumberAsync(now));
            return nest;
        }

        private static Nest CreateDefaultNest(IWin32Window owner, Units? units)
        {
            var nest = new Nest();
            var defaults = MachineSettings.LoadNestDefaults();
            (units is { } converted ? defaults.ConvertedTo(converted) : defaults).ApplyTo(nest);
            // After the defaults so the rail and grid are converted to the new nest's units.
            var machine = MachineSettings.DefaultMachine(defaults, MachineSettings.LoadMachines());
            MachineSettings.LoadClampSettings(machine).ApplyToNewNest(nest);
            MachineSettings.AssignMachine(nest, machine);
            return nest;
        }

        private static async Task<int> AllocateNumberAsync(DateTime now)
        {
            if (now.Year != Settings.Default.LastNestCreatedDate.Year)
                Settings.Default.NestNumber = 1;
            Settings.Default.LastNestCreatedDate = now.Date;

            if (NestStorage.Settings.IsDatabaseMode)
            {
                try
                {
                    using var timeout = new CancellationTokenSource(ServerTimeout);
                    var number = await NestStorage.CreateRepository()
                        .AllocateNestNumberAsync(now.Year, timeout.Token);
                    Settings.Default.NestNumber = System.Math.Max(Settings.Default.NestNumber, number + 1);
                    return number;
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException
                    or OperationCanceledException or NotSupportedException or ArgumentException)
                {
                    // Offline or an older server: fall back to this PC's counter.
                }
            }

            return Settings.Default.NestNumber++;
        }
    }
}
