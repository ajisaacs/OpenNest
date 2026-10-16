using System;
using System.Drawing;
using System.Windows.Forms;

namespace OpenNest.Forms;

/// <summary>
/// Database-mode Save As: asks for the name of the new nest copy, since there is no
/// file dialog to supply one.
/// </summary>
public sealed class NestNameForm : Form
{
    private readonly TextBox nameBox;

    public NestNameForm(string currentName)
    {
        Text = "Save As";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(380, 110);
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;

        var label = new Label
        {
            Text = "New nest name:",
            AutoSize = true,
            Location = new Point(12, 16),
        };
        nameBox = new TextBox
        {
            Location = new Point(12, 36),
            Width = 356,
            Text = currentName ?? "",
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(6),
        };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        var ok = new Button { Text = "Save", AutoSize = true };
        ok.Click += Ok_Click;
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        Controls.Add(nameBox);
        Controls.Add(label);
        Controls.Add(buttons);

        AcceptButton = ok;
        CancelButton = cancel;

        Shown += (_, _) => nameBox.SelectAll();
    }

    public string NestName => nameBox.Text.Trim();

    private void Ok_Click(object sender, EventArgs e)
    {
        if (NestName.Length == 0)
        {
            MessageBox.Show(this, "Enter a name for the new nest.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            nameBox.Focus();
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
