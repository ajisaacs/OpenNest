using System;
using System.IO;
using System.Windows.Forms;
using OpenNest.Data;
using OpenNest.Engine.BestFit;
using OpenNest.IO;
using OpenNest.Properties;

namespace OpenNest.Forms
{
    /// <summary>The main window's persisted placement and its one-time settings upgrades.</summary>
    internal static class MainWindowSettings
    {
        /// <summary>Restores the saved placement when it still fits the primary screen.</summary>
        public static void Load(Form form)
        {
            var screen = Screen.PrimaryScreen.Bounds;

            if (screen.Contains(Settings.Default.MainWindowLocation))
                form.Location = Settings.Default.MainWindowLocation;

            if (
                Settings.Default.MainWindowSize.Width <= screen.Width
                && Settings.Default.MainWindowSize.Height <= screen.Height
            )
            {
                form.Size = Settings.Default.MainWindowSize;
            }

            form.WindowState = Settings.Default.MainWindowState;
        }

        public static void Save(Form form)
        {
            Settings.Default.MainWindowLocation = form.Location;
            Settings.Default.MainWindowSize = form.Size;
            Settings.Default.MainWindowState = form.WindowState;
            Settings.Default.Save();
        }

        /// <summary>
        /// Places the Drawing Sets window where it last closed when that still shows on a
        /// screen; the first time, maximized on the main window's screen.
        /// </summary>
        public static void LoadDrawingSets(Form form)
        {
            var bounds = new System.Drawing.Rectangle(Settings.Default.DrawingSetsWindowLocation, Settings.Default.DrawingSetsWindowSize);
            form.StartPosition = FormStartPosition.Manual;
            if (bounds.Width > 0 && bounds.Height > 0
                && Array.Exists(Screen.AllScreens, s => s.WorkingArea.IntersectsWith(bounds)))
            {
                form.Bounds = bounds;
                form.WindowState = Settings.Default.DrawingSetsWindowState == FormWindowState.Maximized
                    ? FormWindowState.Maximized : FormWindowState.Normal;
                return;
            }

            var owner = Application.OpenForms.Count > 0 ? Screen.FromControl(Application.OpenForms[0]) : Screen.PrimaryScreen;
            var area = owner.WorkingArea;
            form.Bounds = new System.Drawing.Rectangle(area.X + area.Width / 10, area.Y + area.Height / 10,
                area.Width * 8 / 10, area.Height * 8 / 10);
            form.WindowState = FormWindowState.Maximized;
        }

        /// <summary>Remembers the Drawing Sets window's restored bounds and whether it was maximized.</summary>
        public static void SaveDrawingSets(Form form)
        {
            var bounds = form.WindowState == FormWindowState.Normal ? form.Bounds : form.RestoreBounds;
            Settings.Default.DrawingSetsWindowLocation = bounds.Location;
            Settings.Default.DrawingSetsWindowSize = bounds.Size;
            Settings.Default.DrawingSetsWindowState = form.WindowState == FormWindowState.Maximized
                ? FormWindowState.Maximized : FormWindowState.Normal;
            Settings.Default.Save();
        }

        /// <summary>
        /// One-time upgrade: converts a legacy .nstdot nest template
        /// (NestTemplatePath setting) into defaults.json, then clears the
        /// setting so the template mechanism is never consulted again.
        /// </summary>
        public static void MigrateNestTemplate()
        {
            var templatePath = Settings.Default.NestTemplatePath;
            if (string.IsNullOrWhiteSpace(templatePath))
                return;

            // A failed conversion keeps the setting populated so the user
            // can still find their template file.
            var converted = true;
            if (File.Exists(templatePath) && !File.Exists(NestDefaults.DefaultPath))
            {
                try
                {
                    var nest = new NestReader(templatePath) { BestFitStore = BestFitCacheStore.Shared }.Read();
                    NestDefaults.FromNest(nest).Save(NestDefaults.DefaultPath);
                }
                catch (Exception ex)
                {
                    converted = false;
                    MessageBox.Show(
                        $"The nest template could not be converted to the new defaults file:\n{templatePath}\n\n{ex.Message}\n\nIt will no longer be loaded automatically. Set defaults under Tools > Nest Defaults.",
                        "Nest Template",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                }
            }

            if (!converted)
                return;

            Settings.Default.NestTemplatePath = "";
            Settings.Default.Save();
        }
    }
}
