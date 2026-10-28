using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Data.MaterialLedger;

namespace OpenNest.Forms;

/// <summary>
/// Lists the nest's item codes that are no longer free for it and asks what to do on this
/// save or post: go on after acknowledging, remove the codes, or cancel. Like pre-post
/// verification, going on needs the acknowledgement each time; nothing remembers it.
/// </summary>
public sealed class LedgerCodeConflictForm : Form
{
    private readonly CheckBox acknowledgeBox;
    private readonly Button proceedButton;
    private readonly Button removeButton;

    public LedgerCodeConflictForm(string action, IReadOnlyList<LedgerCodeConflict> conflicts)
    {
        ArgumentNullException.ThrowIfNull(conflicts);
        Text = "Ledger Stock Changed";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(560, 340);
        MinimumSize = new Size(440, 300);
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.Sizable;

        var codes = conflicts.Select(c => c.ItemCode).Distinct(StringComparer.Ordinal).Count();
        var headline = new Label
        {
            Text = (codes == 1 ? "1 item code on this nest is" : $"{codes} item codes on this nest are")
                + " no longer free for it. Another job may cut the same sheet.\n"
                + "Removing the codes keeps the layout; MaterialLedger then matches those sheets by size.",
            Dock = DockStyle.Top,
            AutoSize = true,
            MaximumSize = new Size(536, 0),
            Padding = new Padding(0, 0, 0, 8),
        };

        var list = new ListView
        {
            Name = "conflictList",
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
        };
        list.Columns.Add("Plate", 50);
        list.Columns.Add("Item code", 120);
        list.Columns.Add("Why", 340);
        foreach (var conflict in conflicts)
            list.Items.Add(new ListViewItem(new[] { conflict.PlateNumber.ToString(), conflict.ItemCode, conflict.Reason }));

        acknowledgeBox = new CheckBox
        {
            Name = "acknowledgeBox",
            Text = $"{action} with these codes although the sheets may be taken",
            Dock = DockStyle.Bottom,
            AutoSize = true,
            Padding = new Padding(0, 6, 0, 0),
        };
        acknowledgeBox.CheckedChanged += (_, _) => proceedButton.Enabled = acknowledgeBox.Checked;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 6, 0, 0),
        };
        var cancel = new Button { Name = "cancelButton", Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        proceedButton = new Button { Name = "proceedButton", Text = $"{action} Anyway", AutoSize = true, Enabled = false };
        removeButton = new Button { Name = "removeButton", Text = $"Remove Codes and {action}", AutoSize = true };
        proceedButton.Click += (_, _) => Finish(acknowledgeBox.Checked ? LedgerCodeRecheck.Choice.Proceed : LedgerCodeRecheck.Choice.Cancel);
        removeButton.Click += (_, _) => Finish(LedgerCodeRecheck.Choice.RemoveCodes);
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(proceedButton);
        buttons.Controls.Add(removeButton);

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        body.Controls.Add(list);
        body.Controls.Add(headline);
        body.Controls.Add(acknowledgeBox);
        body.Controls.Add(buttons);
        Controls.Add(body);

        AcceptButton = removeButton;
        CancelButton = cancel;
    }

    /// <summary>What the programmer chose; Cancel until a button is clicked.</summary>
    internal LedgerCodeRecheck.Choice Choice { get; private set; } = LedgerCodeRecheck.Choice.Cancel;

    private void Finish(LedgerCodeRecheck.Choice choice)
    {
        // Check again here; an enabled button alone is not consent enforcement.
        if (choice == LedgerCodeRecheck.Choice.Cancel)
            return;
        Choice = choice;
        DialogResult = DialogResult.OK;
        Close();
    }
}
