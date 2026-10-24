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
            layoutTable = new System.Windows.Forms.TableLayoutPanel();
            nameLabel = new System.Windows.Forms.Label();
            nameBox = new System.Windows.Forms.TextBox();
            customerLabel = new System.Windows.Forms.Label();
            customerBox = new System.Windows.Forms.TextBox();
            materialLabel = new System.Windows.Forms.Label();
            materialBox = new System.Windows.Forms.TextBox();
            gradeLabel = new System.Windows.Forms.Label();
            gradeBox = new System.Windows.Forms.TextBox();
            thicknessLabel = new System.Windows.Forms.Label();
            thicknessPanel = new System.Windows.Forms.FlowLayoutPanel();
            thicknessBox = new System.Windows.Forms.NumericUpDown();
            unitsLabel = new System.Windows.Forms.Label();
            cutTypeLabel = new System.Windows.Forms.Label();
            cutTypeBox = new System.Windows.Forms.ComboBox();
            settingsLabel = new System.Windows.Forms.Label();
            cutFirstCheckBox = new System.Windows.Forms.CheckBox();
            priorityLabel = new System.Windows.Forms.Label();
            priorityBox = new System.Windows.Forms.NumericUpDown();
            stepLabel = new System.Windows.Forms.Label();
            stepBox = new System.Windows.Forms.NumericUpDown();
            startLabel = new System.Windows.Forms.Label();
            startBox = new System.Windows.Forms.NumericUpDown();
            endLabel = new System.Windows.Forms.Label();
            endBox = new System.Windows.Forms.NumericUpDown();
            allow180CheckBox = new System.Windows.Forms.CheckBox();
            colorLabel = new System.Windows.Forms.Label();
            colorButton = new System.Windows.Forms.Button();
            notesLabel = new System.Windows.Forms.Label();
            notesBox = new System.Windows.Forms.TextBox();
            statusLabel = new System.Windows.Forms.Label();
            buttonPanel = new System.Windows.Forms.FlowLayoutPanel();
            cancelButton = new System.Windows.Forms.Button();
            saveButton = new System.Windows.Forms.Button();
            layoutTable.SuspendLayout();
            thicknessPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)thicknessBox).BeginInit();
            ((System.ComponentModel.ISupportInitialize)priorityBox).BeginInit();
            ((System.ComponentModel.ISupportInitialize)stepBox).BeginInit();
            ((System.ComponentModel.ISupportInitialize)startBox).BeginInit();
            ((System.ComponentModel.ISupportInitialize)endBox).BeginInit();
            buttonPanel.SuspendLayout();
            SuspendLayout();
            //
            // layoutTable
            //
            layoutTable.ColumnCount = 2;
            layoutTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            layoutTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            layoutTable.Controls.Add(nameLabel, 0, 0);
            layoutTable.Controls.Add(nameBox, 1, 0);
            layoutTable.Controls.Add(customerLabel, 0, 1);
            layoutTable.Controls.Add(customerBox, 1, 1);
            layoutTable.Controls.Add(materialLabel, 0, 2);
            layoutTable.Controls.Add(materialBox, 1, 2);
            layoutTable.Controls.Add(gradeLabel, 0, 3);
            layoutTable.Controls.Add(gradeBox, 1, 3);
            layoutTable.Controls.Add(thicknessLabel, 0, 4);
            layoutTable.Controls.Add(thicknessPanel, 1, 4);
            layoutTable.Controls.Add(cutTypeLabel, 0, 5);
            layoutTable.Controls.Add(cutTypeBox, 1, 5);
            layoutTable.Controls.Add(settingsLabel, 0, 6);
            layoutTable.Controls.Add(cutFirstCheckBox, 1, 7);
            layoutTable.Controls.Add(priorityLabel, 0, 8);
            layoutTable.Controls.Add(priorityBox, 1, 8);
            layoutTable.Controls.Add(stepLabel, 0, 9);
            layoutTable.Controls.Add(stepBox, 1, 9);
            layoutTable.Controls.Add(startLabel, 0, 10);
            layoutTable.Controls.Add(startBox, 1, 10);
            layoutTable.Controls.Add(endLabel, 0, 11);
            layoutTable.Controls.Add(endBox, 1, 11);
            layoutTable.Controls.Add(allow180CheckBox, 1, 12);
            layoutTable.Controls.Add(colorLabel, 0, 13);
            layoutTable.Controls.Add(colorButton, 1, 13);
            layoutTable.Controls.Add(notesLabel, 0, 14);
            layoutTable.Controls.Add(notesBox, 1, 14);
            layoutTable.Controls.Add(statusLabel, 0, 15);
            layoutTable.Controls.Add(buttonPanel, 0, 16);
            layoutTable.SetColumnSpan(settingsLabel, 2);
            layoutTable.SetColumnSpan(statusLabel, 2);
            layoutTable.SetColumnSpan(buttonPanel, 2);
            layoutTable.Dock = System.Windows.Forms.DockStyle.Fill;
            layoutTable.Location = new System.Drawing.Point(0, 0);
            layoutTable.Name = "layoutTable";
            layoutTable.Padding = new System.Windows.Forms.Padding(8);
            layoutTable.RowCount = 17;
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.Size = new System.Drawing.Size(464, 641);
            layoutTable.TabIndex = 0;
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
            customerBox.Dock = System.Windows.Forms.DockStyle.Fill;
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
            materialBox.Dock = System.Windows.Forms.DockStyle.Fill;
            materialBox.MaxLength = 200;
            materialBox.Name = "materialBox";
            materialBox.TabIndex = 2;
            materialBox.TextChanged += Value_Changed;
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
            gradeBox.Dock = System.Windows.Forms.DockStyle.Fill;
            gradeBox.MaxLength = 200;
            gradeBox.Name = "gradeBox";
            gradeBox.TabIndex = 3;
            gradeBox.TextChanged += Value_Changed;
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
            thicknessBox.DecimalPlaces = 4;
            thicknessBox.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            thicknessBox.Maximum = new decimal(new int[] { 100000, 0, 0, 0 });
            thicknessBox.Name = "thicknessBox";
            thicknessBox.Size = new System.Drawing.Size(100, 25);
            thicknessBox.TabIndex = 4;
            thicknessBox.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            thicknessBox.ValueChanged += Value_Changed;
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
            cutTypeBox.Size = new System.Drawing.Size(120, 25);
            cutTypeBox.TabIndex = 5;
            cutTypeBox.SelectedIndexChanged += Value_Changed;
            //
            // settingsLabel
            //
            settingsLabel.AutoSize = true;
            settingsLabel.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Bold);
            settingsLabel.Margin = new System.Windows.Forms.Padding(3, 12, 3, 3);
            settingsLabel.Name = "settingsLabel";
            settingsLabel.Text = "Nesting settings";
            //
            // cutFirstCheckBox
            //
            cutFirstCheckBox.AutoSize = true;
            cutFirstCheckBox.Name = "cutFirstCheckBox";
            cutFirstCheckBox.TabIndex = 6;
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
            priorityBox.TabIndex = 7;
            priorityBox.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            priorityBox.ValueChanged += Value_Changed;
            //
            // stepLabel
            //
            stepLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            stepLabel.AutoSize = true;
            stepLabel.Name = "stepLabel";
            stepLabel.Text = "Rotation step (°):";
            //
            // stepBox
            //
            stepBox.Anchor = System.Windows.Forms.AnchorStyles.Left;
            stepBox.DecimalPlaces = 2;
            stepBox.Maximum = new decimal(new int[] { 360, 0, 0, 0 });
            stepBox.Name = "stepBox";
            stepBox.Size = new System.Drawing.Size(100, 25);
            stepBox.TabIndex = 8;
            stepBox.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            stepBox.ValueChanged += Value_Changed;
            //
            // startLabel
            //
            startLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            startLabel.AutoSize = true;
            startLabel.Name = "startLabel";
            startLabel.Text = "Rotation start (°):";
            //
            // startBox
            //
            startBox.Anchor = System.Windows.Forms.AnchorStyles.Left;
            startBox.DecimalPlaces = 2;
            startBox.Maximum = new decimal(new int[] { 360, 0, 0, 0 });
            startBox.Name = "startBox";
            startBox.Size = new System.Drawing.Size(100, 25);
            startBox.TabIndex = 9;
            startBox.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            startBox.ValueChanged += Value_Changed;
            //
            // endLabel
            //
            endLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            endLabel.AutoSize = true;
            endLabel.Name = "endLabel";
            endLabel.Text = "Rotation end (°):";
            //
            // endBox
            //
            endBox.Anchor = System.Windows.Forms.AnchorStyles.Left;
            endBox.DecimalPlaces = 2;
            endBox.Maximum = new decimal(new int[] { 360, 0, 0, 0 });
            endBox.Name = "endBox";
            endBox.Size = new System.Drawing.Size(100, 25);
            endBox.TabIndex = 10;
            endBox.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            endBox.ValueChanged += Value_Changed;
            //
            // allow180CheckBox
            //
            allow180CheckBox.AutoSize = true;
            allow180CheckBox.Name = "allow180CheckBox";
            allow180CheckBox.TabIndex = 11;
            allow180CheckBox.Text = "Allow 180° equivalent";
            allow180CheckBox.UseVisualStyleBackColor = true;
            allow180CheckBox.CheckedChanged += Value_Changed;
            //
            // colorLabel
            //
            colorLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            colorLabel.AutoSize = true;
            colorLabel.Name = "colorLabel";
            colorLabel.Text = "Color:";
            //
            // colorButton
            //
            colorButton.Anchor = System.Windows.Forms.AnchorStyles.Left;
            colorButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            colorButton.Name = "colorButton";
            colorButton.Size = new System.Drawing.Size(60, 25);
            colorButton.TabIndex = 12;
            colorButton.Click += ColorButton_Click;
            //
            // notesLabel
            //
            notesLabel.AutoSize = true;
            notesLabel.Margin = new System.Windows.Forms.Padding(3, 6, 3, 0);
            notesLabel.Name = "notesLabel";
            notesLabel.Text = "Notes:";
            //
            // notesBox
            //
            notesBox.AcceptsReturn = true;
            notesBox.Dock = System.Windows.Forms.DockStyle.Fill;
            notesBox.MaxLength = 4000;
            notesBox.Multiline = true;
            notesBox.Name = "notesBox";
            notesBox.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            notesBox.TabIndex = 13;
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
            buttonPanel.TabIndex = 14;
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
            ClientSize = new System.Drawing.Size(464, 641);
            Controls.Add(layoutTable);
            Font = new System.Drawing.Font("Segoe UI", 9.75F);
            MaximizeBox = false;
            MinimizeBox = false;
            MinimumSize = new System.Drawing.Size(420, 600);
            Name = "EditLibraryDrawingForm";
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "Edit Drawing";
            thicknessPanel.ResumeLayout(false);
            thicknessPanel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)thicknessBox).EndInit();
            layoutTable.ResumeLayout(false);
            layoutTable.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)priorityBox).EndInit();
            ((System.ComponentModel.ISupportInitialize)stepBox).EndInit();
            ((System.ComponentModel.ISupportInitialize)startBox).EndInit();
            ((System.ComponentModel.ISupportInitialize)endBox).EndInit();
            buttonPanel.ResumeLayout(false);
            buttonPanel.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel layoutTable;
        private System.Windows.Forms.Label nameLabel;
        private System.Windows.Forms.TextBox nameBox;
        private System.Windows.Forms.Label customerLabel;
        private System.Windows.Forms.TextBox customerBox;
        private System.Windows.Forms.Label materialLabel;
        private System.Windows.Forms.TextBox materialBox;
        private System.Windows.Forms.Label gradeLabel;
        private System.Windows.Forms.TextBox gradeBox;
        private System.Windows.Forms.Label thicknessLabel;
        private System.Windows.Forms.FlowLayoutPanel thicknessPanel;
        private System.Windows.Forms.NumericUpDown thicknessBox;
        private System.Windows.Forms.Label unitsLabel;
        private System.Windows.Forms.Label cutTypeLabel;
        private System.Windows.Forms.ComboBox cutTypeBox;
        private System.Windows.Forms.Label settingsLabel;
        private System.Windows.Forms.CheckBox cutFirstCheckBox;
        private System.Windows.Forms.Label priorityLabel;
        private System.Windows.Forms.NumericUpDown priorityBox;
        private System.Windows.Forms.Label stepLabel;
        private System.Windows.Forms.NumericUpDown stepBox;
        private System.Windows.Forms.Label startLabel;
        private System.Windows.Forms.NumericUpDown startBox;
        private System.Windows.Forms.Label endLabel;
        private System.Windows.Forms.NumericUpDown endBox;
        private System.Windows.Forms.CheckBox allow180CheckBox;
        private System.Windows.Forms.Label colorLabel;
        private System.Windows.Forms.Button colorButton;
        private System.Windows.Forms.Label notesLabel;
        private System.Windows.Forms.TextBox notesBox;
        private System.Windows.Forms.Label statusLabel;
        private System.Windows.Forms.FlowLayoutPanel buttonPanel;
        private System.Windows.Forms.Button cancelButton;
        private System.Windows.Forms.Button saveButton;
    }
}
