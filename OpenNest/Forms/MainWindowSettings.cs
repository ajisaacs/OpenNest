using System;
using System.Drawing;
using System.Windows.Forms;
using OpenNest.Data;
using OpenNest.Desktop;
using OpenNest.Engine.BestFit;
using OpenNest.Properties;

namespace OpenNest.Forms
{
    /// <summary>The main window's persisted placement and its one-time settings upgrades.</summary>
    internal static class MainWindowSettings
    {
        /// <summary>
        /// Restores the saved placement when it still fits the primary screen. Without a usable
        /// saved size the window keeps its designer start, maximized.
        /// </summary>
        public static void Load(Form form)
        {
            var screen = Screen.PrimaryScreen.Bounds;
            if (!IsRestorableSize(Settings.Default.MainWindowSize, form.MinimumSize, screen.Size))
                return;

            if (screen.Contains(Settings.Default.MainWindowLocation))
                form.Location = Settings.Default.MainWindowLocation;
            form.Size = Settings.Default.MainWindowSize;
            form.WindowState = Settings.Default.MainWindowState == FormWindowState.Maximized
                ? FormWindowState.Maximized : FormWindowState.Normal;
        }

        public static void Save(Form form)
        {
            var (bounds, state) = SavedPlacement(form.WindowState, form.Bounds, form.RestoreBounds);
            Settings.Default.MainWindowLocation = bounds.Location;
            Settings.Default.MainWindowSize = bounds.Size;
            Settings.Default.MainWindowState = state;
            Settings.Default.Save();
        }

        /// <summary>
        /// Whether a saved main-window size is worth restoring: it fits the screen and is larger
        /// than the window's minimum. The 0, 0 default, applied, shrinks the window to its minimum,
        /// and a later close used to save that minimum, so the window opened at it from then on.
        /// </summary>
        internal static bool IsRestorableSize(Size saved, Size minimum, Size screen) =>
            saved.Width > 0 && saved.Height > 0
            && (saved.Width > minimum.Width || saved.Height > minimum.Height)
            && saved.Width <= screen.Width && saved.Height <= screen.Height;

        /// <summary>
        /// What to remember on close: the restored bounds of a maximized or minimized window, and
        /// never the minimized state.
        /// </summary>
        internal static (Rectangle Bounds, FormWindowState State) SavedPlacement(
            FormWindowState state, Rectangle bounds, Rectangle restoreBounds) =>
            state == FormWindowState.Normal
                ? (bounds, FormWindowState.Normal)
                : (restoreBounds, state == FormWindowState.Maximized
                    ? FormWindowState.Maximized : FormWindowState.Normal);

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
            var result = NestTemplateMigration.Run(
                Settings.Default.NestTemplatePath, NestDefaults.DefaultPath, BestFitCacheStore.Shared);

            // A failed conversion keeps the setting populated so the user
            // can still find their template file.
            if (result.Warning != null)
            {
                MessageBox.Show(
                    result.Warning,
                    "Nest Template",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }

            if (!result.ClearSetting)
                return;

            Settings.Default.NestTemplatePath = "";
            Settings.Default.Save();
        }
    }
}
