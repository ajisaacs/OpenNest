using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace OpenNest.Help
{
    /// <summary>
    /// Single help window hosting the bundled HTML in WebView2. The help folder is served
    /// from a private virtual host so the site's search and scripts run as on a web server.
    /// The window runs its own UI thread, so modal dialogs in the app never disable it:
    /// F1 in a dialog opens help that can be scrolled and searched while the dialog is open.
    /// </summary>
    public sealed class HelpForm : Form
    {
        private const string Host = "help.opennest.local";
        private static readonly string Origin = $"https://{Host}/";
        private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(10);

        private static readonly object Sync = new();
        private static HelpForm instance;

        private readonly WebView2 browser = new() { Dock = DockStyle.Fill };
        private string pendingPage;
        private bool ready;

        private HelpForm(string page)
        {
            pendingPage = page;
            Text = "OpenNest Help";
            Icon = Properties.Resources.app_icon;
            StartPosition = FormStartPosition.Manual;
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(System.Math.Min(1100, area.Width), System.Math.Min(900, area.Height));
            Location = new Point(area.Right - Width, area.Top);
            Controls.Add(browser);
        }

        /// <summary>Shows <paramref name="page"/> (relative to the help folder) in the help window.</summary>
        public static void ShowPage(string page)
        {
            lock (Sync)
            {
                var open = instance;
                if (open != null && !open.IsDisposed)
                {
                    try
                    {
                        open.BeginInvoke(() => open.Reveal(page));
                        return;
                    }
                    catch (InvalidOperationException)
                    {
                        // Closed between the check and the call: start a new window below.
                    }
                }

                using var started = new ManualResetEventSlim();
                var thread = new Thread(() =>
                {
                    var form = new HelpForm(page);
                    form.HandleCreated += (_, _) => started.Set();
                    instance = form;
                    Application.Run(form);
                })
                {
                    IsBackground = true,
                    Name = "OpenNest Help",
                };
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                started.Wait(StartTimeout);
            }
        }

        private void Reveal(string page)
        {
            if (WindowState == FormWindowState.Minimized)
                WindowState = FormWindowState.Normal;
            Activate();
            Navigate(page);
        }

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            try
            {
                var dataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "OpenNest", "WebView2");
                var environment = await CoreWebView2Environment.CreateAsync(null, dataFolder);
                await browser.EnsureCoreWebView2Async(environment);
            }
            catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or COMException or IOException
                                           or UnauthorizedAccessException)
            {
                MessageBox.Show(this,
                    "OpenNest Help needs the Microsoft Edge WebView2 Runtime, which could not be started.\n\n"
                    + ex.Message,
                    "OpenNest Help", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Close();
                return;
            }

            var core = browser.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping(Host, HelpTopics.Directory,
                CoreWebView2HostResourceAccessKind.DenyCors);
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.NavigationStarting += Core_NavigationStarting;
            core.NewWindowRequested += Core_NewWindowRequested;
            // Site page titles already end in "OpenNest Help".
            core.DocumentTitleChanged += (_, _) => Text = core.DocumentTitle;

            ready = true;
            if (pendingPage != null)
                Navigate(pendingPage);
        }

        private void Navigate(string page)
        {
            if (!ready)
            {
                pendingPage = page;
                return;
            }

            pendingPage = null;
            browser.CoreWebView2.Navigate(Origin + page.TrimStart('/'));
        }

        // Help pages stay in this window; anything off-site opens in the user's browser.
        private static void Core_NavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (IsHelpPage(e.Uri))
                return;
            e.Cancel = true;
            OpenExternally(e.Uri);
        }

        private void Core_NewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
            if (IsHelpPage(e.Uri))
                browser.CoreWebView2.Navigate(e.Uri);
            else
                OpenExternally(e.Uri);
        }

        private static bool IsHelpPage(string uri) =>
            uri.StartsWith(Origin, StringComparison.OrdinalIgnoreCase) || uri.StartsWith("about:", StringComparison.Ordinal);

        private static void OpenExternally(string uri)
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
                || (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp))
                return;
            Process.Start(new ProcessStartInfo(parsed.AbsoluteUri) { UseShellExecute = true });
        }
    }
}
