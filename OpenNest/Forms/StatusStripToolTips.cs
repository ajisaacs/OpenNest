using System;
using System.Drawing;
using System.Windows.Forms;

namespace OpenNest.Forms
{
    /// <summary>
    /// Shows a status strip's item tooltips above the strip. The strip's own tooltips open
    /// below the cursor; at the bottom of the screen Windows pushes them back over the
    /// cursor, the strip sees a mouse leave and hides them, and they flash on and off.
    /// </summary>
    internal sealed class StatusStripToolTips : IDisposable
    {
        private const int Gap = 2;

        private readonly ToolStrip strip;
        private readonly ToolTip toolTip = new();
        private readonly Timer hoverTimer = new() { Interval = SystemInformation.MouseHoverTime };
        private ToolStripItem hoverItem;
        private string shownText;

        public StatusStripToolTips(ToolStrip strip)
        {
            this.strip = strip;
            strip.ShowItemToolTips = false;
            strip.MouseMove += Strip_MouseMove;
            strip.MouseLeave += Strip_MouseLeave;
            strip.MouseDown += Strip_MouseDown;
            hoverTimer.Tick += HoverTimer_Tick;
        }

        public void Dispose()
        {
            hoverTimer.Dispose();
            toolTip.Dispose();
        }

        private void Strip_MouseMove(object sender, MouseEventArgs e)
        {
            var item = strip.GetItemAt(e.Location);
            if (item == hoverItem)
                return;

            Hide();
            hoverItem = item;
            if (!string.IsNullOrEmpty(item?.ToolTipText))
                hoverTimer.Start();
        }

        private void Strip_MouseLeave(object sender, EventArgs e)
        {
            Hide();
            hoverItem = null;
        }

        // The next move re-arms the hover, so a click that changes the text (a hold refresh) shows the new tip.
        private void Strip_MouseDown(object sender, MouseEventArgs e)
        {
            Hide();
            hoverItem = null;
        }

        private void HoverTimer_Tick(object sender, EventArgs e)
        {
            hoverTimer.Stop();
            Show();
        }

        private void Show()
        {
            var text = hoverItem?.ToolTipText;
            if (string.IsNullOrEmpty(text))
            {
                Hide();
                return;
            }

            // The tooltip draws its text in the status font plus a few pixels of border and margin.
            var height = TextRenderer.MeasureText(text, SystemFonts.StatusFont).Height + 10;
            toolTip.Show(text, strip, hoverItem.Bounds.Left, -height - Gap);
            shownText = text;
        }

        private void Hide()
        {
            hoverTimer.Stop();
            if (shownText == null)
                return;

            toolTip.Hide(strip);
            shownText = null;
        }
    }
}
