namespace OpenNest.Forms
{
    partial class NestDefaultsForm
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
            this.tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
            this.unitsLabel = new System.Windows.Forms.Label();
            this.unitsCombo = new System.Windows.Forms.ComboBox();
            this.widthLabel = new System.Windows.Forms.Label();
            this.widthBox = new OpenNest.Controls.NumericUpDown();
            this.lengthLabel = new System.Windows.Forms.Label();
            this.lengthBox = new OpenNest.Controls.NumericUpDown();
            this.partSpacingLabel = new System.Windows.Forms.Label();
            this.partSpacingBox = new OpenNest.Controls.NumericUpDown();
            this.edgeLeftLabel = new System.Windows.Forms.Label();
            this.edgeLeftBox = new OpenNest.Controls.NumericUpDown();
            this.edgeBottomLabel = new System.Windows.Forms.Label();
            this.edgeBottomBox = new OpenNest.Controls.NumericUpDown();
            this.edgeRightLabel = new System.Windows.Forms.Label();
            this.edgeRightBox = new OpenNest.Controls.NumericUpDown();
            this.edgeTopLabel = new System.Windows.Forms.Label();
            this.edgeTopBox = new OpenNest.Controls.NumericUpDown();
            this.quadrantLabel = new System.Windows.Forms.Label();
            this.quadrantSelect1 = new OpenNest.Controls.QuadrantSelect();
            this.saveButton = new System.Windows.Forms.Button();
            this.cancelButton = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.widthBox)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.lengthBox)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.partSpacingBox)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.edgeLeftBox)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.edgeBottomBox)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.edgeRightBox)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.edgeTopBox)).BeginInit();
            this.tableLayoutPanel1.SuspendLayout();
            this.SuspendLayout();
            //
            // tableLayoutPanel1
            //
            this.tableLayoutPanel1.ColumnCount = 2;
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayoutPanel1.Controls.Add(this.unitsLabel, 0, 0);
            this.tableLayoutPanel1.Controls.Add(this.unitsCombo, 1, 0);
            this.tableLayoutPanel1.Controls.Add(this.widthLabel, 0, 1);
            this.tableLayoutPanel1.Controls.Add(this.widthBox, 1, 1);
            this.tableLayoutPanel1.Controls.Add(this.lengthLabel, 0, 2);
            this.tableLayoutPanel1.Controls.Add(this.lengthBox, 1, 2);
            this.tableLayoutPanel1.Controls.Add(this.partSpacingLabel, 0, 3);
            this.tableLayoutPanel1.Controls.Add(this.partSpacingBox, 1, 3);
            this.tableLayoutPanel1.Controls.Add(this.edgeLeftLabel, 0, 4);
            this.tableLayoutPanel1.Controls.Add(this.edgeLeftBox, 1, 4);
            this.tableLayoutPanel1.Controls.Add(this.edgeBottomLabel, 0, 5);
            this.tableLayoutPanel1.Controls.Add(this.edgeBottomBox, 1, 5);
            this.tableLayoutPanel1.Controls.Add(this.edgeRightLabel, 0, 6);
            this.tableLayoutPanel1.Controls.Add(this.edgeRightBox, 1, 6);
            this.tableLayoutPanel1.Controls.Add(this.edgeTopLabel, 0, 7);
            this.tableLayoutPanel1.Controls.Add(this.edgeTopBox, 1, 7);
            this.tableLayoutPanel1.Controls.Add(this.quadrantLabel, 0, 8);
            this.tableLayoutPanel1.Controls.Add(this.quadrantSelect1, 1, 8);
            this.tableLayoutPanel1.Location = new System.Drawing.Point(12, 12);
            this.tableLayoutPanel1.Name = "tableLayoutPanel1";
            this.tableLayoutPanel1.RowCount = 9;
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 11.11111F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 11.11111F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 11.11111F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 11.11111F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 11.11111F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 11.11111F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 11.11111F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 11.11111F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 11.11111F));
            this.tableLayoutPanel1.Size = new System.Drawing.Size(410, 260);
            this.tableLayoutPanel1.TabIndex = 0;
            //
            // unitsLabel
            //
            this.unitsLabel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.unitsLabel.AutoSize = true;
            this.unitsLabel.Location = new System.Drawing.Point(3, 9);
            this.unitsLabel.Name = "unitsLabel";
            this.unitsLabel.Size = new System.Drawing.Size(100, 16);
            this.unitsLabel.TabIndex = 0;
            this.unitsLabel.Text = "Units:";
            //
            // unitsCombo
            //
            this.unitsCombo.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.unitsCombo.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.unitsCombo.Items.AddRange(new object[] { "Inches", "Millimeters" });
            this.unitsCombo.Location = new System.Drawing.Point(109, 5);
            this.unitsCombo.Name = "unitsCombo";
            this.unitsCombo.Size = new System.Drawing.Size(298, 24);
            this.unitsCombo.TabIndex = 1;
            //
            // widthLabel
            //
            this.widthLabel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.widthLabel.AutoSize = true;
            this.widthLabel.Location = new System.Drawing.Point(3, 37);
            this.widthLabel.Name = "widthLabel";
            this.widthLabel.Size = new System.Drawing.Size(100, 16);
            this.widthLabel.TabIndex = 2;
            this.widthLabel.Text = "Plate width:";
            //
            // widthBox
            //
            this.widthBox.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.widthBox.DecimalPlaces = 4;
            this.widthBox.Location = new System.Drawing.Point(109, 33);
            this.widthBox.Maximum = new decimal(new int[] { 1000000, 0, 0, 0 });
            this.widthBox.Minimum = new decimal(new int[] { 1, 0, 0, 196608 });
            this.widthBox.Name = "widthBox";
            this.widthBox.Size = new System.Drawing.Size(298, 22);
            this.widthBox.Suffix = "";
            this.widthBox.TabIndex = 3;
            //
            // lengthLabel
            //
            this.lengthLabel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.lengthLabel.AutoSize = true;
            this.lengthLabel.Location = new System.Drawing.Point(3, 65);
            this.lengthLabel.Name = "lengthLabel";
            this.lengthLabel.Size = new System.Drawing.Size(100, 16);
            this.lengthLabel.TabIndex = 4;
            this.lengthLabel.Text = "Plate length:";
            //
            // lengthBox
            //
            this.lengthBox.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.lengthBox.DecimalPlaces = 4;
            this.lengthBox.Location = new System.Drawing.Point(109, 61);
            this.lengthBox.Maximum = new decimal(new int[] { 1000000, 0, 0, 0 });
            this.lengthBox.Minimum = new decimal(new int[] { 1, 0, 0, 196608 });
            this.lengthBox.Name = "lengthBox";
            this.lengthBox.Size = new System.Drawing.Size(298, 22);
            this.lengthBox.Suffix = "";
            this.lengthBox.TabIndex = 5;
            //
            // partSpacingLabel
            //
            this.partSpacingLabel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.partSpacingLabel.AutoSize = true;
            this.partSpacingLabel.Location = new System.Drawing.Point(3, 93);
            this.partSpacingLabel.Name = "partSpacingLabel";
            this.partSpacingLabel.Size = new System.Drawing.Size(100, 16);
            this.partSpacingLabel.TabIndex = 6;
            this.partSpacingLabel.Text = "Part spacing:";
            //
            // partSpacingBox
            //
            this.partSpacingBox.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.partSpacingBox.DecimalPlaces = 4;
            this.partSpacingBox.Location = new System.Drawing.Point(109, 89);
            this.partSpacingBox.Maximum = new decimal(new int[] { 1000000, 0, 0, 0 });
            this.partSpacingBox.Name = "partSpacingBox";
            this.partSpacingBox.Size = new System.Drawing.Size(298, 22);
            this.partSpacingBox.Suffix = "";
            this.partSpacingBox.TabIndex = 7;
            //
            // edgeLeftLabel
            //
            this.edgeLeftLabel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.edgeLeftLabel.AutoSize = true;
            this.edgeLeftLabel.Location = new System.Drawing.Point(3, 121);
            this.edgeLeftLabel.Name = "edgeLeftLabel";
            this.edgeLeftLabel.Size = new System.Drawing.Size(100, 16);
            this.edgeLeftLabel.TabIndex = 8;
            this.edgeLeftLabel.Text = "Edge left:";
            //
            // edgeLeftBox
            //
            this.edgeLeftBox.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.edgeLeftBox.DecimalPlaces = 4;
            this.edgeLeftBox.Location = new System.Drawing.Point(109, 117);
            this.edgeLeftBox.Maximum = new decimal(new int[] { 1000000, 0, 0, 0 });
            this.edgeLeftBox.Name = "edgeLeftBox";
            this.edgeLeftBox.Size = new System.Drawing.Size(298, 22);
            this.edgeLeftBox.Suffix = "";
            this.edgeLeftBox.TabIndex = 9;
            //
            // edgeBottomLabel
            //
            this.edgeBottomLabel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.edgeBottomLabel.AutoSize = true;
            this.edgeBottomLabel.Location = new System.Drawing.Point(3, 149);
            this.edgeBottomLabel.Name = "edgeBottomLabel";
            this.edgeBottomLabel.Size = new System.Drawing.Size(100, 16);
            this.edgeBottomLabel.TabIndex = 10;
            this.edgeBottomLabel.Text = "Edge bottom:";
            //
            // edgeBottomBox
            //
            this.edgeBottomBox.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.edgeBottomBox.DecimalPlaces = 4;
            this.edgeBottomBox.Location = new System.Drawing.Point(109, 145);
            this.edgeBottomBox.Maximum = new decimal(new int[] { 1000000, 0, 0, 0 });
            this.edgeBottomBox.Name = "edgeBottomBox";
            this.edgeBottomBox.Size = new System.Drawing.Size(298, 22);
            this.edgeBottomBox.Suffix = "";
            this.edgeBottomBox.TabIndex = 11;
            //
            // edgeRightLabel
            //
            this.edgeRightLabel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.edgeRightLabel.AutoSize = true;
            this.edgeRightLabel.Location = new System.Drawing.Point(3, 177);
            this.edgeRightLabel.Name = "edgeRightLabel";
            this.edgeRightLabel.Size = new System.Drawing.Size(100, 16);
            this.edgeRightLabel.TabIndex = 12;
            this.edgeRightLabel.Text = "Edge right:";
            //
            // edgeRightBox
            //
            this.edgeRightBox.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.edgeRightBox.DecimalPlaces = 4;
            this.edgeRightBox.Location = new System.Drawing.Point(109, 173);
            this.edgeRightBox.Maximum = new decimal(new int[] { 1000000, 0, 0, 0 });
            this.edgeRightBox.Name = "edgeRightBox";
            this.edgeRightBox.Size = new System.Drawing.Size(298, 22);
            this.edgeRightBox.Suffix = "";
            this.edgeRightBox.TabIndex = 13;
            //
            // edgeTopLabel
            //
            this.edgeTopLabel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.edgeTopLabel.AutoSize = true;
            this.edgeTopLabel.Location = new System.Drawing.Point(3, 205);
            this.edgeTopLabel.Name = "edgeTopLabel";
            this.edgeTopLabel.Size = new System.Drawing.Size(100, 16);
            this.edgeTopLabel.TabIndex = 14;
            this.edgeTopLabel.Text = "Edge top:";
            //
            // edgeTopBox
            //
            this.edgeTopBox.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.edgeTopBox.DecimalPlaces = 4;
            this.edgeTopBox.Location = new System.Drawing.Point(109, 201);
            this.edgeTopBox.Maximum = new decimal(new int[] { 1000000, 0, 0, 0 });
            this.edgeTopBox.Name = "edgeTopBox";
            this.edgeTopBox.Size = new System.Drawing.Size(298, 22);
            this.edgeTopBox.Suffix = "";
            this.edgeTopBox.TabIndex = 15;
            //
            // quadrantLabel
            //
            this.quadrantLabel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.quadrantLabel.AutoSize = true;
            this.quadrantLabel.Location = new System.Drawing.Point(3, 233);
            this.quadrantLabel.Name = "quadrantLabel";
            this.quadrantLabel.Size = new System.Drawing.Size(100, 16);
            this.quadrantLabel.TabIndex = 16;
            this.quadrantLabel.Text = "Quadrant:";
            //
            // quadrantSelect1
            //
            this.quadrantSelect1.Anchor = System.Windows.Forms.AnchorStyles.None;
            this.quadrantSelect1.BackColor = System.Drawing.Color.White;
            this.quadrantSelect1.Location = new System.Drawing.Point(198, 225);
            this.quadrantSelect1.Name = "quadrantSelect1";
            this.quadrantSelect1.Quadrant = 1;
            this.quadrantSelect1.Size = new System.Drawing.Size(120, 28);
            this.quadrantSelect1.TabIndex = 17;
            //
            // saveButton
            //
            this.saveButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.saveButton.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.saveButton.Location = new System.Drawing.Point(243, 285);
            this.saveButton.Name = "saveButton";
            this.saveButton.Size = new System.Drawing.Size(90, 28);
            this.saveButton.TabIndex = 1;
            this.saveButton.Text = "Save";
            this.saveButton.UseVisualStyleBackColor = true;
            //
            // cancelButton
            //
            this.cancelButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.cancelButton.Location = new System.Drawing.Point(339, 285);
            this.cancelButton.Name = "cancelButton";
            this.cancelButton.Size = new System.Drawing.Size(90, 28);
            this.cancelButton.TabIndex = 2;
            this.cancelButton.Text = "Cancel";
            this.cancelButton.UseVisualStyleBackColor = true;
            //
            // NestDefaultsForm
            //
            this.AcceptButton = this.saveButton;
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.CancelButton = this.cancelButton;
            this.ClientSize = new System.Drawing.Size(434, 322);
            this.Controls.Add(this.tableLayoutPanel1);
            this.Controls.Add(this.saveButton);
            this.Controls.Add(this.cancelButton);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "NestDefaultsForm";
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Nest Defaults";
            ((System.ComponentModel.ISupportInitialize)(this.widthBox)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.lengthBox)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.partSpacingBox)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.edgeLeftBox)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.edgeBottomBox)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.edgeRightBox)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.edgeTopBox)).EndInit();
            this.tableLayoutPanel1.ResumeLayout(false);
            this.tableLayoutPanel1.PerformLayout();
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel1;
        private System.Windows.Forms.Label unitsLabel;
        private System.Windows.Forms.ComboBox unitsCombo;
        private System.Windows.Forms.Label widthLabel;
        private OpenNest.Controls.NumericUpDown widthBox;
        private System.Windows.Forms.Label lengthLabel;
        private OpenNest.Controls.NumericUpDown lengthBox;
        private System.Windows.Forms.Label partSpacingLabel;
        private OpenNest.Controls.NumericUpDown partSpacingBox;
        private System.Windows.Forms.Label edgeLeftLabel;
        private OpenNest.Controls.NumericUpDown edgeLeftBox;
        private System.Windows.Forms.Label edgeBottomLabel;
        private OpenNest.Controls.NumericUpDown edgeBottomBox;
        private System.Windows.Forms.Label edgeRightLabel;
        private OpenNest.Controls.NumericUpDown edgeRightBox;
        private System.Windows.Forms.Label edgeTopLabel;
        private OpenNest.Controls.NumericUpDown edgeTopBox;
        private System.Windows.Forms.Label quadrantLabel;
        private OpenNest.Controls.QuadrantSelect quadrantSelect1;
        private System.Windows.Forms.Button saveButton;
        private System.Windows.Forms.Button cancelButton;
    }
}
