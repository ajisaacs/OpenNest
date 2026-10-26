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
            this.components = new System.ComponentModel.Container();
            this.generalGroup = new System.Windows.Forms.GroupBox();
            this.machineLabel = new System.Windows.Forms.Label();
            this.machineCombo = new System.Windows.Forms.ComboBox();
            this.unitsLabel = new System.Windows.Forms.Label();
            this.inchesRadio = new System.Windows.Forms.RadioButton();
            this.millimetersRadio = new System.Windows.Forms.RadioButton();
            this.plateGroup = new System.Windows.Forms.GroupBox();
            this.widthLabel = new System.Windows.Forms.Label();
            this.widthBox = new OpenNest.Controls.NumericUpDown();
            this.lengthLabel = new System.Windows.Forms.Label();
            this.lengthBox = new OpenNest.Controls.NumericUpDown();
            this.quadrantGroup = new System.Windows.Forms.GroupBox();
            this.quadrantSelect1 = new OpenNest.Controls.QuadrantSelect();
            this.quadrantHintLabel = new System.Windows.Forms.Label();
            this.spacingGroup = new System.Windows.Forms.GroupBox();
            this.partSpacingLabel = new System.Windows.Forms.Label();
            this.partSpacingBox = new OpenNest.Controls.NumericUpDown();
            this.edgeSpacingLabel = new System.Windows.Forms.Label();
            this.edgeTopBox = new OpenNest.Controls.NumericUpDown();
            this.edgeLeftBox = new OpenNest.Controls.NumericUpDown();
            this.edgePlateLabel = new System.Windows.Forms.Label();
            this.edgeRightBox = new OpenNest.Controls.NumericUpDown();
            this.edgeBottomBox = new OpenNest.Controls.NumericUpDown();
            this.sourceLabel = new System.Windows.Forms.Label();
            this.saveButton = new System.Windows.Forms.Button();
            this.cancelButton = new System.Windows.Forms.Button();
            this.toolTip = new System.Windows.Forms.ToolTip(this.components);
            ((System.ComponentModel.ISupportInitialize)(this.widthBox)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.lengthBox)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.partSpacingBox)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.edgeTopBox)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.edgeLeftBox)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.edgeRightBox)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.edgeBottomBox)).BeginInit();
            this.generalGroup.SuspendLayout();
            this.plateGroup.SuspendLayout();
            this.quadrantGroup.SuspendLayout();
            this.spacingGroup.SuspendLayout();
            this.SuspendLayout();
            //
            // generalGroup
            //
            this.generalGroup.Controls.Add(this.machineLabel);
            this.generalGroup.Controls.Add(this.machineCombo);
            this.generalGroup.Controls.Add(this.unitsLabel);
            this.generalGroup.Controls.Add(this.inchesRadio);
            this.generalGroup.Controls.Add(this.millimetersRadio);
            this.generalGroup.Location = new System.Drawing.Point(12, 12);
            this.generalGroup.Name = "generalGroup";
            this.generalGroup.Size = new System.Drawing.Size(340, 90);
            this.generalGroup.TabIndex = 0;
            this.generalGroup.TabStop = false;
            this.generalGroup.Text = "General";
            //
            // machineLabel
            //
            this.machineLabel.AutoSize = true;
            this.machineLabel.Location = new System.Drawing.Point(12, 27);
            this.machineLabel.Name = "machineLabel";
            this.machineLabel.Size = new System.Drawing.Size(56, 15);
            this.machineLabel.TabIndex = 0;
            this.machineLabel.Text = "Machine:";
            //
            // machineCombo
            //
            this.machineCombo.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.machineCombo.Location = new System.Drawing.Point(90, 23);
            this.machineCombo.Name = "machineCombo";
            this.machineCombo.Size = new System.Drawing.Size(236, 23);
            this.machineCombo.TabIndex = 1;
            //
            // unitsLabel
            //
            this.unitsLabel.AutoSize = true;
            this.unitsLabel.Location = new System.Drawing.Point(12, 59);
            this.unitsLabel.Name = "unitsLabel";
            this.unitsLabel.Size = new System.Drawing.Size(37, 15);
            this.unitsLabel.TabIndex = 2;
            this.unitsLabel.Text = "Units:";
            //
            // inchesRadio
            //
            this.inchesRadio.AutoSize = true;
            this.inchesRadio.Location = new System.Drawing.Point(90, 57);
            this.inchesRadio.Name = "inchesRadio";
            this.inchesRadio.Size = new System.Drawing.Size(61, 19);
            this.inchesRadio.TabIndex = 3;
            this.inchesRadio.TabStop = true;
            this.inchesRadio.Text = "Inches";
            this.inchesRadio.UseVisualStyleBackColor = true;
            //
            // millimetersRadio
            //
            this.millimetersRadio.AutoSize = true;
            this.millimetersRadio.Location = new System.Drawing.Point(170, 57);
            this.millimetersRadio.Name = "millimetersRadio";
            this.millimetersRadio.Size = new System.Drawing.Size(85, 19);
            this.millimetersRadio.TabIndex = 4;
            this.millimetersRadio.Text = "Millimeters";
            this.millimetersRadio.UseVisualStyleBackColor = true;
            this.millimetersRadio.CheckedChanged += new System.EventHandler(this.UnitsChanged);
            //
            // plateGroup
            //
            this.plateGroup.Controls.Add(this.widthLabel);
            this.plateGroup.Controls.Add(this.widthBox);
            this.plateGroup.Controls.Add(this.lengthLabel);
            this.plateGroup.Controls.Add(this.lengthBox);
            this.plateGroup.Location = new System.Drawing.Point(12, 110);
            this.plateGroup.Name = "plateGroup";
            this.plateGroup.Size = new System.Drawing.Size(340, 90);
            this.plateGroup.TabIndex = 1;
            this.plateGroup.TabStop = false;
            this.plateGroup.Text = "Plate Size";
            //
            // widthLabel
            //
            this.widthLabel.AutoSize = true;
            this.widthLabel.Location = new System.Drawing.Point(12, 27);
            this.widthLabel.Name = "widthLabel";
            this.widthLabel.Size = new System.Drawing.Size(42, 15);
            this.widthLabel.TabIndex = 0;
            this.widthLabel.Text = "Width:";
            //
            // widthBox
            //
            this.widthBox.DecimalPlaces = 4;
            this.widthBox.Location = new System.Drawing.Point(90, 24);
            this.widthBox.Maximum = new decimal(new int[] { 1000000, 0, 0, 0 });
            this.widthBox.Minimum = new decimal(new int[] { 1, 0, 0, 196608 });
            this.widthBox.Name = "widthBox";
            this.widthBox.Size = new System.Drawing.Size(130, 23);
            this.widthBox.Suffix = "";
            this.widthBox.TabIndex = 1;
            //
            // lengthLabel
            //
            this.lengthLabel.AutoSize = true;
            this.lengthLabel.Location = new System.Drawing.Point(12, 59);
            this.lengthLabel.Name = "lengthLabel";
            this.lengthLabel.Size = new System.Drawing.Size(47, 15);
            this.lengthLabel.TabIndex = 2;
            this.lengthLabel.Text = "Length:";
            //
            // lengthBox
            //
            this.lengthBox.DecimalPlaces = 4;
            this.lengthBox.Location = new System.Drawing.Point(90, 56);
            this.lengthBox.Maximum = new decimal(new int[] { 1000000, 0, 0, 0 });
            this.lengthBox.Minimum = new decimal(new int[] { 1, 0, 0, 196608 });
            this.lengthBox.Name = "lengthBox";
            this.lengthBox.Size = new System.Drawing.Size(130, 23);
            this.lengthBox.Suffix = "";
            this.lengthBox.TabIndex = 3;
            //
            // quadrantGroup
            //
            this.quadrantGroup.Controls.Add(this.quadrantSelect1);
            this.quadrantGroup.Controls.Add(this.quadrantHintLabel);
            this.quadrantGroup.Location = new System.Drawing.Point(364, 12);
            this.quadrantGroup.Name = "quadrantGroup";
            this.quadrantGroup.Size = new System.Drawing.Size(224, 188);
            this.quadrantGroup.TabIndex = 2;
            this.quadrantGroup.TabStop = false;
            this.quadrantGroup.Text = "Quadrant";
            //
            // quadrantSelect1
            //
            this.quadrantSelect1.BackColor = System.Drawing.Color.White;
            this.quadrantSelect1.Cursor = System.Windows.Forms.Cursors.Hand;
            this.quadrantSelect1.Location = new System.Drawing.Point(12, 24);
            this.quadrantSelect1.Name = "quadrantSelect1";
            this.quadrantSelect1.Quadrant = 1;
            this.quadrantSelect1.Size = new System.Drawing.Size(200, 126);
            this.quadrantSelect1.TabIndex = 0;
            //
            // quadrantHintLabel
            //
            this.quadrantHintLabel.ForeColor = System.Drawing.SystemColors.GrayText;
            this.quadrantHintLabel.Location = new System.Drawing.Point(12, 156);
            this.quadrantHintLabel.Name = "quadrantHintLabel";
            this.quadrantHintLabel.Size = new System.Drawing.Size(200, 20);
            this.quadrantHintLabel.TabIndex = 1;
            this.quadrantHintLabel.Text = "Click a quadrant to select it.";
            this.quadrantHintLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            //
            // spacingGroup
            //
            this.spacingGroup.Controls.Add(this.partSpacingLabel);
            this.spacingGroup.Controls.Add(this.partSpacingBox);
            this.spacingGroup.Controls.Add(this.edgeSpacingLabel);
            this.spacingGroup.Controls.Add(this.edgeTopBox);
            this.spacingGroup.Controls.Add(this.edgeLeftBox);
            this.spacingGroup.Controls.Add(this.edgePlateLabel);
            this.spacingGroup.Controls.Add(this.edgeRightBox);
            this.spacingGroup.Controls.Add(this.edgeBottomBox);
            this.spacingGroup.Location = new System.Drawing.Point(12, 208);
            this.spacingGroup.Name = "spacingGroup";
            this.spacingGroup.Size = new System.Drawing.Size(576, 136);
            this.spacingGroup.TabIndex = 3;
            this.spacingGroup.TabStop = false;
            this.spacingGroup.Text = "Spacing";
            //
            // partSpacingLabel
            //
            this.partSpacingLabel.AutoSize = true;
            this.partSpacingLabel.Location = new System.Drawing.Point(12, 30);
            this.partSpacingLabel.Name = "partSpacingLabel";
            this.partSpacingLabel.Size = new System.Drawing.Size(77, 15);
            this.partSpacingLabel.TabIndex = 0;
            this.partSpacingLabel.Text = "Between parts:";
            //
            // partSpacingBox
            //
            this.partSpacingBox.DecimalPlaces = 4;
            this.partSpacingBox.Location = new System.Drawing.Point(12, 50);
            this.partSpacingBox.Maximum = new decimal(new int[] { 1000000, 0, 0, 0 });
            this.partSpacingBox.Name = "partSpacingBox";
            this.partSpacingBox.Size = new System.Drawing.Size(130, 23);
            this.partSpacingBox.Suffix = "";
            this.partSpacingBox.TabIndex = 1;
            //
            // edgeSpacingLabel
            //
            this.edgeSpacingLabel.AutoSize = true;
            this.edgeSpacingLabel.Location = new System.Drawing.Point(178, 30);
            this.edgeSpacingLabel.Name = "edgeSpacingLabel";
            this.edgeSpacingLabel.Size = new System.Drawing.Size(67, 15);
            this.edgeSpacingLabel.TabIndex = 2;
            this.edgeSpacingLabel.Text = "Plate edges:";
            //
            // edgeTopBox
            //
            this.edgeTopBox.DecimalPlaces = 4;
            this.edgeTopBox.Location = new System.Drawing.Point(355, 22);
            this.edgeTopBox.Maximum = new decimal(new int[] { 1000000, 0, 0, 0 });
            this.edgeTopBox.Name = "edgeTopBox";
            this.edgeTopBox.Size = new System.Drawing.Size(100, 23);
            this.edgeTopBox.Suffix = "";
            this.edgeTopBox.TabIndex = 3;
            this.toolTip.SetToolTip(this.edgeTopBox, "Top edge spacing");
            //
            // edgeLeftBox
            //
            this.edgeLeftBox.DecimalPlaces = 4;
            this.edgeLeftBox.Location = new System.Drawing.Point(250, 61);
            this.edgeLeftBox.Maximum = new decimal(new int[] { 1000000, 0, 0, 0 });
            this.edgeLeftBox.Name = "edgeLeftBox";
            this.edgeLeftBox.Size = new System.Drawing.Size(100, 23);
            this.edgeLeftBox.Suffix = "";
            this.edgeLeftBox.TabIndex = 4;
            this.toolTip.SetToolTip(this.edgeLeftBox, "Left edge spacing");
            //
            // edgePlateLabel
            //
            this.edgePlateLabel.BackColor = System.Drawing.Color.AliceBlue;
            this.edgePlateLabel.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.edgePlateLabel.ForeColor = System.Drawing.SystemColors.GrayText;
            this.edgePlateLabel.Location = new System.Drawing.Point(358, 50);
            this.edgePlateLabel.Name = "edgePlateLabel";
            this.edgePlateLabel.Size = new System.Drawing.Size(94, 45);
            this.edgePlateLabel.TabIndex = 5;
            this.edgePlateLabel.Text = "Plate";
            this.edgePlateLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            //
            // edgeRightBox
            //
            this.edgeRightBox.DecimalPlaces = 4;
            this.edgeRightBox.Location = new System.Drawing.Point(460, 61);
            this.edgeRightBox.Maximum = new decimal(new int[] { 1000000, 0, 0, 0 });
            this.edgeRightBox.Name = "edgeRightBox";
            this.edgeRightBox.Size = new System.Drawing.Size(100, 23);
            this.edgeRightBox.Suffix = "";
            this.edgeRightBox.TabIndex = 6;
            this.toolTip.SetToolTip(this.edgeRightBox, "Right edge spacing");
            //
            // edgeBottomBox
            //
            this.edgeBottomBox.DecimalPlaces = 4;
            this.edgeBottomBox.Location = new System.Drawing.Point(355, 100);
            this.edgeBottomBox.Maximum = new decimal(new int[] { 1000000, 0, 0, 0 });
            this.edgeBottomBox.Name = "edgeBottomBox";
            this.edgeBottomBox.Size = new System.Drawing.Size(100, 23);
            this.edgeBottomBox.Suffix = "";
            this.edgeBottomBox.TabIndex = 7;
            this.toolTip.SetToolTip(this.edgeBottomBox, "Bottom edge spacing");
            //
            // sourceLabel
            //
            this.sourceLabel.ForeColor = System.Drawing.SystemColors.GrayText;
            this.sourceLabel.Location = new System.Drawing.Point(12, 356);
            this.sourceLabel.Name = "sourceLabel";
            this.sourceLabel.Size = new System.Drawing.Size(380, 28);
            this.sourceLabel.TabIndex = 4;
            this.sourceLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // saveButton
            //
            this.saveButton.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.saveButton.Location = new System.Drawing.Point(402, 356);
            this.saveButton.Name = "saveButton";
            this.saveButton.Size = new System.Drawing.Size(90, 28);
            this.saveButton.TabIndex = 5;
            this.saveButton.Text = "Save";
            this.saveButton.UseVisualStyleBackColor = true;
            //
            // cancelButton
            //
            this.cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.cancelButton.Location = new System.Drawing.Point(498, 356);
            this.cancelButton.Name = "cancelButton";
            this.cancelButton.Size = new System.Drawing.Size(90, 28);
            this.cancelButton.TabIndex = 6;
            this.cancelButton.Text = "Cancel";
            this.cancelButton.UseVisualStyleBackColor = true;
            //
            // NestDefaultsForm
            //
            this.AcceptButton = this.saveButton;
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.CancelButton = this.cancelButton;
            this.ClientSize = new System.Drawing.Size(600, 396);
            this.Controls.Add(this.generalGroup);
            this.Controls.Add(this.plateGroup);
            this.Controls.Add(this.quadrantGroup);
            this.Controls.Add(this.spacingGroup);
            this.Controls.Add(this.sourceLabel);
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
            ((System.ComponentModel.ISupportInitialize)(this.edgeTopBox)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.edgeLeftBox)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.edgeRightBox)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.edgeBottomBox)).EndInit();
            this.generalGroup.ResumeLayout(false);
            this.generalGroup.PerformLayout();
            this.plateGroup.ResumeLayout(false);
            this.plateGroup.PerformLayout();
            this.quadrantGroup.ResumeLayout(false);
            this.spacingGroup.ResumeLayout(false);
            this.spacingGroup.PerformLayout();
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.GroupBox generalGroup;
        private System.Windows.Forms.Label machineLabel;
        private System.Windows.Forms.ComboBox machineCombo;
        private System.Windows.Forms.Label unitsLabel;
        private System.Windows.Forms.RadioButton inchesRadio;
        private System.Windows.Forms.RadioButton millimetersRadio;
        private System.Windows.Forms.GroupBox plateGroup;
        private System.Windows.Forms.Label widthLabel;
        private OpenNest.Controls.NumericUpDown widthBox;
        private System.Windows.Forms.Label lengthLabel;
        private OpenNest.Controls.NumericUpDown lengthBox;
        private System.Windows.Forms.GroupBox quadrantGroup;
        private OpenNest.Controls.QuadrantSelect quadrantSelect1;
        private System.Windows.Forms.Label quadrantHintLabel;
        private System.Windows.Forms.GroupBox spacingGroup;
        private System.Windows.Forms.Label partSpacingLabel;
        private OpenNest.Controls.NumericUpDown partSpacingBox;
        private System.Windows.Forms.Label edgeSpacingLabel;
        private OpenNest.Controls.NumericUpDown edgeTopBox;
        private OpenNest.Controls.NumericUpDown edgeLeftBox;
        private System.Windows.Forms.Label edgePlateLabel;
        private OpenNest.Controls.NumericUpDown edgeRightBox;
        private OpenNest.Controls.NumericUpDown edgeBottomBox;
        private System.Windows.Forms.Label sourceLabel;
        private System.Windows.Forms.Button saveButton;
        private System.Windows.Forms.Button cancelButton;
        private System.Windows.Forms.ToolTip toolTip;
    }
}
