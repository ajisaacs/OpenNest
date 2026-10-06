using System.Drawing;
using System.Windows.Forms;

namespace OpenNest.Forms;

partial class CuttingPlanForm
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            planning?.Cancel();
            components?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        layout = new TableLayoutPanel();
        settingsPanel = new TableLayoutPanel();
        settingsLabel = new Label();
        settingsButton = new Button();
        optionsPanel = new FlowLayoutPanel();
        keepOrderCheckBox = new CheckBox();
        planButton = new Button();
        split = new SplitContainer();
        previewLayout = new TableLayoutPanel();
        previewLabel = new Label();
        previewPanel = new Panel();
        summaryBox = new TextBox();
        statusLabel = new Label();
        buttonsPanel = new FlowLayoutPanel();
        applyButton = new Button();
        cancelButton = new Button();
        layout.SuspendLayout();
        settingsPanel.SuspendLayout();
        optionsPanel.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)split).BeginInit();
        split.Panel1.SuspendLayout();
        split.Panel2.SuspendLayout();
        split.SuspendLayout();
        previewLayout.SuspendLayout();
        buttonsPanel.SuspendLayout();
        SuspendLayout();
        //
        // layout
        //
        layout.ColumnCount = 1;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.Controls.Add(settingsPanel, 0, 0);
        layout.Controls.Add(optionsPanel, 0, 1);
        layout.Controls.Add(split, 0, 2);
        layout.Controls.Add(statusLabel, 0, 3);
        layout.Controls.Add(buttonsPanel, 0, 4);
        layout.Dock = DockStyle.Fill;
        layout.Name = "layout";
        layout.RowCount = 5;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.TabIndex = 0;
        //
        // settingsPanel
        //
        settingsPanel.AutoSize = true;
        settingsPanel.ColumnCount = 2;
        settingsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        settingsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        settingsPanel.Controls.Add(settingsLabel, 0, 0);
        settingsPanel.Controls.Add(settingsButton, 1, 0);
        settingsPanel.Dock = DockStyle.Fill;
        settingsPanel.Margin = new Padding(0, 0, 0, 4);
        settingsPanel.Name = "settingsPanel";
        settingsPanel.RowCount = 1;
        settingsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        settingsPanel.TabIndex = 0;
        //
        // settingsLabel
        //
        settingsLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        settingsLabel.AutoSize = true;
        settingsLabel.Name = "settingsLabel";
        settingsLabel.TabIndex = 0;
        settingsLabel.Text = "Lead-ins:";
        //
        // settingsButton
        //
        settingsButton.AutoSize = true;
        settingsButton.Name = "settingsButton";
        settingsButton.Size = new Size(130, 30);
        settingsButton.TabIndex = 1;
        settingsButton.Text = "Cutting &Settings...";
        settingsButton.UseVisualStyleBackColor = true;
        settingsButton.Click += SettingsButton_Click;
        //
        // optionsPanel
        //
        optionsPanel.AutoSize = true;
        optionsPanel.Controls.Add(keepOrderCheckBox);
        optionsPanel.Controls.Add(planButton);
        optionsPanel.Dock = DockStyle.Fill;
        optionsPanel.Margin = new Padding(0, 0, 0, 4);
        optionsPanel.Name = "optionsPanel";
        optionsPanel.TabIndex = 1;
        optionsPanel.WrapContents = false;
        //
        // keepOrderCheckBox
        //
        keepOrderCheckBox.Anchor = AnchorStyles.Left;
        keepOrderCheckBox.AutoSize = true;
        keepOrderCheckBox.Margin = new Padding(3, 3, 12, 3);
        keepOrderCheckBox.Name = "keepOrderCheckBox";
        keepOrderCheckBox.TabIndex = 0;
        keepOrderCheckBox.Text = "&Keep the current part order";
        keepOrderCheckBox.UseVisualStyleBackColor = true;
        keepOrderCheckBox.CheckedChanged += KeepOrderCheckBox_CheckedChanged;
        //
        // planButton
        //
        planButton.AutoSize = true;
        planButton.Name = "planButton";
        planButton.Size = new Size(85, 30);
        planButton.TabIndex = 1;
        planButton.Text = "&Replan";
        planButton.UseVisualStyleBackColor = true;
        planButton.Click += PlanButton_Click;
        //
        // split
        //
        split.Dock = DockStyle.Fill;
        split.Name = "split";
        split.TabIndex = 2;
        //
        // split.Panel1
        //
        split.Panel1.Controls.Add(previewLayout);
        //
        // split.Panel2
        //
        split.Panel2.Controls.Add(summaryBox);
        //
        // previewLayout
        //
        previewLayout.ColumnCount = 1;
        previewLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        previewLayout.Controls.Add(previewLabel, 0, 0);
        previewLayout.Controls.Add(previewPanel, 0, 1);
        previewLayout.Dock = DockStyle.Fill;
        previewLayout.Margin = new Padding(0);
        previewLayout.Name = "previewLayout";
        previewLayout.RowCount = 2;
        previewLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        previewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        previewLayout.TabIndex = 0;
        //
        // previewLabel
        //
        previewLabel.AutoSize = true;
        previewLabel.Dock = DockStyle.Fill;
        previewLabel.Name = "previewLabel";
        previewLabel.TabIndex = 0;
        previewLabel.Text = "Preview:";
        //
        // previewPanel
        //
        previewPanel.BorderStyle = BorderStyle.FixedSingle;
        previewPanel.Dock = DockStyle.Fill;
        previewPanel.Name = "previewPanel";
        previewPanel.TabIndex = 1;
        //
        // summaryBox
        //
        summaryBox.Dock = DockStyle.Fill;
        summaryBox.Multiline = true;
        summaryBox.Name = "summaryBox";
        summaryBox.ReadOnly = true;
        summaryBox.ScrollBars = ScrollBars.Vertical;
        summaryBox.TabIndex = 0;
        //
        // statusLabel
        //
        statusLabel.AutoSize = true;
        statusLabel.Dock = DockStyle.Fill;
        statusLabel.Margin = new Padding(3, 6, 3, 0);
        statusLabel.Name = "statusLabel";
        statusLabel.TabIndex = 3;
        //
        // buttonsPanel
        //
        buttonsPanel.AutoSize = true;
        buttonsPanel.Controls.Add(cancelButton);
        buttonsPanel.Controls.Add(applyButton);
        buttonsPanel.Dock = DockStyle.Fill;
        buttonsPanel.FlowDirection = FlowDirection.RightToLeft;
        buttonsPanel.Margin = new Padding(0, 8, 0, 0);
        buttonsPanel.Name = "buttonsPanel";
        buttonsPanel.TabIndex = 4;
        buttonsPanel.WrapContents = false;
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
        cancelButton.TabIndex = 0;
        cancelButton.Text = "Cancel";
        cancelButton.UseVisualStyleBackColor = true;
        //
        // CuttingPlanForm
        //
        AcceptButton = planButton;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = cancelButton;
        ClientSize = new Size(940, 620);
        Controls.Add(layout);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        MinimizeBox = false;
        MinimumSize = new Size(700, 480);
        Name = "CuttingPlanForm";
        Padding = new Padding(12);
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Plan Cutting";
        layout.ResumeLayout(false);
        layout.PerformLayout();
        settingsPanel.ResumeLayout(false);
        settingsPanel.PerformLayout();
        optionsPanel.ResumeLayout(false);
        optionsPanel.PerformLayout();
        split.Panel1.ResumeLayout(false);
        split.Panel2.ResumeLayout(false);
        split.Panel2.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)split).EndInit();
        split.ResumeLayout(false);
        previewLayout.ResumeLayout(false);
        previewLayout.PerformLayout();
        buttonsPanel.ResumeLayout(false);
        buttonsPanel.PerformLayout();
        ResumeLayout(false);
    }

    private TableLayoutPanel layout;
    private TableLayoutPanel settingsPanel;
    private Label settingsLabel;
    private Button settingsButton;
    private FlowLayoutPanel optionsPanel;
    private CheckBox keepOrderCheckBox;
    private Button planButton;
    private SplitContainer split;
    private TableLayoutPanel previewLayout;
    private Label previewLabel;
    private Panel previewPanel;
    private TextBox summaryBox;
    private Label statusLabel;
    private FlowLayoutPanel buttonsPanel;
    private Button applyButton;
    private Button cancelButton;
}
