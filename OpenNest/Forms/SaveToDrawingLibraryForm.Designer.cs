namespace OpenNest.Forms
{
    partial class SaveToDrawingLibraryForm
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
            nameLabel = new System.Windows.Forms.Label();
            nameBox = new System.Windows.Forms.TextBox();
            customerLabel = new System.Windows.Forms.Label();
            customerBox = new System.Windows.Forms.TextBox();
            materialLabel = new System.Windows.Forms.Label();
            materialBox = new System.Windows.Forms.ComboBox();
            gradeLabel = new System.Windows.Forms.Label();
            gradeBox = new System.Windows.Forms.TextBox();
            thicknessLabel = new System.Windows.Forms.Label();
            thicknessBox = new System.Windows.Forms.NumericUpDown();
            cutTypeLabel = new System.Windows.Forms.Label();
            cutTypeBox = new System.Windows.Forms.ComboBox();
            notesLabel = new System.Windows.Forms.Label();
            notesBox = new System.Windows.Forms.TextBox();
            settingsTitleLabel = new System.Windows.Forms.Label();
            settingsLabel = new System.Windows.Forms.Label();
            saveAsLabel = new System.Windows.Forms.Label();
            saveAsBox = new System.Windows.Forms.ComboBox();
            duplicateLabel = new System.Windows.Forms.Label();
            statusLabel = new System.Windows.Forms.Label();
            buttonPanel = new System.Windows.Forms.FlowLayoutPanel();
            cancelButton = new System.Windows.Forms.Button();
            saveButton = new System.Windows.Forms.Button();
            lookupTimer = new System.Windows.Forms.Timer(components);
            layoutTable.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)thicknessBox).BeginInit();
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
            layoutTable.Controls.Add(thicknessBox, 1, 4);
            layoutTable.Controls.Add(cutTypeLabel, 0, 5);
            layoutTable.Controls.Add(cutTypeBox, 1, 5);
            layoutTable.Controls.Add(notesLabel, 0, 6);
            layoutTable.Controls.Add(notesBox, 1, 6);
            layoutTable.Controls.Add(settingsTitleLabel, 0, 7);
            layoutTable.Controls.Add(settingsLabel, 1, 7);
            layoutTable.Controls.Add(saveAsLabel, 0, 8);
            layoutTable.Controls.Add(saveAsBox, 1, 8);
            layoutTable.Controls.Add(duplicateLabel, 0, 9);
            layoutTable.Controls.Add(statusLabel, 0, 10);
            layoutTable.Controls.Add(buttonPanel, 0, 11);
            layoutTable.SetColumnSpan(duplicateLabel, 2);
            layoutTable.SetColumnSpan(statusLabel, 2);
            layoutTable.SetColumnSpan(buttonPanel, 2);
            layoutTable.Dock = System.Windows.Forms.DockStyle.Fill;
            layoutTable.Location = new System.Drawing.Point(0, 0);
            layoutTable.Name = "layoutTable";
            layoutTable.Padding = new System.Windows.Forms.Padding(8);
            layoutTable.RowCount = 12;
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
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.Size = new System.Drawing.Size(584, 481);
            layoutTable.TabIndex = 0;
            //
            // nameLabel
            //
            nameLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            nameLabel.AutoSize = true;
            nameLabel.Name = "nameLabel";
            nameLabel.TabIndex = 0;
            nameLabel.Text = "Name:";
            //
            // nameBox
            //
            nameBox.Dock = System.Windows.Forms.DockStyle.Fill;
            nameBox.MaxLength = 200;
            nameBox.Name = "nameBox";
            nameBox.TabIndex = 1;
            nameBox.TextChanged += Details_Changed;
            //
            // customerLabel
            //
            customerLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            customerLabel.AutoSize = true;
            customerLabel.Name = "customerLabel";
            customerLabel.TabIndex = 2;
            customerLabel.Text = "Customer:";
            //
            // customerBox
            //
            customerBox.Dock = System.Windows.Forms.DockStyle.Fill;
            customerBox.MaxLength = 200;
            customerBox.Name = "customerBox";
            customerBox.TabIndex = 3;
            customerBox.TextChanged += Details_Changed;
            //
            // materialLabel
            //
            materialLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            materialLabel.AutoSize = true;
            materialLabel.Name = "materialLabel";
            materialLabel.TabIndex = 4;
            materialLabel.Text = "Material:";
            //
            // materialBox
            //
            materialBox.Dock = System.Windows.Forms.DockStyle.Fill;
            materialBox.MaxLength = 200;
            materialBox.Name = "materialBox";
            materialBox.TabIndex = 5;
            materialBox.TextChanged += Details_Changed;
            //
            // gradeLabel
            //
            gradeLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            gradeLabel.AutoSize = true;
            gradeLabel.Name = "gradeLabel";
            gradeLabel.TabIndex = 6;
            gradeLabel.Text = "Grade:";
            //
            // gradeBox
            //
            gradeBox.Dock = System.Windows.Forms.DockStyle.Fill;
            gradeBox.MaxLength = 200;
            gradeBox.Name = "gradeBox";
            gradeBox.TabIndex = 7;
            gradeBox.TextChanged += Details_Changed;
            //
            // thicknessLabel
            //
            thicknessLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            thicknessLabel.AutoSize = true;
            thicknessLabel.Name = "thicknessLabel";
            thicknessLabel.TabIndex = 8;
            thicknessLabel.Text = "Thickness:";
            //
            // thicknessBox
            //
            thicknessBox.Anchor = System.Windows.Forms.AnchorStyles.Left;
            thicknessBox.DecimalPlaces = 4;
            thicknessBox.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            thicknessBox.Maximum = new decimal(new int[] { 10000, 0, 0, 0 });
            thicknessBox.Name = "thicknessBox";
            thicknessBox.Size = new System.Drawing.Size(100, 25);
            thicknessBox.TabIndex = 9;
            thicknessBox.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            thicknessBox.ValueChanged += Details_Changed;
            //
            // cutTypeLabel
            //
            cutTypeLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            cutTypeLabel.AutoSize = true;
            cutTypeLabel.Name = "cutTypeLabel";
            cutTypeLabel.TabIndex = 10;
            cutTypeLabel.Text = "Cut type:";
            //
            // cutTypeBox
            //
            cutTypeBox.Anchor = System.Windows.Forms.AnchorStyles.Left;
            cutTypeBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            cutTypeBox.Name = "cutTypeBox";
            cutTypeBox.Size = new System.Drawing.Size(140, 25);
            cutTypeBox.TabIndex = 11;
            cutTypeBox.SelectedIndexChanged += Details_Changed;
            //
            // notesLabel
            //
            notesLabel.AutoSize = true;
            notesLabel.Margin = new System.Windows.Forms.Padding(3, 6, 3, 0);
            notesLabel.Name = "notesLabel";
            notesLabel.TabIndex = 12;
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
            notesBox.TextChanged += Notes_Changed;
            //
            // settingsTitleLabel
            //
            settingsTitleLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            settingsTitleLabel.AutoSize = true;
            settingsTitleLabel.Name = "settingsTitleLabel";
            settingsTitleLabel.Text = "Nesting:";
            //
            // settingsLabel
            //
            settingsLabel.AutoEllipsis = true;
            settingsLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            settingsLabel.Name = "settingsLabel";
            settingsLabel.Size = new System.Drawing.Size(100, 24);
            settingsLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // saveAsLabel
            //
            saveAsLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            saveAsLabel.AutoSize = true;
            saveAsLabel.Name = "saveAsLabel";
            saveAsLabel.TabIndex = 14;
            saveAsLabel.Text = "Save as:";
            //
            // saveAsBox
            //
            saveAsBox.Dock = System.Windows.Forms.DockStyle.Fill;
            saveAsBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            saveAsBox.Name = "saveAsBox";
            saveAsBox.TabIndex = 15;
            saveAsBox.SelectedIndexChanged += SaveAsBox_SelectedIndexChanged;
            //
            // duplicateLabel
            //
            duplicateLabel.AutoEllipsis = true;
            duplicateLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            duplicateLabel.ForeColor = System.Drawing.SystemColors.GrayText;
            duplicateLabel.Name = "duplicateLabel";
            duplicateLabel.Size = new System.Drawing.Size(100, 20);
            duplicateLabel.TabIndex = 16;
            duplicateLabel.Text = "Checking the library...";
            //
            // statusLabel
            //
            statusLabel.AutoEllipsis = true;
            statusLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            statusLabel.ForeColor = System.Drawing.Color.Firebrick;
            statusLabel.Name = "statusLabel";
            statusLabel.Size = new System.Drawing.Size(100, 20);
            statusLabel.TabIndex = 17;
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
            buttonPanel.TabIndex = 18;
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
            // lookupTimer
            //
            lookupTimer.Interval = 400;
            lookupTimer.Tick += LookupTimer_Tick;
            //
            // SaveToDrawingLibraryForm
            //
            AcceptButton = saveButton;
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            CancelButton = cancelButton;
            ClientSize = new System.Drawing.Size(584, 481);
            Controls.Add(layoutTable);
            Font = new System.Drawing.Font("Segoe UI", 9.75F);
            MaximizeBox = false;
            MinimizeBox = false;
            MinimumSize = new System.Drawing.Size(480, 420);
            Name = "SaveToDrawingLibraryForm";
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "Save to Drawing Library";
            Shown += SaveToDrawingLibraryForm_Shown;
            layoutTable.ResumeLayout(false);
            layoutTable.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)thicknessBox).EndInit();
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
        private System.Windows.Forms.ComboBox materialBox;
        private System.Windows.Forms.Label gradeLabel;
        private System.Windows.Forms.TextBox gradeBox;
        private System.Windows.Forms.Label thicknessLabel;
        private System.Windows.Forms.NumericUpDown thicknessBox;
        private System.Windows.Forms.Label cutTypeLabel;
        private System.Windows.Forms.ComboBox cutTypeBox;
        private System.Windows.Forms.Label notesLabel;
        private System.Windows.Forms.TextBox notesBox;
        private System.Windows.Forms.Label settingsTitleLabel;
        private System.Windows.Forms.Label settingsLabel;
        private System.Windows.Forms.Label saveAsLabel;
        private System.Windows.Forms.ComboBox saveAsBox;
        private System.Windows.Forms.Label duplicateLabel;
        private System.Windows.Forms.Label statusLabel;
        private System.Windows.Forms.FlowLayoutPanel buttonPanel;
        private System.Windows.Forms.Button cancelButton;
        private System.Windows.Forms.Button saveButton;
        private System.Windows.Forms.Timer lookupTimer;
    }
}
