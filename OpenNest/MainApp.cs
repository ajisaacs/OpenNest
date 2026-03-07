using System;
using System.Windows.Forms;
using OpenNest.Forms;

namespace OpenNest
{
    internal static class MainApp
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
