using System.Drawing;
using System.Windows.Forms;

namespace OpenNest.Forms;

partial class PostVerificationForm
{
    private System.ComponentModel.IContainer components = null;
    private TableLayoutPanel layout;
    private Label summaryLabel;
    private TextBox reportBox;
    private CheckBox acknowledgeBox;
    private FlowLayoutPanel buttons;
    private Button postButton;
    private Button cancelButton;
    private bool disposedResources;

    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposedResources)
        {
            disposedResources = true;
            cancellation.Cancel();
            cancellation.Dispose();
            components?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        layout = new TableLayoutPanel();
        summaryLabel = new Label();
        reportBox = new TextBox();
        acknowledgeBox = new CheckBox();
        buttons = new FlowLayoutPanel();
        postButton = new Button();
        cancelButton = new Button();
        layout.SuspendLayout();
        buttons.SuspendLayout();
        SuspendLayout();

        layout.ColumnCount = 1;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowCount = 4;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Dock = DockStyle.Fill;
        layout.Padding = new Padding(12);
        layout.Controls.Add(summaryLabel, 0, 0);
        layout.Controls.Add(reportBox, 0, 1);
        layout.Controls.Add(acknowledgeBox, 0, 2);
        layout.Controls.Add(buttons, 0, 3);

        summaryLabel.Name = "summaryLabel";
        summaryLabel.AutoSize = true;
        summaryLabel.Dock = DockStyle.Fill;
        summaryLabel.Margin = new Padding(3, 3, 3, 10);
        summaryLabel.Text = "Checking all plates: overlaps, missing lead-ins, and rapid crossings...";
        summaryLabel.TabIndex = 0;

        reportBox.Name = "reportBox";
        reportBox.Dock = DockStyle.Fill;
        reportBox.Multiline = true;
        reportBox.ReadOnly = true;
        reportBox.ScrollBars = ScrollBars.Vertical;
        reportBox.WordWrap = true;
        reportBox.MaxLength = 0;
        reportBox.TabIndex = 1;
        reportBox.AccessibleName = "Pre-post verification findings";
        reportBox.Text = "Verification is running. No CNC output has been written.";

        acknowledgeBox.Name = "acknowledgeBox";
        acknowledgeBox.AutoSize = true;
        acknowledgeBox.Dock = DockStyle.Fill;
        acknowledgeBox.Margin = new Padding(3, 12, 3, 12);
        acknowledgeBox.Enabled = false;
        acknowledgeBox.Checked = false;
        acknowledgeBox.Text = "I understand the listed risks, including possible head crashes and machine or material damage.\r\nI choose to bypass these warnings and continue posting this nest.";
        acknowledgeBox.TabIndex = 2;
        acknowledgeBox.CheckedChanged += AcknowledgeBox_CheckedChanged;

        buttons.Dock = DockStyle.Fill;
        buttons.AutoSize = true;
        buttons.FlowDirection = FlowDirection.RightToLeft;
        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(postButton);
        buttons.TabIndex = 3;

        cancelButton.Name = "cancelButton";
        cancelButton.Text = "Cancel";
        cancelButton.AutoSize = true;
        cancelButton.DialogResult = DialogResult.Cancel;
        cancelButton.TabIndex = 0;

        postButton.Name = "postButton";
        postButton.Text = "Continue Posting";
        postButton.AutoSize = true;
        postButton.Enabled = false;
        postButton.TabIndex = 1;
        postButton.Click += PostButton_Click;

        AcceptButton = cancelButton;
        CancelButton = cancelButton;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(850, 560);
        MinimumSize = new Size(700, 440);
        Controls.Add(layout);
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "PostVerificationForm";
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Verify Nest Before Posting";
        layout.ResumeLayout(false);
        layout.PerformLayout();
        buttons.ResumeLayout(false);
        buttons.PerformLayout();
        ResumeLayout(false);
    }
}
