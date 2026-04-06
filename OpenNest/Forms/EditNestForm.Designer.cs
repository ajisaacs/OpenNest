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
            components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(EditNestForm));
            splitContainer = new System.Windows.Forms.SplitContainer();
            tabControl1 = new System.Windows.Forms.TabControl();
            tabPage1 = new System.Windows.Forms.TabPage();
            platesListView = new System.Windows.Forms.ListView();
            plateNumColumn = new System.Windows.Forms.ColumnHeader();
            sizeColumn = new System.Windows.Forms.ColumnHeader();
            qtyColumn = new System.Windows.Forms.ColumnHeader();
            partsColumn = new System.Windows.Forms.ColumnHeader();
            utilColumn = new System.Windows.Forms.ColumnHeader();
            toolStrip1 = new System.Windows.Forms.ToolStrip();
            toolStripLabel1 = new System.Windows.Forms.ToolStripButton();
            toolStripSeparator3 = new System.Windows.Forms.ToolStripSeparator();
            toolStripLabel2 = new System.Windows.Forms.ToolStripButton();
            tabPage2 = new System.Windows.Forms.TabPage();
            drawingListBox1 = new OpenNest.Controls.DrawingListBox();
            toolStrip2 = new System.Windows.Forms.ToolStrip();
            toolStripButton2 = new System.Windows.Forms.ToolStripButton();
            toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            toolStripButton3 = new System.Windows.Forms.ToolStripButton();
            toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
            hideNestedButton = new System.Windows.Forms.ToolStripButton();
            ((System.ComponentModel.ISupportInitialize)splitContainer).BeginInit();
            splitContainer.Panel1.SuspendLayout();
            splitContainer.SuspendLayout();
            tabControl1.SuspendLayout();
            tabPage1.SuspendLayout();
            toolStrip1.SuspendLayout();
            tabPage2.SuspendLayout();
            toolStrip2.SuspendLayout();
            SuspendLayout();
            // 
            // splitContainer
            // 
            splitContainer.Dock = System.Windows.Forms.DockStyle.Fill;
            splitContainer.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
            splitContainer.Location = new System.Drawing.Point(0, 0);
            splitContainer.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            splitContainer.Name = "splitContainer";
            // 
            // splitContainer.Panel1
            // 
            splitContainer.Panel1.Controls.Add(tabControl1);
            splitContainer.Size = new System.Drawing.Size(858, 457);
            splitContainer.SplitterDistance = 281;
            splitContainer.SplitterWidth = 5;
            splitContainer.TabIndex = 8;
            // 
            // tabControl1
            // 
            tabControl1.Controls.Add(tabPage1);
            tabControl1.Controls.Add(tabPage2);
            tabControl1.Dock = System.Windows.Forms.DockStyle.Fill;
            tabControl1.ItemSize = new System.Drawing.Size(100, 22);
            tabControl1.Location = new System.Drawing.Point(0, 0);
            tabControl1.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            tabControl1.Name = "tabControl1";
            tabControl1.SelectedIndex = 0;
            tabControl1.Size = new System.Drawing.Size(281, 457);
            tabControl1.SizeMode = System.Windows.Forms.TabSizeMode.Fixed;
            tabControl1.TabIndex = 1;
            // 
            // tabPage1
            // 
            tabPage1.Controls.Add(platesListView);
            tabPage1.Controls.Add(toolStrip1);
            tabPage1.Location = new System.Drawing.Point(4, 26);
            tabPage1.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            tabPage1.Name = "tabPage1";
            tabPage1.Padding = new System.Windows.Forms.Padding(4, 3, 4, 3);
            tabPage1.Size = new System.Drawing.Size(273, 427);
            tabPage1.TabIndex = 0;
            tabPage1.Text = "Plates";
            tabPage1.UseVisualStyleBackColor = true;
            // 
            // platesListView
            // 
            platesListView.BorderStyle = System.Windows.Forms.BorderStyle.None;
            platesListView.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] { plateNumColumn, sizeColumn, qtyColumn, partsColumn, utilColumn });
            platesListView.Dock = System.Windows.Forms.DockStyle.Fill;
            platesListView.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
            platesListView.FullRowSelect = true;
            platesListView.HeaderStyle = System.Windows.Forms.ColumnHeaderStyle.Nonclickable;
            platesListView.Location = new System.Drawing.Point(4, 30);
            platesListView.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            platesListView.MultiSelect = false;
            platesListView.Name = "platesListView";
            platesListView.Size = new System.Drawing.Size(265, 394);
            platesListView.TabIndex = 1;
            platesListView.UseCompatibleStateImageBehavior = false;
            platesListView.View = System.Windows.Forms.View.Details;
            platesListView.DoubleClick += EditSelectedPlate_Click;
            // 
            // plateNumColumn
            // 
            plateNumColumn.Text = "";
            plateNumColumn.Width = 30;
            // 
            // sizeColumn
            // 
            sizeColumn.Text = "Size";
            sizeColumn.Width = 80;
            // 
            // qtyColumn
            // 
            qtyColumn.Text = "Qty";
            // 
            // partsColumn
            // 
            partsColumn.Text = "Parts";
            partsColumn.Width = 45;
            // 
            // utilColumn
            // 
            utilColumn.Text = "Util";
            utilColumn.Width = 45;
            // 
            // toolStrip1
            // 
            toolStrip1.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            toolStrip1.ImageScalingSize = new System.Drawing.Size(20, 20);
            toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { toolStripLabel1, toolStripSeparator3, toolStripLabel2 });
            toolStrip1.Location = new System.Drawing.Point(4, 3);
            toolStrip1.Name = "toolStrip1";
            toolStrip1.Size = new System.Drawing.Size(265, 27);
            toolStrip1.TabIndex = 2;
            toolStrip1.Text = "toolStrip1";
            // 
            // toolStripLabel1
            // 
            toolStripLabel1.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            toolStripLabel1.Image = Properties.Resources.plus;
            toolStripLabel1.Name = "toolStripLabel1";
            toolStripLabel1.Padding = new System.Windows.Forms.Padding(5, 0, 5, 0);
            toolStripLabel1.Size = new System.Drawing.Size(34, 24);
            toolStripLabel1.Text = "Remove Selected Plate";
            toolStripLabel1.Click += toolStripLabel1_Click;
            // 
            // toolStripSeparator3
            // 
            toolStripSeparator3.Name = "toolStripSeparator3";
            toolStripSeparator3.Size = new System.Drawing.Size(6, 27);
            // 
            // toolStripLabel2
            // 
            toolStripLabel2.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            toolStripLabel2.Image = Properties.Resources.delete;
            toolStripLabel2.Name = "toolStripLabel2";
            toolStripLabel2.Padding = new System.Windows.Forms.Padding(5, 0, 5, 0);
            toolStripLabel2.Size = new System.Drawing.Size(34, 24);
            toolStripLabel2.Text = "Remove Selected";
            toolStripLabel2.Click += toolStripLabel2_Click;
            // 
            // tabPage2
            // 
            tabPage2.Controls.Add(drawingListBox1);
            tabPage2.Controls.Add(toolStrip2);
            tabPage2.Location = new System.Drawing.Point(4, 26);
            tabPage2.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            tabPage2.Name = "tabPage2";
            tabPage2.Padding = new System.Windows.Forms.Padding(4, 3, 4, 3);
            tabPage2.Size = new System.Drawing.Size(273, 427);
            tabPage2.TabIndex = 1;
            tabPage2.Text = "Drawings";
            tabPage2.UseVisualStyleBackColor = true;
            // 
            // drawingListBox1
            // 
            drawingListBox1.BorderStyle = System.Windows.Forms.BorderStyle.None;
            drawingListBox1.Dock = System.Windows.Forms.DockStyle.Fill;
            drawingListBox1.DrawMode = System.Windows.Forms.DrawMode.OwnerDrawVariable;
            drawingListBox1.FormattingEnabled = true;
            drawingListBox1.HideDepletedParts = false;
            drawingListBox1.HideQuantity = false;
            drawingListBox1.ItemHeight = 85;
            drawingListBox1.Location = new System.Drawing.Point(4, 30);
            drawingListBox1.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            drawingListBox1.Name = "drawingListBox1";
            drawingListBox1.Size = new System.Drawing.Size(265, 394);
            drawingListBox1.TabIndex = 1;
            drawingListBox1.Units = Units.Inches;
            drawingListBox1.Click += drawingListBox1_Click;
            drawingListBox1.DoubleClick += drawingListBox1_DoubleClick;
            // 
            // toolStrip2
            // 
            toolStrip2.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            toolStrip2.ImageScalingSize = new System.Drawing.Size(20, 20);
            toolStrip2.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { toolStripButton2, toolStripSeparator1, toolStripButton3, toolStripSeparator2, hideNestedButton });
            toolStrip2.Location = new System.Drawing.Point(4, 3);
            toolStrip2.Name = "toolStrip2";
            toolStrip2.Size = new System.Drawing.Size(265, 27);
            toolStrip2.TabIndex = 3;
            toolStrip2.Text = "toolStrip2";
            // 
            // toolStripButton2
            // 
            toolStripButton2.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            toolStripButton2.Image = (System.Drawing.Image)resources.GetObject("toolStripButton2.Image");
            toolStripButton2.Name = "toolStripButton2";
            toolStripButton2.Overflow = System.Windows.Forms.ToolStripItemOverflow.Never;
            toolStripButton2.Padding = new System.Windows.Forms.Padding(5, 0, 5, 0);
            toolStripButton2.RightToLeft = System.Windows.Forms.RightToLeft.No;
            toolStripButton2.Size = new System.Drawing.Size(34, 24);
            toolStripButton2.Text = "Import Drawings";
            toolStripButton2.Click += ImportDrawings_Click;
            // 
            // toolStripSeparator1
            // 
            toolStripSeparator1.Name = "toolStripSeparator1";
            toolStripSeparator1.Size = new System.Drawing.Size(6, 27);
            // 
            // toolStripButton3
            // 
            toolStripButton3.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            toolStripButton3.Image = (System.Drawing.Image)resources.GetObject("toolStripButton3.Image");
            toolStripButton3.Name = "toolStripButton3";
            toolStripButton3.Padding = new System.Windows.Forms.Padding(5, 0, 5, 0);
            toolStripButton3.RightToLeft = System.Windows.Forms.RightToLeft.No;
            toolStripButton3.Size = new System.Drawing.Size(34, 24);
            toolStripButton3.Text = "Cleanup unused Drawings";
            toolStripButton3.Click += CleanUnusedDrawings_Click;
            // 
            // toolStripSeparator2
            // 
            toolStripSeparator2.Name = "toolStripSeparator2";
            toolStripSeparator2.Size = new System.Drawing.Size(6, 27);
            // 
            // hideNestedButton
            // 
            hideNestedButton.CheckOnClick = true;
            hideNestedButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            hideNestedButton.Image = (System.Drawing.Image)resources.GetObject("hideNestedButton.Image");
            hideNestedButton.Name = "hideNestedButton";
            hideNestedButton.Padding = new System.Windows.Forms.Padding(5, 0, 5, 0);
            hideNestedButton.Size = new System.Drawing.Size(34, 24);
            hideNestedButton.Text = "Hide fully nested drawings";
            hideNestedButton.CheckedChanged += HideNestedButton_CheckedChanged;
            // 
            // EditNestForm
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(858, 457);
            Controls.Add(splitContainer);
            Icon = (System.Drawing.Icon)resources.GetObject("$this.Icon");
            Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            MinimumSize = new System.Drawing.Size(231, 222);
            Name = "EditNestForm";
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "EditForm";
            splitContainer.Panel1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitContainer).EndInit();
            splitContainer.ResumeLayout(false);
            tabControl1.ResumeLayout(false);
            tabPage1.ResumeLayout(false);
            tabPage1.PerformLayout();
            toolStrip1.ResumeLayout(false);
            toolStrip1.PerformLayout();
            tabPage2.ResumeLayout(false);
            tabPage2.PerformLayout();
            toolStrip2.ResumeLayout(false);
            toolStrip2.PerformLayout();
            ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.SplitContainer splitContainer;
        private System.Windows.Forms.TabControl tabControl1;
        private System.Windows.Forms.TabPage tabPage1;
        private System.Windows.Forms.ListView platesListView;
        private System.Windows.Forms.ColumnHeader sizeColumn;
        private System.Windows.Forms.ColumnHeader qtyColumn;
        private System.Windows.Forms.ToolStrip toolStrip1;
        private System.Windows.Forms.TabPage tabPage2;
        private Controls.DrawingListBox drawingListBox1;
        private System.Windows.Forms.ColumnHeader plateNumColumn;
        private System.Windows.Forms.ColumnHeader partsColumn;
        private System.Windows.Forms.ColumnHeader utilColumn;
        private System.Windows.Forms.ToolStrip toolStrip2;
        private System.Windows.Forms.ToolStripButton toolStripButton2;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.ToolStripButton toolStripButton3;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator2;
        private System.Windows.Forms.ToolStripButton hideNestedButton;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator3;
        private System.Windows.Forms.ToolStripButton toolStripLabel1;
        private System.Windows.Forms.ToolStripButton toolStripLabel2;
    }
}