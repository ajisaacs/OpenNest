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
            this.tabControl = new System.Windows.Forms.TabControl();
            this.partsTab = new System.Windows.Forms.TabPage();
            this.platesTab = new System.Windows.Forms.TabPage();
            this.partsGrid = new System.Windows.Forms.DataGridView();
            this.summaryLabel = new System.Windows.Forms.Label();
            this.engineLabel = new System.Windows.Forms.Label();
            this.engineComboBox = new System.Windows.Forms.ComboBox();
            this.createNewPlatesAsNeededBox = new System.Windows.Forms.CheckBox();
            this.partFirstGroup = new System.Windows.Forms.GroupBox();
            this.partFirstCheckBox = new System.Windows.Forms.CheckBox();
            this.sortOrderLabel = new System.Windows.Forms.Label();
            this.sortOrderComboBox = new System.Windows.Forms.ComboBox();
            this.minRemnantLabel = new System.Windows.Forms.Label();
            this.minRemnantBox = new System.Windows.Forms.TextBox();
            this.plateOptimizerGroup = new System.Windows.Forms.GroupBox();
            this.optimizePlateSizeBox = new System.Windows.Forms.CheckBox();
            this.plateGrid = new System.Windows.Forms.DataGridView();
            this.salvageRateLabel = new System.Windows.Forms.Label();
            this.salvageRateBox = new System.Windows.Forms.TextBox();
            this.salvageRatePercentLabel = new System.Windows.Forms.Label();
            this.buttonPanel = new System.Windows.Forms.Panel();
            this.acceptButton = new System.Windows.Forms.Button();
            this.cancelButton = new System.Windows.Forms.Button();
            this.tabControl.SuspendLayout();
            this.partsTab.SuspendLayout();
            this.platesTab.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.partsGrid)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.plateGrid)).BeginInit();
            this.partFirstGroup.SuspendLayout();
            this.plateOptimizerGroup.SuspendLayout();
            this.buttonPanel.SuspendLayout();
            this.SuspendLayout();
            //
            // tabControl
            //
            this.tabControl.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this.tabControl.Controls.Add(this.partsTab);
            this.tabControl.Controls.Add(this.platesTab);
            this.tabControl.Location = new System.Drawing.Point(12, 12);
            this.tabControl.Name = "tabControl";
            this.tabControl.SelectedIndex = 0;
            this.tabControl.Size = new System.Drawing.Size(556, 490);
            this.tabControl.TabIndex = 0;
            //
            // partsTab
            //
            this.partsTab.Controls.Add(this.partsGrid);
            this.partsTab.Controls.Add(this.summaryLabel);
            this.partsTab.Location = new System.Drawing.Point(4, 25);
            this.partsTab.Name = "partsTab";
            this.partsTab.Padding = new System.Windows.Forms.Padding(6);
            this.partsTab.Size = new System.Drawing.Size(548, 461);
            this.partsTab.TabIndex = 0;
            this.partsTab.Text = "Parts";
            this.partsTab.UseVisualStyleBackColor = true;
            //
            // platesTab
            //
            this.platesTab.Controls.Add(this.engineLabel);
            this.platesTab.Controls.Add(this.engineComboBox);
            this.platesTab.Controls.Add(this.createNewPlatesAsNeededBox);
            this.platesTab.Controls.Add(this.partFirstGroup);
            this.platesTab.Controls.Add(this.plateOptimizerGroup);
            this.platesTab.Location = new System.Drawing.Point(4, 25);
            this.platesTab.Name = "platesTab";
            this.platesTab.Padding = new System.Windows.Forms.Padding(6);
            this.platesTab.Size = new System.Drawing.Size(548, 461);
            this.platesTab.TabIndex = 1;
            this.platesTab.Text = "Plates";
            this.platesTab.UseVisualStyleBackColor = true;
            //
            // partsGrid
            //
            this.partsGrid.AllowUserToAddRows = false;
            this.partsGrid.AllowUserToDeleteRows = false;
            this.partsGrid.AllowUserToResizeRows = false;
            this.partsGrid.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this.partsGrid.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.partsGrid.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.partsGrid.Location = new System.Drawing.Point(10, 10);
            this.partsGrid.Name = "partsGrid";
            this.partsGrid.RowHeadersVisible = false;
            this.partsGrid.AutoGenerateColumns = false;
            this.partsGrid.Size = new System.Drawing.Size(528, 420);
            this.partsGrid.TabIndex = 0;
            //
            // summaryLabel
            //
            this.summaryLabel.AutoSize = true;
            this.summaryLabel.ForeColor = System.Drawing.SystemColors.GrayText;
            this.summaryLabel.Location = new System.Drawing.Point(10, 436);
            this.summaryLabel.Name = "summaryLabel";
            this.summaryLabel.Size = new System.Drawing.Size(0, 16);
            this.summaryLabel.TabIndex = 1;
            //
            // engineLabel
            //
            this.engineLabel.AutoSize = true;
            this.engineLabel.Location = new System.Drawing.Point(10, 15);
            this.engineLabel.Name = "engineLabel";
            this.engineLabel.Size = new System.Drawing.Size(82, 16);
            this.engineLabel.TabIndex = 0;
            this.engineLabel.Text = "Nest Engine:";
            //
            // engineComboBox
            //
            this.engineComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.engineComboBox.Location = new System.Drawing.Point(98, 12);
            this.engineComboBox.Name = "engineComboBox";
            this.engineComboBox.Size = new System.Drawing.Size(200, 24);
            this.engineComboBox.TabIndex = 1;
            //
            // createNewPlatesAsNeededBox
            //
            this.createNewPlatesAsNeededBox.AutoSize = true;
            this.createNewPlatesAsNeededBox.Location = new System.Drawing.Point(10, 44);
            this.createNewPlatesAsNeededBox.Name = "createNewPlatesAsNeededBox";
            this.createNewPlatesAsNeededBox.Size = new System.Drawing.Size(202, 20);
            this.createNewPlatesAsNeededBox.TabIndex = 2;
            this.createNewPlatesAsNeededBox.Text = "Create new plates as needed";
            this.createNewPlatesAsNeededBox.UseVisualStyleBackColor = true;
            //
            // partFirstGroup
            //
            this.partFirstGroup.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this.partFirstGroup.Controls.Add(this.partFirstCheckBox);
            this.partFirstGroup.Controls.Add(this.sortOrderLabel);
            this.partFirstGroup.Controls.Add(this.sortOrderComboBox);
            this.partFirstGroup.Controls.Add(this.minRemnantLabel);
            this.partFirstGroup.Controls.Add(this.minRemnantBox);
            this.partFirstGroup.Location = new System.Drawing.Point(10, 72);
            this.partFirstGroup.Name = "partFirstGroup";
            this.partFirstGroup.Size = new System.Drawing.Size(528, 80);
            this.partFirstGroup.TabIndex = 3;
            this.partFirstGroup.TabStop = false;
            this.partFirstGroup.Text = "      Part-First Mode";
            //
            // partFirstCheckBox
            //
            this.partFirstCheckBox.AutoSize = true;
            this.partFirstCheckBox.Location = new System.Drawing.Point(10, 0);
            this.partFirstCheckBox.Name = "partFirstCheckBox";
            this.partFirstCheckBox.Size = new System.Drawing.Size(15, 14);
            this.partFirstCheckBox.TabIndex = 0;
            this.partFirstCheckBox.UseVisualStyleBackColor = true;
            this.partFirstCheckBox.CheckedChanged += new System.EventHandler(this.partFirstCheckBox_CheckedChanged);
            //
            // sortOrderLabel
            //
            this.sortOrderLabel.AutoSize = true;
            this.sortOrderLabel.Location = new System.Drawing.Point(10, 26);
            this.sortOrderLabel.Name = "sortOrderLabel";
            this.sortOrderLabel.Size = new System.Drawing.Size(75, 16);
            this.sortOrderLabel.TabIndex = 1;
            this.sortOrderLabel.Text = "Sort Order:";
            //
            // sortOrderComboBox
            //
            this.sortOrderComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.sortOrderComboBox.Location = new System.Drawing.Point(100, 23);
            this.sortOrderComboBox.Name = "sortOrderComboBox";
            this.sortOrderComboBox.Size = new System.Drawing.Size(180, 24);
            this.sortOrderComboBox.TabIndex = 2;
            //
            // minRemnantLabel
            //
            this.minRemnantLabel.AutoSize = true;
            this.minRemnantLabel.Location = new System.Drawing.Point(10, 54);
            this.minRemnantLabel.Name = "minRemnantLabel";
            this.minRemnantLabel.Size = new System.Drawing.Size(117, 16);
            this.minRemnantLabel.TabIndex = 3;
            this.minRemnantLabel.Text = "Min Remnant Size:";
            //
            // minRemnantBox
            //
            this.minRemnantBox.Location = new System.Drawing.Point(133, 51);
            this.minRemnantBox.Name = "minRemnantBox";
            this.minRemnantBox.Size = new System.Drawing.Size(60, 22);
            this.minRemnantBox.TabIndex = 4;
            this.minRemnantBox.Text = "12";
            //
            // plateOptimizerGroup
            //
            this.plateOptimizerGroup.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this.plateOptimizerGroup.Controls.Add(this.optimizePlateSizeBox);
            this.plateOptimizerGroup.Controls.Add(this.plateGrid);
            this.plateOptimizerGroup.Controls.Add(this.salvageRateLabel);
            this.plateOptimizerGroup.Controls.Add(this.salvageRateBox);
            this.plateOptimizerGroup.Controls.Add(this.salvageRatePercentLabel);
            this.plateOptimizerGroup.Location = new System.Drawing.Point(10, 158);
            this.plateOptimizerGroup.Name = "plateOptimizerGroup";
            this.plateOptimizerGroup.Size = new System.Drawing.Size(528, 188);
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
            this.plateGrid.Size = new System.Drawing.Size(508, 130);
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
            this.buttonPanel.TabIndex = 1;
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
            this.Controls.Add(this.tabControl);
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
            this.tabControl.ResumeLayout(false);
            this.partsTab.ResumeLayout(false);
            this.partsTab.PerformLayout();
            this.platesTab.ResumeLayout(false);
            this.platesTab.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.partsGrid)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.plateGrid)).EndInit();
            this.partFirstGroup.ResumeLayout(false);
            this.partFirstGroup.PerformLayout();
            this.plateOptimizerGroup.ResumeLayout(false);
            this.plateOptimizerGroup.PerformLayout();
            this.buttonPanel.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.TabControl tabControl;
        private System.Windows.Forms.TabPage partsTab;
        private System.Windows.Forms.TabPage platesTab;
        private System.Windows.Forms.DataGridView partsGrid;
        private System.Windows.Forms.Label summaryLabel;
        private System.Windows.Forms.Label engineLabel;
        private System.Windows.Forms.ComboBox engineComboBox;
        private System.Windows.Forms.CheckBox createNewPlatesAsNeededBox;
        private System.Windows.Forms.GroupBox partFirstGroup;
        private System.Windows.Forms.CheckBox partFirstCheckBox;
        private System.Windows.Forms.Label sortOrderLabel;
        private System.Windows.Forms.ComboBox sortOrderComboBox;
        private System.Windows.Forms.Label minRemnantLabel;
        private System.Windows.Forms.TextBox minRemnantBox;
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
