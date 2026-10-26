namespace OpenNest.Forms
{
    partial class EditLibraryDrawingForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            layoutTable = new System.Windows.Forms.TableLayoutPanel();
            detailsGroup = new System.Windows.Forms.GroupBox();
            detailsTable = new System.Windows.Forms.TableLayoutPanel();
            nameLabel = new System.Windows.Forms.Label();
            nameBox = new System.Windows.Forms.TextBox();
            customerLabel = new System.Windows.Forms.Label();
            customerBox = new System.Windows.Forms.ComboBox();
            materialLabel = new System.Windows.Forms.Label();
            materialBox = new System.Windows.Forms.ComboBox();
            gradeLabel = new System.Windows.Forms.Label();
            gradeBox = new System.Windows.Forms.ComboBox();
            thicknessLabel = new System.Windows.Forms.Label();
            thicknessPanel = new System.Windows.Forms.FlowLayoutPanel();
            thicknessBox = new OpenNest.Controls.ThicknessComboBox();
            unitsLabel = new System.Windows.Forms.Label();
            cutTypeLabel = new System.Windows.Forms.Label();
            cutTypeBox = new System.Windows.Forms.ComboBox();
            nestingGroup = new System.Windows.Forms.GroupBox();
            nestingTable = new System.Windows.Forms.TableLayoutPanel();
            cutFirstCheckBox = new System.Windows.Forms.CheckBox();
            priorityLabel = new System.Windows.Forms.Label();
            priorityBox = new System.Windows.Forms.NumericUpDown();
            colorLabel = new System.Windows.Forms.Label();
            colorPanel = new System.Windows.Forms.FlowLayoutPanel();
            colorButton = new System.Windows.Forms.Button();
            automaticColorButton = new System.Windows.Forms.Button();
            resetColorButton = new System.Windows.Forms.Button();
            colorModeLabel = new System.Windows.Forms.Label();
            rotationGroup = new System.Windows.Forms.GroupBox();
            rotationTable = new System.Windows.Forms.TableLayoutPanel();
            rotationDial = new OpenNest.Controls.RotationRangeDial();
            angleTable = new System.Windows.Forms.TableLayoutPanel();
            startLabel = new System.Windows.Forms.Label();
            startBox = new System.Windows.Forms.NumericUpDown();
            endLabel = new System.Windows.Forms.Label();
            endBox = new System.Windows.Forms.NumericUpDown();
            stepLabel = new System.Windows.Forms.Label();
            stepBox = new System.Windows.Forms.NumericUpDown();
            allow180CheckBox = new System.Windows.Forms.CheckBox();
            rotationSummaryLabel = new System.Windows.Forms.Label();
            notesLabel = new System.Windows.Forms.Label();
            notesBox = new System.Windows.Forms.TextBox();
            statusLabel = new System.Windows.Forms.Label();
            buttonPanel = new System.Windows.Forms.FlowLayoutPanel();
            cancelButton = new System.Windows.Forms.Button();
            saveButton = new System.Windows.Forms.Button();
            toolTip = new System.Windows.Forms.ToolTip(components);
            layoutTable.SuspendLayout();
            detailsGroup.SuspendLayout();
            detailsTable.SuspendLayout();
            thicknessPanel.SuspendLayout();
            nestingGroup.SuspendLayout();
            nestingTable.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)priorityBox).BeginInit();
            colorPanel.SuspendLayout();
            rotationGroup.SuspendLayout();
            rotationTable.SuspendLayout();
            angleTable.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)startBox).BeginInit();
            ((System.ComponentModel.ISupportInitialize)endBox).BeginInit();
            ((System.ComponentModel.ISupportInitialize)stepBox).BeginInit();
            buttonPanel.SuspendLayout();
            SuspendLayout();
            //
            // layoutTable
            //
            layoutTable.ColumnCount = 1;
            layoutTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            layoutTable.Controls.Add(detailsGroup, 0, 0);
            layoutTable.Controls.Add(nestingGroup, 0, 1);
            layoutTable.Controls.Add(rotationGroup, 0, 2);
            layoutTable.Controls.Add(notesLabel, 0, 3);
            layoutTable.Controls.Add(notesBox, 0, 4);
            layoutTable.Controls.Add(statusLabel, 0, 5);
            layoutTable.Controls.Add(buttonPanel, 0, 6);
            layoutTable.Dock = System.Windows.Forms.DockStyle.Fill;
            layoutTable.Location = new System.Drawing.Point(0, 0);
            layoutTable.Name = "layoutTable";
            layoutTable.Padding = new System.Windows.Forms.Padding(10);
            layoutTable.RowCount = 7;
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.Size = new System.Drawing.Size(540, 800);
            layoutTable.TabIndex = 0;
            //
            // detailsGroup
            //
            detailsGroup.AutoSize = true;
            detailsGroup.Controls.Add(detailsTable);
            detailsGroup.Dock = System.Windows.Forms.DockStyle.Fill;
            detailsGroup.Name = "detailsGroup";
            detailsGroup.Padding = new System.Windows.Forms.Padding(8, 4, 8, 8);
            detailsGroup.TabIndex = 0;
            detailsGroup.TabStop = false;
            detailsGroup.Text = "Drawing";
            //
            // detailsTable
            //
            detailsTable.AutoSize = true;
            detailsTable.ColumnCount = 2;
            detailsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 90F));
            detailsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            detailsTable.Controls.Add(nameLabel, 0, 0);
            detailsTable.Controls.Add(nameBox, 1, 0);
            detailsTable.Controls.Add(customerLabel, 0, 1);
            detailsTable.Controls.Add(customerBox, 1, 1);
            detailsTable.Controls.Add(materialLabel, 0, 2);
            detailsTable.Controls.Add(materialBox, 1, 2);
            detailsTable.Controls.Add(gradeLabel, 0, 3);
            detailsTable.Controls.Add(gradeBox, 1, 3);
            detailsTable.Controls.Add(thicknessLabel, 0, 4);
            detailsTable.Controls.Add(thicknessPanel, 1, 4);
            detailsTable.Controls.Add(cutTypeLabel, 0, 5);
            detailsTable.Controls.Add(cutTypeBox, 1, 5);
            detailsTable.Dock = System.Windows.Forms.DockStyle.Fill;
            detailsTable.Name = "detailsTable";
            detailsTable.RowCount = 6;
            detailsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            detailsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            detailsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            detailsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            detailsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            detailsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            detailsTable.TabIndex = 0;
            //
            // nameLabel
            //
            nameLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            nameLabel.AutoSize = true;
            nameLabel.Name = "nameLabel";
            nameLabel.Text = "Name:";
            //
            // nameBox
            //
            nameBox.Dock = System.Windows.Forms.DockStyle.Fill;
            nameBox.MaxLength = 200;
            nameBox.Name = "nameBox";
            nameBox.TabIndex = 0;
            nameBox.TextChanged += Value_Changed;
            //
            // customerLabel
            //
            customerLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            customerLabel.AutoSize = true;
            customerLabel.Name = "customerLabel";
            customerLabel.Text = "Customer:";
            //
            // customerBox
            //
            customerBox.AutoCompleteMode = System.Windows.Forms.AutoCompleteMode.SuggestAppend;
            customerBox.AutoCompleteSource = System.Windows.Forms.AutoCompleteSource.ListItems;
            customerBox.Dock = System.Windows.Forms.DockStyle.Fill;
            customerBox.MaxDropDownItems = 16;
            customerBox.MaxLength = 200;
            customerBox.Name = "customerBox";
            customerBox.TabIndex = 1;
            customerBox.TextChanged += Value_Changed;
            //
            // materialLabel
            //
            materialLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            materialLabel.AutoSize = true;
            materialLabel.Name = "materialLabel";
            materialLabel.Text = "Material:";
            //
            // materialBox
            //
            materialBox.AutoCompleteMode = System.Windows.Forms.AutoCompleteMode.SuggestAppend;
            materialBox.AutoCompleteSource = System.Windows.Forms.AutoCompleteSource.ListItems;
            materialBox.Dock = System.Windows.Forms.DockStyle.Fill;
            materialBox.MaxDropDownItems = 16;
            materialBox.MaxLength = 200;
            materialBox.Name = "materialBox";
            materialBox.TabIndex = 2;
            materialBox.TextChanged += Material_Changed;
            //
            // gradeLabel
            //
            gradeLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            gradeLabel.AutoSize = true;
            gradeLabel.Name = "gradeLabel";
            gradeLabel.Text = "Grade:";
            //
            // gradeBox
            //
            gradeBox.AutoCompleteMode = System.Windows.Forms.AutoCompleteMode.SuggestAppend;
            gradeBox.AutoCompleteSource = System.Windows.Forms.AutoCompleteSource.ListItems;
            gradeBox.Dock = System.Windows.Forms.DockStyle.Fill;
            gradeBox.MaxDropDownItems = 16;
            gradeBox.MaxLength = 200;
            gradeBox.Name = "gradeBox";
            gradeBox.TabIndex = 3;
            gradeBox.TextChanged += Grade_Changed;
            //
            // thicknessLabel
            //
            thicknessLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            thicknessLabel.AutoSize = true;
            thicknessLabel.Name = "thicknessLabel";
            thicknessLabel.Text = "Thickness:";
            //
            // thicknessPanel
            //
            thicknessPanel.AutoSize = true;
            thicknessPanel.Controls.Add(thicknessBox);
            thicknessPanel.Controls.Add(unitsLabel);
            thicknessPanel.Margin = new System.Windows.Forms.Padding(0);
            thicknessPanel.Name = "thicknessPanel";
            thicknessPanel.TabIndex = 4;
            thicknessPanel.WrapContents = false;
            //
            // thicknessBox
            //
            thicknessBox.Name = "thicknessBox";
            thicknessBox.Size = new System.Drawing.Size(200, 25);
            thicknessBox.TabIndex = 4;
            thicknessBox.TextChanged += Value_Changed;
            //
            // unitsLabel
            //
            unitsLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            unitsLabel.AutoSize = true;
            unitsLabel.Name = "unitsLabel";
            unitsLabel.Text = "in";
            //
            // cutTypeLabel
            //
            cutTypeLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            cutTypeLabel.AutoSize = true;
            cutTypeLabel.Name = "cutTypeLabel";
            cutTypeLabel.Text = "Cut type:";
            //
            // cutTypeBox
            //
            cutTypeBox.Anchor = System.Windows.Forms.AnchorStyles.Left;
            cutTypeBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            cutTypeBox.Name = "cutTypeBox";
            cutTypeBox.Size = new System.Drawing.Size(200, 25);
            cutTypeBox.TabIndex = 5;
            cutTypeBox.SelectedIndexChanged += Value_Changed;
            //
            // nestingGroup
            //
            nestingGroup.AutoSize = true;
            nestingGroup.Controls.Add(nestingTable);
            nestingGroup.Dock = System.Windows.Forms.DockStyle.Fill;
            nestingGroup.Margin = new System.Windows.Forms.Padding(3, 8, 3, 3);
            nestingGroup.Name = "nestingGroup";
            nestingGroup.Padding = new System.Windows.Forms.Padding(8, 4, 8, 8);
            nestingGroup.TabIndex = 1;
            nestingGroup.TabStop = false;
            nestingGroup.Text = "Nesting";
            //
            // nestingTable
            //
            nestingTable.AutoSize = true;
            nestingTable.ColumnCount = 2;
            nestingTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 90F));
            nestingTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            nestingTable.Controls.Add(cutFirstCheckBox, 1, 0);
            nestingTable.Controls.Add(priorityLabel, 0, 1);
            nestingTable.Controls.Add(priorityBox, 1, 1);
            nestingTable.Controls.Add(colorLabel, 0, 2);
            nestingTable.Controls.Add(colorPanel, 1, 2);
            nestingTable.Controls.Add(colorModeLabel, 1, 3);
            nestingTable.Dock = System.Windows.Forms.DockStyle.Fill;
            nestingTable.Name = "nestingTable";
            nestingTable.RowCount = 4;
            nestingTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            nestingTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            nestingTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            nestingTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            nestingTable.TabIndex = 0;
            //
            // cutFirstCheckBox
            //
            cutFirstCheckBox.AutoSize = true;
            cutFirstCheckBox.Name = "cutFirstCheckBox";
            cutFirstCheckBox.TabIndex = 0;
            cutFirstCheckBox.Text = "Cut first (sequence these parts before others)";
            cutFirstCheckBox.UseVisualStyleBackColor = true;
            cutFirstCheckBox.CheckedChanged += Value_Changed;
            //
            // priorityLabel
            //
            priorityLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            priorityLabel.AutoSize = true;
            priorityLabel.Name = "priorityLabel";
            priorityLabel.Text = "Priority:";
            //
            // priorityBox
            //
            priorityBox.Anchor = System.Windows.Forms.AnchorStyles.Left;
            priorityBox.Maximum = int.MaxValue;
            priorityBox.Minimum = int.MinValue;
            priorityBox.Name = "priorityBox";
            priorityBox.Size = new System.Drawing.Size(100, 25);
            priorityBox.TabIndex = 1;
            priorityBox.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            priorityBox.ValueChanged += Value_Changed;
            //
            // colorLabel
            //
            colorLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            colorLabel.AutoSize = true;
            colorLabel.Name = "colorLabel";
            colorLabel.Text = "Color:";
            //
            // colorPanel
            //
            colorPanel.AutoSize = true;
            colorPanel.Controls.Add(colorButton);
            colorPanel.Controls.Add(automaticColorButton);
            colorPanel.Controls.Add(resetColorButton);
            colorPanel.Margin = new System.Windows.Forms.Padding(0);
            colorPanel.Name = "colorPanel";
            colorPanel.TabIndex = 2;
            colorPanel.WrapContents = false;
            //
            // colorButton
            //
            colorButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            colorButton.Name = "colorButton";
            colorButton.Size = new System.Drawing.Size(72, 27);
            colorButton.TabIndex = 0;
            toolTip.SetToolTip(colorButton, "Choose a custom color");
            colorButton.Click += ColorButton_Click;
            colorButton.Paint += ColorButton_Paint;
            //
            // automaticColorButton
            //
            automaticColorButton.AutoSize = true;
            automaticColorButton.Name = "automaticColorButton";
            automaticColorButton.TabIndex = 1;
            automaticColorButton.Text = "Automatic";
            toolTip.SetToolTip(automaticColorButton, "Use the color scheme's part color, which follows the scheme (Workshop, Dark, ...) in each nest");
            automaticColorButton.UseVisualStyleBackColor = true;
            automaticColorButton.Click += AutomaticColorButton_Click;
            //
            // resetColorButton
            //
            resetColorButton.AutoSize = true;
            resetColorButton.Name = "resetColorButton";
            resetColorButton.TabIndex = 2;
            resetColorButton.Text = "Reset";
            toolTip.SetToolTip(resetColorButton, "Return to the color this library revision has");
            resetColorButton.UseVisualStyleBackColor = true;
            resetColorButton.Click += ResetColorButton_Click;
            //
            // colorModeLabel
            //
            colorModeLabel.AutoSize = true;
            colorModeLabel.ForeColor = System.Drawing.SystemColors.GrayText;
            colorModeLabel.Margin = new System.Windows.Forms.Padding(3, 0, 3, 3);
            colorModeLabel.Name = "colorModeLabel";
            colorModeLabel.Text = "Automatic";
            //
            // rotationGroup
            //
            rotationGroup.AutoSize = true;
            rotationGroup.Controls.Add(rotationTable);
            rotationGroup.Dock = System.Windows.Forms.DockStyle.Fill;
            rotationGroup.Margin = new System.Windows.Forms.Padding(3, 8, 3, 3);
            rotationGroup.Name = "rotationGroup";
            rotationGroup.Padding = new System.Windows.Forms.Padding(8, 4, 8, 8);
            rotationGroup.TabIndex = 2;
            rotationGroup.TabStop = false;
            rotationGroup.Text = "Rotation";
            //
            // rotationTable
            //
            rotationTable.AutoSize = true;
            rotationTable.ColumnCount = 2;
            rotationTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            rotationTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            rotationTable.Controls.Add(rotationDial, 0, 0);
            rotationTable.Controls.Add(angleTable, 1, 0);
            rotationTable.Dock = System.Windows.Forms.DockStyle.Fill;
            rotationTable.Name = "rotationTable";
            rotationTable.RowCount = 1;
            rotationTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            rotationTable.TabIndex = 0;
            //
            // rotationDial
            //
            rotationDial.Margin = new System.Windows.Forms.Padding(0, 3, 12, 3);
            rotationDial.Name = "rotationDial";
            rotationDial.Size = new System.Drawing.Size(180, 180);
            rotationDial.TabIndex = 0;
            toolTip.SetToolTip(rotationDial, "Drag the handles to set the start (hollow) and end (solid) angles; hold Shift to snap to 15°.\r\nWith focus, the arrow keys move a handle and Space switches handles.");
            rotationDial.AnglesChanged += RotationDial_AnglesChanged;
            //
            // angleTable
            //
            angleTable.AutoSize = true;
            angleTable.ColumnCount = 2;
            angleTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            angleTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            angleTable.Controls.Add(startLabel, 0, 0);
            angleTable.Controls.Add(startBox, 1, 0);
            angleTable.Controls.Add(endLabel, 0, 1);
            angleTable.Controls.Add(endBox, 1, 1);
            angleTable.Controls.Add(stepLabel, 0, 2);
            angleTable.Controls.Add(stepBox, 1, 2);
            angleTable.Controls.Add(allow180CheckBox, 0, 3);
            angleTable.Controls.Add(rotationSummaryLabel, 0, 4);
            angleTable.SetColumnSpan(allow180CheckBox, 2);
            angleTable.SetColumnSpan(rotationSummaryLabel, 2);
            angleTable.Dock = System.Windows.Forms.DockStyle.Fill;
            angleTable.Margin = new System.Windows.Forms.Padding(0);
            angleTable.Name = "angleTable";
            angleTable.RowCount = 5;
            angleTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            angleTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            angleTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            angleTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            angleTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            angleTable.TabIndex = 1;
            //
            // startLabel
            //
            startLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            startLabel.AutoSize = true;
            startLabel.Name = "startLabel";
            startLabel.Text = "Start (°):";
            //
            // startBox
            //
            startBox.Anchor = System.Windows.Forms.AnchorStyles.Left;
            startBox.DecimalPlaces = 2;
            startBox.Maximum = new decimal(new int[] { 360, 0, 0, 0 });
            startBox.Name = "startBox";
            startBox.Size = new System.Drawing.Size(90, 25);
            startBox.TabIndex = 0;
            startBox.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            startBox.ValueChanged += Rotation_Changed;
            //
            // endLabel
            //
            endLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            endLabel.AutoSize = true;
            endLabel.Name = "endLabel";
            endLabel.Text = "End (°):";
            //
            // endBox
            //
            endBox.Anchor = System.Windows.Forms.AnchorStyles.Left;
            endBox.DecimalPlaces = 2;
            endBox.Maximum = new decimal(new int[] { 360, 0, 0, 0 });
            endBox.Name = "endBox";
            endBox.Size = new System.Drawing.Size(90, 25);
            endBox.TabIndex = 1;
            endBox.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            endBox.ValueChanged += Rotation_Changed;
            //
            // stepLabel
            //
            stepLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            stepLabel.AutoSize = true;
            stepLabel.Name = "stepLabel";
            stepLabel.Text = "Step (°):";
            //
            // stepBox
            //
            stepBox.Anchor = System.Windows.Forms.AnchorStyles.Left;
            stepBox.DecimalPlaces = 2;
            stepBox.Maximum = new decimal(new int[] { 360, 0, 0, 0 });
            stepBox.Name = "stepBox";
            stepBox.Size = new System.Drawing.Size(90, 25);
            stepBox.TabIndex = 2;
            stepBox.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            toolTip.SetToolTip(stepBox, "0 lets the nesting engines choose (0°, 90°, 180° and 270°)");
            stepBox.ValueChanged += Rotation_Changed;
            //
            // allow180CheckBox
            //
            allow180CheckBox.AutoSize = true;
            allow180CheckBox.Name = "allow180CheckBox";
            allow180CheckBox.TabIndex = 3;
            allow180CheckBox.Text = "Allow 180° equivalent";
            allow180CheckBox.UseVisualStyleBackColor = true;
            allow180CheckBox.CheckedChanged += Rotation_Changed;
            //
            // rotationSummaryLabel
            //
            rotationSummaryLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            rotationSummaryLabel.ForeColor = System.Drawing.SystemColors.GrayText;
            rotationSummaryLabel.Margin = new System.Windows.Forms.Padding(3, 8, 3, 0);
            rotationSummaryLabel.Name = "rotationSummaryLabel";
            rotationSummaryLabel.Size = new System.Drawing.Size(200, 60);
            //
            // notesLabel
            //
            notesLabel.AutoSize = true;
            notesLabel.Margin = new System.Windows.Forms.Padding(3, 8, 3, 0);
            notesLabel.Name = "notesLabel";
            notesLabel.Text = "Notes:";
            //
            // notesBox
            //
            notesBox.AcceptsReturn = true;
            notesBox.Dock = System.Windows.Forms.DockStyle.Fill;
            notesBox.MaxLength = 4000;
            notesBox.MinimumSize = new System.Drawing.Size(0, 60);
            notesBox.Multiline = true;
            notesBox.Name = "notesBox";
            notesBox.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            notesBox.TabIndex = 3;
            notesBox.TextChanged += Value_Changed;
            //
            // statusLabel
            //
            statusLabel.AutoEllipsis = true;
            statusLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            statusLabel.ForeColor = System.Drawing.Color.Firebrick;
            statusLabel.Name = "statusLabel";
            statusLabel.Size = new System.Drawing.Size(100, 20);
            //
            // buttonPanel
            //
            buttonPanel.AutoSize = true;
            buttonPanel.Controls.Add(cancelButton);
            buttonPanel.Controls.Add(saveButton);
            buttonPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            buttonPanel.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
            buttonPanel.Margin = new System.Windows.Forms.Padding(0, 6, 0, 0);
            buttonPanel.Name = "buttonPanel";
            buttonPanel.TabIndex = 4;
            //
            // cancelButton
            //
            cancelButton.AutoSize = true;
            cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            cancelButton.Name = "cancelButton";
            cancelButton.TabIndex = 1;
            cancelButton.Text = "Cancel";
            cancelButton.UseVisualStyleBackColor = true;
            //
            // saveButton
            //
            saveButton.AutoSize = true;
            saveButton.Enabled = false;
            saveButton.Name = "saveButton";
            saveButton.TabIndex = 0;
            saveButton.Text = "Save to Library";
            saveButton.UseVisualStyleBackColor = true;
            saveButton.Click += SaveButton_Click;
            //
            // EditLibraryDrawingForm
            //
            AcceptButton = saveButton;
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            CancelButton = cancelButton;
            ClientSize = new System.Drawing.Size(540, 800);
            Controls.Add(layoutTable);
            Font = new System.Drawing.Font("Segoe UI", 9.75F);
            MaximizeBox = false;
            MinimizeBox = false;
            MinimumSize = new System.Drawing.Size(520, 780);
            Name = "EditLibraryDrawingForm";
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "Edit Drawing";
            Shown += EditLibraryDrawingForm_Shown;
            thicknessPanel.ResumeLayout(false);
            thicknessPanel.PerformLayout();
            detailsTable.ResumeLayout(false);
            detailsTable.PerformLayout();
            detailsGroup.ResumeLayout(false);
            detailsGroup.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)priorityBox).EndInit();
            colorPanel.ResumeLayout(false);
            colorPanel.PerformLayout();
            nestingTable.ResumeLayout(false);
            nestingTable.PerformLayout();
            nestingGroup.ResumeLayout(false);
            nestingGroup.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)startBox).EndInit();
            ((System.ComponentModel.ISupportInitialize)endBox).EndInit();
            ((System.ComponentModel.ISupportInitialize)stepBox).EndInit();
            angleTable.ResumeLayout(false);
            angleTable.PerformLayout();
            rotationTable.ResumeLayout(false);
            rotationTable.PerformLayout();
            rotationGroup.ResumeLayout(false);
            rotationGroup.PerformLayout();
            buttonPanel.ResumeLayout(false);
            buttonPanel.PerformLayout();
            layoutTable.ResumeLayout(false);
            layoutTable.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel layoutTable;
        private System.Windows.Forms.GroupBox detailsGroup;
        private System.Windows.Forms.TableLayoutPanel detailsTable;
        private System.Windows.Forms.Label nameLabel;
        private System.Windows.Forms.TextBox nameBox;
        private System.Windows.Forms.Label customerLabel;
        private System.Windows.Forms.ComboBox customerBox;
        private System.Windows.Forms.Label materialLabel;
        private System.Windows.Forms.ComboBox materialBox;
        private System.Windows.Forms.Label gradeLabel;
        private System.Windows.Forms.ComboBox gradeBox;
        private System.Windows.Forms.Label thicknessLabel;
        private System.Windows.Forms.FlowLayoutPanel thicknessPanel;
        private OpenNest.Controls.ThicknessComboBox thicknessBox;
        private System.Windows.Forms.Label unitsLabel;
        private System.Windows.Forms.Label cutTypeLabel;
        private System.Windows.Forms.ComboBox cutTypeBox;
        private System.Windows.Forms.GroupBox nestingGroup;
        private System.Windows.Forms.TableLayoutPanel nestingTable;
        private System.Windows.Forms.CheckBox cutFirstCheckBox;
        private System.Windows.Forms.Label priorityLabel;
        private System.Windows.Forms.NumericUpDown priorityBox;
        private System.Windows.Forms.Label colorLabel;
        private System.Windows.Forms.FlowLayoutPanel colorPanel;
        private System.Windows.Forms.Button colorButton;
        private System.Windows.Forms.Button automaticColorButton;
        private System.Windows.Forms.Button resetColorButton;
        private System.Windows.Forms.Label colorModeLabel;
        private System.Windows.Forms.GroupBox rotationGroup;
        private System.Windows.Forms.TableLayoutPanel rotationTable;
        private OpenNest.Controls.RotationRangeDial rotationDial;
        private System.Windows.Forms.TableLayoutPanel angleTable;
        private System.Windows.Forms.Label startLabel;
        private System.Windows.Forms.NumericUpDown startBox;
        private System.Windows.Forms.Label endLabel;
        private System.Windows.Forms.NumericUpDown endBox;
        private System.Windows.Forms.Label stepLabel;
        private System.Windows.Forms.NumericUpDown stepBox;
        private System.Windows.Forms.CheckBox allow180CheckBox;
        private System.Windows.Forms.Label rotationSummaryLabel;
        private System.Windows.Forms.Label notesLabel;
        private System.Windows.Forms.TextBox notesBox;
        private System.Windows.Forms.Label statusLabel;
        private System.Windows.Forms.FlowLayoutPanel buttonPanel;
        private System.Windows.Forms.Button cancelButton;
        private System.Windows.Forms.Button saveButton;
        private System.Windows.Forms.ToolTip toolTip;
    }
}
