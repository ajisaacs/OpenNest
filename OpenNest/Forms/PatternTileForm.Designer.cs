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
            ColorScheme colorScheme1 = new ColorScheme();
            Plate plate1 = new Plate();
            Material material1 = new Material();
            Collections.ObservableList<Part> observableList_11 = new Collections.ObservableList<Part>();
            Plate plate2 = new Plate();
            Material material2 = new Material();
            Collections.ObservableList<Part> observableList_12 = new Collections.ObservableList<Part>();
            Plate plate3 = new Plate();
            Material material3 = new Material();
            Collections.ObservableList<Part> observableList_13 = new Collections.ObservableList<Part>();
            topPanel = new System.Windows.Forms.FlowLayoutPanel();
            lblDrawingA = new System.Windows.Forms.Label();
            cboDrawingA = new System.Windows.Forms.ComboBox();
            lblDrawingB = new System.Windows.Forms.Label();
            cboDrawingB = new System.Windows.Forms.ComboBox();
            lblPlateSize = new System.Windows.Forms.Label();
            txtPlateSize = new System.Windows.Forms.TextBox();
            lblPartSpacing = new System.Windows.Forms.Label();
            nudPartSpacing = new System.Windows.Forms.NumericUpDown();
            btnAutoArrange = new System.Windows.Forms.Button();
            btnApply = new System.Windows.Forms.Button();
            splitContainer = new System.Windows.Forms.SplitContainer();
            cellView = new OpenNest.Controls.PlateView();
            splitContainer1 = new System.Windows.Forms.SplitContainer();
            hPreview = new OpenNest.Controls.PlateView();
            hLabel = new System.Windows.Forms.Label();
            vPreview = new OpenNest.Controls.PlateView();
            vLabel = new System.Windows.Forms.Label();
            topPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)nudPartSpacing).BeginInit();
            ((System.ComponentModel.ISupportInitialize)splitContainer).BeginInit();
            splitContainer.Panel1.SuspendLayout();
            splitContainer.Panel2.SuspendLayout();
            splitContainer.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)splitContainer1).BeginInit();
            splitContainer1.Panel1.SuspendLayout();
            splitContainer1.Panel2.SuspendLayout();
            splitContainer1.SuspendLayout();
            SuspendLayout();
            // 
            // topPanel
            // 
            topPanel.Controls.Add(lblDrawingA);
            topPanel.Controls.Add(cboDrawingA);
            topPanel.Controls.Add(lblDrawingB);
            topPanel.Controls.Add(cboDrawingB);
            topPanel.Controls.Add(lblPlateSize);
            topPanel.Controls.Add(txtPlateSize);
            topPanel.Controls.Add(lblPartSpacing);
            topPanel.Controls.Add(nudPartSpacing);
            topPanel.Controls.Add(btnAutoArrange);
            topPanel.Controls.Add(btnApply);
            topPanel.Dock = System.Windows.Forms.DockStyle.Top;
            topPanel.Location = new System.Drawing.Point(0, 0);
            topPanel.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            topPanel.Name = "topPanel";
            topPanel.Padding = new System.Windows.Forms.Padding(5, 2, 5, 2);
            topPanel.Size = new System.Drawing.Size(1220, 42);
            topPanel.TabIndex = 2;
            topPanel.WrapContents = false;
            // 
            // lblDrawingA
            // 
            lblDrawingA.AutoSize = true;
            lblDrawingA.Location = new System.Drawing.Point(9, 8);
            lblDrawingA.Margin = new System.Windows.Forms.Padding(4, 6, 0, 0);
            lblDrawingA.Name = "lblDrawingA";
            lblDrawingA.Size = new System.Drawing.Size(65, 15);
            lblDrawingA.TabIndex = 0;
            lblDrawingA.Text = "Drawing A:";
            // 
            // cboDrawingA
            // 
            cboDrawingA.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            cboDrawingA.Location = new System.Drawing.Point(78, 5);
            cboDrawingA.Margin = new System.Windows.Forms.Padding(4, 3, 0, 0);
            cboDrawingA.Name = "cboDrawingA";
            cboDrawingA.Size = new System.Drawing.Size(151, 23);
            cboDrawingA.TabIndex = 1;
            // 
            // lblDrawingB
            // 
            lblDrawingB.AutoSize = true;
            lblDrawingB.Location = new System.Drawing.Point(241, 8);
            lblDrawingB.Margin = new System.Windows.Forms.Padding(12, 6, 0, 0);
            lblDrawingB.Name = "lblDrawingB";
            lblDrawingB.Size = new System.Drawing.Size(64, 15);
            lblDrawingB.TabIndex = 2;
            lblDrawingB.Text = "Drawing B:";
            // 
            // cboDrawingB
            // 
            cboDrawingB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            cboDrawingB.Location = new System.Drawing.Point(309, 5);
            cboDrawingB.Margin = new System.Windows.Forms.Padding(4, 3, 0, 0);
            cboDrawingB.Name = "cboDrawingB";
            cboDrawingB.Size = new System.Drawing.Size(151, 23);
            cboDrawingB.TabIndex = 3;
            // 
            // lblPlateSize
            // 
            lblPlateSize.AutoSize = true;
            lblPlateSize.Location = new System.Drawing.Point(472, 8);
            lblPlateSize.Margin = new System.Windows.Forms.Padding(12, 6, 0, 0);
            lblPlateSize.Name = "lblPlateSize";
            lblPlateSize.Size = new System.Drawing.Size(36, 15);
            lblPlateSize.TabIndex = 4;
            lblPlateSize.Text = "Plate:";
            // 
            // txtPlateSize
            // 
            txtPlateSize.Location = new System.Drawing.Point(512, 5);
            txtPlateSize.Margin = new System.Windows.Forms.Padding(4, 3, 0, 0);
            txtPlateSize.Name = "txtPlateSize";
            txtPlateSize.Size = new System.Drawing.Size(104, 23);
            txtPlateSize.TabIndex = 5;
            // 
            // lblPartSpacing
            // 
            lblPartSpacing.AutoSize = true;
            lblPartSpacing.Location = new System.Drawing.Point(628, 8);
            lblPartSpacing.Margin = new System.Windows.Forms.Padding(12, 6, 0, 0);
            lblPartSpacing.Name = "lblPartSpacing";
            lblPartSpacing.Size = new System.Drawing.Size(52, 15);
            lblPartSpacing.TabIndex = 6;
            lblPartSpacing.Text = "Spacing:";
            // 
            // nudPartSpacing
            // 
            nudPartSpacing.DecimalPlaces = 2;
            nudPartSpacing.Increment = new decimal(new int[] { 25, 0, 0, 131072 });
            nudPartSpacing.Location = new System.Drawing.Point(684, 5);
            nudPartSpacing.Margin = new System.Windows.Forms.Padding(4, 3, 0, 0);
            nudPartSpacing.Name = "nudPartSpacing";
            nudPartSpacing.Size = new System.Drawing.Size(82, 23);
            nudPartSpacing.TabIndex = 7;
            // 
            // btnAutoArrange
            // 
            btnAutoArrange.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            btnAutoArrange.Location = new System.Drawing.Point(778, 5);
            btnAutoArrange.Margin = new System.Windows.Forms.Padding(12, 3, 0, 0);
            btnAutoArrange.Name = "btnAutoArrange";
            btnAutoArrange.Size = new System.Drawing.Size(117, 30);
            btnAutoArrange.TabIndex = 8;
            btnAutoArrange.Text = "Auto Arrange";
            // 
            // btnApply
            // 
            btnApply.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            btnApply.Location = new System.Drawing.Point(902, 5);
            btnApply.Margin = new System.Windows.Forms.Padding(7, 3, 0, 0);
            btnApply.Name = "btnApply";
            btnApply.Size = new System.Drawing.Size(93, 30);
            btnApply.TabIndex = 9;
            btnApply.Text = "Apply";
            // 
            // splitContainer
            // 
            splitContainer.Dock = System.Windows.Forms.DockStyle.Fill;
            splitContainer.Location = new System.Drawing.Point(0, 42);
            splitContainer.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            splitContainer.Name = "splitContainer";
            // 
            // splitContainer.Panel1
            // 
            splitContainer.Panel1.Controls.Add(cellView);
            // 
            // splitContainer.Panel2
            // 
            splitContainer.Panel2.Controls.Add(splitContainer1);
            splitContainer.Size = new System.Drawing.Size(1220, 677);
            splitContainer.SplitterDistance = 610;
            splitContainer.SplitterWidth = 5;
            splitContainer.TabIndex = 1;
            // 
            // cellView
            // 
            cellView.ActiveWorkArea = null;
            cellView.AllowDrop = true;
            cellView.AllowPan = true;
            cellView.AllowSelect = true;
            cellView.AllowZoom = true;
            cellView.BackColor = System.Drawing.Color.DarkGray;
            colorScheme1.BackgroundColor = System.Drawing.Color.DarkGray;
            colorScheme1.BoundingBoxColor = System.Drawing.Color.FromArgb(128, 128, 255);
            colorScheme1.EdgeSpacingColor = System.Drawing.Color.FromArgb(180, 180, 180);
            colorScheme1.LayoutFillColor = System.Drawing.Color.WhiteSmoke;
            colorScheme1.LayoutOutlineColor = System.Drawing.Color.Gray;
            colorScheme1.OriginColor = System.Drawing.Color.Gray;
            colorScheme1.PreviewPartColor = System.Drawing.Color.FromArgb(255, 140, 0);
            colorScheme1.RapidColor = System.Drawing.Color.DodgerBlue;
            cellView.ColorScheme = colorScheme1;
            cellView.DebugRemnantPriorities = null;
            cellView.DebugRemnants = null;
            cellView.Dock = System.Windows.Forms.DockStyle.Fill;
            cellView.DrawBounds = false;
            cellView.DrawOffset = false;
            cellView.DrawOrigin = false;
            cellView.DrawRapid = false;
            cellView.FillParts = true;
            cellView.Location = new System.Drawing.Point(0, 0);
            cellView.Name = "cellView";
            cellView.OffsetIncrementDistance = 10D;
            cellView.OffsetTolerance = 0.001D;
            material1.Density = 0D;
            material1.Grade = null;
            material1.Name = null;
            plate1.Material = material1;
            plate1.Parts = observableList_11;
            plate1.PartSpacing = 0D;
            plate1.Quadrant = 1;
            plate1.Quantity = 0;
            plate1.Thickness = 0D;
            cellView.Plate = plate1;
            cellView.RotateIncrementAngle = 10D;
            cellView.Size = new System.Drawing.Size(610, 677);
            cellView.Status = "Select";
            cellView.TabIndex = 0;
            // 
            // splitContainer1
            // 
            splitContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
            splitContainer1.IsSplitterFixed = true;
            splitContainer1.Location = new System.Drawing.Point(0, 0);
            splitContainer1.Name = "splitContainer1";
            splitContainer1.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // splitContainer1.Panel1
            // 
            splitContainer1.Panel1.Controls.Add(hPreview);
            splitContainer1.Panel1.Controls.Add(hLabel);
            // 
            // splitContainer1.Panel2
            // 
            splitContainer1.Panel2.Controls.Add(vPreview);
            splitContainer1.Panel2.Controls.Add(vLabel);
            splitContainer1.Size = new System.Drawing.Size(605, 677);
            splitContainer1.SplitterDistance = 333;
            splitContainer1.TabIndex = 0;
            // 
            // hPreview
            // 
            hPreview.ActiveWorkArea = null;
            hPreview.AllowPan = true;
            hPreview.AllowSelect = false;
            hPreview.AllowZoom = true;
            hPreview.BackColor = System.Drawing.Color.DarkGray;
            hPreview.ColorScheme = colorScheme1;
            hPreview.DebugRemnantPriorities = null;
            hPreview.DebugRemnants = null;
            hPreview.Dock = System.Windows.Forms.DockStyle.Fill;
            hPreview.DrawBounds = false;
            hPreview.DrawOffset = false;
            hPreview.DrawOrigin = true;
            hPreview.DrawRapid = false;
            hPreview.FillParts = true;
            hPreview.Location = new System.Drawing.Point(0, 20);
            hPreview.Name = "hPreview";
            hPreview.OffsetIncrementDistance = 10D;
            hPreview.OffsetTolerance = 0.001D;
            material2.Density = 0D;
            material2.Grade = null;
            material2.Name = null;
            plate2.Material = material2;
            plate2.Parts = observableList_12;
            plate2.PartSpacing = 0D;
            plate2.Quadrant = 1;
            plate2.Quantity = 0;
            plate2.Thickness = 0D;
            hPreview.Plate = plate2;
            hPreview.RotateIncrementAngle = 10D;
            hPreview.Size = new System.Drawing.Size(605, 313);
            hPreview.Status = "Select";
            hPreview.TabIndex = 0;
            // 
            // hLabel
            // 
            hLabel.Dock = System.Windows.Forms.DockStyle.Top;
            hLabel.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            hLabel.ForeColor = System.Drawing.Color.FromArgb(80, 80, 80);
            hLabel.Location = new System.Drawing.Point(0, 0);
            hLabel.Name = "hLabel";
            hLabel.Padding = new System.Windows.Forms.Padding(4, 0, 0, 0);
            hLabel.Size = new System.Drawing.Size(605, 20);
            hLabel.TabIndex = 1;
            hLabel.Text = "Horizontal — 0 parts";
            hLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // vPreview
            // 
            vPreview.ActiveWorkArea = null;
            vPreview.AllowPan = true;
            vPreview.AllowSelect = false;
            vPreview.AllowZoom = true;
            vPreview.BackColor = System.Drawing.Color.DarkGray;
            vPreview.ColorScheme = colorScheme1;
            vPreview.DebugRemnantPriorities = null;
            vPreview.DebugRemnants = null;
            vPreview.Dock = System.Windows.Forms.DockStyle.Fill;
            vPreview.DrawBounds = false;
            vPreview.DrawOffset = false;
            vPreview.DrawOrigin = true;
            vPreview.DrawRapid = false;
            vPreview.FillParts = true;
            vPreview.Location = new System.Drawing.Point(0, 20);
            vPreview.Name = "vPreview";
            vPreview.OffsetIncrementDistance = 10D;
            vPreview.OffsetTolerance = 0.001D;
            material3.Density = 0D;
            material3.Grade = null;
            material3.Name = null;
            plate3.Material = material3;
            plate3.Parts = observableList_13;
            plate3.PartSpacing = 0D;
            plate3.Quadrant = 1;
            plate3.Quantity = 0;
            plate3.Thickness = 0D;
            vPreview.Plate = plate3;
            vPreview.RotateIncrementAngle = 10D;
            vPreview.Size = new System.Drawing.Size(605, 320);
            vPreview.Status = "Select";
            vPreview.TabIndex = 0;
            // 
            // vLabel
            // 
            vLabel.Dock = System.Windows.Forms.DockStyle.Top;
            vLabel.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            vLabel.ForeColor = System.Drawing.Color.FromArgb(80, 80, 80);
            vLabel.Location = new System.Drawing.Point(0, 0);
            vLabel.Name = "vLabel";
            vLabel.Padding = new System.Windows.Forms.Padding(4, 0, 0, 0);
            vLabel.Size = new System.Drawing.Size(605, 20);
            vLabel.TabIndex = 1;
            vLabel.Text = "Vertical — 0 parts";
            vLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // PatternTileForm
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(1220, 719);
            Controls.Add(splitContainer);
            Controls.Add(topPanel);
            Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            MinimumSize = new System.Drawing.Size(814, 456);
            Name = "PatternTileForm";
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "Pattern Tile";
            WindowState = System.Windows.Forms.FormWindowState.Maximized;
            topPanel.ResumeLayout(false);
            topPanel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)nudPartSpacing).EndInit();
            splitContainer.Panel1.ResumeLayout(false);
            splitContainer.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitContainer).EndInit();
            splitContainer.ResumeLayout(false);
            splitContainer1.Panel1.ResumeLayout(false);
            splitContainer1.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitContainer1).EndInit();
            splitContainer1.ResumeLayout(false);
            ResumeLayout(false);
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
        private OpenNest.Controls.PlateView cellView;
        private System.Windows.Forms.SplitContainer splitContainer1;
        private OpenNest.Controls.PlateView hPreview;
        private System.Windows.Forms.Label hLabel;
        private OpenNest.Controls.PlateView vPreview;
        private System.Windows.Forms.Label vLabel;
    }
}
