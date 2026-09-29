using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace OpenNest.Forms;

/// <summary>A fresh, explicit decision for each invalid automatic nesting proposal.</summary>
public sealed class NestValidationForm : Form
{
    public NestValidationForm(IReadOnlyList<string> violations, bool canKeep)
    {
        Text = "Auto Nest — validation problems";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(740, 440);
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        var summary = new Label
        {
            Dock = DockStyle.Top,
            Height = 70,
            Padding = new Padding(10),
            Text = canKeep
                ? "This layout failed validation. Discard leaves your plates unchanged. Keep anyway applies the entire layout and enables Overlap Check.\nOverlap Check highlights material overlaps; spacing, stock and rotation warnings remain listed here."
                : "The engine returned malformed placements. No layout can be kept. Discard leaves your plates unchanged.",
        };
        var report = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Text = string.Join("\r\n\r\n", violations),
        };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(6),
        };
        var discard = new Button { Text = "Discard", AutoSize = true, DialogResult = DialogResult.Cancel };
        var keep = new Button
        {
            Text = "Keep anyway",
            AutoSize = true,
            Enabled = canKeep,
            DialogResult = DialogResult.OK,
        };
        buttons.Controls.Add(discard);
        buttons.Controls.Add(keep);
        Controls.Add(report);
        Controls.Add(summary);
        Controls.Add(buttons);
        AcceptButton = discard;
        CancelButton = discard;
        ActiveControl = discard;
    }
}
