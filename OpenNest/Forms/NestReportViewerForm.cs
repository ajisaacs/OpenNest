using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace OpenNest.Forms
{
    /// <summary>
    /// Shows a rendered nest report PDF in WebView2 with Save As and Print. The PDF is a private
    /// temporary copy owned by this window: saving copies it, and closing deletes it.
    /// </summary>
    public sealed class NestReportViewerForm : Form
    {
        private readonly string pdfPath;
        private readonly string suggestedFileName;
        private readonly WebView2 browser = new() { Dock = DockStyle.Fill };
        private readonly ToolStripButton printButton;

        public NestReportViewerForm(string pdfPath, string nestName, string suggestedFileName)
        {
            this.pdfPath = pdfPath ?? throw new ArgumentNullException(nameof(pdfPath));
            this.suggestedFileName = suggestedFileName;
            Text = $"Nest Report - {nestName}";
            Icon = Properties.Resources.app_icon;
            StartPosition = FormStartPosition.CenterParent;
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(System.Math.Min(1000, area.Width), System.Math.Min(900, area.Height));

            var saveButton = new ToolStripButton("Save As...") { Name = "btnSaveAs" };
            saveButton.Click += (_, _) => SaveAs();
            printButton = new ToolStripButton("Print...") { Name = "btnPrint", Enabled = false };
            printButton.Click += (_, _) => Print();
            var toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
            toolbar.Items.AddRange(new ToolStripItem[] { saveButton, printButton });

            Controls.Add(browser);
            Controls.Add(toolbar);
        }

        /// <summary>The report file this window shows and deletes on close.</summary>
        public string PdfPath => pdfPath;

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
                // Saving needs no viewer; only the preview and Print are unavailable.
                MessageBox.Show(this,
                    "The report preview needs the Microsoft Edge WebView2 Runtime, which could not be started. "
                    + "Use Save As to keep the report.\n\n" + ex.Message,
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (IsDisposed)
                return;
            var core = browser.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            // The report is the only page this window shows.
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.Navigate(new Uri(pdfPath).AbsoluteUri);
            printButton.Enabled = true;
        }

        private void SaveAs()
        {
            using var dlg = new SaveFileDialog
            {
                Filter = "PDF report (*.pdf)|*.pdf",
                FileName = suggestedFileName,
                AddExtension = true,
                DefaultExt = ".pdf",
                OverwritePrompt = true,
            };
            if (dlg.ShowDialog(this) != DialogResult.OK)
                return;
            try
            {
                // Overwrite consent is the dialog prompt.
                File.Copy(pdfPath, dlg.FileName, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                MessageBox.Show(this, $"Could not save the nest report: {ex.Message}", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void Print()
        {
            try
            {
                browser.CoreWebView2?.ShowPrintUI(CoreWebView2PrintDialogKind.Browser);
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException)
            {
                MessageBox.Show(this, $"Could not print the nest report: {ex.Message}", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // Release WebView2's hold on the file before deleting its private folder.
            browser.Dispose();
            base.OnFormClosed(e);
            DeleteReportFolder(pdfPath);
        }

        /// <summary>Best effort: a leftover temp report is harmless.</summary>
        internal static void DeleteReportFolder(string pdfPath)
        {
            try
            {
                var folder = Path.GetDirectoryName(pdfPath);
                if (folder != null && Directory.Exists(folder))
                    Directory.Delete(folder, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
