namespace OpenNest.Forms
{
    partial class PatternTileForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.topPanel = new System.Windows.Forms.FlowLayoutPanel();
            this.lblDrawingA = new System.Windows.Forms.Label();
            this.cboDrawingA = new System.Windows.Forms.ComboBox();
            this.lblDrawingB = new System.Windows.Forms.Label();
            this.cboDrawingB = new System.Windows.Forms.ComboBox();
            this.lblPlateSize = new System.Windows.Forms.Label();
            this.txtPlateSize = new System.Windows.Forms.TextBox();
            this.lblPartSpacing = new System.Windows.Forms.Label();
            this.nudPartSpacing = new System.Windows.Forms.NumericUpDown();
            this.btnAutoArrange = new System.Windows.Forms.Button();
            this.btnApply = new System.Windows.Forms.Button();
            this.splitContainer = new System.Windows.Forms.SplitContainer();
            this.topPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.nudPartSpacing)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer)).BeginInit();
            this.splitContainer.SuspendLayout();
            this.SuspendLayout();
            //
            // topPanel
            //
            this.topPanel.Controls.Add(this.lblDrawingA);
            this.topPanel.Controls.Add(this.cboDrawingA);
            this.topPanel.Controls.Add(this.lblDrawingB);
            this.topPanel.Controls.Add(this.cboDrawingB);
            this.topPanel.Controls.Add(this.lblPlateSize);
            this.topPanel.Controls.Add(this.txtPlateSize);
            this.topPanel.Controls.Add(this.lblPartSpacing);
            this.topPanel.Controls.Add(this.nudPartSpacing);
            this.topPanel.Controls.Add(this.btnAutoArrange);
            this.topPanel.Controls.Add(this.btnApply);
            this.topPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.topPanel.Height = 36;
            this.topPanel.Name = "topPanel";
            this.topPanel.WrapContents = false;
            this.topPanel.Padding = new System.Windows.Forms.Padding(4, 2, 4, 2);
            //
            // lblDrawingA
            //
            this.lblDrawingA.AutoSize = true;
            this.lblDrawingA.Margin = new System.Windows.Forms.Padding(3, 5, 0, 0);
            this.lblDrawingA.Name = "lblDrawingA";
            this.lblDrawingA.Text = "Drawing A:";
            //
            // cboDrawingA
            //
            this.cboDrawingA.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboDrawingA.Margin = new System.Windows.Forms.Padding(3, 3, 0, 0);
            this.cboDrawingA.Name = "cboDrawingA";
            this.cboDrawingA.Width = 130;
            //
            // lblDrawingB
            //
            this.lblDrawingB.AutoSize = true;
            this.lblDrawingB.Margin = new System.Windows.Forms.Padding(10, 5, 0, 0);
            this.lblDrawingB.Name = "lblDrawingB";
            this.lblDrawingB.Text = "Drawing B:";
            //
            // cboDrawingB
            //
            this.cboDrawingB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboDrawingB.Margin = new System.Windows.Forms.Padding(3, 3, 0, 0);
            this.cboDrawingB.Name = "cboDrawingB";
            this.cboDrawingB.Width = 130;
            //
            // lblPlateSize
            //
            this.lblPlateSize.AutoSize = true;
            this.lblPlateSize.Margin = new System.Windows.Forms.Padding(10, 5, 0, 0);
            this.lblPlateSize.Name = "lblPlateSize";
            this.lblPlateSize.Text = "Plate:";
            //
            // txtPlateSize
            //
            this.txtPlateSize.Margin = new System.Windows.Forms.Padding(3, 3, 0, 0);
            this.txtPlateSize.Name = "txtPlateSize";
            this.txtPlateSize.Width = 90;
            //
            // lblPartSpacing
            //
            this.lblPartSpacing.AutoSize = true;
            this.lblPartSpacing.Margin = new System.Windows.Forms.Padding(10, 5, 0, 0);
            this.lblPartSpacing.Name = "lblPartSpacing";
            this.lblPartSpacing.Text = "Spacing:";
            //
            // nudPartSpacing
            //
            this.nudPartSpacing.DecimalPlaces = 2;
            this.nudPartSpacing.Increment = new decimal(new int[] { 25, 0, 0, 131072 });
            this.nudPartSpacing.Maximum = new decimal(new int[] { 100, 0, 0, 0 });
            this.nudPartSpacing.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
            this.nudPartSpacing.Margin = new System.Windows.Forms.Padding(3, 3, 0, 0);
            this.nudPartSpacing.Name = "nudPartSpacing";
            this.nudPartSpacing.Width = 70;
            //
            // btnAutoArrange
            //
            this.btnAutoArrange.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnAutoArrange.Margin = new System.Windows.Forms.Padding(10, 3, 0, 0);
            this.btnAutoArrange.Name = "btnAutoArrange";
            this.btnAutoArrange.Size = new System.Drawing.Size(100, 26);
            this.btnAutoArrange.Text = "Auto Arrange";
            //
            // btnApply
            //
            this.btnApply.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnApply.Margin = new System.Windows.Forms.Padding(6, 3, 0, 0);
            this.btnApply.Name = "btnApply";
            this.btnApply.Size = new System.Drawing.Size(80, 26);
            this.btnApply.Text = "Apply";
            //
            // splitContainer
            //
            this.splitContainer.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer.Name = "splitContainer";
            this.splitContainer.SplitterDistance = 350;
            this.splitContainer.TabIndex = 1;
            //
            // PatternTileForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(900, 550);
            this.Controls.Add(this.splitContainer);
            this.Controls.Add(this.topPanel);
            this.MinimumSize = new System.Drawing.Size(700, 400);
            this.Name = "PatternTileForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Pattern Tile";
            this.topPanel.ResumeLayout(false);
            this.topPanel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.nudPartSpacing)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer)).EndInit();
            this.splitContainer.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        private System.Windows.Forms.FlowLayoutPanel topPanel;
        private System.Windows.Forms.Label lblDrawingA;
        private System.Windows.Forms.ComboBox cboDrawingA;
        private System.Windows.Forms.Label lblDrawingB;
        private System.Windows.Forms.ComboBox cboDrawingB;
        private System.Windows.Forms.Label lblPlateSize;
        private System.Windows.Forms.TextBox txtPlateSize;
        private System.Windows.Forms.Label lblPartSpacing;
        private System.Windows.Forms.NumericUpDown nudPartSpacing;
        private System.Windows.Forms.Button btnAutoArrange;
        private System.Windows.Forms.Button btnApply;
        private System.Windows.Forms.SplitContainer splitContainer;
    }
}
