using System;
using System.Windows.Forms;
using OpenNest.Engine.BestFit;
using OpenNest.Forms;
using OpenNest.Gpu;

namespace OpenNest
{
    internal static class MainApp
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            BestFitCache.CreateEvaluator = GpuEvaluatorFactory.Create;
            Application.Run(new MainForm());
        }
    }
}
