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
            : this($"Review Auto Nest — {result.EngineName}", result.Plates,
                result.IsValid ? 0 : result.Violations.Count, result.Unplaced, colorScheme, costSummary)
        {
        }

        /// <summary>
        /// Any set of proposed sheets: <paramref name="acceptedViolations"/> is how many layout
        /// violations the validation review accepted (zero for a valid proposal). Parts already on a
        /// plate (<see cref="ProposedPlate.Occupants"/>) show in one color and do not count as placed.
        /// </summary>
        public NestReviewForm(string title, IReadOnlyList<ProposedPlate> sheets, int acceptedViolations,
            IReadOnlyList<NestUnplacedPart> unplaced, ColorScheme colorScheme, string costSummary = null)
        {
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            Size = new Size(960, 520);
            MinimumSize = new Size(560, 360);
            Font = new Font("Segoe UI", 9.75F);

            var strip = new SheetStripControl { Dock = DockStyle.Fill, ColorScheme = colorScheme, ShowRemnant = true };
            strip.SetSheets(sheets);
            Strip = strip;

            var lines = new List<string>
            {
                $"{strip.SheetCount} sheet(s), {strip.PartCount} part(s) placed, {strip.Utilization:P1} utilization",
            };
            if (strip.ExistingPartCount > 0)
                lines.Add($"Parts already on the plates ({strip.ExistingPartCount}) are shown in gray and stay where they are.");
            if (acceptedViolations > 0)
                lines.Add($"{acceptedViolations} layout violation(s) accepted in the validation review; overlaps are marked after Keep.");
            if (!string.IsNullOrEmpty(costSummary))
                lines.Add(costSummary);
            // Never drop demand silently: name every unplaced part and why.
            if (unplaced.Count > 0)
            {
                lines.Add($"Not placed ({unplaced.Sum(u => u.Count)}):");
                lines.AddRange(unplaced.Select(u => "  " + u.Describe()));
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
