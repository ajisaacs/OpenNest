using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using OpenNest.Desktop;

namespace OpenNest.Forms
{
    /// <summary>
    /// Shows a rendered nest report or part printout PDF in WebView2 with Save As and Print. The PDF is a private
    /// temporary copy owned by this window: saving copies it, and closing deletes it.
    /// </summary>
    public sealed class NestReportViewerForm : Form
    {
        private readonly string pdfPath;
        private readonly string suggestedFileName;
        private readonly string documentName;
        private readonly WebView2 browser = new() { Dock = DockStyle.Fill };
        private readonly ToolStripButton printButton;

        /// <param name="documentName">What the PDF is, for the title and messages, e.g. "Part Printouts".</param>
        public NestReportViewerForm(string pdfPath, string nestName, string suggestedFileName,
            string documentName = "Nest Report")
        {
            this.pdfPath = pdfPath ?? throw new ArgumentNullException(nameof(pdfPath));
            this.suggestedFileName = suggestedFileName;
            this.documentName = documentName;
            Text = $"{documentName} - {nestName}";
            Icon = Properties.Resources.app_icon;
            // CenterParent is ignored for modeless windows; OnLoad centers on the owner instead.
            StartPosition = FormStartPosition.Manual;
            Size = new Size(1000, 900);

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
            Bounds = CenteredBounds(Owner?.Bounds ?? Screen.FromPoint(Cursor.Position).WorkingArea, Size);
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
                    $"The {documentName.ToLowerInvariant()} preview needs the Microsoft Edge WebView2 Runtime, "
                    + "which could not be started. Use Save As to keep the PDF.\n\n" + ex.Message,
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (IsDisposed)
                return;
            var core = browser.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            // The PDF is the only page this window shows.
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.Navigate(new Uri(pdfPath).AbsoluteUri);
            printButton.Enabled = true;
        }

        /// <summary>
        /// Centers <paramref name="size"/> on <paramref name="owner"/>, shrunk to fit and kept inside
        /// the working area of the screen holding the owner's center.
        /// </summary>
        internal static Rectangle CenteredBounds(Rectangle owner, Size size)
        {
            var center = new Point(owner.Left + owner.Width / 2, owner.Top + owner.Height / 2);
            var area = Screen.FromPoint(center).WorkingArea;
            var width = System.Math.Min(size.Width, area.Width);
            var height = System.Math.Min(size.Height, area.Height);
            var left = System.Math.Clamp(center.X - width / 2, area.Left, area.Right - width);
            var top = System.Math.Clamp(center.Y - height / 2, area.Top, area.Bottom - height);
            return new Rectangle(left, top, width, height);
        }

        private void SaveAs()
        {
            using var dlg = new SaveFileDialog
            {
                Filter = "PDF (*.pdf)|*.pdf",
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
                MessageBox.Show(this, $"Could not save the {documentName.ToLowerInvariant()}: {ex.Message}", Text,
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
                MessageBox.Show(this, $"Could not print the {documentName.ToLowerInvariant()}: {ex.Message}", Text,
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
        internal static void DeleteReportFolder(string pdfPath) => NestReportExport.DeleteReportFolder(pdfPath);
    }
}
