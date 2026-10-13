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
        components = new System.ComponentModel.Container();
        layout = new TableLayoutPanel();
        sheetLabel = new Label();
        spacingPanel = new TableLayoutPanel();
        spacingLabel = new Label();
        spacingBox = new TextBox();
        minimumTailLabel = new Label();
        minimumTailBox = new TextBox();
        statusLabel = new Label();
        metricsPanel = new TableLayoutPanel();
        occupiedCaption = new Label();
        usedCaption = new Label();
        separatorCaption = new Label();
        tailCaption = new Label();
        occupiedLabel = new Label();
        usedLabel = new Label();
        separatorLabel = new Label();
        tailLabel = new Label();
        warningLabel = new Label();
        toolTip = new ToolTip(components);
        plateGrid = new ListView();
        plateColumn = new ColumnHeader();
        cutoffsColumn = new ColumnHeader();
        occupiedColumn = new ColumnHeader();
        usedColumn = new ColumnHeader();
        tailColumn = new ColumnHeader();
        statusColumn = new ColumnHeader();
        diagnosticsLabel = new Label();
        diagnosticsBox = new TextBox();
        buttonsPanel = new FlowLayoutPanel();
        previewButton = new Button();
        applyButton = new Button();
        cancelButton = new Button();
        layout.SuspendLayout();
        spacingPanel.SuspendLayout();
        metricsPanel.SuspendLayout();
        buttonsPanel.SuspendLayout();
        SuspendLayout();
        //
        // layout
        //
        layout.ColumnCount = 1;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.Controls.Add(sheetLabel, 0, 0);
        layout.Controls.Add(spacingPanel, 0, 1);
        layout.Controls.Add(statusLabel, 0, 2);
        layout.Controls.Add(metricsPanel, 0, 3);
        layout.Controls.Add(warningLabel, 0, 4);
        layout.Controls.Add(plateGrid, 0, 5);
        layout.Controls.Add(diagnosticsLabel, 0, 6);
        layout.Controls.Add(diagnosticsBox, 0, 7);
        layout.Controls.Add(buttonsPanel, 0, 8);
        layout.Dock = DockStyle.Fill;
        layout.Name = "layout";
        layout.RowCount = 9;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        plateGridRow = new RowStyle(SizeType.Absolute, 112F);
        layout.RowStyles.Add(plateGridRow);
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
        spacingPanel.Controls.Add(minimumTailLabel, 0, 1);
        spacingPanel.Controls.Add(minimumTailBox, 1, 1);
        spacingPanel.Dock = DockStyle.Fill;
        spacingPanel.Margin = new Padding(0, 0, 0, 6);
        spacingPanel.Name = "spacingPanel";
        spacingPanel.RowCount = 2;
        spacingPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
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
        spacingBox.TextChanged += SettingsBox_TextChanged;
        //
        // minimumTailLabel
        //
        minimumTailLabel.Anchor = AnchorStyles.Left;
        minimumTailLabel.AutoSize = true;
        minimumTailLabel.Name = "minimumTailLabel";
        minimumTailLabel.TabIndex = 2;
        minimumTailLabel.Text = "Minimum tail to keep:";
        //
        // minimumTailBox
        //
        minimumTailBox.Dock = DockStyle.Fill;
        minimumTailBox.Name = "minimumTailBox";
        minimumTailBox.TabIndex = 3;
        minimumTailBox.TextChanged += SettingsBox_TextChanged;
        //
        // statusLabel
        //
        statusLabel.AutoSize = true;
        statusLabel.Dock = DockStyle.Fill;
        statusLabel.Font = new Font("Segoe UI", 11.25F, FontStyle.Bold, GraphicsUnit.Point);
        statusLabel.Margin = new Padding(3, 0, 3, 8);
        statusLabel.Name = "statusLabel";
        statusLabel.TabIndex = 2;
        statusLabel.Text = "Preparing preview…";
        //
        // metricsPanel
        //
        metricsPanel.AutoSize = true;
        metricsPanel.ColumnCount = 4;
        metricsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        metricsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        metricsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        metricsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        metricsPanel.Controls.Add(occupiedCaption, 0, 0);
        metricsPanel.Controls.Add(usedCaption, 1, 0);
        metricsPanel.Controls.Add(separatorCaption, 2, 0);
        metricsPanel.Controls.Add(tailCaption, 3, 0);
        metricsPanel.Controls.Add(occupiedLabel, 0, 1);
        metricsPanel.Controls.Add(usedLabel, 1, 1);
        metricsPanel.Controls.Add(separatorLabel, 2, 1);
        metricsPanel.Controls.Add(tailLabel, 3, 1);
        metricsPanel.Dock = DockStyle.Fill;
        metricsPanel.Margin = new Padding(0, 0, 0, 6);
        metricsPanel.Name = "metricsPanel";
        metricsPanel.RowCount = 2;
        metricsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        metricsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        metricsPanel.TabIndex = 3;
        //
        // metric captions
        //
        occupiedCaption.AutoSize = true;
        occupiedCaption.Dock = DockStyle.Fill;
        occupiedCaption.ForeColor = SystemColors.GrayText;
        occupiedCaption.Margin = new Padding(3, 0, 6, 0);
        occupiedCaption.Name = "occupiedCaption";
        occupiedCaption.Text = "Occupied span";
        usedCaption.AutoSize = true;
        usedCaption.Dock = DockStyle.Fill;
        usedCaption.ForeColor = SystemColors.GrayText;
        usedCaption.Margin = new Padding(3, 0, 6, 0);
        usedCaption.Name = "usedCaption";
        usedCaption.Text = "Used span";
        separatorCaption.AutoSize = true;
        separatorCaption.Dock = DockStyle.Fill;
        separatorCaption.ForeColor = SystemColors.GrayText;
        separatorCaption.Margin = new Padding(3, 0, 6, 0);
        separatorCaption.Name = "separatorCaption";
        separatorCaption.Text = "Separator X";
        tailCaption.AutoSize = true;
        tailCaption.Dock = DockStyle.Fill;
        tailCaption.ForeColor = SystemColors.GrayText;
        tailCaption.Margin = new Padding(3, 0, 3, 0);
        tailCaption.Name = "tailCaption";
        tailCaption.Text = "Retained tail";
        //
        // metric values
        //
        var valueFont = new Font("Segoe UI", 9.75F, FontStyle.Bold, GraphicsUnit.Point);
        occupiedLabel.AutoSize = true;
        occupiedLabel.Dock = DockStyle.Fill;
        occupiedLabel.Font = valueFont;
        occupiedLabel.Margin = new Padding(3, 0, 6, 0);
        occupiedLabel.Name = "occupiedLabel";
        occupiedLabel.Text = "—";
        usedLabel.AutoSize = true;
        usedLabel.Dock = DockStyle.Fill;
        usedLabel.Font = valueFont;
        usedLabel.Margin = new Padding(3, 0, 6, 0);
        usedLabel.Name = "usedLabel";
        usedLabel.Text = "—";
        separatorLabel.AutoSize = true;
        separatorLabel.Dock = DockStyle.Fill;
        separatorLabel.Font = valueFont;
        separatorLabel.Margin = new Padding(3, 0, 6, 0);
        separatorLabel.Name = "separatorLabel";
        separatorLabel.Text = "—";
        tailLabel.AutoSize = true;
        tailLabel.Dock = DockStyle.Fill;
        tailLabel.Font = valueFont;
        tailLabel.Margin = new Padding(3, 0, 3, 0);
        tailLabel.Name = "tailLabel";
        tailLabel.Text = "—";
        //
        // warningLabel
        //
        warningLabel.AutoSize = true;
        warningLabel.Dock = DockStyle.Fill;
        warningLabel.ForeColor = WarningColor;
        warningLabel.Margin = new Padding(3, 0, 3, 8);
        warningLabel.Name = "warningLabel";
        warningLabel.TabIndex = 4;
        warningLabel.Text = "⚠ Nominal spacing is not the cut-line length — verify scrap separation and hopper fit.";
        toolTip.SetToolTip(warningLabel,
            "Nominal spacing differs from cut-line length. Clearances and omitted segments can leave bridges. "
            + "Confirm disconnected scrap and hopper fit before relying on it. Tail dimensions describe a proposed "
            + "program; verify the physical separation.");
        toolTip.SetToolTip(occupiedCaption, "Distance from the sheet origin to the furthest real material.");
        toolTip.SetToolTip(usedCaption, "Span through the proposed used area, from the sheet origin.");
        toolTip.SetToolTip(separatorCaption,
            "Signed X of the verified separator cut. A negative value means the separator is on the origin side.");
        toolTip.SetToolTip(tailCaption,
            "Length along X of the reusable tail beyond the verified separator, at full sheet width.");
        //
        // plateGrid
        //
        plateGrid.Columns.AddRange(new ColumnHeader[] {
            plateColumn, cutoffsColumn, occupiedColumn, usedColumn, tailColumn, statusColumn });
        plateGrid.Dock = DockStyle.Fill;
        plateGrid.FullRowSelect = true;
        plateGrid.GridLines = true;
        plateGrid.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        plateGrid.HideSelection = false;
        plateGrid.MultiSelect = false;
        plateGrid.Name = "plateGrid";
        plateGrid.TabIndex = 5;
        plateGrid.UseCompatibleStateImageBehavior = false;
        plateGrid.View = View.Details;
        plateColumn.Text = "Plate";
        plateColumn.Width = 80;
        cutoffsColumn.Text = "Cut-offs";
        cutoffsColumn.Width = 65;
        occupiedColumn.Text = "Occupied";
        occupiedColumn.Width = 90;
        usedColumn.Text = "Used";
        usedColumn.Width = 90;
        tailColumn.Text = "Tail";
        tailColumn.Width = 90;
        statusColumn.Text = "Status";
        statusColumn.Width = 150;
        //
        // diagnosticsLabel
        //
        diagnosticsLabel.AutoSize = true;
        diagnosticsLabel.Dock = DockStyle.Fill;
        diagnosticsLabel.Name = "diagnosticsLabel";
        diagnosticsLabel.TabIndex = 6;
        diagnosticsLabel.Text = "Details — issues and notices:";
        //
        // diagnosticsBox
        //
        diagnosticsBox.Dock = DockStyle.Fill;
        diagnosticsBox.Multiline = true;
        diagnosticsBox.Name = "diagnosticsBox";
        diagnosticsBox.ReadOnly = true;
        diagnosticsBox.ScrollBars = ScrollBars.Vertical;
        diagnosticsBox.TabIndex = 7;
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
        buttonsPanel.TabIndex = 8;
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
        ClientSize = new Size(660, 560);
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
        metricsPanel.ResumeLayout(false);
        metricsPanel.PerformLayout();
        buttonsPanel.ResumeLayout(false);
        buttonsPanel.PerformLayout();
        ResumeLayout(false);
    }

    private TableLayoutPanel layout;
    private RowStyle plateGridRow;
    private Label sheetLabel;
    private TableLayoutPanel spacingPanel;
    private Label spacingLabel;
    private TextBox spacingBox;
    private Label minimumTailLabel;
    private TextBox minimumTailBox;
    private Label statusLabel;
    private TableLayoutPanel metricsPanel;
    private Label occupiedCaption;
    private Label usedCaption;
    private Label separatorCaption;
    private Label tailCaption;
    private Label occupiedLabel;
    private Label usedLabel;
    private Label separatorLabel;
    private Label tailLabel;
    private Label warningLabel;
    private ToolTip toolTip;
    private ListView plateGrid;
    private ColumnHeader plateColumn;
    private ColumnHeader cutoffsColumn;
    private ColumnHeader occupiedColumn;
    private ColumnHeader usedColumn;
    private ColumnHeader tailColumn;
    private ColumnHeader statusColumn;
    private Label diagnosticsLabel;
    private TextBox diagnosticsBox;
    private FlowLayoutPanel buttonsPanel;
    private Button previewButton;
    private Button applyButton;
    private Button cancelButton;
}
