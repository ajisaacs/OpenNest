namespace OpenNest.Forms
{
    partial class SplitDrawingForm
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
            this.pnlSettings = new System.Windows.Forms.Panel();
            this.pnlPreview = new System.Windows.Forms.Panel();
            this.toolStrip = new System.Windows.Forms.ToolStrip();
            this.btnAddLine = new System.Windows.Forms.ToolStripButton();
            this.btnDeleteLine = new System.Windows.Forms.ToolStripButton();
            this.statusStrip = new System.Windows.Forms.StatusStrip();
            this.lblStatus = new System.Windows.Forms.ToolStripStatusLabel();
            this.lblCursor = new System.Windows.Forms.ToolStripStatusLabel();

            // Split Method group
            this.grpMethod = new System.Windows.Forms.GroupBox();
            this.radManual = new System.Windows.Forms.RadioButton();
            this.radFitToPlate = new System.Windows.Forms.RadioButton();
            this.radByCount = new System.Windows.Forms.RadioButton();

            // Auto-fit group
            this.grpAutoFit = new System.Windows.Forms.GroupBox();
            this.lblPlateWidth = new System.Windows.Forms.Label();
            this.nudPlateWidth = new System.Windows.Forms.NumericUpDown();
            this.lblPlateHeight = new System.Windows.Forms.Label();
            this.nudPlateHeight = new System.Windows.Forms.NumericUpDown();
            this.lblEdgeSpacing = new System.Windows.Forms.Label();
            this.nudEdgeSpacing = new System.Windows.Forms.NumericUpDown();
            this.lblSplitAxis = new System.Windows.Forms.Label();
            this.cboSplitAxis = new System.Windows.Forms.ComboBox();

            // By Count group
            this.grpByCount = new System.Windows.Forms.GroupBox();
            this.lblHorizontalPieces = new System.Windows.Forms.Label();
            this.nudHorizontalPieces = new System.Windows.Forms.NumericUpDown();
            this.lblVerticalPieces = new System.Windows.Forms.Label();
            this.nudVerticalPieces = new System.Windows.Forms.NumericUpDown();

            // Split Type group
            this.grpType = new System.Windows.Forms.GroupBox();
            this.radStraight = new System.Windows.Forms.RadioButton();
            this.radTabs = new System.Windows.Forms.RadioButton();
            this.radSpike = new System.Windows.Forms.RadioButton();

            // Tab Parameters group
            this.grpTabParams = new System.Windows.Forms.GroupBox();
            this.lblTabWidth = new System.Windows.Forms.Label();
            this.nudTabWidth = new System.Windows.Forms.NumericUpDown();
            this.lblTabHeight = new System.Windows.Forms.Label();
            this.nudTabHeight = new System.Windows.Forms.NumericUpDown();
            this.lblTabCount = new System.Windows.Forms.Label();
            this.nudTabCount = new System.Windows.Forms.NumericUpDown();

            // Spike Parameters group
            this.grpSpikeParams = new System.Windows.Forms.GroupBox();
            this.lblSpikeDepth = new System.Windows.Forms.Label();
            this.nudSpikeDepth = new System.Windows.Forms.NumericUpDown();
            this.lblSpikeAngle = new System.Windows.Forms.Label();
            this.nudSpikeAngle = new System.Windows.Forms.NumericUpDown();
            this.lblSpikePairCount = new System.Windows.Forms.Label();
            this.nudSpikePairCount = new System.Windows.Forms.NumericUpDown();

            // OK/Cancel buttons
            this.btnOK = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();

            // Begin init
            ((System.ComponentModel.ISupportInitialize)(this.nudPlateWidth)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudPlateHeight)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudEdgeSpacing)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudHorizontalPieces)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudVerticalPieces)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudTabWidth)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudTabHeight)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudTabCount)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudSpikeDepth)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudSpikeAngle)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudSpikePairCount)).BeginInit();
            this.pnlSettings.SuspendLayout();
            this.grpMethod.SuspendLayout();
            this.grpAutoFit.SuspendLayout();
            this.grpByCount.SuspendLayout();
            this.grpType.SuspendLayout();
            this.grpTabParams.SuspendLayout();
            this.grpSpikeParams.SuspendLayout();
            this.toolStrip.SuspendLayout();
            this.statusStrip.SuspendLayout();
            this.SuspendLayout();

            // ---- ToolStrip ----
            this.toolStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
                this.btnAddLine,
                this.btnDeleteLine
            });
            this.toolStrip.Location = new System.Drawing.Point(0, 0);
            this.toolStrip.Name = "toolStrip";
            this.toolStrip.Size = new System.Drawing.Size(580, 25);
            this.toolStrip.TabIndex = 0;

            // btnAddLine
            this.btnAddLine.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.btnAddLine.Name = "btnAddLine";
            this.btnAddLine.Size = new System.Drawing.Size(86, 22);
            this.btnAddLine.Text = "Add Split Line";
            this.btnAddLine.Click += new System.EventHandler(this.OnAddSplitLine);

            // btnDeleteLine
            this.btnDeleteLine.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.btnDeleteLine.Name = "btnDeleteLine";
            this.btnDeleteLine.Size = new System.Drawing.Size(68, 22);
            this.btnDeleteLine.Text = "Delete Line";
            this.btnDeleteLine.Click += new System.EventHandler(this.OnDeleteSplitLine);

            // ---- StatusStrip ----
            this.statusStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
                this.lblStatus,
                this.lblCursor
            });
            this.statusStrip.Location = new System.Drawing.Point(0, 528);
            this.statusStrip.Name = "statusStrip";
            this.statusStrip.Size = new System.Drawing.Size(800, 22);
            this.statusStrip.TabIndex = 1;

            // lblStatus
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(100, 17);
            this.lblStatus.Spring = true;
            this.lblStatus.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblStatus.Text = "";

            // lblCursor
            this.lblCursor.Name = "lblCursor";
            this.lblCursor.Size = new System.Drawing.Size(150, 17);
            this.lblCursor.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.lblCursor.Text = "Cursor: 0.00, 0.00";

            // ---- Settings Panel (right side) ----
            this.pnlSettings.AutoScroll = true;
            this.pnlSettings.Dock = System.Windows.Forms.DockStyle.Right;
            this.pnlSettings.Location = new System.Drawing.Point(580, 25);
            this.pnlSettings.Name = "pnlSettings";
            this.pnlSettings.Padding = new System.Windows.Forms.Padding(6);
            this.pnlSettings.Size = new System.Drawing.Size(220, 503);
            this.pnlSettings.TabIndex = 2;
            this.pnlSettings.Controls.Add(this.btnCancel);
            this.pnlSettings.Controls.Add(this.btnOK);
            this.pnlSettings.Controls.Add(this.grpSpikeParams);
            this.pnlSettings.Controls.Add(this.grpTabParams);
            this.pnlSettings.Controls.Add(this.grpType);
            this.pnlSettings.Controls.Add(this.grpByCount);
            this.pnlSettings.Controls.Add(this.grpAutoFit);
            this.pnlSettings.Controls.Add(this.grpMethod);

            // ---- Split Method Group ----
            this.grpMethod.Dock = System.Windows.Forms.DockStyle.Top;
            this.grpMethod.Location = new System.Drawing.Point(6, 6);
            this.grpMethod.Name = "grpMethod";
            this.grpMethod.Size = new System.Drawing.Size(208, 95);
            this.grpMethod.TabIndex = 0;
            this.grpMethod.TabStop = false;
            this.grpMethod.Text = "Split Method";
            this.grpMethod.Controls.Add(this.radByCount);
            this.grpMethod.Controls.Add(this.radFitToPlate);
            this.grpMethod.Controls.Add(this.radManual);

            // radManual
            this.radManual.AutoSize = true;
            this.radManual.Checked = true;
            this.radManual.Location = new System.Drawing.Point(10, 20);
            this.radManual.Name = "radManual";
            this.radManual.Size = new System.Drawing.Size(65, 19);
            this.radManual.TabIndex = 0;
            this.radManual.TabStop = true;
            this.radManual.Text = "Manual";
            this.radManual.CheckedChanged += new System.EventHandler(this.OnMethodChanged);

            // radFitToPlate
            this.radFitToPlate.AutoSize = true;
            this.radFitToPlate.Location = new System.Drawing.Point(10, 43);
            this.radFitToPlate.Name = "radFitToPlate";
            this.radFitToPlate.Size = new System.Drawing.Size(85, 19);
            this.radFitToPlate.TabIndex = 1;
            this.radFitToPlate.Text = "Fit to Plate";
            this.radFitToPlate.CheckedChanged += new System.EventHandler(this.OnMethodChanged);

            // radByCount
            this.radByCount.AutoSize = true;
            this.radByCount.Location = new System.Drawing.Point(10, 66);
            this.radByCount.Name = "radByCount";
            this.radByCount.Size = new System.Drawing.Size(102, 19);
            this.radByCount.TabIndex = 2;
            this.radByCount.Text = "Split by Count";
            this.radByCount.CheckedChanged += new System.EventHandler(this.OnMethodChanged);

            // ---- Auto-Fit Group ----
            this.grpAutoFit.Dock = System.Windows.Forms.DockStyle.Top;
            this.grpAutoFit.Location = new System.Drawing.Point(6, 101);
            this.grpAutoFit.Name = "grpAutoFit";
            this.grpAutoFit.Size = new System.Drawing.Size(208, 132);
            this.grpAutoFit.TabIndex = 1;
            this.grpAutoFit.TabStop = false;
            this.grpAutoFit.Text = "Auto-Fit Options";
            this.grpAutoFit.Visible = false;
            this.grpAutoFit.Controls.Add(this.cboSplitAxis);
            this.grpAutoFit.Controls.Add(this.lblSplitAxis);
            this.grpAutoFit.Controls.Add(this.nudEdgeSpacing);
            this.grpAutoFit.Controls.Add(this.lblEdgeSpacing);
            this.grpAutoFit.Controls.Add(this.nudPlateHeight);
            this.grpAutoFit.Controls.Add(this.lblPlateHeight);
            this.grpAutoFit.Controls.Add(this.nudPlateWidth);
            this.grpAutoFit.Controls.Add(this.lblPlateWidth);

            // lblPlateWidth
            this.lblPlateWidth.AutoSize = true;
            this.lblPlateWidth.Location = new System.Drawing.Point(10, 22);
            this.lblPlateWidth.Name = "lblPlateWidth";
            this.lblPlateWidth.Size = new System.Drawing.Size(70, 15);
            this.lblPlateWidth.Text = "Plate Length:";

            // nudPlateWidth
            this.nudPlateWidth.DecimalPlaces = 2;
            this.nudPlateWidth.Location = new System.Drawing.Point(110, 20);
            this.nudPlateWidth.Maximum = new decimal(new int[] { 100000, 0, 0, 0 });
            this.nudPlateWidth.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.nudPlateWidth.Name = "nudPlateWidth";
            this.nudPlateWidth.Size = new System.Drawing.Size(88, 23);
            this.nudPlateWidth.TabIndex = 0;
            this.nudPlateWidth.Value = new decimal(new int[] { 120, 0, 0, 0 });
            this.nudPlateWidth.ValueChanged += new System.EventHandler(this.OnAutoFitValueChanged);

            // lblPlateHeight
            this.lblPlateHeight.AutoSize = true;
            this.lblPlateHeight.Location = new System.Drawing.Point(10, 49);
            this.lblPlateHeight.Name = "lblPlateHeight";
            this.lblPlateHeight.Size = new System.Drawing.Size(74, 15);
            this.lblPlateHeight.Text = "Plate Width:";

            // nudPlateHeight
            this.nudPlateHeight.DecimalPlaces = 2;
            this.nudPlateHeight.Location = new System.Drawing.Point(110, 47);
            this.nudPlateHeight.Maximum = new decimal(new int[] { 100000, 0, 0, 0 });
            this.nudPlateHeight.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.nudPlateHeight.Name = "nudPlateHeight";
            this.nudPlateHeight.Size = new System.Drawing.Size(88, 23);
            this.nudPlateHeight.TabIndex = 1;
            this.nudPlateHeight.Value = new decimal(new int[] { 60, 0, 0, 0 });
            this.nudPlateHeight.ValueChanged += new System.EventHandler(this.OnAutoFitValueChanged);

            // lblEdgeSpacing
            this.lblEdgeSpacing.AutoSize = true;
            this.lblEdgeSpacing.Location = new System.Drawing.Point(10, 76);
            this.lblEdgeSpacing.Name = "lblEdgeSpacing";
            this.lblEdgeSpacing.Size = new System.Drawing.Size(82, 15);
            this.lblEdgeSpacing.Text = "Edge Spacing:";

            // nudEdgeSpacing
            this.nudEdgeSpacing.DecimalPlaces = 2;
            this.nudEdgeSpacing.Location = new System.Drawing.Point(110, 74);
            this.nudEdgeSpacing.Maximum = new decimal(new int[] { 100, 0, 0, 0 });
            this.nudEdgeSpacing.Name = "nudEdgeSpacing";
            this.nudEdgeSpacing.Size = new System.Drawing.Size(88, 23);
            this.nudEdgeSpacing.TabIndex = 2;
            this.nudEdgeSpacing.Value = new decimal(new int[] { 5, 0, 0, 131072 });
            this.nudEdgeSpacing.ValueChanged += new System.EventHandler(this.OnAutoFitValueChanged);

            // lblSplitAxis
            this.lblSplitAxis.AutoSize = true;
            this.lblSplitAxis.Location = new System.Drawing.Point(10, 103);
            this.lblSplitAxis.Name = "lblSplitAxis";
            this.lblSplitAxis.Size = new System.Drawing.Size(60, 15);
            this.lblSplitAxis.Text = "Split Axis:";

            // cboSplitAxis
            this.cboSplitAxis.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboSplitAxis.Items.AddRange(new object[] { "Auto", "Vertical Only", "Horizontal Only" });
            this.cboSplitAxis.Location = new System.Drawing.Point(110, 100);
            this.cboSplitAxis.Name = "cboSplitAxis";
            this.cboSplitAxis.Size = new System.Drawing.Size(88, 23);
            this.cboSplitAxis.TabIndex = 3;
            this.cboSplitAxis.SelectedIndex = 0;
            this.cboSplitAxis.SelectedIndexChanged += new System.EventHandler(this.OnAutoFitValueChanged);

            // ---- By Count Group ----
            this.grpByCount.Dock = System.Windows.Forms.DockStyle.Top;
            this.grpByCount.Location = new System.Drawing.Point(6, 206);
            this.grpByCount.Name = "grpByCount";
            this.grpByCount.Size = new System.Drawing.Size(208, 78);
            this.grpByCount.TabIndex = 2;
            this.grpByCount.TabStop = false;
            this.grpByCount.Text = "Split by Count";
            this.grpByCount.Visible = false;
            this.grpByCount.Controls.Add(this.nudVerticalPieces);
            this.grpByCount.Controls.Add(this.lblVerticalPieces);
            this.grpByCount.Controls.Add(this.nudHorizontalPieces);
            this.grpByCount.Controls.Add(this.lblHorizontalPieces);

            // lblHorizontalPieces
            this.lblHorizontalPieces.AutoSize = true;
            this.lblHorizontalPieces.Location = new System.Drawing.Point(10, 22);
            this.lblHorizontalPieces.Name = "lblHorizontalPieces";
            this.lblHorizontalPieces.Size = new System.Drawing.Size(72, 15);
            this.lblHorizontalPieces.Text = "H. Pieces:";

            // nudHorizontalPieces
            this.nudHorizontalPieces.Location = new System.Drawing.Point(110, 20);
            this.nudHorizontalPieces.Maximum = new decimal(new int[] { 20, 0, 0, 0 });
            this.nudHorizontalPieces.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.nudHorizontalPieces.Name = "nudHorizontalPieces";
            this.nudHorizontalPieces.Size = new System.Drawing.Size(88, 23);
            this.nudHorizontalPieces.TabIndex = 0;
            this.nudHorizontalPieces.Value = new decimal(new int[] { 2, 0, 0, 0 });
            this.nudHorizontalPieces.ValueChanged += new System.EventHandler(this.OnByCountValueChanged);

            // lblVerticalPieces
            this.lblVerticalPieces.AutoSize = true;
            this.lblVerticalPieces.Location = new System.Drawing.Point(10, 49);
            this.lblVerticalPieces.Name = "lblVerticalPieces";
            this.lblVerticalPieces.Size = new System.Drawing.Size(63, 15);
            this.lblVerticalPieces.Text = "V. Pieces:";

            // nudVerticalPieces
            this.nudVerticalPieces.Location = new System.Drawing.Point(110, 47);
            this.nudVerticalPieces.Maximum = new decimal(new int[] { 20, 0, 0, 0 });
            this.nudVerticalPieces.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.nudVerticalPieces.Name = "nudVerticalPieces";
            this.nudVerticalPieces.Size = new System.Drawing.Size(88, 23);
            this.nudVerticalPieces.TabIndex = 1;
            this.nudVerticalPieces.Value = new decimal(new int[] { 1, 0, 0, 0 });
            this.nudVerticalPieces.ValueChanged += new System.EventHandler(this.OnByCountValueChanged);

            // ---- Split Type Group ----
            this.grpType.Dock = System.Windows.Forms.DockStyle.Top;
            this.grpType.Location = new System.Drawing.Point(6, 284);
            this.grpType.Name = "grpType";
            this.grpType.Size = new System.Drawing.Size(208, 95);
            this.grpType.TabIndex = 3;
            this.grpType.TabStop = false;
            this.grpType.Text = "Split Type";
            this.grpType.Controls.Add(this.radSpike);
            this.grpType.Controls.Add(this.radTabs);
            this.grpType.Controls.Add(this.radStraight);

            // radStraight
            this.radStraight.AutoSize = true;
            this.radStraight.Checked = true;
            this.radStraight.Location = new System.Drawing.Point(10, 20);
            this.radStraight.Name = "radStraight";
            this.radStraight.Size = new System.Drawing.Size(68, 19);
            this.radStraight.TabIndex = 0;
            this.radStraight.TabStop = true;
            this.radStraight.Text = "Straight";
            this.radStraight.CheckedChanged += new System.EventHandler(this.OnTypeChanged);

            // radTabs
            this.radTabs.AutoSize = true;
            this.radTabs.Location = new System.Drawing.Point(10, 43);
            this.radTabs.Name = "radTabs";
            this.radTabs.Size = new System.Drawing.Size(107, 19);
            this.radTabs.TabIndex = 1;
            this.radTabs.Text = "Weld-Gap Tabs";
            this.radTabs.CheckedChanged += new System.EventHandler(this.OnTypeChanged);

            // radSpike
            this.radSpike.AutoSize = true;
            this.radSpike.Location = new System.Drawing.Point(10, 66);
            this.radSpike.Name = "radSpike";
            this.radSpike.Size = new System.Drawing.Size(99, 19);
            this.radSpike.TabIndex = 2;
            this.radSpike.Text = "Spike-Groove";
            this.radSpike.CheckedChanged += new System.EventHandler(this.OnTypeChanged);

            // ---- Tab Parameters Group ----
            this.grpTabParams.Dock = System.Windows.Forms.DockStyle.Top;
            this.grpTabParams.Location = new System.Drawing.Point(6, 379);
            this.grpTabParams.Name = "grpTabParams";
            this.grpTabParams.Size = new System.Drawing.Size(208, 105);
            this.grpTabParams.TabIndex = 4;
            this.grpTabParams.TabStop = false;
            this.grpTabParams.Text = "Tab Parameters";
            this.grpTabParams.Visible = false;
            this.grpTabParams.Controls.Add(this.nudTabCount);
            this.grpTabParams.Controls.Add(this.lblTabCount);
            this.grpTabParams.Controls.Add(this.nudTabHeight);
            this.grpTabParams.Controls.Add(this.lblTabHeight);
            this.grpTabParams.Controls.Add(this.nudTabWidth);
            this.grpTabParams.Controls.Add(this.lblTabWidth);

            // lblTabWidth
            this.lblTabWidth.AutoSize = true;
            this.lblTabWidth.Location = new System.Drawing.Point(10, 22);
            this.lblTabWidth.Name = "lblTabWidth";
            this.lblTabWidth.Size = new System.Drawing.Size(65, 15);
            this.lblTabWidth.Text = "Tab Width:";

            // nudTabWidth
            this.nudTabWidth.DecimalPlaces = 2;
            this.nudTabWidth.Location = new System.Drawing.Point(110, 20);
            this.nudTabWidth.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
            this.nudTabWidth.Minimum = new decimal(new int[] { 1, 0, 0, 131072 });
            this.nudTabWidth.Name = "nudTabWidth";
            this.nudTabWidth.Size = new System.Drawing.Size(88, 23);
            this.nudTabWidth.TabIndex = 0;
            this.nudTabWidth.Value = new decimal(new int[] { 5, 0, 0, 65536 });

            // lblTabHeight
            this.lblTabHeight.AutoSize = true;
            this.lblTabHeight.Location = new System.Drawing.Point(10, 49);
            this.lblTabHeight.Name = "lblTabHeight";
            this.lblTabHeight.Size = new System.Drawing.Size(69, 15);
            this.lblTabHeight.Text = "Tab Height:";

            // nudTabHeight
            this.nudTabHeight.DecimalPlaces = 2;
            this.nudTabHeight.Location = new System.Drawing.Point(110, 47);
            this.nudTabHeight.Maximum = new decimal(new int[] { 100, 0, 0, 0 });
            this.nudTabHeight.Minimum = new decimal(new int[] { 1, 0, 0, 131072 });
            this.nudTabHeight.Name = "nudTabHeight";
            this.nudTabHeight.Size = new System.Drawing.Size(88, 23);
            this.nudTabHeight.TabIndex = 1;
            this.nudTabHeight.Value = new decimal(new int[] { 1, 0, 0, 65536 });

            // lblTabCount
            this.lblTabCount.AutoSize = true;
            this.lblTabCount.Location = new System.Drawing.Point(10, 76);
            this.lblTabCount.Name = "lblTabCount";
            this.lblTabCount.Size = new System.Drawing.Size(64, 15);
            this.lblTabCount.Text = "Tab Count:";

            // nudTabCount
            this.nudTabCount.Location = new System.Drawing.Point(110, 74);
            this.nudTabCount.Maximum = new decimal(new int[] { 50, 0, 0, 0 });
            this.nudTabCount.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.nudTabCount.Name = "nudTabCount";
            this.nudTabCount.Size = new System.Drawing.Size(88, 23);
            this.nudTabCount.TabIndex = 2;
            this.nudTabCount.Value = new decimal(new int[] { 3, 0, 0, 0 });

            // ---- Spike Parameters Group ----
            this.grpSpikeParams.Dock = System.Windows.Forms.DockStyle.Top;
            this.grpSpikeParams.Location = new System.Drawing.Point(6, 484);
            this.grpSpikeParams.Name = "grpSpikeParams";
            this.grpSpikeParams.Size = new System.Drawing.Size(208, 105);
            this.grpSpikeParams.TabIndex = 5;
            this.grpSpikeParams.TabStop = false;
            this.grpSpikeParams.Text = "Spike Parameters";
            this.grpSpikeParams.Visible = false;
            this.grpSpikeParams.Controls.Add(this.nudSpikePairCount);
            this.grpSpikeParams.Controls.Add(this.lblSpikePairCount);
            this.grpSpikeParams.Controls.Add(this.nudSpikeAngle);
            this.grpSpikeParams.Controls.Add(this.lblSpikeAngle);
            this.grpSpikeParams.Controls.Add(this.nudSpikeDepth);
            this.grpSpikeParams.Controls.Add(this.lblSpikeDepth);

            // lblSpikeDepth
            this.lblSpikeDepth.AutoSize = true;
            this.lblSpikeDepth.Location = new System.Drawing.Point(10, 22);
            this.lblSpikeDepth.Name = "lblSpikeDepth";
            this.lblSpikeDepth.Size = new System.Drawing.Size(74, 15);
            this.lblSpikeDepth.Text = "Spike Depth:";

            // nudSpikeDepth
            this.nudSpikeDepth.DecimalPlaces = 2;
            this.nudSpikeDepth.Location = new System.Drawing.Point(110, 20);
            this.nudSpikeDepth.Maximum = new decimal(new int[] { 100, 0, 0, 0 });
            this.nudSpikeDepth.Minimum = new decimal(new int[] { 1, 0, 0, 131072 });
            this.nudSpikeDepth.Name = "nudSpikeDepth";
            this.nudSpikeDepth.Size = new System.Drawing.Size(88, 23);
            this.nudSpikeDepth.TabIndex = 0;
            this.nudSpikeDepth.Value = new decimal(new int[] { 5, 0, 0, 65536 });

            // lblSpikeAngle
            this.lblSpikeAngle.AutoSize = true;
            this.lblSpikeAngle.Location = new System.Drawing.Point(10, 49);
            this.lblSpikeAngle.Name = "lblSpikeAngle";
            this.lblSpikeAngle.Size = new System.Drawing.Size(74, 15);
            this.lblSpikeAngle.Text = "Spike Angle:";

            // nudSpikeAngle
            this.nudSpikeAngle.DecimalPlaces = 1;
            this.nudSpikeAngle.Location = new System.Drawing.Point(110, 47);
            this.nudSpikeAngle.Maximum = new decimal(new int[] { 89, 0, 0, 0 });
            this.nudSpikeAngle.Minimum = new decimal(new int[] { 10, 0, 0, 0 });
            this.nudSpikeAngle.Name = "nudSpikeAngle";
            this.nudSpikeAngle.Size = new System.Drawing.Size(88, 23);
            this.nudSpikeAngle.TabIndex = 1;
            this.nudSpikeAngle.Value = new decimal(new int[] { 45, 0, 0, 0 });

            // lblSpikePairCount
            this.lblSpikePairCount.AutoSize = true;
            this.lblSpikePairCount.Location = new System.Drawing.Point(10, 76);
            this.lblSpikePairCount.Name = "lblSpikePairCount";
            this.lblSpikePairCount.Size = new System.Drawing.Size(65, 15);
            this.lblSpikePairCount.Text = "Pair Count:";

            // nudSpikePairCount
            this.nudSpikePairCount.Location = new System.Drawing.Point(110, 74);
            this.nudSpikePairCount.Maximum = new decimal(new int[] { 50, 0, 0, 0 });
            this.nudSpikePairCount.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.nudSpikePairCount.Name = "nudSpikePairCount";
            this.nudSpikePairCount.Size = new System.Drawing.Size(88, 23);
            this.nudSpikePairCount.TabIndex = 2;
            this.nudSpikePairCount.Value = new decimal(new int[] { 3, 0, 0, 0 });

            // ---- OK / Cancel Buttons ----
            this.btnOK.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnOK.Location = new System.Drawing.Point(30, 468);
            this.btnOK.Name = "btnOK";
            this.btnOK.Size = new System.Drawing.Size(80, 28);
            this.btnOK.TabIndex = 6;
            this.btnOK.Text = "OK";
            this.btnOK.UseVisualStyleBackColor = true;
            this.btnOK.Click += new System.EventHandler(this.OnOK);

            this.btnCancel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Location = new System.Drawing.Point(120, 468);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(80, 28);
            this.btnCancel.TabIndex = 7;
            this.btnCancel.Text = "Cancel";
            this.btnCancel.UseVisualStyleBackColor = true;
            this.btnCancel.Click += new System.EventHandler(this.OnCancel);

            // ---- Preview Panel (fills remaining space) ----
            this.pnlPreview.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlPreview.Location = new System.Drawing.Point(0, 25);
            this.pnlPreview.Name = "pnlPreview";
            this.pnlPreview.Size = new System.Drawing.Size(580, 503);
            this.pnlPreview.TabIndex = 3;
            this.pnlPreview.Paint += new System.Windows.Forms.PaintEventHandler(this.OnPreviewPaint);
            this.pnlPreview.MouseDown += new System.Windows.Forms.MouseEventHandler(this.OnPreviewMouseDown);
            this.pnlPreview.MouseMove += new System.Windows.Forms.MouseEventHandler(this.OnPreviewMouseMove);
            this.pnlPreview.MouseUp += new System.Windows.Forms.MouseEventHandler(this.OnPreviewMouseUp);
            this.pnlPreview.MouseWheel += new System.Windows.Forms.MouseEventHandler(this.OnPreviewMouseWheel);

            // ---- SplitDrawingForm ----
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(800, 550);
            this.Controls.Add(this.pnlPreview);
            this.Controls.Add(this.pnlSettings);
            this.Controls.Add(this.statusStrip);
            this.Controls.Add(this.toolStrip);
            this.MinimumSize = new System.Drawing.Size(600, 450);
            this.Name = "SplitDrawingForm";
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Split Drawing";

            ((System.ComponentModel.ISupportInitialize)(this.nudPlateWidth)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudPlateHeight)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudEdgeSpacing)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudHorizontalPieces)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudVerticalPieces)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudTabWidth)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudTabHeight)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudTabCount)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudSpikeDepth)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudSpikeAngle)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudSpikePairCount)).EndInit();
            this.pnlSettings.ResumeLayout(false);
            this.grpMethod.ResumeLayout(false);
            this.grpMethod.PerformLayout();
            this.grpAutoFit.ResumeLayout(false);
            this.grpAutoFit.PerformLayout();
            this.grpByCount.ResumeLayout(false);
            this.grpByCount.PerformLayout();
            this.grpType.ResumeLayout(false);
            this.grpType.PerformLayout();
            this.grpTabParams.ResumeLayout(false);
            this.grpTabParams.PerformLayout();
            this.grpSpikeParams.ResumeLayout(false);
            this.grpSpikeParams.PerformLayout();
            this.toolStrip.ResumeLayout(false);
            this.toolStrip.PerformLayout();
            this.statusStrip.ResumeLayout(false);
            this.statusStrip.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Panel pnlPreview;
        private System.Windows.Forms.Panel pnlSettings;
        private System.Windows.Forms.ToolStrip toolStrip;
        private System.Windows.Forms.ToolStripButton btnAddLine;
        private System.Windows.Forms.ToolStripButton btnDeleteLine;
        private System.Windows.Forms.StatusStrip statusStrip;
        private System.Windows.Forms.ToolStripStatusLabel lblStatus;
        private System.Windows.Forms.ToolStripStatusLabel lblCursor;

        private System.Windows.Forms.GroupBox grpMethod;
        private System.Windows.Forms.RadioButton radManual;
        private System.Windows.Forms.RadioButton radFitToPlate;
        private System.Windows.Forms.RadioButton radByCount;

        private System.Windows.Forms.GroupBox grpAutoFit;
        private System.Windows.Forms.Label lblPlateWidth;
        private System.Windows.Forms.NumericUpDown nudPlateWidth;
        private System.Windows.Forms.Label lblPlateHeight;
        private System.Windows.Forms.NumericUpDown nudPlateHeight;
        private System.Windows.Forms.Label lblEdgeSpacing;
        private System.Windows.Forms.NumericUpDown nudEdgeSpacing;
        private System.Windows.Forms.Label lblSplitAxis;
        private System.Windows.Forms.ComboBox cboSplitAxis;

        private System.Windows.Forms.GroupBox grpByCount;
        private System.Windows.Forms.Label lblHorizontalPieces;
        private System.Windows.Forms.NumericUpDown nudHorizontalPieces;
        private System.Windows.Forms.Label lblVerticalPieces;
        private System.Windows.Forms.NumericUpDown nudVerticalPieces;

        private System.Windows.Forms.GroupBox grpType;
        private System.Windows.Forms.RadioButton radStraight;
        private System.Windows.Forms.RadioButton radTabs;
        private System.Windows.Forms.RadioButton radSpike;

        private System.Windows.Forms.GroupBox grpTabParams;
        private System.Windows.Forms.Label lblTabWidth;
        private System.Windows.Forms.NumericUpDown nudTabWidth;
        private System.Windows.Forms.Label lblTabHeight;
        private System.Windows.Forms.NumericUpDown nudTabHeight;
        private System.Windows.Forms.Label lblTabCount;
        private System.Windows.Forms.NumericUpDown nudTabCount;

        private System.Windows.Forms.GroupBox grpSpikeParams;
        private System.Windows.Forms.Label lblSpikeDepth;
        private System.Windows.Forms.NumericUpDown nudSpikeDepth;
        private System.Windows.Forms.Label lblSpikeAngle;
        private System.Windows.Forms.NumericUpDown nudSpikeAngle;
        private System.Windows.Forms.Label lblSpikePairCount;
        private System.Windows.Forms.NumericUpDown nudSpikePairCount;

        private System.Windows.Forms.Button btnOK;
        private System.Windows.Forms.Button btnCancel;
    }
}
