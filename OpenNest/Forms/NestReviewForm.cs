using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Engine.Jobs;

namespace OpenNest.Forms
{
    /// <summary>
    /// The final Auto Nest result as a sheet strip, with what it places and leaves unplaced.
    /// Keep commits it to the nest; Discard drops the whole proposal. Nothing is committed while
    /// this is open, and it never attaches parts to a plate.
    /// </summary>
    internal sealed class NestReviewForm : Form
    {
        public NestReviewForm(NestPipelineResult result, ColorScheme colorScheme, string costSummary = null)
        {
            Text = $"Review Auto Nest — {result.EngineName}";
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            Size = new Size(960, 520);
            MinimumSize = new Size(560, 360);
            Font = new Font("Segoe UI", 9.75F);

            var strip = new SheetStripControl { Dock = DockStyle.Fill, ColorScheme = colorScheme };
            strip.SetSheets(result.Plates);
            Strip = strip;

            var lines = new List<string>
            {
                $"{strip.SheetCount} sheet(s), {strip.PartCount} part(s) placed, {strip.Utilization:P1} utilization",
            };
            if (!result.IsValid)
                lines.Add($"{result.Violations.Count} layout violation(s) accepted in the validation review; overlaps are marked after Keep.");
            if (!string.IsNullOrEmpty(costSummary))
                lines.Add(costSummary);
            // Never drop demand silently: name every unplaced part and why.
            if (result.Unplaced.Count > 0)
            {
                lines.Add($"Not placed ({result.Unplaced.Sum(u => u.Count)}):");
                lines.AddRange(result.Unplaced.Select(u => "  " + u.Describe()));
            }
            var summary = new TextBox
            {
                Dock = DockStyle.Bottom,
                Multiline = true,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = SystemColors.Control,
                ScrollBars = lines.Count > 6 ? ScrollBars.Vertical : ScrollBars.None,
                Height = System.Math.Min(lines.Count, 6) * (Font.Height + 2) + 12,
                Text = string.Join("\r\n", lines),
                TabStop = false,
            };
            Summary = summary.Text;

            var keep = new Button { Text = "Keep", DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(93, 29) };
            var discard = new Button { Text = "Discard", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(93, 29) };
            AcceptButton = keep;
            CancelButton = discard;
            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(9, 6, 9, 6),
            };
            buttons.Controls.Add(discard);
            buttons.Controls.Add(keep);

            var summaryPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = summary.Height + 16,
                Padding = new Padding(12, 10, 12, 6),
            };
            summary.Dock = DockStyle.Fill;
            summaryPanel.Controls.Add(summary);

            Controls.Add(strip);
            Controls.Add(summaryPanel);
            Controls.Add(buttons);
        }

        internal SheetStripControl Strip { get; }

        internal string Summary { get; }
    }
}
