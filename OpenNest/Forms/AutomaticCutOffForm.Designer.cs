using System.Drawing;
using System.Windows.Forms;

namespace OpenNest.Forms;

partial class AutomaticCutOffForm
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ClearPreview();
            components?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        layout = new TableLayoutPanel();
        sheetLabel = new Label();
        spacingPanel = new TableLayoutPanel();
        spacingLabel = new Label();
        spacingBox = new TextBox();
        warningLabel = new Label();
        occupiedLabel = new Label();
        usedLabel = new Label();
        separatorLabel = new Label();
        tailLabel = new Label();
        diagnosticsLabel = new Label();
        diagnosticsBox = new TextBox();
        buttonsPanel = new FlowLayoutPanel();
        previewButton = new Button();
        applyButton = new Button();
        cancelButton = new Button();
        layout.SuspendLayout();
        spacingPanel.SuspendLayout();
        buttonsPanel.SuspendLayout();
        SuspendLayout();
        //
        // layout
        //
        layout.ColumnCount = 1;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.Controls.Add(sheetLabel, 0, 0);
        layout.Controls.Add(spacingPanel, 0, 1);
        layout.Controls.Add(warningLabel, 0, 2);
        layout.Controls.Add(occupiedLabel, 0, 3);
        layout.Controls.Add(usedLabel, 0, 4);
        layout.Controls.Add(separatorLabel, 0, 5);
        layout.Controls.Add(tailLabel, 0, 6);
        layout.Controls.Add(diagnosticsLabel, 0, 7);
        layout.Controls.Add(diagnosticsBox, 0, 8);
        layout.Controls.Add(buttonsPanel, 0, 9);
        layout.Dock = DockStyle.Fill;
        layout.Name = "layout";
        layout.RowCount = 10;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.TabIndex = 0;
        //
        // sheetLabel
        //
        sheetLabel.AutoSize = true;
        sheetLabel.Dock = DockStyle.Fill;
        sheetLabel.Margin = new Padding(3, 3, 3, 8);
        sheetLabel.Name = "sheetLabel";
        sheetLabel.TabIndex = 0;
        sheetLabel.Text = "Sheet:";
        //
        // spacingPanel
        //
        spacingPanel.AutoSize = true;
        spacingPanel.ColumnCount = 2;
        spacingPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        spacingPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        spacingPanel.Controls.Add(spacingLabel, 0, 0);
        spacingPanel.Controls.Add(spacingBox, 1, 0);
        spacingPanel.Dock = DockStyle.Fill;
        spacingPanel.Margin = new Padding(0, 0, 0, 6);
        spacingPanel.Name = "spacingPanel";
        spacingPanel.RowCount = 1;
        spacingPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        spacingPanel.TabIndex = 1;
        //
        // spacingLabel
        //
        spacingLabel.Anchor = AnchorStyles.Left;
        spacingLabel.AutoSize = true;
        spacingLabel.Name = "spacingLabel";
        spacingLabel.TabIndex = 0;
        spacingLabel.Text = "Nominal spacing along sheet length:";
        //
        // spacingBox
        //
        spacingBox.Dock = DockStyle.Fill;
        spacingBox.Name = "spacingBox";
        spacingBox.TabIndex = 1;
        spacingBox.TextChanged += SpacingBox_TextChanged;
        //
        // warningLabel
        //
        warningLabel.AutoSize = true;
        warningLabel.Dock = DockStyle.Fill;
        warningLabel.Margin = new Padding(3, 3, 3, 10);
        warningLabel.Name = "warningLabel";
        warningLabel.TabIndex = 2;
        warningLabel.Text = "Nominal spacing is not cut-line length. Clearances and omitted segments can leave bridges. "
            + "This does not certify disconnected scrap or hopper fit. Tail dimensions describe a proposed program, not a physical separation.";
        //
        // occupiedLabel
        //
        occupiedLabel.AutoSize = true;
        occupiedLabel.Dock = DockStyle.Fill;
        occupiedLabel.Margin = new Padding(3, 3, 3, 5);
        occupiedLabel.Name = "occupiedLabel";
        occupiedLabel.TabIndex = 3;
        occupiedLabel.Text = "Occupied span from origin: —";
        //
        // usedLabel
        //
        usedLabel.AutoSize = true;
        usedLabel.Dock = DockStyle.Fill;
        usedLabel.Margin = new Padding(3, 3, 3, 5);
        usedLabel.Name = "usedLabel";
        usedLabel.TabIndex = 4;
        usedLabel.Text = "Proposed used span from origin: —";
        //
        // separatorLabel
        //
        separatorLabel.AutoSize = true;
        separatorLabel.Dock = DockStyle.Fill;
        separatorLabel.Margin = new Padding(3, 3, 3, 5);
        separatorLabel.Name = "separatorLabel";
        separatorLabel.TabIndex = 5;
        separatorLabel.Text = "Verified separator program: —";
        //
        // tailLabel
        //
        tailLabel.AutoSize = true;
        tailLabel.Dock = DockStyle.Fill;
        tailLabel.Margin = new Padding(3, 3, 3, 8);
        tailLabel.Name = "tailLabel";
        tailLabel.TabIndex = 6;
        tailLabel.Text = "Proposed retained tail: —";
        //
        // diagnosticsLabel
        //
        diagnosticsLabel.AutoSize = true;
        diagnosticsLabel.Dock = DockStyle.Fill;
        diagnosticsLabel.Name = "diagnosticsLabel";
        diagnosticsLabel.TabIndex = 7;
        diagnosticsLabel.Text = "Diagnostics / plan status:";
        //
        // diagnosticsBox
        //
        diagnosticsBox.Dock = DockStyle.Fill;
        diagnosticsBox.Multiline = true;
        diagnosticsBox.Name = "diagnosticsBox";
        diagnosticsBox.ReadOnly = true;
        diagnosticsBox.ScrollBars = ScrollBars.Vertical;
        diagnosticsBox.TabIndex = 8;
        //
        // buttonsPanel
        //
        buttonsPanel.AutoSize = true;
        buttonsPanel.Controls.Add(cancelButton);
        buttonsPanel.Controls.Add(applyButton);
        buttonsPanel.Controls.Add(previewButton);
        buttonsPanel.Dock = DockStyle.Fill;
        buttonsPanel.FlowDirection = FlowDirection.RightToLeft;
        buttonsPanel.Margin = new Padding(0, 8, 0, 0);
        buttonsPanel.Name = "buttonsPanel";
        buttonsPanel.TabIndex = 9;
        buttonsPanel.WrapContents = false;
        //
        // previewButton
        //
        previewButton.AutoSize = true;
        previewButton.Name = "previewButton";
        previewButton.Size = new Size(85, 30);
        previewButton.TabIndex = 0;
        previewButton.Text = "&Preview";
        previewButton.UseVisualStyleBackColor = true;
        previewButton.Click += PreviewButton_Click;
        //
        // applyButton
        //
        applyButton.AutoSize = true;
        applyButton.Enabled = false;
        applyButton.Name = "applyButton";
        applyButton.Size = new Size(85, 30);
        applyButton.TabIndex = 1;
        applyButton.Text = "&Apply";
        applyButton.UseVisualStyleBackColor = true;
        applyButton.Click += ApplyButton_Click;
        //
        // cancelButton
        //
        cancelButton.AutoSize = true;
        cancelButton.DialogResult = DialogResult.Cancel;
        cancelButton.Name = "cancelButton";
        cancelButton.Size = new Size(85, 30);
        cancelButton.TabIndex = 2;
        cancelButton.Text = "Cancel";
        cancelButton.UseVisualStyleBackColor = true;
        //
        // AutomaticCutOffForm
        //
        AcceptButton = previewButton;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = cancelButton;
        ClientSize = new Size(600, 470);
        Controls.Add(layout);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "AutomaticCutOffForm";
        Padding = new Padding(12);
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Automatic Scrap Cutoffs";
        layout.ResumeLayout(false);
        layout.PerformLayout();
        spacingPanel.ResumeLayout(false);
        spacingPanel.PerformLayout();
        buttonsPanel.ResumeLayout(false);
        buttonsPanel.PerformLayout();
        ResumeLayout(false);
    }

    private TableLayoutPanel layout;
    private Label sheetLabel;
    private TableLayoutPanel spacingPanel;
    private Label spacingLabel;
    private TextBox spacingBox;
    private Label warningLabel;
    private Label occupiedLabel;
    private Label usedLabel;
    private Label separatorLabel;
    private Label tailLabel;
    private Label diagnosticsLabel;
    private TextBox diagnosticsBox;
    private FlowLayoutPanel buttonsPanel;
    private Button previewButton;
    private Button applyButton;
    private Button cancelButton;
}
