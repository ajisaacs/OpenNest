namespace OpenNest.Forms
{
    partial class OptionsForm
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
            this.components = new System.ComponentModel.Container();
            this.checkBox1 = new System.Windows.Forms.CheckBox();
            this.label1 = new System.Windows.Forms.Label();
            this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
            this.numericUpDown1 = new OpenNest.Controls.NumericUpDown();
            this.tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
            this.saveButton = new System.Windows.Forms.Button();
            this.cancelButton = new System.Windows.Forms.Button();
            this.bottomPanel1 = new OpenNest.Controls.BottomPanel();
            this.strategyGrid = new System.Windows.Forms.DataGridView();
            this.strategyGroupBox = new System.Windows.Forms.GroupBox();
            this.colorSchemeLabel = new System.Windows.Forms.Label();
            this.colorSchemeCombo = new System.Windows.Forms.ComboBox();
            this.serverGroupBox = new System.Windows.Forms.GroupBox();
            this.serverUrlBox = new System.Windows.Forms.TextBox();
            this.setServerButton = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown1)).BeginInit();
            this.tableLayoutPanel1.SuspendLayout();
            this.bottomPanel1.SuspendLayout();
            this.serverGroupBox.SuspendLayout();
            this.SuspendLayout();
            // 
            // checkBox1
            // 
            this.checkBox1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.checkBox1.AutoSize = true;
            this.tableLayoutPanel1.SetColumnSpan(this.checkBox1, 2);
            this.checkBox1.Location = new System.Drawing.Point(3, 130);
            this.checkBox1.Name = "checkBox1";
            this.checkBox1.Size = new System.Drawing.Size(281, 20);
            this.checkBox1.TabIndex = 7;
            this.checkBox1.Text = "Create a new nest on startup";
            this.checkBox1.UseVisualStyleBackColor = true;
            // 
            // label1
            // 
            this.label1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(3, 52);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(145, 16);
            this.label1.TabIndex = 3;
            this.label1.Text = "Auto-size plate factor:";
            // 
            // numericUpDown1
            // 
            this.numericUpDown1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.numericUpDown1.DecimalPlaces = 4;
            this.numericUpDown1.Increment = new decimal(new int[] {
            125,
            0,
            0,
            196608});
            this.numericUpDown1.Location = new System.Drawing.Point(154, 49);
            this.numericUpDown1.Name = "numericUpDown1";
            this.numericUpDown1.Size = new System.Drawing.Size(130, 22);
            this.numericUpDown1.Suffix = "";
            this.numericUpDown1.TabIndex = 4;
            this.toolTip1.SetToolTip(this.numericUpDown1, "The amount to round the plate size up.");
            //
            // tableLayoutPanel1
            //
            this.tableLayoutPanel1.ColumnCount = 4;
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 297F));
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 100F));
            this.tableLayoutPanel1.Controls.Add(this.label1, 0, 0);
            this.tableLayoutPanel1.Controls.Add(this.colorSchemeLabel, 0, 1);
            this.tableLayoutPanel1.Controls.Add(this.colorSchemeCombo, 1, 1);
            this.tableLayoutPanel1.Controls.Add(this.checkBox1, 0, 2);
            this.tableLayoutPanel1.Controls.Add(this.numericUpDown1, 1, 0);
            this.tableLayoutPanel1.Location = new System.Drawing.Point(12, 12);
            this.tableLayoutPanel1.Name = "tableLayoutPanel1";
            this.tableLayoutPanel1.RowCount = 3;
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 33.33333F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 33.33333F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 33.33333F));
            this.tableLayoutPanel1.Size = new System.Drawing.Size(684, 160);
            this.tableLayoutPanel1.TabIndex = 0;
            // 
            // saveButton
            // 
            this.saveButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.saveButton.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.saveButton.Location = new System.Drawing.Point(507, 11);
            this.saveButton.Margin = new System.Windows.Forms.Padding(4);
            this.saveButton.Name = "saveButton";
            this.saveButton.Size = new System.Drawing.Size(90, 28);
            this.saveButton.TabIndex = 0;
            this.saveButton.Text = "Save";
            this.saveButton.UseVisualStyleBackColor = true;
            this.saveButton.Click += new System.EventHandler(this.SaveSettings_Click);
            // 
            // cancelButton
            // 
            this.cancelButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.cancelButton.Location = new System.Drawing.Point(605, 11);
            this.cancelButton.Margin = new System.Windows.Forms.Padding(4);
            this.cancelButton.Name = "cancelButton";
            this.cancelButton.Size = new System.Drawing.Size(90, 28);
            this.cancelButton.TabIndex = 1;
            this.cancelButton.Text = "Cancel";
            this.cancelButton.UseVisualStyleBackColor = true;
            // 
            // bottomPanel1
            // 
            this.bottomPanel1.Controls.Add(this.cancelButton);
            this.bottomPanel1.Controls.Add(this.saveButton);
            this.bottomPanel1.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.bottomPanel1.Location = new System.Drawing.Point(0, 443);
            this.bottomPanel1.Name = "bottomPanel1";
            this.bottomPanel1.Size = new System.Drawing.Size(708, 50);
            this.bottomPanel1.TabIndex = 1;
            //
            // strategyGrid
            //
            this.strategyGrid.AllowUserToAddRows = false;
            this.strategyGrid.AllowUserToDeleteRows = false;
            this.strategyGrid.AllowUserToResizeRows = false;
            this.strategyGrid.BackgroundColor = System.Drawing.SystemColors.Window;
            this.strategyGrid.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.strategyGrid.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.strategyGrid.Dock = System.Windows.Forms.DockStyle.Fill;
            this.strategyGrid.Location = new System.Drawing.Point(3, 18);
            this.strategyGrid.Name = "strategyGrid";
            this.strategyGrid.RowHeadersVisible = false;
            this.strategyGrid.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.strategyGrid.TabIndex = 0;
            //
            // strategyGroupBox
            //
            this.strategyGroupBox.Controls.Add(this.strategyGrid);
            this.strategyGroupBox.Location = new System.Drawing.Point(12, 253);
            this.strategyGroupBox.Name = "strategyGroupBox";
            this.strategyGroupBox.Size = new System.Drawing.Size(684, 180);
            this.strategyGroupBox.TabIndex = 2;
            this.strategyGroupBox.TabStop = false;
            this.strategyGroupBox.Text = "Fill Strategies";
            //
            // colorSchemeLabel
            //
            this.colorSchemeLabel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.colorSchemeLabel.AutoSize = true;
            this.colorSchemeLabel.Location = new System.Drawing.Point(3, 92);
            this.colorSchemeLabel.Name = "colorSchemeLabel";
            this.colorSchemeLabel.Size = new System.Drawing.Size(145, 16);
            this.colorSchemeLabel.TabIndex = 10;
            this.colorSchemeLabel.Text = "Color scheme:";
            //
            // colorSchemeCombo
            //
            this.colorSchemeCombo.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.colorSchemeCombo.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.colorSchemeCombo.Location = new System.Drawing.Point(154, 89);
            this.colorSchemeCombo.Name = "colorSchemeCombo";
            this.colorSchemeCombo.Size = new System.Drawing.Size(130, 24);
            this.colorSchemeCombo.TabIndex = 11;
            //
            // serverGroupBox
            //
            this.serverGroupBox.Controls.Add(this.serverUrlBox);
            this.serverGroupBox.Controls.Add(this.setServerButton);
            this.serverGroupBox.Location = new System.Drawing.Point(12, 178);
            this.serverGroupBox.Name = "serverGroupBox";
            this.serverGroupBox.Size = new System.Drawing.Size(684, 69);
            this.serverGroupBox.TabIndex = 1;
            this.serverGroupBox.TabStop = false;
            this.serverGroupBox.Text = "Nest server URL (File / Database mode is selected under File > Storage Mode)";
            //
            // serverUrlBox
            //
            this.serverUrlBox.Location = new System.Drawing.Point(12, 29);
            this.serverUrlBox.Name = "serverUrlBox";
            this.serverUrlBox.PlaceholderText = "http://server:8090";
            this.serverUrlBox.Size = new System.Drawing.Size(485, 22);
            this.serverUrlBox.TabIndex = 0;
            //
            // setServerButton
            //
            this.setServerButton.Location = new System.Drawing.Point(504, 26);
            this.setServerButton.Name = "setServerButton";
            this.setServerButton.Size = new System.Drawing.Size(168, 28);
            this.setServerButton.TabIndex = 1;
            this.setServerButton.Text = "Set / Change Server";
            this.setServerButton.UseVisualStyleBackColor = true;
            this.setServerButton.Click += new System.EventHandler(this.SetServer_Click);
            //
            // OptionsForm
            //
            this.AcceptButton = this.saveButton;
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.CancelButton = this.cancelButton;
            this.ClientSize = new System.Drawing.Size(708, 493);
            this.Controls.Add(this.strategyGroupBox);
            this.Controls.Add(this.serverGroupBox);
            this.Controls.Add(this.tableLayoutPanel1);
            this.Controls.Add(this.bottomPanel1);
            this.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "OptionsForm";
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Options";
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown1)).EndInit();
            this.tableLayoutPanel1.ResumeLayout(false);
            this.tableLayoutPanel1.PerformLayout();
            this.bottomPanel1.ResumeLayout(false);
            this.serverGroupBox.ResumeLayout(false);
            this.serverGroupBox.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.CheckBox checkBox1;
        private System.Windows.Forms.Button cancelButton;
        private System.Windows.Forms.Button saveButton;
        private System.Windows.Forms.Label label1;
        private Controls.NumericUpDown numericUpDown1;
        private System.Windows.Forms.ToolTip toolTip1;
        private Controls.BottomPanel bottomPanel1;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel1;
        private System.Windows.Forms.DataGridView strategyGrid;
        private System.Windows.Forms.GroupBox strategyGroupBox;
        private System.Windows.Forms.Label colorSchemeLabel;
        private System.Windows.Forms.ComboBox colorSchemeCombo;
        private System.Windows.Forms.GroupBox serverGroupBox;
        private System.Windows.Forms.TextBox serverUrlBox;
        private System.Windows.Forms.Button setServerButton;
    }
}