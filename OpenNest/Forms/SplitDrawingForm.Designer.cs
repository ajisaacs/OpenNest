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
            pnlSettings = new System.Windows.Forms.Panel();
            grpSpikeParams = new System.Windows.Forms.GroupBox();
            nudSpikePairCount = new System.Windows.Forms.NumericUpDown();
            lblSpikePairCount = new System.Windows.Forms.Label();
            nudSpikeWeldGap = new System.Windows.Forms.NumericUpDown();
            lblSpikeWeldGap = new System.Windows.Forms.Label();
            nudGrooveDepth = new System.Windows.Forms.NumericUpDown();
            lblGrooveDepth = new System.Windows.Forms.Label();
            nudSpikeAngle = new System.Windows.Forms.NumericUpDown();
            lblSpikeAngle = new System.Windows.Forms.Label();
            grpTabParams = new System.Windows.Forms.GroupBox();
            nudTabCount = new System.Windows.Forms.NumericUpDown();
            lblTabCount = new System.Windows.Forms.Label();
            nudTabHeight = new System.Windows.Forms.NumericUpDown();
            lblTabHeight = new System.Windows.Forms.Label();
            nudTabWidth = new System.Windows.Forms.NumericUpDown();
            lblTabWidth = new System.Windows.Forms.Label();
            grpType = new System.Windows.Forms.GroupBox();
            radSpike = new System.Windows.Forms.RadioButton();
            radTabs = new System.Windows.Forms.RadioButton();
            radStraight = new System.Windows.Forms.RadioButton();
            grpByCount = new System.Windows.Forms.GroupBox();
            nudVerticalPieces = new System.Windows.Forms.NumericUpDown();
            lblVerticalPieces = new System.Windows.Forms.Label();
            nudHorizontalPieces = new System.Windows.Forms.NumericUpDown();
            lblHorizontalPieces = new System.Windows.Forms.Label();
            grpAutoFit = new System.Windows.Forms.GroupBox();
            cboSplitAxis = new System.Windows.Forms.ComboBox();
            lblSplitAxis = new System.Windows.Forms.Label();
            nudEdgeSpacing = new System.Windows.Forms.NumericUpDown();
            lblEdgeSpacing = new System.Windows.Forms.Label();
            nudPlateHeight = new System.Windows.Forms.NumericUpDown();
            lblPlateHeight = new System.Windows.Forms.Label();
            nudPlateWidth = new System.Windows.Forms.NumericUpDown();
            lblPlateWidth = new System.Windows.Forms.Label();
            grpMethod = new System.Windows.Forms.GroupBox();
            radByCount = new System.Windows.Forms.RadioButton();
            radFitToPlate = new System.Windows.Forms.RadioButton();
            radManual = new System.Windows.Forms.RadioButton();
            pnlButtons = new System.Windows.Forms.Panel();
            btnOK = new System.Windows.Forms.Button();
            btnCancel = new System.Windows.Forms.Button();
            pnlPreview = new SplitPreview();
            toolStrip = new System.Windows.Forms.ToolStrip();
            btnAddLine = new System.Windows.Forms.ToolStripButton();
            btnDeleteLine = new System.Windows.Forms.ToolStripButton();
            statusStrip = new System.Windows.Forms.StatusStrip();
            lblStatus = new System.Windows.Forms.ToolStripStatusLabel();
            lblCursor = new System.Windows.Forms.ToolStripStatusLabel();
            pnlSettings.SuspendLayout();
            grpSpikeParams.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)nudSpikePairCount).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudSpikeWeldGap).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudGrooveDepth).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudSpikeAngle).BeginInit();
            grpTabParams.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)nudTabCount).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudTabHeight).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudTabWidth).BeginInit();
            grpType.SuspendLayout();
            grpByCount.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)nudVerticalPieces).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudHorizontalPieces).BeginInit();
            grpAutoFit.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)nudEdgeSpacing).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudPlateHeight).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudPlateWidth).BeginInit();
            grpMethod.SuspendLayout();
            pnlButtons.SuspendLayout();
            toolStrip.SuspendLayout();
            statusStrip.SuspendLayout();
            SuspendLayout();
            // 
            // pnlSettings
            // 
            pnlSettings.AutoScroll = true;
            pnlSettings.Controls.Add(grpSpikeParams);
            pnlSettings.Controls.Add(grpTabParams);
            pnlSettings.Controls.Add(grpType);
            pnlSettings.Controls.Add(grpByCount);
            pnlSettings.Controls.Add(grpAutoFit);
            pnlSettings.Controls.Add(grpMethod);
            pnlSettings.Controls.Add(pnlButtons);
            pnlSettings.Dock = System.Windows.Forms.DockStyle.Right;
            pnlSettings.Location = new System.Drawing.Point(647, 25);
            pnlSettings.Name = "pnlSettings";
            pnlSettings.Padding = new System.Windows.Forms.Padding(6);
            pnlSettings.Size = new System.Drawing.Size(220, 611);
            pnlSettings.TabIndex = 2;
            // 
            // grpSpikeParams
            // 
            grpSpikeParams.Controls.Add(nudSpikePairCount);
            grpSpikeParams.Controls.Add(lblSpikePairCount);
            grpSpikeParams.Controls.Add(nudSpikeWeldGap);
            grpSpikeParams.Controls.Add(lblSpikeWeldGap);
            grpSpikeParams.Controls.Add(nudGrooveDepth);
            grpSpikeParams.Controls.Add(lblGrooveDepth);
            grpSpikeParams.Controls.Add(nudSpikeAngle);
            grpSpikeParams.Controls.Add(lblSpikeAngle);
            grpSpikeParams.Dock = System.Windows.Forms.DockStyle.Top;
            grpSpikeParams.Location = new System.Drawing.Point(6, 511);
            grpSpikeParams.Name = "grpSpikeParams";
            grpSpikeParams.Size = new System.Drawing.Size(191, 132);
            grpSpikeParams.TabIndex = 5;
            grpSpikeParams.TabStop = false;
            grpSpikeParams.Text = "Spike Parameters";
            grpSpikeParams.Visible = false;
            // 
            // nudSpikePairCount
            // 
            nudSpikePairCount.Location = new System.Drawing.Point(110, 101);
            nudSpikePairCount.Maximum = new decimal(new int[] { 50, 0, 0, 0 });
            nudSpikePairCount.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            nudSpikePairCount.Name = "nudSpikePairCount";
            nudSpikePairCount.Size = new System.Drawing.Size(88, 23);
            nudSpikePairCount.TabIndex = 4;
            nudSpikePairCount.Value = new decimal(new int[] { 2, 0, 0, 0 });
            nudSpikePairCount.ValueChanged += OnFeatureCountChanged;
            // 
            // lblSpikePairCount
            // 
            lblSpikePairCount.AutoSize = true;
            lblSpikePairCount.Location = new System.Drawing.Point(10, 103);
            lblSpikePairCount.Name = "lblSpikePairCount";
            lblSpikePairCount.Size = new System.Drawing.Size(66, 15);
            lblSpikePairCount.TabIndex = 5;
            lblSpikePairCount.Text = "Pair Count:";
            // 
            // nudSpikeWeldGap
            // 
            nudSpikeWeldGap.DecimalPlaces = 3;
            nudSpikeWeldGap.Location = new System.Drawing.Point(110, 47);
            nudSpikeWeldGap.Maximum = new decimal(new int[] { 10, 0, 0, 0 });
            nudSpikeWeldGap.Name = "nudSpikeWeldGap";
            nudSpikeWeldGap.Size = new System.Drawing.Size(88, 23);
            nudSpikeWeldGap.TabIndex = 2;
            nudSpikeWeldGap.Value = new decimal(new int[] { 125, 0, 0, 196608 });
            nudSpikeWeldGap.ValueChanged += OnSpikeParamChanged;
            // 
            // lblSpikeWeldGap
            // 
            lblSpikeWeldGap.AutoSize = true;
            lblSpikeWeldGap.Location = new System.Drawing.Point(10, 49);
            lblSpikeWeldGap.Name = "lblSpikeWeldGap";
            lblSpikeWeldGap.Size = new System.Drawing.Size(61, 15);
            lblSpikeWeldGap.TabIndex = 6;
            lblSpikeWeldGap.Text = "Weld Gap:";
            // 
            // nudGrooveDepth
            // 
            nudGrooveDepth.DecimalPlaces = 3;
            nudGrooveDepth.Location = new System.Drawing.Point(110, 20);
            nudGrooveDepth.Minimum = new decimal(new int[] { 1, 0, 0, 131072 });
            nudGrooveDepth.Name = "nudGrooveDepth";
            nudGrooveDepth.Size = new System.Drawing.Size(88, 23);
            nudGrooveDepth.TabIndex = 1;
            nudGrooveDepth.Value = new decimal(new int[] { 125, 0, 0, 196608 });
            nudGrooveDepth.ValueChanged += OnSpikeParamChanged;
            // 
            // lblGrooveDepth
            // 
            lblGrooveDepth.AutoSize = true;
            lblGrooveDepth.Location = new System.Drawing.Point(10, 22);
            lblGrooveDepth.Name = "lblGrooveDepth";
            lblGrooveDepth.Size = new System.Drawing.Size(83, 15);
            lblGrooveDepth.TabIndex = 7;
            lblGrooveDepth.Text = "Groove Depth:";
            // 
            // nudSpikeAngle
            // 
            nudSpikeAngle.DecimalPlaces = 1;
            nudSpikeAngle.Location = new System.Drawing.Point(110, 74);
            nudSpikeAngle.Maximum = new decimal(new int[] { 89, 0, 0, 0 });
            nudSpikeAngle.Minimum = new decimal(new int[] { 10, 0, 0, 0 });
            nudSpikeAngle.Name = "nudSpikeAngle";
            nudSpikeAngle.Size = new System.Drawing.Size(88, 23);
            nudSpikeAngle.TabIndex = 3;
            nudSpikeAngle.Value = new decimal(new int[] { 45, 0, 0, 0 });
            // 
            // lblSpikeAngle
            // 
            lblSpikeAngle.AutoSize = true;
            lblSpikeAngle.Location = new System.Drawing.Point(10, 76);
            lblSpikeAngle.Name = "lblSpikeAngle";
            lblSpikeAngle.Size = new System.Drawing.Size(72, 15);
            lblSpikeAngle.TabIndex = 8;
            lblSpikeAngle.Text = "Spike Angle:";
            // 
            // grpTabParams
            // 
            grpTabParams.Controls.Add(nudTabCount);
            grpTabParams.Controls.Add(lblTabCount);
            grpTabParams.Controls.Add(nudTabHeight);
            grpTabParams.Controls.Add(lblTabHeight);
            grpTabParams.Controls.Add(nudTabWidth);
            grpTabParams.Controls.Add(lblTabWidth);
            grpTabParams.Dock = System.Windows.Forms.DockStyle.Top;
            grpTabParams.Location = new System.Drawing.Point(6, 406);
            grpTabParams.Name = "grpTabParams";
            grpTabParams.Size = new System.Drawing.Size(191, 105);
            grpTabParams.TabIndex = 4;
            grpTabParams.TabStop = false;
            grpTabParams.Text = "Tab Parameters";
            grpTabParams.Visible = false;
            // 
            // nudTabCount
            // 
            nudTabCount.Location = new System.Drawing.Point(110, 74);
            nudTabCount.Maximum = new decimal(new int[] { 50, 0, 0, 0 });
            nudTabCount.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            nudTabCount.Name = "nudTabCount";
            nudTabCount.Size = new System.Drawing.Size(88, 23);
            nudTabCount.TabIndex = 2;
            nudTabCount.Value = new decimal(new int[] { 2, 0, 0, 0 });
            nudTabCount.ValueChanged += OnFeatureCountChanged;
            // 
            // lblTabCount
            // 
            lblTabCount.AutoSize = true;
            lblTabCount.Location = new System.Drawing.Point(10, 76);
            lblTabCount.Name = "lblTabCount";
            lblTabCount.Size = new System.Drawing.Size(65, 15);
            lblTabCount.TabIndex = 3;
            lblTabCount.Text = "Tab Count:";
            // 
            // nudTabHeight
            // 
            nudTabHeight.DecimalPlaces = 2;
            nudTabHeight.Location = new System.Drawing.Point(110, 47);
            nudTabHeight.Minimum = new decimal(new int[] { 1, 0, 0, 131072 });
            nudTabHeight.Name = "nudTabHeight";
            nudTabHeight.Size = new System.Drawing.Size(88, 23);
            nudTabHeight.TabIndex = 1;
            nudTabHeight.Value = new decimal(new int[] { 1, 0, 0, 65536 });
            // 
            // lblTabHeight
            // 
            lblTabHeight.AutoSize = true;
            lblTabHeight.Location = new System.Drawing.Point(10, 49);
            lblTabHeight.Name = "lblTabHeight";
            lblTabHeight.Size = new System.Drawing.Size(61, 15);
            lblTabHeight.TabIndex = 4;
            lblTabHeight.Text = "Weld Gap:";
            // 
            // nudTabWidth
            // 
            nudTabWidth.DecimalPlaces = 2;
            nudTabWidth.Location = new System.Drawing.Point(110, 20);
            nudTabWidth.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
            nudTabWidth.Minimum = new decimal(new int[] { 1, 0, 0, 131072 });
            nudTabWidth.Name = "nudTabWidth";
            nudTabWidth.Size = new System.Drawing.Size(88, 23);
            nudTabWidth.TabIndex = 0;
            nudTabWidth.Value = new decimal(new int[] { 5, 0, 0, 65536 });
            // 
            // lblTabWidth
            // 
            lblTabWidth.AutoSize = true;
            lblTabWidth.Location = new System.Drawing.Point(10, 22);
            lblTabWidth.Name = "lblTabWidth";
            lblTabWidth.Size = new System.Drawing.Size(69, 15);
            lblTabWidth.TabIndex = 5;
            lblTabWidth.Text = "Tab Length:";
            // 
            // grpType
            // 
            grpType.Controls.Add(radSpike);
            grpType.Controls.Add(radTabs);
            grpType.Controls.Add(radStraight);
            grpType.Dock = System.Windows.Forms.DockStyle.Top;
            grpType.Location = new System.Drawing.Point(6, 311);
            grpType.Name = "grpType";
            grpType.Size = new System.Drawing.Size(191, 95);
            grpType.TabIndex = 3;
            grpType.TabStop = false;
            grpType.Text = "Split Type";
            // 
            // radSpike
            // 
            radSpike.AutoSize = true;
            radSpike.Location = new System.Drawing.Point(10, 66);
            radSpike.Name = "radSpike";
            radSpike.Size = new System.Drawing.Size(96, 19);
            radSpike.TabIndex = 2;
            radSpike.Text = "Spike-Groove";
            radSpike.CheckedChanged += OnTypeChanged;
            // 
            // radTabs
            // 
            radTabs.AutoSize = true;
            radTabs.Location = new System.Drawing.Point(10, 43);
            radTabs.Name = "radTabs";
            radTabs.Size = new System.Drawing.Size(105, 19);
            radTabs.TabIndex = 1;
            radTabs.Text = "Weld-Gap Tabs";
            radTabs.CheckedChanged += OnTypeChanged;
            // 
            // radStraight
            // 
            radStraight.AutoSize = true;
            radStraight.Checked = true;
            radStraight.Location = new System.Drawing.Point(10, 20);
            radStraight.Name = "radStraight";
            radStraight.Size = new System.Drawing.Size(66, 19);
            radStraight.TabIndex = 0;
            radStraight.TabStop = true;
            radStraight.Text = "Straight";
            radStraight.CheckedChanged += OnTypeChanged;
            // 
            // grpByCount
            // 
            grpByCount.Controls.Add(nudVerticalPieces);
            grpByCount.Controls.Add(lblVerticalPieces);
            grpByCount.Controls.Add(nudHorizontalPieces);
            grpByCount.Controls.Add(lblHorizontalPieces);
            grpByCount.Dock = System.Windows.Forms.DockStyle.Top;
            grpByCount.Location = new System.Drawing.Point(6, 233);
            grpByCount.Name = "grpByCount";
            grpByCount.Size = new System.Drawing.Size(191, 78);
            grpByCount.TabIndex = 2;
            grpByCount.TabStop = false;
            grpByCount.Text = "Split by Count";
            grpByCount.Visible = false;
            // 
            // nudVerticalPieces
            // 
            nudVerticalPieces.Location = new System.Drawing.Point(110, 47);
            nudVerticalPieces.Maximum = new decimal(new int[] { 20, 0, 0, 0 });
            nudVerticalPieces.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            nudVerticalPieces.Name = "nudVerticalPieces";
            nudVerticalPieces.Size = new System.Drawing.Size(88, 23);
            nudVerticalPieces.TabIndex = 1;
            nudVerticalPieces.Value = new decimal(new int[] { 1, 0, 0, 0 });
            nudVerticalPieces.ValueChanged += OnByCountValueChanged;
            // 
            // lblVerticalPieces
            // 
            lblVerticalPieces.AutoSize = true;
            lblVerticalPieces.Location = new System.Drawing.Point(10, 49);
            lblVerticalPieces.Name = "lblVerticalPieces";
            lblVerticalPieces.Size = new System.Drawing.Size(56, 15);
            lblVerticalPieces.TabIndex = 2;
            lblVerticalPieces.Text = "V. Pieces:";
            // 
            // nudHorizontalPieces
            // 
            nudHorizontalPieces.Location = new System.Drawing.Point(110, 20);
            nudHorizontalPieces.Maximum = new decimal(new int[] { 20, 0, 0, 0 });
            nudHorizontalPieces.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            nudHorizontalPieces.Name = "nudHorizontalPieces";
            nudHorizontalPieces.Size = new System.Drawing.Size(88, 23);
            nudHorizontalPieces.TabIndex = 0;
            nudHorizontalPieces.Value = new decimal(new int[] { 2, 0, 0, 0 });
            nudHorizontalPieces.ValueChanged += OnByCountValueChanged;
            // 
            // lblHorizontalPieces
            // 
            lblHorizontalPieces.AutoSize = true;
            lblHorizontalPieces.Location = new System.Drawing.Point(10, 22);
            lblHorizontalPieces.Name = "lblHorizontalPieces";
            lblHorizontalPieces.Size = new System.Drawing.Size(58, 15);
            lblHorizontalPieces.TabIndex = 3;
            lblHorizontalPieces.Text = "H. Pieces:";
            // 
            // grpAutoFit
            // 
            grpAutoFit.Controls.Add(cboSplitAxis);
            grpAutoFit.Controls.Add(lblSplitAxis);
            grpAutoFit.Controls.Add(nudEdgeSpacing);
            grpAutoFit.Controls.Add(lblEdgeSpacing);
            grpAutoFit.Controls.Add(nudPlateHeight);
            grpAutoFit.Controls.Add(lblPlateHeight);
            grpAutoFit.Controls.Add(nudPlateWidth);
            grpAutoFit.Controls.Add(lblPlateWidth);
            grpAutoFit.Dock = System.Windows.Forms.DockStyle.Top;
            grpAutoFit.Location = new System.Drawing.Point(6, 101);
            grpAutoFit.Name = "grpAutoFit";
            grpAutoFit.Size = new System.Drawing.Size(191, 132);
            grpAutoFit.TabIndex = 1;
            grpAutoFit.TabStop = false;
            grpAutoFit.Text = "Auto-Fit Options";
            grpAutoFit.Visible = false;
            // 
            // cboSplitAxis
            // 
            cboSplitAxis.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            cboSplitAxis.Items.AddRange(new object[] { "Auto", "Vertical Only", "Horizontal Only" });
            cboSplitAxis.Location = new System.Drawing.Point(110, 100);
            cboSplitAxis.Name = "cboSplitAxis";
            cboSplitAxis.Size = new System.Drawing.Size(88, 23);
            cboSplitAxis.TabIndex = 3;
            cboSplitAxis.SelectedIndexChanged += OnAutoFitValueChanged;
            // 
            // lblSplitAxis
            // 
            lblSplitAxis.AutoSize = true;
            lblSplitAxis.Location = new System.Drawing.Point(10, 103);
            lblSplitAxis.Name = "lblSplitAxis";
            lblSplitAxis.Size = new System.Drawing.Size(57, 15);
            lblSplitAxis.TabIndex = 4;
            lblSplitAxis.Text = "Split Axis:";
            // 
            // nudEdgeSpacing
            // 
            nudEdgeSpacing.DecimalPlaces = 2;
            nudEdgeSpacing.Location = new System.Drawing.Point(110, 74);
            nudEdgeSpacing.Name = "nudEdgeSpacing";
            nudEdgeSpacing.Size = new System.Drawing.Size(88, 23);
            nudEdgeSpacing.TabIndex = 2;
            nudEdgeSpacing.Value = new decimal(new int[] { 5, 0, 0, 65536 });
            nudEdgeSpacing.ValueChanged += OnAutoFitValueChanged;
            // 
            // lblEdgeSpacing
            // 
            lblEdgeSpacing.AutoSize = true;
            lblEdgeSpacing.Location = new System.Drawing.Point(10, 76);
            lblEdgeSpacing.Name = "lblEdgeSpacing";
            lblEdgeSpacing.Size = new System.Drawing.Size(81, 15);
            lblEdgeSpacing.TabIndex = 5;
            lblEdgeSpacing.Text = "Edge Spacing:";
            // 
            // nudPlateHeight
            // 
            nudPlateHeight.DecimalPlaces = 2;
            nudPlateHeight.Location = new System.Drawing.Point(110, 47);
            nudPlateHeight.Maximum = new decimal(new int[] { 100000, 0, 0, 0 });
            nudPlateHeight.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            nudPlateHeight.Name = "nudPlateHeight";
            nudPlateHeight.Size = new System.Drawing.Size(88, 23);
            nudPlateHeight.TabIndex = 1;
            nudPlateHeight.Value = new decimal(new int[] { 120, 0, 0, 0 });
            nudPlateHeight.ValueChanged += OnAutoFitValueChanged;
            // 
            // lblPlateHeight
            // 
            lblPlateHeight.AutoSize = true;
            lblPlateHeight.Location = new System.Drawing.Point(10, 49);
            lblPlateHeight.Name = "lblPlateHeight";
            lblPlateHeight.Size = new System.Drawing.Size(76, 15);
            lblPlateHeight.TabIndex = 6;
            lblPlateHeight.Text = "Plate Length:";
            // 
            // nudPlateWidth
            // 
            nudPlateWidth.DecimalPlaces = 2;
            nudPlateWidth.Location = new System.Drawing.Point(110, 20);
            nudPlateWidth.Maximum = new decimal(new int[] { 100000, 0, 0, 0 });
            nudPlateWidth.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            nudPlateWidth.Name = "nudPlateWidth";
            nudPlateWidth.Size = new System.Drawing.Size(88, 23);
            nudPlateWidth.TabIndex = 0;
            nudPlateWidth.Value = new decimal(new int[] { 60, 0, 0, 0 });
            nudPlateWidth.ValueChanged += OnAutoFitValueChanged;
            // 
            // lblPlateWidth
            // 
            lblPlateWidth.AutoSize = true;
            lblPlateWidth.Location = new System.Drawing.Point(10, 22);
            lblPlateWidth.Name = "lblPlateWidth";
            lblPlateWidth.Size = new System.Drawing.Size(71, 15);
            lblPlateWidth.TabIndex = 7;
            lblPlateWidth.Text = "Plate Width:";
            // 
            // grpMethod
            // 
            grpMethod.Controls.Add(radByCount);
            grpMethod.Controls.Add(radFitToPlate);
            grpMethod.Controls.Add(radManual);
            grpMethod.Dock = System.Windows.Forms.DockStyle.Top;
            grpMethod.Location = new System.Drawing.Point(6, 6);
            grpMethod.Name = "grpMethod";
            grpMethod.Size = new System.Drawing.Size(191, 95);
            grpMethod.TabIndex = 0;
            grpMethod.TabStop = false;
            grpMethod.Text = "Split Method";
            // 
            // radByCount
            // 
            radByCount.AutoSize = true;
            radByCount.Location = new System.Drawing.Point(10, 66);
            radByCount.Name = "radByCount";
            radByCount.Size = new System.Drawing.Size(100, 19);
            radByCount.TabIndex = 2;
            radByCount.Text = "Split by Count";
            radByCount.CheckedChanged += OnMethodChanged;
            // 
            // radFitToPlate
            // 
            radFitToPlate.AutoSize = true;
            radFitToPlate.Location = new System.Drawing.Point(10, 43);
            radFitToPlate.Name = "radFitToPlate";
            radFitToPlate.Size = new System.Drawing.Size(81, 19);
            radFitToPlate.TabIndex = 1;
            radFitToPlate.Text = "Fit to Plate";
            radFitToPlate.CheckedChanged += OnMethodChanged;
            // 
            // radManual
            // 
            radManual.AutoSize = true;
            radManual.Checked = true;
            radManual.Location = new System.Drawing.Point(10, 20);
            radManual.Name = "radManual";
            radManual.Size = new System.Drawing.Size(65, 19);
            radManual.TabIndex = 0;
            radManual.TabStop = true;
            radManual.Text = "Manual";
            radManual.CheckedChanged += OnMethodChanged;
            // 
            // pnlButtons
            // 
            pnlButtons.Controls.Add(btnOK);
            pnlButtons.Controls.Add(btnCancel);
            pnlButtons.Dock = System.Windows.Forms.DockStyle.Bottom;
            pnlButtons.Location = new System.Drawing.Point(6, 637);
            pnlButtons.Name = "pnlButtons";
            pnlButtons.Size = new System.Drawing.Size(191, 40);
            pnlButtons.TabIndex = 8;
            // 
            // btnOK
            // 
            btnOK.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            btnOK.Location = new System.Drawing.Point(11, 6);
            btnOK.Name = "btnOK";
            btnOK.Size = new System.Drawing.Size(80, 28);
            btnOK.TabIndex = 6;
            btnOK.Text = "OK";
            btnOK.UseVisualStyleBackColor = true;
            btnOK.Click += OnOK;
            // 
            // btnCancel
            // 
            btnCancel.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            btnCancel.Location = new System.Drawing.Point(101, 6);
            btnCancel.Name = "btnCancel";
            btnCancel.Size = new System.Drawing.Size(80, 28);
            btnCancel.TabIndex = 7;
            btnCancel.Text = "Cancel";
            btnCancel.UseVisualStyleBackColor = true;
            btnCancel.Click += OnCancel;
            // 
            // pnlPreview
            // 
            pnlPreview.BackColor = System.Drawing.Color.FromArgb(33, 40, 48);
            pnlPreview.Dock = System.Windows.Forms.DockStyle.Fill;
            pnlPreview.DrawOverlays = null;
            pnlPreview.Location = new System.Drawing.Point(0, 25);
            pnlPreview.Name = "pnlPreview";
            pnlPreview.Size = new System.Drawing.Size(647, 611);
            pnlPreview.TabIndex = 3;
            pnlPreview.MouseDown += OnPreviewMouseDown;
            pnlPreview.MouseMove += OnPreviewMouseMove;
            pnlPreview.MouseUp += OnPreviewMouseUp;
            // 
            // toolStrip
            // 
            toolStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { btnAddLine, btnDeleteLine });
            toolStrip.Location = new System.Drawing.Point(0, 0);
            toolStrip.Name = "toolStrip";
            toolStrip.Size = new System.Drawing.Size(867, 25);
            toolStrip.TabIndex = 0;
            // 
            // btnAddLine
            // 
            btnAddLine.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            btnAddLine.Name = "btnAddLine";
            btnAddLine.Size = new System.Drawing.Size(84, 22);
            btnAddLine.Text = "Add Split Line";
            btnAddLine.Click += OnAddSplitLine;
            // 
            // btnDeleteLine
            // 
            btnDeleteLine.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            btnDeleteLine.Name = "btnDeleteLine";
            btnDeleteLine.Size = new System.Drawing.Size(69, 22);
            btnDeleteLine.Text = "Delete Line";
            btnDeleteLine.Click += OnDeleteSplitLine;
            // 
            // statusStrip
            // 
            statusStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { lblStatus, lblCursor });
            statusStrip.Location = new System.Drawing.Point(0, 636);
            statusStrip.Name = "statusStrip";
            statusStrip.Size = new System.Drawing.Size(867, 22);
            statusStrip.TabIndex = 1;
            // 
            // lblStatus
            // 
            lblStatus.Name = "lblStatus";
            lblStatus.Size = new System.Drawing.Size(756, 17);
            lblStatus.Spring = true;
            lblStatus.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // lblCursor
            // 
            lblCursor.Name = "lblCursor";
            lblCursor.Size = new System.Drawing.Size(96, 17);
            lblCursor.Text = "Cursor: 0.00, 0.00";
            lblCursor.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // SplitDrawingForm
            // 
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            CancelButton = btnCancel;
            ClientSize = new System.Drawing.Size(867, 658);
            Controls.Add(pnlPreview);
            Controls.Add(pnlSettings);
            Controls.Add(statusStrip);
            Controls.Add(toolStrip);
            MinimumSize = new System.Drawing.Size(600, 450);
            Name = "SplitDrawingForm";
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "Split Drawing";
            pnlSettings.ResumeLayout(false);
            grpSpikeParams.ResumeLayout(false);
            grpSpikeParams.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)nudSpikePairCount).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudSpikeWeldGap).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudGrooveDepth).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudSpikeAngle).EndInit();
            grpTabParams.ResumeLayout(false);
            grpTabParams.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)nudTabCount).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudTabHeight).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudTabWidth).EndInit();
            grpType.ResumeLayout(false);
            grpType.PerformLayout();
            grpByCount.ResumeLayout(false);
            grpByCount.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)nudVerticalPieces).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudHorizontalPieces).EndInit();
            grpAutoFit.ResumeLayout(false);
            grpAutoFit.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)nudEdgeSpacing).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudPlateHeight).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudPlateWidth).EndInit();
            grpMethod.ResumeLayout(false);
            grpMethod.PerformLayout();
            pnlButtons.ResumeLayout(false);
            toolStrip.ResumeLayout(false);
            toolStrip.PerformLayout();
            statusStrip.ResumeLayout(false);
            statusStrip.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private SplitPreview pnlPreview;
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
        private System.Windows.Forms.Label lblSpikeAngle;
        private System.Windows.Forms.NumericUpDown nudSpikeAngle;
        private System.Windows.Forms.Label lblSpikePairCount;
        private System.Windows.Forms.NumericUpDown nudSpikePairCount;
        private System.Windows.Forms.Label lblGrooveDepth;
        private System.Windows.Forms.NumericUpDown nudGrooveDepth;
        private System.Windows.Forms.Label lblSpikeWeldGap;
        private System.Windows.Forms.NumericUpDown nudSpikeWeldGap;

        private System.Windows.Forms.Panel pnlButtons;
        private System.Windows.Forms.Button btnOK;
        private System.Windows.Forms.Button btnCancel;
    }
}
