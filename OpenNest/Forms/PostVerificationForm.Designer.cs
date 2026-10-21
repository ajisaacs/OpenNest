using System.Drawing;
using System.Windows.Forms;

namespace OpenNest.Forms;

partial class PostVerificationForm
{
    private System.ComponentModel.IContainer components = null;
    private TableLayoutPanel banner;
    private Label statusIcon;
    private Label headlineLabel;
    private Label detailLabel;
    private ProgressBar progressBar;
    private TableLayoutPanel layout;
    private FlowLayoutPanel tilesPanel;
    private Panel findingsHost;
    private ListView findingsList;
    private ColumnHeader plateColumn;
    private ColumnHeader partColumn;
    private ColumnHeader otherPartColumn;
    private ColumnHeader messageColumn;
    private Label emptyLabel;
    private Label findingDetailLabel;
    private Label footnoteLabel;
    private Panel acknowledgePanel;
    private CheckBox acknowledgeBox;
    private TableLayoutPanel buttonRow;
    private Button copyButton;
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
        banner = new TableLayoutPanel();
        statusIcon = new Label();
        headlineLabel = new Label();
        detailLabel = new Label();
        progressBar = new ProgressBar();
        layout = new TableLayoutPanel();
        tilesPanel = new FlowLayoutPanel();
        findingsHost = new Panel();
        findingsList = new ListView();
        plateColumn = new ColumnHeader();
        partColumn = new ColumnHeader();
        otherPartColumn = new ColumnHeader();
        messageColumn = new ColumnHeader();
        emptyLabel = new Label();
        findingDetailLabel = new Label();
        footnoteLabel = new Label();
        acknowledgePanel = new Panel();
        acknowledgeBox = new CheckBox();
        buttonRow = new TableLayoutPanel();
        copyButton = new Button();
        postButton = new Button();
        cancelButton = new Button();
        banner.SuspendLayout();
        layout.SuspendLayout();
        findingsHost.SuspendLayout();
        acknowledgePanel.SuspendLayout();
        buttonRow.SuspendLayout();
        SuspendLayout();

        banner.Name = "banner";
        banner.Dock = DockStyle.Top;
        banner.AutoSize = true;
        banner.Padding = new Padding(16, 14, 16, 14);
        banner.ColumnCount = 2;
        banner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        banner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        banner.RowCount = 3;
        banner.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        banner.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        banner.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        banner.Controls.Add(statusIcon, 0, 0);
        banner.SetRowSpan(statusIcon, 3);
        banner.Controls.Add(headlineLabel, 1, 0);
        banner.Controls.Add(detailLabel, 1, 1);
        banner.Controls.Add(progressBar, 1, 2);

        statusIcon.Name = "statusIcon";
        statusIcon.AutoSize = true;
        statusIcon.Font = new Font("Segoe MDL2 Assets", 22F, FontStyle.Regular, GraphicsUnit.Point);
        statusIcon.Margin = new Padding(0, 2, 14, 0);
        statusIcon.AccessibleRole = AccessibleRole.Graphic;

        headlineLabel.Name = "headlineLabel";
        headlineLabel.AutoSize = true;
        headlineLabel.Dock = DockStyle.Fill;
        headlineLabel.Font = new Font("Segoe UI Semibold", 13F, FontStyle.Regular, GraphicsUnit.Point);
        headlineLabel.Margin = new Padding(0);

        detailLabel.Name = "detailLabel";
        detailLabel.AutoSize = true;
        detailLabel.Dock = DockStyle.Fill;
        detailLabel.Margin = new Padding(1, 4, 0, 0);

        progressBar.Name = "progressBar";
        progressBar.Style = ProgressBarStyle.Marquee;
        progressBar.MarqueeAnimationSpeed = 30;
        progressBar.Height = 6;
        progressBar.Dock = DockStyle.Fill;
        progressBar.Margin = new Padding(1, 10, 0, 0);

        layout.Name = "layout";
        layout.ColumnCount = 1;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowCount = 6;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Dock = DockStyle.Fill;
        layout.Padding = new Padding(12, 10, 12, 12);
        layout.Controls.Add(tilesPanel, 0, 0);
        layout.Controls.Add(findingsHost, 0, 1);
        layout.Controls.Add(findingDetailLabel, 0, 2);
        layout.Controls.Add(footnoteLabel, 0, 3);
        layout.Controls.Add(acknowledgePanel, 0, 4);
        layout.Controls.Add(buttonRow, 0, 5);

        tilesPanel.Name = "tilesPanel";
        tilesPanel.AutoSize = true;
        tilesPanel.Dock = DockStyle.Fill;
        tilesPanel.WrapContents = true;
        tilesPanel.Margin = new Padding(0, 0, 0, 6);

        findingsHost.Name = "findingsHost";
        findingsHost.Dock = DockStyle.Fill;
        findingsHost.Margin = new Padding(3);
        findingsHost.Controls.Add(findingsList);
        findingsHost.Controls.Add(emptyLabel);

        findingsList.Name = "findingsList";
        findingsList.Dock = DockStyle.Fill;
        findingsList.View = View.Details;
        findingsList.FullRowSelect = true;
        findingsList.MultiSelect = false;
        findingsList.HideSelection = false;
        findingsList.ShowGroups = true;
        findingsList.ShowItemToolTips = true;
        findingsList.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        findingsList.TabIndex = 0;
        findingsList.AccessibleName = "Pre-post verification findings";
        findingsList.Columns.AddRange(new[] { plateColumn, partColumn, otherPartColumn, messageColumn });
        findingsList.SelectedIndexChanged += FindingsList_SelectedIndexChanged;
        findingsList.Resize += FindingsList_Resize;

