namespace OpenNest.Forms
{
    partial class AutoNestForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.dataGridView1 = new System.Windows.Forms.DataGridView();
            this.bottomPanel1 = new OpenNest.Controls.BottomPanel();
            this.acceptButton = new System.Windows.Forms.Button();
            this.createNewPlatesAsNeededBox = new System.Windows.Forms.CheckBox();
            this.cancelButton = new System.Windows.Forms.Button();
            this.optimizePlateSizeBox = new System.Windows.Forms.CheckBox();
            this.plateOptionsPanel = new System.Windows.Forms.Panel();
            this.plateOptionsGrid = new System.Windows.Forms.DataGridView();
            this.salvageRateLabel = new System.Windows.Forms.Label();
            this.salvageRateBox = new System.Windows.Forms.TextBox();
            this.salvageRatePercentLabel = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.plateOptionsGrid)).BeginInit();
            this.bottomPanel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // dataGridView1
            // 
            this.dataGridView1.AllowUserToDeleteRows = false;
            this.dataGridView1.AllowUserToOrderColumns = true;
            this.dataGridView1.AllowUserToResizeRows = false;
            this.dataGridView1.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.dataGridView1.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dataGridView1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dataGridView1.Location = new System.Drawing.Point(0, 0);
            this.dataGridView1.Name = "dataGridView1";
            this.dataGridView1.RowHeadersVisible = false;
            this.dataGridView1.Size = new System.Drawing.Size(545, 385);
            this.dataGridView1.TabIndex = 0;
            // 
            // bottomPanel1
            // 
            this.bottomPanel1.Controls.Add(this.optimizePlateSizeBox);
            this.bottomPanel1.Controls.Add(this.acceptButton);
            this.bottomPanel1.Controls.Add(this.createNewPlatesAsNeededBox);
            this.bottomPanel1.Controls.Add(this.cancelButton);
            this.bottomPanel1.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.bottomPanel1.Location = new System.Drawing.Point(0, 335);
            this.bottomPanel1.Name = "bottomPanel1";
            this.bottomPanel1.Size = new System.Drawing.Size(545, 50);
            this.bottomPanel1.TabIndex = 9;
            // 
            // acceptButton
            // 
            this.acceptButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.acceptButton.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.acceptButton.Location = new System.Drawing.Point(344, 9);
            this.acceptButton.Margin = new System.Windows.Forms.Padding(4);
            this.acceptButton.Name = "acceptButton";
            this.acceptButton.Size = new System.Drawing.Size(90, 28);
            this.acceptButton.TabIndex = 6;
            this.acceptButton.Text = "Accept";
            this.acceptButton.UseVisualStyleBackColor = true;
            // 
            // createNewPlatesAsNeededBox
            // 
            this.createNewPlatesAsNeededBox.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.createNewPlatesAsNeededBox.AutoSize = true;
            this.createNewPlatesAsNeededBox.Location = new System.Drawing.Point(12, 15);
            this.createNewPlatesAsNeededBox.Name = "createNewPlatesAsNeededBox";
            this.createNewPlatesAsNeededBox.Size = new System.Drawing.Size(202, 20);
            this.createNewPlatesAsNeededBox.TabIndex = 8;
            this.createNewPlatesAsNeededBox.Text = "Create new plates as needed";
            this.createNewPlatesAsNeededBox.UseVisualStyleBackColor = true;
            // 
            // cancelButton
            // 
            this.cancelButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.cancelButton.Location = new System.Drawing.Point(442, 9);
            this.cancelButton.Margin = new System.Windows.Forms.Padding(4);
            this.cancelButton.Name = "cancelButton";
            this.cancelButton.Size = new System.Drawing.Size(90, 28);
            this.cancelButton.TabIndex = 7;
            this.cancelButton.Text = "Cancel";
            this.cancelButton.UseVisualStyleBackColor = true;
            //
            // optimizePlateSizeBox
            //
            this.optimizePlateSizeBox.AutoSize = true;
            this.optimizePlateSizeBox.Location = new System.Drawing.Point(220, 15);
            this.optimizePlateSizeBox.Name = "optimizePlateSizeBox";
            this.optimizePlateSizeBox.Size = new System.Drawing.Size(148, 20);
            this.optimizePlateSizeBox.TabIndex = 10;
            this.optimizePlateSizeBox.Text = "Optimize plate size";
            this.optimizePlateSizeBox.UseVisualStyleBackColor = true;
            this.optimizePlateSizeBox.CheckedChanged += new System.EventHandler(this.optimizePlateSizeBox_CheckedChanged);
            //
            // plateOptionsPanel
            //
            this.plateOptionsPanel.Controls.Add(this.plateOptionsGrid);
            this.plateOptionsPanel.Controls.Add(this.salvageRateLabel);
            this.plateOptionsPanel.Controls.Add(this.salvageRateBox);
            this.plateOptionsPanel.Controls.Add(this.salvageRatePercentLabel);
            this.plateOptionsPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.plateOptionsPanel.Location = new System.Drawing.Point(0, 135);
            this.plateOptionsPanel.Name = "plateOptionsPanel";
            this.plateOptionsPanel.Size = new System.Drawing.Size(545, 200);
            this.plateOptionsPanel.TabIndex = 11;
            this.plateOptionsPanel.Visible = false;
            //
            // plateOptionsGrid
            //
            this.plateOptionsGrid.AllowUserToOrderColumns = true;
            this.plateOptionsGrid.AllowUserToResizeRows = false;
            this.plateOptionsGrid.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.plateOptionsGrid.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.plateOptionsGrid.Dock = System.Windows.Forms.DockStyle.Fill;
            this.plateOptionsGrid.Location = new System.Drawing.Point(0, 0);
            this.plateOptionsGrid.Name = "plateOptionsGrid";
            this.plateOptionsGrid.RowHeadersVisible = false;
            this.plateOptionsGrid.Size = new System.Drawing.Size(545, 170);
            this.plateOptionsGrid.TabIndex = 0;
            //
            // salvageRateLabel
            //
            this.salvageRateLabel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.salvageRateLabel.AutoSize = true;
            this.salvageRateLabel.Location = new System.Drawing.Point(8, 176);
            this.salvageRateLabel.Name = "salvageRateLabel";
            this.salvageRateLabel.Size = new System.Drawing.Size(96, 16);
            this.salvageRateLabel.TabIndex = 1;
            this.salvageRateLabel.Text = "Salvage Rate:";
            //
            // salvageRateBox
            //
            this.salvageRateBox.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.salvageRateBox.Location = new System.Drawing.Point(106, 173);
            this.salvageRateBox.Name = "salvageRateBox";
            this.salvageRateBox.Size = new System.Drawing.Size(50, 22);
            this.salvageRateBox.TabIndex = 2;
            this.salvageRateBox.Text = "50";
            //
            // salvageRatePercentLabel
            //
            this.salvageRatePercentLabel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.salvageRatePercentLabel.AutoSize = true;
            this.salvageRatePercentLabel.Location = new System.Drawing.Point(158, 176);
            this.salvageRatePercentLabel.Name = "salvageRatePercentLabel";
            this.salvageRatePercentLabel.Size = new System.Drawing.Size(21, 16);
            this.salvageRatePercentLabel.TabIndex = 3;
            this.salvageRatePercentLabel.Text = "%";
            //
            // AutoNestForm
            // 
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.ClientSize = new System.Drawing.Size(545, 385);
            this.Controls.Add(this.plateOptionsPanel);
            this.Controls.Add(this.bottomPanel1);
            this.Controls.Add(this.dataGridView1);
            this.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.Name = "AutoNestForm";
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "AutoNest";
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.plateOptionsGrid)).EndInit();
            this.bottomPanel1.ResumeLayout(false);
            this.bottomPanel1.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.DataGridView dataGridView1;
        private System.Windows.Forms.Button acceptButton;
        private System.Windows.Forms.Button cancelButton;
        private System.Windows.Forms.CheckBox createNewPlatesAsNeededBox;
        private Controls.BottomPanel bottomPanel1;
        private System.Windows.Forms.CheckBox optimizePlateSizeBox;
        private System.Windows.Forms.Panel plateOptionsPanel;
        private System.Windows.Forms.DataGridView plateOptionsGrid;
        private System.Windows.Forms.Label salvageRateLabel;
        private System.Windows.Forms.TextBox salvageRateBox;
        private System.Windows.Forms.Label salvageRatePercentLabel;
    }
}