using System;
using System.Windows.Forms;
using OpenNest.Forms;
using Velopack;

namespace OpenNest
{
    internal static class MainApp
    {
        [STAThread]
        private static void Main()
        {
            // Must run before anything else: on first launch after an update,
            // Velopack re-invokes the new exe with hidden hook arguments and
            // this call handles them and exits, so the UI below never runs.
            VelopackApp.Build().Run();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