        plateColumn.Text = "Plate";
        plateColumn.Width = 60;
        partColumn.Text = "Part";
        partColumn.Width = 55;
        otherPartColumn.Text = "Other part";
        otherPartColumn.Width = 75;
        messageColumn.Text = "Finding";
        messageColumn.Width = 600;

        emptyLabel.Name = "emptyLabel";
        emptyLabel.Dock = DockStyle.Fill;
        emptyLabel.TextAlign = ContentAlignment.MiddleCenter;
        emptyLabel.ForeColor = Color.FromArgb(110, 110, 110);
        emptyLabel.BorderStyle = BorderStyle.FixedSingle;
        emptyLabel.BackColor = Color.FromArgb(250, 250, 250);
        emptyLabel.Visible = false;

        findingDetailLabel.Name = "findingDetailLabel";
        findingDetailLabel.AutoSize = true;
        findingDetailLabel.Dock = DockStyle.Fill;
        findingDetailLabel.BackColor = Color.FromArgb(245, 245, 245);
        findingDetailLabel.ForeColor = Color.FromArgb(51, 51, 51);
        findingDetailLabel.Padding = new Padding(8, 6, 8, 6);
        findingDetailLabel.Margin = new Padding(3, 0, 3, 6);
        findingDetailLabel.MinimumSize = new Size(0, 44);
        findingDetailLabel.Visible = false;

        footnoteLabel.Name = "footnoteLabel";
        footnoteLabel.AutoSize = true;
        footnoteLabel.Dock = DockStyle.Fill;
        footnoteLabel.Font = new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point);
        footnoteLabel.ForeColor = Color.FromArgb(110, 110, 110);
        footnoteLabel.Margin = new Padding(3, 2, 3, 8);
        footnoteLabel.Text = "These checks cover direct XY rapids in plate/program order; the post may change order, " +
            "routing or retracts. Inspect the posted machine program and machine setup before cutting, " +
            "and confirm actual contour gaps give adequate retention.";

        acknowledgePanel.Name = "acknowledgePanel";
        acknowledgePanel.AutoSize = true;
        acknowledgePanel.Dock = DockStyle.Fill;
        acknowledgePanel.Padding = new Padding(10, 8, 10, 8);
        acknowledgePanel.Margin = new Padding(3, 0, 3, 10);
        acknowledgePanel.BackColor = Color.FromArgb(255, 246, 224);
        acknowledgePanel.Visible = false;
        acknowledgePanel.Controls.Add(acknowledgeBox);

        acknowledgeBox.Name = "acknowledgeBox";
        acknowledgeBox.AutoSize = true;
        acknowledgeBox.Dock = DockStyle.Fill;
        acknowledgeBox.Enabled = false;
        acknowledgeBox.Checked = false;
        acknowledgeBox.ForeColor = Color.FromArgb(110, 60, 0);
        acknowledgeBox.Text = "I understand the listed risks, including possible head crashes and machine or material damage.\r\nI choose to bypass these warnings and continue posting this nest.";
        acknowledgeBox.TabIndex = 0;
        acknowledgeBox.CheckedChanged += AcknowledgeBox_CheckedChanged;

        buttonRow.Name = "buttonRow";
        buttonRow.AutoSize = true;
        buttonRow.Dock = DockStyle.Fill;
        buttonRow.Margin = new Padding(0);
        buttonRow.ColumnCount = 4;
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttonRow.RowCount = 1;
        buttonRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        buttonRow.Controls.Add(copyButton, 0, 0);
        buttonRow.Controls.Add(postButton, 2, 0);
        buttonRow.Controls.Add(cancelButton, 3, 0);

        copyButton.Name = "copyButton";
        copyButton.Text = "Copy Report";
        copyButton.AutoSize = true;
        copyButton.MinimumSize = new Size(100, 30);
        copyButton.Enabled = false;
        copyButton.TabIndex = 0;
        copyButton.Click += CopyButton_Click;

        postButton.Name = "postButton";
        postButton.Text = "Continue Posting";
        postButton.AutoSize = true;
        postButton.MinimumSize = new Size(120, 30);
        postButton.Enabled = false;
        postButton.TabIndex = 1;
        postButton.Click += PostButton_Click;

        cancelButton.Name = "cancelButton";
        cancelButton.Text = "Cancel";
        cancelButton.AutoSize = true;
        cancelButton.MinimumSize = new Size(100, 30);
        cancelButton.DialogResult = DialogResult.Cancel;
        cancelButton.TabIndex = 2;

        AcceptButton = cancelButton;
        CancelButton = cancelButton;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        BackColor = Color.White;
        ClientSize = new Size(880, 600);
        MinimumSize = new Size(720, 480);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        // Fill first, then the top banner, so the banner docks ahead of the content.
        Controls.Add(layout);
        Controls.Add(banner);
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "PostVerificationForm";
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Verify Nest Before Posting";
        banner.ResumeLayout(false);
        banner.PerformLayout();
        findingsHost.ResumeLayout(false);
        acknowledgePanel.ResumeLayout(false);
        acknowledgePanel.PerformLayout();
        buttonRow.ResumeLayout(false);
        buttonRow.PerformLayout();
        layout.ResumeLayout(false);
        layout.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }
}
