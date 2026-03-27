namespace OpenNest.Forms
{
    partial class SimplifierViewerForm
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
            this.listView = new System.Windows.Forms.ListView();
            this.columnLines = new System.Windows.Forms.ColumnHeader();
            this.columnRadius = new System.Windows.Forms.ColumnHeader();
            this.columnDeviation = new System.Windows.Forms.ColumnHeader();
            this.columnLocation = new System.Windows.Forms.ColumnHeader();
            this.bottomPanel = new System.Windows.Forms.FlowLayoutPanel();
            this.lblTolerance = new System.Windows.Forms.Label();
            this.numTolerance = new System.Windows.Forms.NumericUpDown();
            this.lblCount = new System.Windows.Forms.Label();
            this.btnApply = new System.Windows.Forms.Button();
            this.bottomPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numTolerance)).BeginInit();
            this.SuspendLayout();
            //
            // listView
            //
            this.listView.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.columnLines,
            this.columnRadius,
            this.columnDeviation,
            this.columnLocation});
            this.listView.CheckBoxes = true;
            this.listView.Dock = System.Windows.Forms.DockStyle.Fill;
            this.listView.FullRowSelect = true;
            this.listView.GridLines = true;
            this.listView.Location = new System.Drawing.Point(0, 0);
            this.listView.Name = "listView";
            this.listView.Size = new System.Drawing.Size(404, 378);
            this.listView.TabIndex = 0;
            this.listView.UseCompatibleStateImageBehavior = false;
            this.listView.View = System.Windows.Forms.View.Details;
            this.listView.ItemChecked += new System.Windows.Forms.ItemCheckedEventHandler(this.OnItemChecked);
            this.listView.ItemSelectionChanged += new System.Windows.Forms.ListViewItemSelectionChangedEventHandler(this.OnItemSelected);
            //
            // columnLines
            //
            this.columnLines.Text = "Lines";
            this.columnLines.Width = 50;
            //
            // columnRadius
            //
            this.columnRadius.Text = "Radius";
            this.columnRadius.Width = 70;
            //
            // columnDeviation
            //
            this.columnDeviation.Text = "Deviation";
            this.columnDeviation.Width = 75;
            //
            // columnLocation
            //
            this.columnLocation.Text = "Location";
            this.columnLocation.Width = 100;
            //
            // bottomPanel
            //
            this.bottomPanel.Controls.Add(this.lblTolerance);
            this.bottomPanel.Controls.Add(this.numTolerance);
            this.bottomPanel.Controls.Add(this.lblCount);
            this.bottomPanel.Controls.Add(this.btnApply);
            this.bottomPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.bottomPanel.Location = new System.Drawing.Point(0, 378);
            this.bottomPanel.Name = "bottomPanel";
            this.bottomPanel.Padding = new System.Windows.Forms.Padding(4, 6, 4, 4);
            this.bottomPanel.Size = new System.Drawing.Size(404, 36);
            this.bottomPanel.TabIndex = 1;
            this.bottomPanel.WrapContents = false;
            //
            // lblTolerance
            //
            this.lblTolerance.AutoSize = true;
            this.lblTolerance.Location = new System.Drawing.Point(7, 9);
            this.lblTolerance.Margin = new System.Windows.Forms.Padding(0, 3, 2, 0);
            this.lblTolerance.Name = "lblTolerance";
            this.lblTolerance.Size = new System.Drawing.Size(61, 15);
            this.lblTolerance.TabIndex = 0;
            this.lblTolerance.Text = "Tolerance:";
            //
            // numTolerance
            //
            this.numTolerance.DecimalPlaces = 3;
            this.numTolerance.Increment = new decimal(new int[] {
            5,
            0,
            0,
            196608});
            this.numTolerance.Location = new System.Drawing.Point(73, 6);
            this.numTolerance.Maximum = new decimal(new int[] {
            5,
            0,
            0,
            0});
            this.numTolerance.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            196608});
            this.numTolerance.Name = "numTolerance";
            this.numTolerance.Size = new System.Drawing.Size(70, 23);
            this.numTolerance.TabIndex = 1;
            this.numTolerance.Value = new decimal(new int[] {
            20,
            0,
            0,
            196608});
            this.numTolerance.ValueChanged += new System.EventHandler(this.OnToleranceChanged);
            //
            // lblCount
            //
            this.lblCount.AutoSize = true;
            this.lblCount.Location = new System.Drawing.Point(155, 9);
            this.lblCount.Margin = new System.Windows.Forms.Padding(8, 3, 4, 0);
            this.lblCount.Name = "lblCount";
            this.lblCount.Size = new System.Drawing.Size(84, 15);
            this.lblCount.TabIndex = 2;
            this.lblCount.Text = "0 of 0 selected";
            //
            // btnApply
            //
            this.btnApply.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnApply.Location = new System.Drawing.Point(247, 6);
            this.btnApply.Margin = new System.Windows.Forms.Padding(4, 0, 0, 0);
            this.btnApply.Name = "btnApply";
            this.btnApply.Size = new System.Drawing.Size(60, 25);
            this.btnApply.TabIndex = 3;
            this.btnApply.Text = "Apply";
            this.btnApply.UseVisualStyleBackColor = true;
            this.btnApply.Click += new System.EventHandler(this.OnApplyClick);
            //
            // SimplifierViewerForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(404, 414);
            this.Controls.Add(this.listView);
            this.Controls.Add(this.bottomPanel);
            this.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.SizableToolWindow;
            this.Name = "SimplifierViewerForm";
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            this.Text = "Geometry Simplifier";
            this.TopMost = true;
            this.bottomPanel.ResumeLayout(false);
            this.bottomPanel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numTolerance)).EndInit();
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.ListView listView;
        private System.Windows.Forms.ColumnHeader columnLines;
        private System.Windows.Forms.ColumnHeader columnRadius;
        private System.Windows.Forms.ColumnHeader columnDeviation;
        private System.Windows.Forms.ColumnHeader columnLocation;
        private System.Windows.Forms.FlowLayoutPanel bottomPanel;
        private System.Windows.Forms.Label lblTolerance;
        private System.Windows.Forms.NumericUpDown numTolerance;
        private System.Windows.Forms.Label lblCount;
        private System.Windows.Forms.Button btnApply;
    }
}
