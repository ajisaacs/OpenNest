namespace OpenNest.Forms
{
    partial class SetValueForm
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
            this.buttonSet = new System.Windows.Forms.Button();
            this.tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
            this.labelIncrement = new System.Windows.Forms.Label();
            this.numericUpDownValue = new OpenNest.Controls.NumericUpDown();
            this.tableLayoutPanel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDownValue)).BeginInit();
            this.SuspendLayout();
            // 
            // buttonSet
            // 
            this.buttonSet.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonSet.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.buttonSet.Location = new System.Drawing.Point(256, 6);
            this.buttonSet.Name = "buttonSet";
            this.buttonSet.Size = new System.Drawing.Size(85, 28);
            this.buttonSet.TabIndex = 2;
            this.buttonSet.Text = "Set";
            this.buttonSet.UseVisualStyleBackColor = true;
            // 
            // tableLayoutPanel1
            // 
            this.tableLayoutPanel1.ColumnCount = 3;
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.tableLayoutPanel1.Controls.Add(this.buttonSet, 2, 0);
            this.tableLayoutPanel1.Controls.Add(this.labelIncrement, 0, 0);
            this.tableLayoutPanel1.Controls.Add(this.numericUpDownValue, 1, 0);
            this.tableLayoutPanel1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tableLayoutPanel1.Location = new System.Drawing.Point(6, 5);
            this.tableLayoutPanel1.Name = "tableLayoutPanel1";
            this.tableLayoutPanel1.RowCount = 1;
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayoutPanel1.Size = new System.Drawing.Size(344, 41);
            this.tableLayoutPanel1.TabIndex = 0;
            // 
            // labelIncrement
            // 
            this.labelIncrement.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.labelIncrement.AutoSize = true;
            this.labelIncrement.Location = new System.Drawing.Point(3, 12);
            this.labelIncrement.Name = "labelIncrement";
            this.labelIncrement.Size = new System.Drawing.Size(72, 16);
            this.labelIncrement.TabIndex = 0;
            this.labelIncrement.Text = "Increment :";
            this.labelIncrement.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // numericUpDownValue
            // 
            this.numericUpDownValue.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.numericUpDownValue.DecimalPlaces = 4;
            this.numericUpDownValue.Location = new System.Drawing.Point(80, 9);
            this.numericUpDownValue.Margin = new System.Windows.Forms.Padding(2);
            this.numericUpDownValue.Maximum = new decimal(new int[] {
            999999,
            0,
            0,
            0});
            this.numericUpDownValue.Name = "numericUpDownValue";
            this.numericUpDownValue.Size = new System.Drawing.Size(171, 22);
            this.numericUpDownValue.TabIndex = 1;
            // 
            // SetValueForm
            // 
            this.AcceptButton = this.buttonSet;
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.ClientSize = new System.Drawing.Size(356, 51);
            this.Controls.Add(this.tableLayoutPanel1);
            this.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "SetValueForm";
            this.Padding = new System.Windows.Forms.Padding(6, 5, 6, 5);
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.tableLayoutPanel1.ResumeLayout(false);
            this.tableLayoutPanel1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDownValue)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Button buttonSet;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel1;
        private System.Windows.Forms.Label labelIncrement;
        private Controls.NumericUpDown numericUpDownValue;
    }
}