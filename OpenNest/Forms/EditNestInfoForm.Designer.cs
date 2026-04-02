namespace OpenNest.Forms
{
    partial class EditNestInfoForm
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
            this.label1 = new System.Windows.Forms.Label();
            this.nameBox = new System.Windows.Forms.TextBox();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.tableLayoutPanel2 = new System.Windows.Forms.TableLayoutPanel();
            this.labelEdgeSpacingLeft = new System.Windows.Forms.Label();
            this.labelEdgeSpacingTop = new System.Windows.Forms.Label();
            this.labelEdgeSpacingRight = new System.Windows.Forms.Label();
            this.labelEdgeSpacingBottom = new System.Windows.Forms.Label();
            this.leftSpacingBox = new OpenNest.Controls.NumericUpDown();
            this.topSpacingBox = new OpenNest.Controls.NumericUpDown();
            this.rightSpacingBox = new OpenNest.Controls.NumericUpDown();
            this.bottomSpacingBox = new OpenNest.Controls.NumericUpDown();
            this.tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
            this.labelSize = new System.Windows.Forms.Label();
            this.sizeBox = new System.Windows.Forms.TextBox();
            this.labelPartSpacing = new System.Windows.Forms.Label();
            this.partSpacingBox = new OpenNest.Controls.NumericUpDown();
            this.labelThk = new System.Windows.Forms.Label();
            this.groupBox3 = new System.Windows.Forms.GroupBox();
            this.quadrantSelect1 = new OpenNest.Controls.QuadrantSelect();
            this.tabControl1 = new System.Windows.Forms.TabControl();
            this.tabPage1 = new System.Windows.Forms.TabPage();
            this.tableLayoutPanel3 = new System.Windows.Forms.TableLayoutPanel();
            this.tableLayoutPanel4 = new System.Windows.Forms.TableLayoutPanel();
            this.radioButton1 = new System.Windows.Forms.RadioButton();
            this.radioButton2 = new System.Windows.Forms.RadioButton();
            this.label2 = new System.Windows.Forms.Label();
            this.thicknessBox = new OpenNest.Controls.NumericUpDown();
            this.label3 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.customerBox = new System.Windows.Forms.TextBox();
            this.textBox1 = new System.Windows.Forms.TextBox();
            this.textBox2 = new System.Windows.Forms.TextBox();
            this.label5 = new System.Windows.Forms.Label();
            this.labelMaterial = new System.Windows.Forms.Label();
            this.materialBox = new System.Windows.Forms.TextBox();
            this.tabPage2 = new System.Windows.Forms.TabPage();
            this.tabPage3 = new System.Windows.Forms.TabPage();
            this.notesBox = new System.Windows.Forms.TextBox();
            this.cancelButton = new System.Windows.Forms.Button();
            this.bottomPanel1 = new OpenNest.Controls.BottomPanel();
            this.applyButton = new System.Windows.Forms.Button();
            this.groupBox1.SuspendLayout();
            this.tableLayoutPanel2.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.leftSpacingBox)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.topSpacingBox)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.rightSpacingBox)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.bottomSpacingBox)).BeginInit();
            this.tableLayoutPanel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.partSpacingBox)).BeginInit();
            this.groupBox3.SuspendLayout();
            this.tabControl1.SuspendLayout();
            this.tabPage1.SuspendLayout();
            this.tableLayoutPanel3.SuspendLayout();
            this.tableLayoutPanel4.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.thicknessBox)).BeginInit();
            this.tabPage2.SuspendLayout();
            this.tabPage3.SuspendLayout();
            this.bottomPanel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // label1
            // 
            this.label1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(3, 11);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(126, 16);
            this.label1.TabIndex = 0;
            this.label1.Text = "Name :";
            // 
            // nameBox
            // 
            this.nameBox.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.nameBox.Location = new System.Drawing.Point(135, 8);
            this.nameBox.Name = "nameBox";
            this.nameBox.ReadOnly = true;
            this.nameBox.Size = new System.Drawing.Size(224, 22);
            this.nameBox.TabIndex = 1;
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.tableLayoutPanel2);
            this.groupBox1.Location = new System.Drawing.Point(6, 100);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(279, 176);
            this.groupBox1.TabIndex = 3;
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
            this.tableLayoutPanel2.Controls.Add(this.leftSpacingBox, 1, 0);
            this.tableLayoutPanel2.Controls.Add(this.topSpacingBox, 1, 1);
            this.tableLayoutPanel2.Controls.Add(this.rightSpacingBox, 1, 2);
            this.tableLayoutPanel2.Controls.Add(this.bottomSpacingBox, 1, 3);
            this.tableLayoutPanel2.Location = new System.Drawing.Point(6, 19);
            this.tableLayoutPanel2.Name = "tableLayoutPanel2";
            this.tableLayoutPanel2.RowCount = 4;
            this.tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 25F));
            this.tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 25F));
            this.tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 25F));
            this.tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 25F));
            this.tableLayoutPanel2.Size = new System.Drawing.Size(267, 151);
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
            this.labelEdgeSpacingBottom.Location = new System.Drawing.Point(3, 123);
            this.labelEdgeSpacingBottom.Name = "labelEdgeSpacingBottom";
            this.labelEdgeSpacingBottom.Size = new System.Drawing.Size(56, 16);
            this.labelEdgeSpacingBottom.TabIndex = 6;
            this.labelEdgeSpacingBottom.Text = "Bottom :";
            this.labelEdgeSpacingBottom.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // leftSpacingBox
            // 
            this.leftSpacingBox.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.leftSpacingBox.DecimalPlaces = 4;
            this.leftSpacingBox.Increment = new decimal(new int[] {
            25,
            0,
            0,
            131072});
            this.leftSpacingBox.Location = new System.Drawing.Point(65, 7);
            this.leftSpacingBox.Maximum = new decimal(new int[] {
            999999,
            0,
            0,
            0});
            this.leftSpacingBox.Name = "leftSpacingBox";
            this.leftSpacingBox.Size = new System.Drawing.Size(199, 22);
            this.leftSpacingBox.Suffix = "";
            this.leftSpacingBox.TabIndex = 1;
            // 
            // topSpacingBox
            // 
            this.topSpacingBox.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.topSpacingBox.DecimalPlaces = 4;
            this.topSpacingBox.Increment = new decimal(new int[] {
            25,
            0,
            0,
            131072});
            this.topSpacingBox.Location = new System.Drawing.Point(65, 44);
            this.topSpacingBox.Maximum = new decimal(new int[] {
            999999,
            0,
            0,
            0});
            this.topSpacingBox.Name = "topSpacingBox";
            this.topSpacingBox.Size = new System.Drawing.Size(199, 22);
            this.topSpacingBox.Suffix = "";
            this.topSpacingBox.TabIndex = 3;
            // 
            // rightSpacingBox
            // 
            this.rightSpacingBox.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.rightSpacingBox.DecimalPlaces = 4;
            this.rightSpacingBox.Increment = new decimal(new int[] {
            25,
            0,
            0,
            131072});
            this.rightSpacingBox.Location = new System.Drawing.Point(65, 81);
            this.rightSpacingBox.Maximum = new decimal(new int[] {
            999999,
            0,
            0,
            0});
            this.rightSpacingBox.Name = "rightSpacingBox";
            this.rightSpacingBox.Size = new System.Drawing.Size(199, 22);
            this.rightSpacingBox.Suffix = "";
            this.rightSpacingBox.TabIndex = 5;
            // 
            // bottomSpacingBox
            // 
            this.bottomSpacingBox.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.bottomSpacingBox.DecimalPlaces = 4;
            this.bottomSpacingBox.Increment = new decimal(new int[] {
            25,
            0,
            0,
            131072});
            this.bottomSpacingBox.Location = new System.Drawing.Point(65, 120);
            this.bottomSpacingBox.Maximum = new decimal(new int[] {
            999999,
            0,
            0,
            0});
            this.bottomSpacingBox.Name = "bottomSpacingBox";
            this.bottomSpacingBox.Size = new System.Drawing.Size(199, 22);
            this.bottomSpacingBox.Suffix = "";
            this.bottomSpacingBox.TabIndex = 7;
            // 
            // tableLayoutPanel1
            // 
            this.tableLayoutPanel1.ColumnCount = 2;
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayoutPanel1.Controls.Add(this.labelSize, 0, 0);
            this.tableLayoutPanel1.Controls.Add(this.sizeBox, 1, 0);
            this.tableLayoutPanel1.Controls.Add(this.labelPartSpacing, 0, 1);
            this.tableLayoutPanel1.Controls.Add(this.partSpacingBox, 1, 1);
            this.tableLayoutPanel1.Location = new System.Drawing.Point(12, 6);
            this.tableLayoutPanel1.Name = "tableLayoutPanel1";
            this.tableLayoutPanel1.RowCount = 2;
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 25F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 25F));
            this.tableLayoutPanel1.Size = new System.Drawing.Size(267, 78);
            this.tableLayoutPanel1.TabIndex = 2;
            // 
            // labelSize
            // 
            this.labelSize.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.labelSize.AutoSize = true;
            this.labelSize.Location = new System.Drawing.Point(3, 11);
            this.labelSize.Name = "labelSize";
            this.labelSize.Size = new System.Drawing.Size(91, 16);
            this.labelSize.TabIndex = 0;
            this.labelSize.Text = "Size :";
            this.labelSize.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // sizeBox
            // 
            this.sizeBox.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.sizeBox.Location = new System.Drawing.Point(100, 8);
            this.sizeBox.Name = "sizeBox";
            this.sizeBox.Size = new System.Drawing.Size(164, 22);
            this.sizeBox.TabIndex = 1;
            this.sizeBox.TextChanged += new System.EventHandler(this.sizeBox_TextChanged);
            // 
            // labelPartSpacing
            // 
            this.labelPartSpacing.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.labelPartSpacing.AutoSize = true;
            this.labelPartSpacing.Location = new System.Drawing.Point(3, 50);
            this.labelPartSpacing.Name = "labelPartSpacing";
            this.labelPartSpacing.Size = new System.Drawing.Size(91, 16);
            this.labelPartSpacing.TabIndex = 2;
            this.labelPartSpacing.Text = "Part Spacing :";
            this.labelPartSpacing.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // partSpacingBox
            // 
            this.partSpacingBox.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.partSpacingBox.DecimalPlaces = 4;
            this.partSpacingBox.Increment = new decimal(new int[] {
            25,
            0,
            0,
            131072});
            this.partSpacingBox.Location = new System.Drawing.Point(100, 47);
            this.partSpacingBox.Maximum = new decimal(new int[] {
            999999,
            0,
            0,
            0});
            this.partSpacingBox.Name = "partSpacingBox";
            this.partSpacingBox.Size = new System.Drawing.Size(164, 22);
            this.partSpacingBox.Suffix = "";
            this.partSpacingBox.TabIndex = 1;
            // 
            // labelThk
            // 
            this.labelThk.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.labelThk.AutoSize = true;
            this.labelThk.Location = new System.Drawing.Point(3, 128);
            this.labelThk.Name = "labelThk";
            this.labelThk.Size = new System.Drawing.Size(126, 16);
            this.labelThk.TabIndex = 6;
            this.labelThk.Text = "Thickness :";
            // 
            // groupBox3
            // 
            this.groupBox3.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.groupBox3.Controls.Add(this.quadrantSelect1);
            this.groupBox3.Location = new System.Drawing.Point(306, 6);
            this.groupBox3.Name = "groupBox3";
            this.groupBox3.Size = new System.Drawing.Size(315, 270);
            this.groupBox3.TabIndex = 7;
            this.groupBox3.TabStop = false;
            this.groupBox3.Text = "Quadrant";
            // 
            // quadrantSelect1
            // 
            this.quadrantSelect1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.quadrantSelect1.BackColor = System.Drawing.Color.White;
            this.quadrantSelect1.Location = new System.Drawing.Point(6, 19);
            this.quadrantSelect1.Name = "quadrantSelect1";
            this.quadrantSelect1.Quadrant = 1;
            this.quadrantSelect1.Size = new System.Drawing.Size(303, 245);
            this.quadrantSelect1.TabIndex = 5;
            this.quadrantSelect1.Text = "quadrantSelect1";
            // 
            // tabControl1
            // 
            this.tabControl1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.tabControl1.Controls.Add(this.tabPage1);
            this.tabControl1.Controls.Add(this.tabPage2);
            this.tabControl1.Controls.Add(this.tabPage3);
            this.tabControl1.ItemSize = new System.Drawing.Size(100, 22);
            this.tabControl1.Location = new System.Drawing.Point(12, 12);
            this.tabControl1.Name = "tabControl1";
            this.tabControl1.SelectedIndex = 0;
            this.tabControl1.Size = new System.Drawing.Size(635, 453);
            this.tabControl1.SizeMode = System.Windows.Forms.TabSizeMode.Fixed;
            this.tabControl1.TabIndex = 0;
            // 
            // tabPage1
            // 
            this.tabPage1.Controls.Add(this.tableLayoutPanel3);
            this.tabPage1.Location = new System.Drawing.Point(4, 26);
            this.tabPage1.Name = "tabPage1";
            this.tabPage1.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage1.Size = new System.Drawing.Size(627, 423);
            this.tabPage1.TabIndex = 0;
            this.tabPage1.Text = "Info";
            this.tabPage1.UseVisualStyleBackColor = true;
            // 
            // tableLayoutPanel3
            // 
            this.tableLayoutPanel3.ColumnCount = 2;
            this.tableLayoutPanel3.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.tableLayoutPanel3.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayoutPanel3.Controls.Add(this.tableLayoutPanel4, 1, 6);
            this.tableLayoutPanel3.Controls.Add(this.label1, 0, 0);
            this.tableLayoutPanel3.Controls.Add(this.nameBox, 1, 0);
            this.tableLayoutPanel3.Controls.Add(this.label2, 0, 5);
            this.tableLayoutPanel3.Controls.Add(this.labelThk, 0, 3);
            this.tableLayoutPanel3.Controls.Add(this.thicknessBox, 1, 3);
            this.tableLayoutPanel3.Controls.Add(this.labelMaterial, 0, 4);
            this.tableLayoutPanel3.Controls.Add(this.materialBox, 1, 4);
            this.tableLayoutPanel3.Controls.Add(this.label3, 0, 1);
            this.tableLayoutPanel3.Controls.Add(this.label4, 0, 2);
            this.tableLayoutPanel3.Controls.Add(this.customerBox, 1, 5);
            this.tableLayoutPanel3.Controls.Add(this.textBox1, 1, 1);
            this.tableLayoutPanel3.Controls.Add(this.textBox2, 1, 2);
            this.tableLayoutPanel3.Controls.Add(this.label5, 0, 6);
            this.tableLayoutPanel3.Location = new System.Drawing.Point(6, 6);
            this.tableLayoutPanel3.Name = "tableLayoutPanel3";
            this.tableLayoutPanel3.RowCount = 7;
            this.tableLayoutPanel3.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 14.28571F));
            this.tableLayoutPanel3.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 14.28571F));
            this.tableLayoutPanel3.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 14.28571F));
            this.tableLayoutPanel3.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 14.28571F));
            this.tableLayoutPanel3.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 14.28571F));
            this.tableLayoutPanel3.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 14.28571F));
            this.tableLayoutPanel3.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 14.28571F));
            this.tableLayoutPanel3.Size = new System.Drawing.Size(362, 280);
            this.tableLayoutPanel3.TabIndex = 0;
            // 
            // tableLayoutPanel4
            // 
            this.tableLayoutPanel4.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.tableLayoutPanel4.ColumnCount = 2;
            this.tableLayoutPanel4.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.tableLayoutPanel4.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.tableLayoutPanel4.Controls.Add(this.radioButton1, 0, 0);
            this.tableLayoutPanel4.Controls.Add(this.radioButton2, 1, 0);
            this.tableLayoutPanel4.Location = new System.Drawing.Point(135, 198);
            this.tableLayoutPanel4.Name = "tableLayoutPanel4";
            this.tableLayoutPanel4.RowCount = 1;
            this.tableLayoutPanel4.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.tableLayoutPanel4.Size = new System.Drawing.Size(224, 39);
            this.tableLayoutPanel4.TabIndex = 1;
            // 
            // radioButton1
            // 
            this.radioButton1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.radioButton1.AutoSize = true;
            this.radioButton1.Location = new System.Drawing.Point(3, 9);
            this.radioButton1.Name = "radioButton1";
            this.radioButton1.Size = new System.Drawing.Size(106, 20);
            this.radioButton1.TabIndex = 0;
            this.radioButton1.TabStop = true;
            this.radioButton1.Text = "Inches";
            this.radioButton1.UseVisualStyleBackColor = true;
            this.radioButton1.CheckedChanged += new System.EventHandler(this.Units_Changed);
            // 
            // radioButton2
            // 
            this.radioButton2.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.radioButton2.AutoSize = true;
            this.radioButton2.Location = new System.Drawing.Point(115, 9);
            this.radioButton2.Name = "radioButton2";
            this.radioButton2.Size = new System.Drawing.Size(106, 20);
            this.radioButton2.TabIndex = 0;
            this.radioButton2.TabStop = true;
            this.radioButton2.Text = "Millimeters";
            this.radioButton2.UseVisualStyleBackColor = true;
            this.radioButton2.CheckedChanged += new System.EventHandler(this.Units_Changed);
            // 
            // label2
            // 
            this.label2.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(3, 167);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(126, 16);
            this.label2.TabIndex = 8;
            this.label2.Text = "Customer :";
            // 
            // thicknessBox
            // 
            this.thicknessBox.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.thicknessBox.DecimalPlaces = 4;
            this.thicknessBox.Increment = new decimal(new int[] {
            25,
            0,
            0,
            131072});
            this.thicknessBox.Location = new System.Drawing.Point(135, 125);
            this.thicknessBox.Maximum = new decimal(new int[] {
            999999,
            0,
            0,
            0});
            this.thicknessBox.Name = "thicknessBox";
            this.thicknessBox.Size = new System.Drawing.Size(224, 22);
            this.thicknessBox.Suffix = "";
            this.thicknessBox.TabIndex = 7;
            //
            // labelMaterial
            //
            this.labelMaterial.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.labelMaterial.AutoSize = true;
            this.labelMaterial.Location = new System.Drawing.Point(3, 162);
            this.labelMaterial.Name = "labelMaterial";
            this.labelMaterial.Size = new System.Drawing.Size(126, 16);
            this.labelMaterial.TabIndex = 10;
            this.labelMaterial.Text = "Material :";
            //
            // materialBox
            //
            this.materialBox.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.materialBox.Location = new System.Drawing.Point(135, 159);
            this.materialBox.Name = "materialBox";
            this.materialBox.Size = new System.Drawing.Size(224, 22);
            this.materialBox.TabIndex = 11;
            //
            // label3
            //
            this.label3.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(3, 50);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(126, 16);
            this.label3.TabIndex = 2;
            this.label3.Text = "Date Created :";
            // 
            // label4
            // 
            this.label4.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(3, 89);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(126, 16);
            this.label4.TabIndex = 4;
            this.label4.Text = "Date Last Modified :";
            // 
            // customerBox
            // 
            this.customerBox.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.customerBox.Location = new System.Drawing.Point(135, 164);
            this.customerBox.Name = "customerBox";
            this.customerBox.Size = new System.Drawing.Size(224, 22);
            this.customerBox.TabIndex = 9;
            // 
            // textBox1
            // 
            this.textBox1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.textBox1.Location = new System.Drawing.Point(135, 47);
            this.textBox1.Name = "textBox1";
            this.textBox1.ReadOnly = true;
            this.textBox1.Size = new System.Drawing.Size(224, 22);
            this.textBox1.TabIndex = 3;
            // 
            // textBox2
            // 
            this.textBox2.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.textBox2.Location = new System.Drawing.Point(135, 86);
            this.textBox2.Name = "textBox2";
            this.textBox2.ReadOnly = true;
            this.textBox2.Size = new System.Drawing.Size(224, 22);
            this.textBox2.TabIndex = 5;
            // 
            // label5
            // 
            this.label5.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(3, 209);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(126, 16);
            this.label5.TabIndex = 8;
            this.label5.Text = "Units :";
            // 
            // tabPage2
            // 
            this.tabPage2.Controls.Add(this.groupBox3);
            this.tabPage2.Controls.Add(this.groupBox1);
            this.tabPage2.Controls.Add(this.tableLayoutPanel1);
            this.tabPage2.Location = new System.Drawing.Point(4, 26);
            this.tabPage2.Name = "tabPage2";
            this.tabPage2.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage2.Size = new System.Drawing.Size(627, 423);
            this.tabPage2.TabIndex = 1;
            this.tabPage2.Text = "Defaults";
            this.tabPage2.UseVisualStyleBackColor = true;
            // 
            // tabPage3
            // 
            this.tabPage3.Controls.Add(this.notesBox);
            this.tabPage3.Location = new System.Drawing.Point(4, 26);
            this.tabPage3.Name = "tabPage3";
            this.tabPage3.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage3.Size = new System.Drawing.Size(627, 423);
            this.tabPage3.TabIndex = 2;
            this.tabPage3.Text = "Notes";
            this.tabPage3.UseVisualStyleBackColor = true;
            // 
            // notesBox
            // 
            this.notesBox.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.notesBox.Location = new System.Drawing.Point(6, 6);
            this.notesBox.Multiline = true;
            this.notesBox.Name = "notesBox";
            this.notesBox.Size = new System.Drawing.Size(615, 408);
            this.notesBox.TabIndex = 2;
            // 
            // cancelButton
            // 
            this.cancelButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.cancelButton.Location = new System.Drawing.Point(557, 10);
            this.cancelButton.Name = "cancelButton";
            this.cancelButton.Size = new System.Drawing.Size(90, 28);
            this.cancelButton.TabIndex = 1;
            this.cancelButton.Text = "Cancel";
            this.cancelButton.UseVisualStyleBackColor = true;
            // 
            // bottomPanel1
            // 
            this.bottomPanel1.Controls.Add(this.applyButton);
            this.bottomPanel1.Controls.Add(this.cancelButton);
            this.bottomPanel1.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.bottomPanel1.Location = new System.Drawing.Point(0, 471);
            this.bottomPanel1.Name = "bottomPanel1";
            this.bottomPanel1.Size = new System.Drawing.Size(659, 50);
            this.bottomPanel1.TabIndex = 1;
            // 
            // applyButton
            // 
            this.applyButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.applyButton.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.applyButton.Enabled = false;
            this.applyButton.Location = new System.Drawing.Point(461, 10);
            this.applyButton.Name = "applyButton";
            this.applyButton.Size = new System.Drawing.Size(90, 28);
            this.applyButton.TabIndex = 0;
            this.applyButton.Text = "Done";
            this.applyButton.UseVisualStyleBackColor = true;
            // 
            // EditNestInfoForm
            // 
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.CancelButton = this.cancelButton;
            this.ClientSize = new System.Drawing.Size(659, 521);
            this.Controls.Add(this.tabControl1);
            this.Controls.Add(this.bottomPanel1);
            this.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "EditNestInfoForm";
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Edit Nest";
            this.groupBox1.ResumeLayout(false);
            this.tableLayoutPanel2.ResumeLayout(false);
            this.tableLayoutPanel2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.leftSpacingBox)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.topSpacingBox)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.rightSpacingBox)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.bottomSpacingBox)).EndInit();
            this.tableLayoutPanel1.ResumeLayout(false);
            this.tableLayoutPanel1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.partSpacingBox)).EndInit();
            this.groupBox3.ResumeLayout(false);
            this.tabControl1.ResumeLayout(false);
            this.tabPage1.ResumeLayout(false);
            this.tableLayoutPanel3.ResumeLayout(false);
            this.tableLayoutPanel3.PerformLayout();
            this.tableLayoutPanel4.ResumeLayout(false);
            this.tableLayoutPanel4.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.thicknessBox)).EndInit();
            this.tabPage2.ResumeLayout(false);
            this.tabPage3.ResumeLayout(false);
            this.tabPage3.PerformLayout();
            this.bottomPanel1.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox nameBox;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel2;
        private System.Windows.Forms.Label labelEdgeSpacingLeft;
        private System.Windows.Forms.Label labelEdgeSpacingTop;
        private System.Windows.Forms.Label labelEdgeSpacingRight;
        private System.Windows.Forms.Label labelEdgeSpacingBottom;
        private Controls.NumericUpDown leftSpacingBox;
        private Controls.NumericUpDown topSpacingBox;
        private Controls.NumericUpDown rightSpacingBox;
        private Controls.NumericUpDown bottomSpacingBox;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel1;
        private System.Windows.Forms.Label labelSize;
        private System.Windows.Forms.TextBox sizeBox;
        private Controls.NumericUpDown thicknessBox;
        private System.Windows.Forms.Label labelThk;
        private System.Windows.Forms.Label labelPartSpacing;
        private Controls.NumericUpDown partSpacingBox;
        private Controls.BottomPanel bottomPanel1;
        private System.Windows.Forms.GroupBox groupBox3;
        private Controls.QuadrantSelect quadrantSelect1;
        private System.Windows.Forms.Button applyButton;
        private System.Windows.Forms.Button cancelButton;
        private System.Windows.Forms.TabControl tabControl1;
        private System.Windows.Forms.TabPage tabPage1;
        private System.Windows.Forms.TabPage tabPage2;
        private System.Windows.Forms.TabPage tabPage3;
        private System.Windows.Forms.TextBox notesBox;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel3;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.TextBox customerBox;
        private System.Windows.Forms.TextBox textBox1;
        private System.Windows.Forms.TextBox textBox2;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel4;
        private System.Windows.Forms.RadioButton radioButton1;
        private System.Windows.Forms.RadioButton radioButton2;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.Label labelMaterial;
        private System.Windows.Forms.TextBox materialBox;
    }
}