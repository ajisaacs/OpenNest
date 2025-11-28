namespace OpenNest.Forms
{
    partial class EditPlateForm
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
            this.labelQty = new System.Windows.Forms.Label();
            this.labelSize = new System.Windows.Forms.Label();
            this.textBoxSize = new System.Windows.Forms.TextBox();
            this.labelThk = new System.Windows.Forms.Label();
            this.labelPartSpacing = new System.Windows.Forms.Label();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.tableLayoutPanel2 = new System.Windows.Forms.TableLayoutPanel();
            this.labelEdgeSpacingLeft = new System.Windows.Forms.Label();
            this.labelEdgeSpacingTop = new System.Windows.Forms.Label();
            this.labelEdgeSpacingRight = new System.Windows.Forms.Label();
            this.labelEdgeSpacingBottom = new System.Windows.Forms.Label();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.applyButton = new System.Windows.Forms.Button();
            this.cancelButton = new System.Windows.Forms.Button();
            this.bottomPanel1 = new OpenNest.Controls.BottomPanel();
            this.quadrantSelect1 = new OpenNest.Controls.QuadrantSelect();
            this.numericUpDownEdgeSpacingLeft = new OpenNest.Controls.NumericUpDown();
            this.numericUpDownEdgeSpacingTop = new OpenNest.Controls.NumericUpDown();
            this.numericUpDownEdgeSpacingRight = new OpenNest.Controls.NumericUpDown();
            this.numericUpDownEdgeSpacingBottom = new OpenNest.Controls.NumericUpDown();
            this.numericUpDownQty = new OpenNest.Controls.NumericUpDown();
            this.numericUpDownThickness = new OpenNest.Controls.NumericUpDown();
            this.numericUpDownPartSpacing = new OpenNest.Controls.NumericUpDown();
            this.tableLayoutPanel1.SuspendLayout();
            this.groupBox1.SuspendLayout();
            this.tableLayoutPanel2.SuspendLayout();
            this.groupBox2.SuspendLayout();
            this.bottomPanel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDownEdgeSpacingLeft)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDownEdgeSpacingTop)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDownEdgeSpacingRight)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDownEdgeSpacingBottom)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDownQty)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDownThickness)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDownPartSpacing)).BeginInit();
            this.SuspendLayout();
            // 
            // tableLayoutPanel1
            // 
            this.tableLayoutPanel1.ColumnCount = 2;
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayoutPanel1.Controls.Add(this.labelQty, 0, 1);
            this.tableLayoutPanel1.Controls.Add(this.labelSize, 0, 0);
            this.tableLayoutPanel1.Controls.Add(this.textBoxSize, 1, 0);
            this.tableLayoutPanel1.Controls.Add(this.numericUpDownQty, 1, 1);
            this.tableLayoutPanel1.Controls.Add(this.numericUpDownThickness, 1, 2);
            this.tableLayoutPanel1.Controls.Add(this.labelThk, 0, 2);
            this.tableLayoutPanel1.Controls.Add(this.labelPartSpacing, 0, 3);
            this.tableLayoutPanel1.Controls.Add(this.numericUpDownPartSpacing, 1, 3);
            this.tableLayoutPanel1.Location = new System.Drawing.Point(18, 12);
            this.tableLayoutPanel1.Name = "tableLayoutPanel1";
            this.tableLayoutPanel1.RowCount = 4;
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 25F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 25F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 25F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 25F));
            this.tableLayoutPanel1.Size = new System.Drawing.Size(236, 144);
            this.tableLayoutPanel1.TabIndex = 0;
            // 
            // labelQty
            // 
            this.labelQty.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.labelQty.AutoSize = true;
            this.labelQty.Location = new System.Drawing.Point(3, 46);
            this.labelQty.Name = "labelQty";
            this.labelQty.Size = new System.Drawing.Size(91, 16);
            this.labelQty.TabIndex = 2;
            this.labelQty.Text = "Quantity :";
            this.labelQty.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // labelSize
            // 
            this.labelSize.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.labelSize.AutoSize = true;
            this.labelSize.Location = new System.Drawing.Point(3, 10);
            this.labelSize.Name = "labelSize";
            this.labelSize.Size = new System.Drawing.Size(91, 16);
            this.labelSize.TabIndex = 0;
            this.labelSize.Text = "Size :";
            this.labelSize.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // textBoxSize
            // 
            this.textBoxSize.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.textBoxSize.Location = new System.Drawing.Point(100, 7);
            this.textBoxSize.Name = "textBoxSize";
            this.textBoxSize.Size = new System.Drawing.Size(133, 22);
            this.textBoxSize.TabIndex = 1;
            this.textBoxSize.TextChanged += new System.EventHandler(this.textBox1_TextChanged);
            // 
            // labelThk
            // 
            this.labelThk.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.labelThk.AutoSize = true;
            this.labelThk.Location = new System.Drawing.Point(3, 82);
            this.labelThk.Name = "labelThk";
            this.labelThk.Size = new System.Drawing.Size(91, 16);
            this.labelThk.TabIndex = 4;
            this.labelThk.Text = "Thickness :";
            this.labelThk.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // labelPartSpacing
            // 
            this.labelPartSpacing.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.labelPartSpacing.AutoSize = true;
            this.labelPartSpacing.Location = new System.Drawing.Point(3, 118);
            this.labelPartSpacing.Name = "labelPartSpacing";
            this.labelPartSpacing.Size = new System.Drawing.Size(91, 16);
            this.labelPartSpacing.TabIndex = 6;
            this.labelPartSpacing.Text = "Part Spacing :";
            this.labelPartSpacing.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.tableLayoutPanel2);
            this.groupBox1.Location = new System.Drawing.Point(12, 173);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(248, 175);
            this.groupBox1.TabIndex = 1;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "Edge Spacing";
            // 
            // tableLayoutPanel2
            // 
            this.tableLayoutPanel2.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.tableLayoutPanel2.ColumnCount = 2;
            this.tableLayoutPanel2.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.tableLayoutPanel2.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayoutPanel2.Controls.Add(this.labelEdgeSpacingLeft, 0, 0);
            this.tableLayoutPanel2.Controls.Add(this.labelEdgeSpacingTop, 0, 1);
            this.tableLayoutPanel2.Controls.Add(this.labelEdgeSpacingRight, 0, 2);
            this.tableLayoutPanel2.Controls.Add(this.labelEdgeSpacingBottom, 0, 3);
            this.tableLayoutPanel2.Controls.Add(this.numericUpDownEdgeSpacingLeft, 1, 0);
            this.tableLayoutPanel2.Controls.Add(this.numericUpDownEdgeSpacingTop, 1, 1);
            this.tableLayoutPanel2.Controls.Add(this.numericUpDownEdgeSpacingRight, 1, 2);
            this.tableLayoutPanel2.Controls.Add(this.numericUpDownEdgeSpacingBottom, 1, 3);
            this.tableLayoutPanel2.Location = new System.Drawing.Point(6, 21);
            this.tableLayoutPanel2.Name = "tableLayoutPanel2";
            this.tableLayoutPanel2.RowCount = 4;
            this.tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 25F));
            this.tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 25F));
            this.tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 25F));
            this.tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 25F));
            this.tableLayoutPanel2.Size = new System.Drawing.Size(236, 148);
            this.tableLayoutPanel2.TabIndex = 0;
            // 
            // labelEdgeSpacingLeft
            // 
            this.labelEdgeSpacingLeft.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.labelEdgeSpacingLeft.AutoSize = true;
            this.labelEdgeSpacingLeft.Location = new System.Drawing.Point(3, 10);
            this.labelEdgeSpacingLeft.Name = "labelEdgeSpacingLeft";
            this.labelEdgeSpacingLeft.Size = new System.Drawing.Size(56, 16);
            this.labelEdgeSpacingLeft.TabIndex = 0;
            this.labelEdgeSpacingLeft.Text = "Left :";
            this.labelEdgeSpacingLeft.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // labelEdgeSpacingTop
            // 
            this.labelEdgeSpacingTop.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.labelEdgeSpacingTop.AutoSize = true;
            this.labelEdgeSpacingTop.Location = new System.Drawing.Point(3, 47);
            this.labelEdgeSpacingTop.Name = "labelEdgeSpacingTop";
            this.labelEdgeSpacingTop.Size = new System.Drawing.Size(56, 16);
            this.labelEdgeSpacingTop.TabIndex = 2;
            this.labelEdgeSpacingTop.Text = "Top :";
            this.labelEdgeSpacingTop.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // labelEdgeSpacingRight
            // 
            this.labelEdgeSpacingRight.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.labelEdgeSpacingRight.AutoSize = true;
            this.labelEdgeSpacingRight.Location = new System.Drawing.Point(3, 84);
            this.labelEdgeSpacingRight.Name = "labelEdgeSpacingRight";
            this.labelEdgeSpacingRight.Size = new System.Drawing.Size(56, 16);
            this.labelEdgeSpacingRight.TabIndex = 4;
            this.labelEdgeSpacingRight.Text = "Right :";
            this.labelEdgeSpacingRight.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // labelEdgeSpacingBottom
            // 
            this.labelEdgeSpacingBottom.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.labelEdgeSpacingBottom.AutoSize = true;
            this.labelEdgeSpacingBottom.Location = new System.Drawing.Point(3, 121);
            this.labelEdgeSpacingBottom.Name = "labelEdgeSpacingBottom";
            this.labelEdgeSpacingBottom.Size = new System.Drawing.Size(56, 16);
            this.labelEdgeSpacingBottom.TabIndex = 6;
            this.labelEdgeSpacingBottom.Text = "Bottom :";
            this.labelEdgeSpacingBottom.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // groupBox2
            // 
            this.groupBox2.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.groupBox2.Controls.Add(this.quadrantSelect1);
            this.groupBox2.Location = new System.Drawing.Point(277, 12);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Size = new System.Drawing.Size(261, 209);
            this.groupBox2.TabIndex = 6;
            this.groupBox2.TabStop = false;
            this.groupBox2.Text = "Quadrant";
            // 
            // applyButton
            // 
            this.applyButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.applyButton.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.applyButton.Enabled = false;
            this.applyButton.Location = new System.Drawing.Point(352, 11);
            this.applyButton.Name = "applyButton";
            this.applyButton.Size = new System.Drawing.Size(90, 28);
            this.applyButton.TabIndex = 3;
            this.applyButton.Text = "Done";
            this.applyButton.UseVisualStyleBackColor = true;
            this.applyButton.Click += new System.EventHandler(this.button1_Click);
            // 
            // cancelButton
            // 
            this.cancelButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.cancelButton.Location = new System.Drawing.Point(448, 11);
            this.cancelButton.Name = "cancelButton";
            this.cancelButton.Size = new System.Drawing.Size(90, 28);
            this.cancelButton.TabIndex = 4;
            this.cancelButton.Text = "Cancel";
            this.cancelButton.UseVisualStyleBackColor = true;
            // 
            // bottomPanel1
            // 
            this.bottomPanel1.Controls.Add(this.applyButton);
            this.bottomPanel1.Controls.Add(this.cancelButton);
            this.bottomPanel1.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.bottomPanel1.Location = new System.Drawing.Point(0, 359);
            this.bottomPanel1.Name = "bottomPanel1";
            this.bottomPanel1.Size = new System.Drawing.Size(550, 50);
            this.bottomPanel1.TabIndex = 7;
            // 
            // quadrantSelect1
            // 
            this.quadrantSelect1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.quadrantSelect1.Location = new System.Drawing.Point(6, 19);
            this.quadrantSelect1.Name = "quadrantSelect1";
            this.quadrantSelect1.Quadrant = 1;
            this.quadrantSelect1.Size = new System.Drawing.Size(249, 184);
            this.quadrantSelect1.TabIndex = 5;
            this.quadrantSelect1.Text = "quadrantSelect1";
            // 
            // numericUpDownEdgeSpacingLeft
            // 
            this.numericUpDownEdgeSpacingLeft.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.numericUpDownEdgeSpacingLeft.DecimalPlaces = 4;
            this.numericUpDownEdgeSpacingLeft.Increment = new decimal(new int[] {
            25,
            0,
            0,
            131072});
            this.numericUpDownEdgeSpacingLeft.Location = new System.Drawing.Point(65, 7);
            this.numericUpDownEdgeSpacingLeft.Name = "numericUpDownEdgeSpacingLeft";
            this.numericUpDownEdgeSpacingLeft.Size = new System.Drawing.Size(168, 22);
            this.numericUpDownEdgeSpacingLeft.Suffix = "";
            this.numericUpDownEdgeSpacingLeft.TabIndex = 1;
            this.numericUpDownEdgeSpacingLeft.ValueChanged += new System.EventHandler(this.spacing_ValueChanged);
            // 
            // numericUpDownEdgeSpacingTop
            // 
            this.numericUpDownEdgeSpacingTop.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.numericUpDownEdgeSpacingTop.DecimalPlaces = 4;
            this.numericUpDownEdgeSpacingTop.Increment = new decimal(new int[] {
            25,
            0,
            0,
            131072});
            this.numericUpDownEdgeSpacingTop.Location = new System.Drawing.Point(65, 44);
            this.numericUpDownEdgeSpacingTop.Name = "numericUpDownEdgeSpacingTop";
            this.numericUpDownEdgeSpacingTop.Size = new System.Drawing.Size(168, 22);
            this.numericUpDownEdgeSpacingTop.Suffix = "";
            this.numericUpDownEdgeSpacingTop.TabIndex = 3;
            this.numericUpDownEdgeSpacingTop.ValueChanged += new System.EventHandler(this.spacing_ValueChanged);
            // 
            // numericUpDownEdgeSpacingRight
            // 
            this.numericUpDownEdgeSpacingRight.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.numericUpDownEdgeSpacingRight.DecimalPlaces = 4;
            this.numericUpDownEdgeSpacingRight.Increment = new decimal(new int[] {
            25,
            0,
            0,
            131072});
            this.numericUpDownEdgeSpacingRight.Location = new System.Drawing.Point(65, 81);
            this.numericUpDownEdgeSpacingRight.Name = "numericUpDownEdgeSpacingRight";
            this.numericUpDownEdgeSpacingRight.Size = new System.Drawing.Size(168, 22);
            this.numericUpDownEdgeSpacingRight.Suffix = "";
            this.numericUpDownEdgeSpacingRight.TabIndex = 5;
            this.numericUpDownEdgeSpacingRight.ValueChanged += new System.EventHandler(this.spacing_ValueChanged);
            // 
            // numericUpDownEdgeSpacingBottom
            // 
            this.numericUpDownEdgeSpacingBottom.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.numericUpDownEdgeSpacingBottom.DecimalPlaces = 4;
            this.numericUpDownEdgeSpacingBottom.Increment = new decimal(new int[] {
            25,
            0,
            0,
            131072});
            this.numericUpDownEdgeSpacingBottom.Location = new System.Drawing.Point(65, 118);
            this.numericUpDownEdgeSpacingBottom.Name = "numericUpDownEdgeSpacingBottom";
            this.numericUpDownEdgeSpacingBottom.Size = new System.Drawing.Size(168, 22);
            this.numericUpDownEdgeSpacingBottom.Suffix = "";
            this.numericUpDownEdgeSpacingBottom.TabIndex = 7;
            this.numericUpDownEdgeSpacingBottom.ValueChanged += new System.EventHandler(this.spacing_ValueChanged);
            // 
            // numericUpDownQty
            // 
            this.numericUpDownQty.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.numericUpDownQty.Location = new System.Drawing.Point(100, 43);
            this.numericUpDownQty.Name = "numericUpDownQty";
            this.numericUpDownQty.Size = new System.Drawing.Size(133, 22);
            this.numericUpDownQty.Suffix = "";
            this.numericUpDownQty.TabIndex = 3;
            // 
            // numericUpDownThickness
            // 
            this.numericUpDownThickness.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.numericUpDownThickness.DecimalPlaces = 4;
            this.numericUpDownThickness.Increment = new decimal(new int[] {
            25,
            0,
            0,
            131072});
            this.numericUpDownThickness.Location = new System.Drawing.Point(100, 79);
            this.numericUpDownThickness.Name = "numericUpDownThickness";
            this.numericUpDownThickness.Size = new System.Drawing.Size(133, 22);
            this.numericUpDownThickness.Suffix = "";
            this.numericUpDownThickness.TabIndex = 5;
            // 
            // numericUpDownPartSpacing
            // 
            this.numericUpDownPartSpacing.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.numericUpDownPartSpacing.DecimalPlaces = 4;
            this.numericUpDownPartSpacing.Increment = new decimal(new int[] {
            25,
            0,
            0,
            131072});
            this.numericUpDownPartSpacing.Location = new System.Drawing.Point(100, 115);
            this.numericUpDownPartSpacing.Name = "numericUpDownPartSpacing";
            this.numericUpDownPartSpacing.Size = new System.Drawing.Size(133, 22);
            this.numericUpDownPartSpacing.Suffix = "";
            this.numericUpDownPartSpacing.TabIndex = 7;
            // 
            // EditPlateForm
            // 
            this.AcceptButton = this.applyButton;
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.CancelButton = this.cancelButton;
            this.ClientSize = new System.Drawing.Size(550, 409);
            this.Controls.Add(this.bottomPanel1);
            this.Controls.Add(this.groupBox2);
            this.Controls.Add(this.groupBox1);
            this.Controls.Add(this.tableLayoutPanel1);
            this.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "EditPlateForm";
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Edit Plate";
            this.tableLayoutPanel1.ResumeLayout(false);
            this.tableLayoutPanel1.PerformLayout();
            this.groupBox1.ResumeLayout(false);
            this.tableLayoutPanel2.ResumeLayout(false);
            this.tableLayoutPanel2.PerformLayout();
            this.groupBox2.ResumeLayout(false);
            this.bottomPanel1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDownEdgeSpacingLeft)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDownEdgeSpacingTop)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDownEdgeSpacingRight)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDownEdgeSpacingBottom)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDownQty)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDownThickness)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDownPartSpacing)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel1;
        private System.Windows.Forms.Label labelSize;
        private System.Windows.Forms.Button applyButton;
        private System.Windows.Forms.TextBox textBoxSize;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel2;
        private System.Windows.Forms.Label labelEdgeSpacingLeft;
        private System.Windows.Forms.Label labelEdgeSpacingTop;
        private System.Windows.Forms.Label labelEdgeSpacingRight;
        private System.Windows.Forms.Label labelEdgeSpacingBottom;
        private Controls.NumericUpDown numericUpDownEdgeSpacingLeft;
        private Controls.NumericUpDown numericUpDownEdgeSpacingTop;
        private Controls.NumericUpDown numericUpDownEdgeSpacingRight;
        private Controls.NumericUpDown numericUpDownEdgeSpacingBottom;
        private System.Windows.Forms.Button cancelButton;
        private System.Windows.Forms.Label labelQty;
        private Controls.NumericUpDown numericUpDownQty;
        private Controls.QuadrantSelect quadrantSelect1;
        private System.Windows.Forms.GroupBox groupBox2;
        private Controls.NumericUpDown numericUpDownThickness;
        private System.Windows.Forms.Label labelThk;
        private System.Windows.Forms.Label labelPartSpacing;
        private Controls.NumericUpDown numericUpDownPartSpacing;
        private Controls.BottomPanel bottomPanel1;
    }
}