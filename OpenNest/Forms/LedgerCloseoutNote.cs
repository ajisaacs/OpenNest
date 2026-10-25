using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace OpenNest.Forms
{
    /// <summary>
    /// A non-modal reminder, shown when a nest is marked Has Been Cut while MaterialLedger still
    /// holds sheets for it, that links to the nest's ledger page. It never blocks or gates anything;
    /// holds are closed out only in MaterialLedger.
    /// </summary>
    internal sealed class LedgerCloseoutNote : Form
    {
        public LedgerCloseoutNote(string nestName, int openHolds, string ledgerNestUrl)
        {
            Text = "MaterialLedger";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(12);

            Message = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(360, 0),
                Text = MessageText(nestName, openHolds),
            };
            Link = new LinkLabel { AutoSize = true, Text = "Open the nest in MaterialLedger", Tag = ledgerNestUrl };
            Link.LinkClicked += (_, _) => OpenLink(ledgerNestUrl);
            var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
            close.Click += (_, _) => Close();
            CancelButton = close;

            var layout = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Dock = DockStyle.Fill,
            };
            Message.Margin = new Padding(0, 0, 0, 8);
            Link.Margin = new Padding(0, 0, 0, 12);
            layout.Controls.AddRange(new Control[] { Message, Link, close });
            Controls.Add(layout);
        }

        internal Label Message { get; }

        internal LinkLabel Link { get; }

        internal static string MessageText(string nestName, int openHolds)
        {
            var holds = openHolds == 1 ? "1 hold" : $"{openHolds} holds";
            var nest = string.IsNullOrWhiteSpace(nestName) ? "This nest" : $"\"{nestName}\"";
            return $"{nest} is marked Has Been Cut. Close out {holds} in MaterialLedger.";
        }

        /// <summary>Shows the note beside <paramref name="owner"/>'s lower right corner without taking over input.</summary>
        public void ShowBeside(Form owner)
        {
            var area = owner != null ? owner.RectangleToScreen(owner.ClientRectangle) : Screen.PrimaryScreen.WorkingArea;
            var size = GetPreferredSize(Size.Empty);
            Location = new Point(System.Math.Max(area.Left, area.Right - size.Width - 24),
                System.Math.Max(area.Top, area.Bottom - size.Height - 48));
            Show(owner);
        }

        private static void OpenLink(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)
                || parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
                return;
            try
            {
                Process.Start(new ProcessStartInfo(parsed.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // No browser registered; the note's text still says what to do.
            }
        }
    }
}
