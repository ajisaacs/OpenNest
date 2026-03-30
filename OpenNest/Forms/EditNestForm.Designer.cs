namespace OpenNest.Forms
{
    partial class EditNestForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(EditNestForm));
            this.splitContainer = new System.Windows.Forms.SplitContainer();
            this.tabControl1 = new System.Windows.Forms.TabControl();
            this.tabPage1 = new System.Windows.Forms.TabPage();
            this.platesListView = new System.Windows.Forms.ListView();
            this.plateNumColumn = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.sizeColumn = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.qtyColumn = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.toolStrip1 = new System.Windows.Forms.ToolStrip();
            this.toolStripButton1 = new System.Windows.Forms.ToolStripButton();
            this.btnAssignLeadIns = new System.Windows.Forms.ToolStripButton();
            this.btnPlaceLeadIn = new System.Windows.Forms.ToolStripButton();
            this.tabPage2 = new System.Windows.Forms.TabPage();
            this.drawingListBox1 = new OpenNest.Controls.DrawingListBox();
            this.toolStrip2 = new System.Windows.Forms.ToolStrip();
            this.toolStripButton2 = new System.Windows.Forms.ToolStripButton();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.toolStripButton3 = new System.Windows.Forms.ToolStripButton();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer)).BeginInit();
            this.splitContainer.Panel1.SuspendLayout();
            this.splitContainer.SuspendLayout();
            this.tabControl1.SuspendLayout();
            this.tabPage1.SuspendLayout();
            this.toolStrip1.SuspendLayout();
            this.tabPage2.SuspendLayout();
            this.toolStrip2.SuspendLayout();
            this.SuspendLayout();
            // 
            // splitContainer
            // 
            this.splitContainer.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
            this.splitContainer.Location = new System.Drawing.Point(0, 0);
            this.splitContainer.Name = "splitContainer";
            // 
            // splitContainer.Panel1
            // 
            this.splitContainer.Panel1.Controls.Add(this.tabControl1);
            this.splitContainer.Size = new System.Drawing.Size(735, 396);
            this.splitContainer.SplitterDistance = 241;
            this.splitContainer.TabIndex = 8;
            // 
            // tabControl1
            // 
            this.tabControl1.Controls.Add(this.tabPage1);
            this.tabControl1.Controls.Add(this.tabPage2);
            this.tabControl1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabControl1.ItemSize = new System.Drawing.Size(100, 22);
            this.tabControl1.Location = new System.Drawing.Point(0, 0);
            this.tabControl1.Name = "tabControl1";
            this.tabControl1.SelectedIndex = 0;
            this.tabControl1.Size = new System.Drawing.Size(241, 396);
            this.tabControl1.SizeMode = System.Windows.Forms.TabSizeMode.Fixed;
            this.tabControl1.TabIndex = 1;
            // 
            // tabPage1
            // 
            this.tabPage1.Controls.Add(this.platesListView);
            this.tabPage1.Controls.Add(this.toolStrip1);
            this.tabPage1.Location = new System.Drawing.Point(4, 22);
            this.tabPage1.Name = "tabPage1";
            this.tabPage1.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage1.Size = new System.Drawing.Size(233, 370);
            this.tabPage1.TabIndex = 0;
            this.tabPage1.Text = "Plates";
            this.tabPage1.UseVisualStyleBackColor = true;
            // 
            // platesListView
            // 
            this.platesListView.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.platesListView.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.plateNumColumn,
            this.sizeColumn,
            this.qtyColumn});
            this.platesListView.Dock = System.Windows.Forms.DockStyle.Fill;
            this.platesListView.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.platesListView.FullRowSelect = true;
            this.platesListView.HeaderStyle = System.Windows.Forms.ColumnHeaderStyle.Nonclickable;
            this.platesListView.HideSelection = false;
            this.platesListView.Location = new System.Drawing.Point(3, 34);
            this.platesListView.MultiSelect = false;
            this.platesListView.Name = "platesListView";
            this.platesListView.Size = new System.Drawing.Size(227, 333);
            this.platesListView.TabIndex = 1;
            this.platesListView.UseCompatibleStateImageBehavior = false;
            this.platesListView.View = System.Windows.Forms.View.Details;
            this.platesListView.DoubleClick += new System.EventHandler(this.EditSelectedPlate_Click);
            // 
            // plateNumColumn
            // 
            this.plateNumColumn.Text = "";
            this.plateNumColumn.Width = 30;
            // 
            // sizeColumn
            // 
            this.sizeColumn.Text = "Size";
            this.sizeColumn.Width = 80;
            // 
            // qtyColumn
            // 
            this.qtyColumn.Text = "Qty";
            // 
            // toolStrip1
            // 
            this.toolStrip1.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this.toolStrip1.ImageScalingSize = new System.Drawing.Size(20, 20);
            this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripButton1,
            this.btnAssignLeadIns,
            this.btnPlaceLeadIn});
            this.toolStrip1.Location = new System.Drawing.Point(3, 3);
            this.toolStrip1.Name = "toolStrip1";
            this.toolStrip1.Size = new System.Drawing.Size(227, 31);
            this.toolStrip1.TabIndex = 2;
            this.toolStrip1.Text = "toolStrip1";
            // 
            // toolStripButton1
            // 
            this.toolStripButton1.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripButton1.Image = global::OpenNest.Properties.Resources.clock;
            this.toolStripButton1.ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None;
            this.toolStripButton1.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.toolStripButton1.Name = "toolStripButton1";
            this.toolStripButton1.Padding = new System.Windows.Forms.Padding(5, 0, 5, 0);
            this.toolStripButton1.Size = new System.Drawing.Size(38, 28);
            this.toolStripButton1.Text = "Calculate Cut Time";
            this.toolStripButton1.Click += new System.EventHandler(this.CalculateSelectedPlateCutTime_Click);
            //
            // btnAssignLeadIns
            //
            this.btnAssignLeadIns.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.btnAssignLeadIns.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.btnAssignLeadIns.Name = "btnAssignLeadIns";
            this.btnAssignLeadIns.Size = new System.Drawing.Size(96, 28);
            this.btnAssignLeadIns.Text = "Assign Lead-ins";
            this.btnAssignLeadIns.Click += new System.EventHandler(this.AssignLeadIns_Click);
            //
            // btnPlaceLeadIn
            //
            this.btnPlaceLeadIn.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.btnPlaceLeadIn.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.btnPlaceLeadIn.Name = "btnPlaceLeadIn";
            this.btnPlaceLeadIn.Size = new System.Drawing.Size(90, 28);
            this.btnPlaceLeadIn.Text = "Place Lead-in";
            this.btnPlaceLeadIn.Click += new System.EventHandler(this.PlaceLeadIn_Click);
            //
            // tabPage2
            // 
            this.tabPage2.Controls.Add(this.drawingListBox1);
            this.tabPage2.Controls.Add(this.toolStrip2);
            this.tabPage2.Location = new System.Drawing.Point(4, 26);
            this.tabPage2.Name = "tabPage2";
            this.tabPage2.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage2.Size = new System.Drawing.Size(233, 366);
            this.tabPage2.TabIndex = 1;
            this.tabPage2.Text = "Drawings";
            this.tabPage2.UseVisualStyleBackColor = true;
            // 
            // drawingListBox1
            // 
            this.drawingListBox1.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.drawingListBox1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.drawingListBox1.DrawMode = System.Windows.Forms.DrawMode.OwnerDrawVariable;
            this.drawingListBox1.FormattingEnabled = true;
            this.drawingListBox1.ItemHeight = 85;
            this.drawingListBox1.Location = new System.Drawing.Point(3, 34);
            this.drawingListBox1.Name = "drawingListBox1";
            this.drawingListBox1.Size = new System.Drawing.Size(227, 329);
            this.drawingListBox1.TabIndex = 1;
            this.drawingListBox1.Units = OpenNest.Units.Inches;
            this.drawingListBox1.Click += new System.EventHandler(this.drawingListBox1_Click);
            this.drawingListBox1.DoubleClick += new System.EventHandler(this.drawingListBox1_DoubleClick);
            // 
            // toolStrip2
            // 
            this.toolStrip2.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this.toolStrip2.ImageScalingSize = new System.Drawing.Size(20, 20);
            this.toolStrip2.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripButton2,
            this.toolStripSeparator1,
            this.toolStripButton3});
            this.toolStrip2.Location = new System.Drawing.Point(3, 3);
            this.toolStrip2.Name = "toolStrip2";
            this.toolStrip2.Size = new System.Drawing.Size(227, 31);
            this.toolStrip2.TabIndex = 3;
            this.toolStrip2.Text = "toolStrip2";
            // 
            // toolStripButton2
            // 
            this.toolStripButton2.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripButton2.Image = global::OpenNest.Properties.Resources.import;
            this.toolStripButton2.ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None;
            this.toolStripButton2.Name = "toolStripButton2";
            this.toolStripButton2.Padding = new System.Windows.Forms.Padding(5, 0, 5, 0);
            this.toolStripButton2.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this.toolStripButton2.Size = new System.Drawing.Size(38, 28);
            this.toolStripButton2.Text = "Import Drawings";
            this.toolStripButton2.Click += new System.EventHandler(this.ImportDrawings_Click);
            // 
            // toolStripSeparator1
            // 
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new System.Drawing.Size(6, 31);
            // 
            // toolStripButton3
            // 
            this.toolStripButton3.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripButton3.Image = global::OpenNest.Properties.Resources.clear;
            this.toolStripButton3.ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None;
            this.toolStripButton3.Name = "toolStripButton3";
            this.toolStripButton3.Padding = new System.Windows.Forms.Padding(5, 0, 5, 0);
            this.toolStripButton3.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this.toolStripButton3.Size = new System.Drawing.Size(38, 28);
            this.toolStripButton3.Text = "Cleanup unused Drawings";
            this.toolStripButton3.Click += new System.EventHandler(this.CleanUnusedDrawings_Click);
            // 
            // EditNestForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(735, 396);
            this.Controls.Add(this.splitContainer);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MinimumSize = new System.Drawing.Size(200, 198);
            this.Name = "EditNestForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "EditForm";
            this.splitContainer.Panel1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer)).EndInit();
            this.splitContainer.ResumeLayout(false);
            this.tabControl1.ResumeLayout(false);
            this.tabPage1.ResumeLayout(false);
            this.tabPage1.PerformLayout();
            this.toolStrip1.ResumeLayout(false);
            this.toolStrip1.PerformLayout();
            this.tabPage2.ResumeLayout(false);
            this.tabPage2.PerformLayout();
            this.toolStrip2.ResumeLayout(false);
            this.toolStrip2.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.SplitContainer splitContainer;
        private System.Windows.Forms.TabControl tabControl1;
        private System.Windows.Forms.TabPage tabPage1;
        private System.Windows.Forms.ListView platesListView;
        private System.Windows.Forms.ColumnHeader sizeColumn;
        private System.Windows.Forms.ColumnHeader qtyColumn;
        private System.Windows.Forms.ToolStrip toolStrip1;
        private System.Windows.Forms.ToolStripButton toolStripButton1;
        private System.Windows.Forms.TabPage tabPage2;
        private Controls.DrawingListBox drawingListBox1;
        private System.Windows.Forms.ColumnHeader plateNumColumn;
        private System.Windows.Forms.ToolStrip toolStrip2;
        private System.Windows.Forms.ToolStripButton toolStripButton2;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.ToolStripButton toolStripButton3;
        private System.Windows.Forms.ToolStripButton btnAssignLeadIns;
        private System.Windows.Forms.ToolStripButton btnPlaceLeadIn;
    }
}