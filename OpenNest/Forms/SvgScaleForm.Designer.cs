namespace OpenNest.Forms
{
    partial class SvgScaleForm
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
            this.labelMessage = new System.Windows.Forms.Label();
            this.radioPixels = new System.Windows.Forms.RadioButton();
            this.radioPoints = new System.Windows.Forms.RadioButton();
            this.radioMillimeters = new System.Windows.Forms.RadioButton();
            this.radioInches = new System.Windows.Forms.RadioButton();
            this.labelSize = new System.Windows.Forms.Label();
            this.buttonOk = new System.Windows.Forms.Button();
            this.buttonCancel = new System.Windows.Forms.Button();
            this.SuspendLayout();
            //
            // labelMessage
            //
            this.labelMessage.Location = new System.Drawing.Point(12, 9);
            this.labelMessage.Name = "labelMessage";
            this.labelMessage.Size = new System.Drawing.Size(376, 34);
            this.labelMessage.TabIndex = 0;
            //
            // radioPixels
            //
            this.radioPixels.AutoSize = true;
            this.radioPixels.Checked = true;
            this.radioPixels.Location = new System.Drawing.Point(15, 50);
            this.radioPixels.Name = "radioPixels";
            this.radioPixels.Size = new System.Drawing.Size(250, 19);
            this.radioPixels.TabIndex = 1;
            this.radioPixels.TabStop = true;
            this.radioPixels.Text = "1/96 inch (web and Inkscape pixels)";
            this.radioPixels.UseVisualStyleBackColor = true;
            this.radioPixels.CheckedChanged += new System.EventHandler(this.UpdateSize);
            //
            // radioPoints
            //
            this.radioPoints.AutoSize = true;
            this.radioPoints.Location = new System.Drawing.Point(15, 75);
            this.radioPoints.Name = "radioPoints";
            this.radioPoints.Size = new System.Drawing.Size(250, 19);
            this.radioPoints.TabIndex = 2;
            this.radioPoints.Text = "1/72 inch (Illustrator points)";
            this.radioPoints.UseVisualStyleBackColor = true;
            this.radioPoints.CheckedChanged += new System.EventHandler(this.UpdateSize);
            //
            // radioMillimeters
            //
            this.radioMillimeters.AutoSize = true;
            this.radioMillimeters.Location = new System.Drawing.Point(15, 100);
            this.radioMillimeters.Name = "radioMillimeters";
            this.radioMillimeters.Size = new System.Drawing.Size(100, 19);
            this.radioMillimeters.TabIndex = 3;
            this.radioMillimeters.Text = "1 millimeter";
            this.radioMillimeters.UseVisualStyleBackColor = true;
            this.radioMillimeters.CheckedChanged += new System.EventHandler(this.UpdateSize);
            //
            // radioInches
            //
            this.radioInches.AutoSize = true;
            this.radioInches.Location = new System.Drawing.Point(15, 125);
            this.radioInches.Name = "radioInches";
            this.radioInches.Size = new System.Drawing.Size(70, 19);
            this.radioInches.TabIndex = 4;
            this.radioInches.Text = "1 inch";
            this.radioInches.UseVisualStyleBackColor = true;
            this.radioInches.CheckedChanged += new System.EventHandler(this.UpdateSize);
            //
            // labelSize
            //
            this.labelSize.Location = new System.Drawing.Point(12, 155);
            this.labelSize.Name = "labelSize";
            this.labelSize.Size = new System.Drawing.Size(376, 20);
            this.labelSize.TabIndex = 5;
            //
            // buttonOk
            //
            this.buttonOk.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.buttonOk.Location = new System.Drawing.Point(232, 185);
            this.buttonOk.Name = "buttonOk";
            this.buttonOk.Size = new System.Drawing.Size(75, 25);
            this.buttonOk.TabIndex = 6;
            this.buttonOk.Text = "OK";
            this.buttonOk.UseVisualStyleBackColor = true;
            //
            // buttonCancel
            //
            this.buttonCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.buttonCancel.Location = new System.Drawing.Point(313, 185);
            this.buttonCancel.Name = "buttonCancel";
            this.buttonCancel.Size = new System.Drawing.Size(75, 25);
            this.buttonCancel.TabIndex = 7;
            this.buttonCancel.Text = "Skip File";
            this.buttonCancel.UseVisualStyleBackColor = true;
            //
            // SvgScaleForm
            //
            this.AcceptButton = this.buttonOk;
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.buttonCancel;
            this.ClientSize = new System.Drawing.Size(400, 222);
            this.Controls.Add(this.buttonCancel);
            this.Controls.Add(this.buttonOk);
            this.Controls.Add(this.labelSize);
            this.Controls.Add(this.radioInches);
            this.Controls.Add(this.radioMillimeters);
            this.Controls.Add(this.radioPoints);
            this.Controls.Add(this.radioPixels);
            this.Controls.Add(this.labelMessage);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "SvgScaleForm";
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "SVG Scale";
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Label labelMessage;
        private System.Windows.Forms.RadioButton radioPixels;
        private System.Windows.Forms.RadioButton radioPoints;
        private System.Windows.Forms.RadioButton radioMillimeters;
        private System.Windows.Forms.RadioButton radioInches;
        private System.Windows.Forms.Label labelSize;
        private System.Windows.Forms.Button buttonOk;
        private System.Windows.Forms.Button buttonCancel;
    }
}
