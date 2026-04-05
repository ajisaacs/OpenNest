namespace OpenNest.Forms
{
    partial class AutoNestForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.engineLabel = new System.Windows.Forms.Label();
            this.engineComboBox = new System.Windows.Forms.ComboBox();
            this.partsGroup = new System.Windows.Forms.GroupBox();
            this.partsGrid = new System.Windows.Forms.DataGridView();
            this.summaryLabel = new System.Windows.Forms.Label();
            this.optionsGroup = new System.Windows.Forms.GroupBox();
            this.createNewPlatesAsNeededBox = new System.Windows.Forms.CheckBox();
            this.plateOptimizerGroup = new System.Windows.Forms.GroupBox();
            this.optimizePlateSizeBox = new System.Windows.Forms.CheckBox();
            this.plateGrid = new System.Windows.Forms.DataGridView();
            this.salvageRateLabel = new System.Windows.Forms.Label();
            this.salvageRateBox = new System.Windows.Forms.TextBox();
            this.salvageRatePercentLabel = new System.Windows.Forms.Label();
            this.buttonPanel = new System.Windows.Forms.Panel();
            this.acceptButton = new System.Windows.Forms.Button();
            this.cancelButton = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.partsGrid)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.plateGrid)).BeginInit();
            this.partsGroup.SuspendLayout();
            this.optionsGroup.SuspendLayout();
            this.plateOptimizerGroup.SuspendLayout();
            this.buttonPanel.SuspendLayout();
            this.SuspendLayout();
            //
            // engineLabel
            //
            this.engineLabel.AutoSize = true;
            this.engineLabel.Location = new System.Drawing.Point(12, 15);
            this.engineLabel.Name = "engineLabel";
            this.engineLabel.Size = new System.Drawing.Size(82, 16);
            this.engineLabel.TabIndex = 0;
            this.engineLabel.Text = "Nest Engine:";
            //
            // engineComboBox
            //
            this.engineComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.engineComboBox.Location = new System.Drawing.Point(100, 12);
            this.engineComboBox.Name = "engineComboBox";
            this.engineComboBox.Size = new System.Drawing.Size(200, 24);
            this.engineComboBox.TabIndex = 1;
            //
            // partsGroup
            //
            this.partsGroup.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this.partsGroup.Controls.Add(this.partsGrid);
            this.partsGroup.Controls.Add(this.summaryLabel);
            this.partsGroup.Location = new System.Drawing.Point(12, 42);
            this.partsGroup.Name = "partsGroup";
            this.partsGroup.Size = new System.Drawing.Size(556, 210);
            this.partsGroup.TabIndex = 2;
            this.partsGroup.TabStop = false;
            this.partsGroup.Text = "Parts";
            //
            // partsGrid
            //
            this.partsGrid.AllowUserToAddRows = false;
            this.partsGrid.AllowUserToDeleteRows = false;
            this.partsGrid.AllowUserToResizeRows = false;
            this.partsGrid.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this.partsGrid.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.partsGrid.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.partsGrid.Location = new System.Drawing.Point(10, 22);
            this.partsGrid.Name = "partsGrid";
            this.partsGrid.RowHeadersVisible = false;
            this.partsGrid.AutoGenerateColumns = false;
            this.partsGrid.Size = new System.Drawing.Size(536, 160);
            this.partsGrid.TabIndex = 0;
            //
            // summaryLabel
            //
            this.summaryLabel.AutoSize = true;
            this.summaryLabel.ForeColor = System.Drawing.SystemColors.GrayText;
            this.summaryLabel.Location = new System.Drawing.Point(10, 188);
            this.summaryLabel.Name = "summaryLabel";
            this.summaryLabel.Size = new System.Drawing.Size(0, 16);
            this.summaryLabel.TabIndex = 1;
            //
            // optionsGroup
            //
            this.optionsGroup.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this.optionsGroup.Controls.Add(this.createNewPlatesAsNeededBox);
            this.optionsGroup.Location = new System.Drawing.Point(12, 258);
            this.optionsGroup.Name = "optionsGroup";
            this.optionsGroup.Size = new System.Drawing.Size(556, 48);
            this.optionsGroup.TabIndex = 3;
            this.optionsGroup.TabStop = false;
            this.optionsGroup.Text = "Options";
            //
            // createNewPlatesAsNeededBox
            //
            this.createNewPlatesAsNeededBox.AutoSize = true;
            this.createNewPlatesAsNeededBox.Location = new System.Drawing.Point(10, 22);
            this.createNewPlatesAsNeededBox.Name = "createNewPlatesAsNeededBox";
            this.createNewPlatesAsNeededBox.Size = new System.Drawing.Size(202, 20);
            this.createNewPlatesAsNeededBox.TabIndex = 0;
            this.createNewPlatesAsNeededBox.Text = "Create new plates as needed";
            this.createNewPlatesAsNeededBox.UseVisualStyleBackColor = true;
            //
            // plateOptimizerGroup
            //
            this.plateOptimizerGroup.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this.plateOptimizerGroup.Controls.Add(this.optimizePlateSizeBox);
            this.plateOptimizerGroup.Controls.Add(this.plateGrid);
            this.plateOptimizerGroup.Controls.Add(this.salvageRateLabel);
            this.plateOptimizerGroup.Controls.Add(this.salvageRateBox);
            this.plateOptimizerGroup.Controls.Add(this.salvageRatePercentLabel);
            this.plateOptimizerGroup.Location = new System.Drawing.Point(12, 312);
            this.plateOptimizerGroup.Name = "plateOptimizerGroup";
            this.plateOptimizerGroup.Size = new System.Drawing.Size(556, 188);
            this.plateOptimizerGroup.TabIndex = 4;
            this.plateOptimizerGroup.TabStop = false;
            this.plateOptimizerGroup.Text = "      Plate Optimizer";
            //
            // optimizePlateSizeBox
            //
            this.optimizePlateSizeBox.AutoSize = true;
            this.optimizePlateSizeBox.Location = new System.Drawing.Point(10, 0);
            this.optimizePlateSizeBox.Name = "optimizePlateSizeBox";
            this.optimizePlateSizeBox.Size = new System.Drawing.Size(15, 14);
            this.optimizePlateSizeBox.TabIndex = 0;
            this.optimizePlateSizeBox.UseVisualStyleBackColor = true;
            this.optimizePlateSizeBox.CheckedChanged += new System.EventHandler(this.optimizePlateSizeBox_CheckedChanged);
            //
            // plateGrid
            //
            this.plateGrid.AllowUserToResizeRows = false;
            this.plateGrid.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this.plateGrid.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.plateGrid.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.plateGrid.Location = new System.Drawing.Point(10, 22);
            this.plateGrid.Name = "plateGrid";
            this.plateGrid.RowHeadersVisible = false;
            this.plateGrid.AutoGenerateColumns = false;
            this.plateGrid.Size = new System.Drawing.Size(536, 130);
            this.plateGrid.TabIndex = 1;
            //
            // salvageRateLabel
            //
            this.salvageRateLabel.AutoSize = true;
            this.salvageRateLabel.Location = new System.Drawing.Point(10, 162);
            this.salvageRateLabel.Name = "salvageRateLabel";
            this.salvageRateLabel.Size = new System.Drawing.Size(96, 16);
            this.salvageRateLabel.TabIndex = 2;
            this.salvageRateLabel.Text = "Salvage Rate:";
            //
            // salvageRateBox
            //
            this.salvageRateBox.Location = new System.Drawing.Point(108, 159);
            this.salvageRateBox.Name = "salvageRateBox";
            this.salvageRateBox.Size = new System.Drawing.Size(50, 22);
            this.salvageRateBox.TabIndex = 3;
            this.salvageRateBox.Text = "50";
            //
            // salvageRatePercentLabel
            //
            this.salvageRatePercentLabel.AutoSize = true;
            this.salvageRatePercentLabel.Location = new System.Drawing.Point(160, 162);
            this.salvageRatePercentLabel.Name = "salvageRatePercentLabel";
            this.salvageRatePercentLabel.Size = new System.Drawing.Size(21, 16);
            this.salvageRatePercentLabel.TabIndex = 4;
            this.salvageRatePercentLabel.Text = "%";
            //
            // buttonPanel
            //
            this.buttonPanel.Controls.Add(this.acceptButton);
            this.buttonPanel.Controls.Add(this.cancelButton);
            this.buttonPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.buttonPanel.Location = new System.Drawing.Point(0, 506);
            this.buttonPanel.Name = "buttonPanel";
            this.buttonPanel.Size = new System.Drawing.Size(580, 50);
            this.buttonPanel.TabIndex = 5;
            //
            // acceptButton
            //
            this.acceptButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.acceptButton.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.acceptButton.Location = new System.Drawing.Point(376, 12);
            this.acceptButton.Name = "acceptButton";
            this.acceptButton.Size = new System.Drawing.Size(90, 28);
            this.acceptButton.TabIndex = 0;
            this.acceptButton.Text = "Accept";
            this.acceptButton.UseVisualStyleBackColor = true;
            //
            // cancelButton
            //
            this.cancelButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.cancelButton.Location = new System.Drawing.Point(474, 12);
            this.cancelButton.Name = "cancelButton";
            this.cancelButton.Size = new System.Drawing.Size(90, 28);
            this.cancelButton.TabIndex = 1;
            this.cancelButton.Text = "Cancel";
            this.cancelButton.UseVisualStyleBackColor = true;
            //
            // AutoNestForm
            //
            this.AcceptButton = this.acceptButton;
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.CancelButton = this.cancelButton;
            this.ClientSize = new System.Drawing.Size(580, 556);
            this.Controls.Add(this.engineLabel);
            this.Controls.Add(this.engineComboBox);
            this.Controls.Add(this.partsGroup);
            this.Controls.Add(this.optionsGroup);
            this.Controls.Add(this.plateOptimizerGroup);
            this.Controls.Add(this.buttonPanel);
            this.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "AutoNestForm";
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "AutoNest";
            ((System.ComponentModel.ISupportInitialize)(this.partsGrid)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.plateGrid)).EndInit();
            this.partsGroup.ResumeLayout(false);
            this.partsGroup.PerformLayout();
            this.optionsGroup.ResumeLayout(false);
            this.optionsGroup.PerformLayout();
            this.plateOptimizerGroup.ResumeLayout(false);
            this.plateOptimizerGroup.PerformLayout();
            this.buttonPanel.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Label engineLabel;
        private System.Windows.Forms.ComboBox engineComboBox;
        private System.Windows.Forms.GroupBox partsGroup;
        private System.Windows.Forms.DataGridView partsGrid;
        private System.Windows.Forms.Label summaryLabel;
        private System.Windows.Forms.GroupBox optionsGroup;
        private System.Windows.Forms.CheckBox createNewPlatesAsNeededBox;
        private System.Windows.Forms.GroupBox plateOptimizerGroup;
        private System.Windows.Forms.CheckBox optimizePlateSizeBox;
        private System.Windows.Forms.DataGridView plateGrid;
        private System.Windows.Forms.Label salvageRateLabel;
        private System.Windows.Forms.TextBox salvageRateBox;
        private System.Windows.Forms.Label salvageRatePercentLabel;
        private System.Windows.Forms.Panel buttonPanel;
        private System.Windows.Forms.Button acceptButton;
        private System.Windows.Forms.Button cancelButton;
    }
}
