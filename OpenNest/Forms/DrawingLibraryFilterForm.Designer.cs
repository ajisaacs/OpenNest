namespace OpenNest.Forms
{
    partial class DrawingLibraryFilterForm
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
            rootTable = new System.Windows.Forms.TableLayoutPanel();
            customerLabel = new System.Windows.Forms.Label();
            customerBox = new System.Windows.Forms.ComboBox();
            thicknessLabel = new System.Windows.Forms.Label();
            minThicknessBox = new OpenNest.Controls.ThicknessComboBox();
            thicknessToLabel = new System.Windows.Forms.Label();
            maxThicknessBox = new OpenNest.Controls.ThicknessComboBox();
            blankThicknessCheckBox = new System.Windows.Forms.CheckBox();
            lengthLabel = new System.Windows.Forms.Label();
            minLengthBox = new System.Windows.Forms.TextBox();
            lengthToLabel = new System.Windows.Forms.Label();
            maxLengthBox = new System.Windows.Forms.TextBox();
            widthLabel = new System.Windows.Forms.Label();
            minWidthBox = new System.Windows.Forms.TextBox();
            widthToLabel = new System.Windows.Forms.Label();
            maxWidthBox = new System.Windows.Forms.TextBox();
            holesLabel = new System.Windows.Forms.Label();
            minHolesBox = new System.Windows.Forms.TextBox();
            holesToLabel = new System.Windows.Forms.Label();
            maxHolesBox = new System.Windows.Forms.TextBox();
            savedLabel = new System.Windows.Forms.Label();
            savedFromPicker = new System.Windows.Forms.DateTimePicker();
            savedToLabel = new System.Windows.Forms.Label();
            savedToPicker = new System.Windows.Forms.DateTimePicker();
            noteLabel = new System.Windows.Forms.Label();
            errorLabel = new System.Windows.Forms.Label();
            buttonPanel = new System.Windows.Forms.FlowLayoutPanel();
            cancelButton = new System.Windows.Forms.Button();
            okButton = new System.Windows.Forms.Button();
            clearButton = new System.Windows.Forms.Button();
            rootTable.SuspendLayout();
            buttonPanel.SuspendLayout();
            SuspendLayout();
            //
            // rootTable
            //
            rootTable.AutoSize = true;
            rootTable.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            rootTable.ColumnCount = 4;
            rootTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            rootTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 150F));
            rootTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            rootTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 150F));
            rootTable.Controls.Add(customerLabel, 0, 0);
            rootTable.Controls.Add(customerBox, 1, 0);
            rootTable.Controls.Add(thicknessLabel, 0, 1);
            rootTable.Controls.Add(minThicknessBox, 1, 1);
            rootTable.Controls.Add(thicknessToLabel, 2, 1);
            rootTable.Controls.Add(maxThicknessBox, 3, 1);
            rootTable.Controls.Add(blankThicknessCheckBox, 1, 2);
            rootTable.Controls.Add(lengthLabel, 0, 3);
            rootTable.Controls.Add(minLengthBox, 1, 3);
            rootTable.Controls.Add(lengthToLabel, 2, 3);
            rootTable.Controls.Add(maxLengthBox, 3, 3);
            rootTable.Controls.Add(widthLabel, 0, 4);
            rootTable.Controls.Add(minWidthBox, 1, 4);
            rootTable.Controls.Add(widthToLabel, 2, 4);
            rootTable.Controls.Add(maxWidthBox, 3, 4);
            rootTable.Controls.Add(holesLabel, 0, 5);
            rootTable.Controls.Add(minHolesBox, 1, 5);
            rootTable.Controls.Add(holesToLabel, 2, 5);
            rootTable.Controls.Add(maxHolesBox, 3, 5);
            rootTable.Controls.Add(savedLabel, 0, 6);
            rootTable.Controls.Add(savedFromPicker, 1, 6);
            rootTable.Controls.Add(savedToLabel, 2, 6);
            rootTable.Controls.Add(savedToPicker, 3, 6);
            rootTable.Controls.Add(noteLabel, 0, 7);
            rootTable.Controls.Add(errorLabel, 0, 8);
            rootTable.Controls.Add(buttonPanel, 0, 9);
            rootTable.SetColumnSpan(customerBox, 3);
            rootTable.SetColumnSpan(blankThicknessCheckBox, 3);
            rootTable.SetColumnSpan(noteLabel, 4);
            rootTable.SetColumnSpan(errorLabel, 4);
            rootTable.SetColumnSpan(buttonPanel, 4);
            rootTable.Location = new System.Drawing.Point(12, 12);
            rootTable.Name = "rootTable";
            rootTable.RowCount = 10;
            rootTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            rootTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            rootTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            rootTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            rootTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            rootTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            rootTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            rootTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            rootTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            rootTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            rootTable.TabIndex = 0;
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
            customerBox.Name = "customerBox";
            customerBox.TabIndex = 1;
            //
            // thicknessLabel
            //
            thicknessLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            thicknessLabel.AutoSize = true;
            thicknessLabel.Name = "thicknessLabel";
            thicknessLabel.Text = "Thickness (in):";
            //
            // minThicknessBox
            //
            minThicknessBox.Dock = System.Windows.Forms.DockStyle.Fill;
            minThicknessBox.Name = "minThicknessBox";
            minThicknessBox.TabIndex = 2;
            //
            // thicknessToLabel
            //
            thicknessToLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            thicknessToLabel.AutoSize = true;
            thicknessToLabel.Name = "thicknessToLabel";
            thicknessToLabel.Text = "to";
            //
            // maxThicknessBox
            //
            maxThicknessBox.Dock = System.Windows.Forms.DockStyle.Fill;
            maxThicknessBox.Name = "maxThicknessBox";
            maxThicknessBox.TabIndex = 3;
            //
            // blankThicknessCheckBox
            //
            blankThicknessCheckBox.AutoSize = true;
            blankThicknessCheckBox.Checked = true;
            blankThicknessCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            blankThicknessCheckBox.Name = "blankThicknessCheckBox";
            blankThicknessCheckBox.TabIndex = 4;
            blankThicknessCheckBox.Text = "Include drawings saved without a thickness";
            blankThicknessCheckBox.UseVisualStyleBackColor = true;
            //
            // lengthLabel
            //
            lengthLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            lengthLabel.AutoSize = true;
            lengthLabel.Name = "lengthLabel";
            lengthLabel.Text = "Longer side (in):";
            //
            // minLengthBox
            //
            minLengthBox.Dock = System.Windows.Forms.DockStyle.Fill;
            minLengthBox.Name = "minLengthBox";
            minLengthBox.TabIndex = 5;
            //
            // lengthToLabel
            //
            lengthToLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            lengthToLabel.AutoSize = true;
            lengthToLabel.Name = "lengthToLabel";
            lengthToLabel.Text = "to";
            //
            // maxLengthBox
            //
            maxLengthBox.Dock = System.Windows.Forms.DockStyle.Fill;
            maxLengthBox.Name = "maxLengthBox";
            maxLengthBox.TabIndex = 6;
            //
            // widthLabel
            //
            widthLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            widthLabel.AutoSize = true;
            widthLabel.Name = "widthLabel";
            widthLabel.Text = "Shorter side (in):";
            //
            // minWidthBox
            //
            minWidthBox.Dock = System.Windows.Forms.DockStyle.Fill;
            minWidthBox.Name = "minWidthBox";
            minWidthBox.TabIndex = 7;
            //
            // widthToLabel
            //
            widthToLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            widthToLabel.AutoSize = true;
            widthToLabel.Name = "widthToLabel";
            widthToLabel.Text = "to";
            //
            // maxWidthBox
            //
            maxWidthBox.Dock = System.Windows.Forms.DockStyle.Fill;
            maxWidthBox.Name = "maxWidthBox";
            maxWidthBox.TabIndex = 8;
            //
            // holesLabel
            //
            holesLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            holesLabel.AutoSize = true;
            holesLabel.Name = "holesLabel";
            holesLabel.Text = "Holes:";
            //
            // minHolesBox
            //
            minHolesBox.Dock = System.Windows.Forms.DockStyle.Fill;
            minHolesBox.Name = "minHolesBox";
            minHolesBox.TabIndex = 9;
            //
            // holesToLabel
            //
            holesToLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            holesToLabel.AutoSize = true;
            holesToLabel.Name = "holesToLabel";
            holesToLabel.Text = "to";
            //
            // maxHolesBox
            //
            maxHolesBox.Dock = System.Windows.Forms.DockStyle.Fill;
            maxHolesBox.Name = "maxHolesBox";
            maxHolesBox.TabIndex = 10;
            //
            // savedLabel
            //
            savedLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            savedLabel.AutoSize = true;
            savedLabel.Name = "savedLabel";
            savedLabel.Text = "Saved:";
            //
            // savedFromPicker
            //
            savedFromPicker.Dock = System.Windows.Forms.DockStyle.Fill;
            savedFromPicker.Format = System.Windows.Forms.DateTimePickerFormat.Short;
            savedFromPicker.Name = "savedFromPicker";
            savedFromPicker.ShowCheckBox = true;
            savedFromPicker.TabIndex = 11;
            //
            // savedToLabel
            //
            savedToLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            savedToLabel.AutoSize = true;
            savedToLabel.Name = "savedToLabel";
            savedToLabel.Text = "to";
            //
            // savedToPicker
            //
            savedToPicker.Dock = System.Windows.Forms.DockStyle.Fill;
            savedToPicker.Format = System.Windows.Forms.DateTimePickerFormat.Short;
            savedToPicker.Name = "savedToPicker";
            savedToPicker.ShowCheckBox = true;
            savedToPicker.TabIndex = 12;
            //
            // noteLabel
            //
            noteLabel.AutoSize = true;
            noteLabel.ForeColor = System.Drawing.SystemColors.GrayText;
            noteLabel.Margin = new System.Windows.Forms.Padding(3, 8, 3, 0);
            noteLabel.MaximumSize = new System.Drawing.Size(440, 0);
            noteLabel.Name = "noteLabel";
            noteLabel.Text = "Leave a box blank for no limit; every limit includes its value. Thickness takes "
                + "gauges and fractions (16 ga, 3/16). Sizes are the bounding box of the cut "
                + "geometry and holes are closed cutouts, both measured by the server, so a size "
                + "or hole limit leaves out drawings it has not measured yet.";
            //
            // errorLabel
            //
            errorLabel.AutoSize = true;
            errorLabel.ForeColor = System.Drawing.Color.Firebrick;
            errorLabel.Margin = new System.Windows.Forms.Padding(3, 6, 3, 0);
            errorLabel.MaximumSize = new System.Drawing.Size(440, 0);
            errorLabel.Name = "errorLabel";
            //
            // buttonPanel
            //
            buttonPanel.AutoSize = true;
            buttonPanel.Controls.Add(cancelButton);
            buttonPanel.Controls.Add(okButton);
            buttonPanel.Controls.Add(clearButton);
            buttonPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            buttonPanel.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
            buttonPanel.Margin = new System.Windows.Forms.Padding(0, 8, 0, 0);
            buttonPanel.Name = "buttonPanel";
            buttonPanel.TabIndex = 13;
            //
            // cancelButton
            //
            cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            cancelButton.Name = "cancelButton";
            cancelButton.Size = new System.Drawing.Size(80, 27);
            cancelButton.TabIndex = 2;
            cancelButton.Text = "Cancel";
            cancelButton.UseVisualStyleBackColor = true;
            //
            // okButton
            //
            okButton.Name = "okButton";
            okButton.Size = new System.Drawing.Size(80, 27);
            okButton.TabIndex = 1;
            okButton.Text = "OK";
            okButton.UseVisualStyleBackColor = true;
            okButton.Click += OkButton_Click;
            //
            // clearButton
            //
            clearButton.Margin = new System.Windows.Forms.Padding(3, 3, 60, 3);
            clearButton.Name = "clearButton";
            clearButton.Size = new System.Drawing.Size(80, 27);
            clearButton.TabIndex = 0;
            clearButton.Text = "Clear";
            clearButton.UseVisualStyleBackColor = true;
            clearButton.Click += ClearButton_Click;
            //
            // DrawingLibraryFilterForm
            //
            AcceptButton = okButton;
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            AutoSize = true;
            AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            CancelButton = cancelButton;
            Controls.Add(rootTable);
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "DrawingLibraryFilterForm";
            Padding = new System.Windows.Forms.Padding(12);
            ShowInTaskbar = false;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "Filter Drawing Library";
            rootTable.ResumeLayout(false);
            rootTable.PerformLayout();
            buttonPanel.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel rootTable;
        private System.Windows.Forms.Label customerLabel;
        private System.Windows.Forms.ComboBox customerBox;
        private System.Windows.Forms.Label thicknessLabel;
        private OpenNest.Controls.ThicknessComboBox minThicknessBox;
        private System.Windows.Forms.Label thicknessToLabel;
        private OpenNest.Controls.ThicknessComboBox maxThicknessBox;
        private System.Windows.Forms.CheckBox blankThicknessCheckBox;
        private System.Windows.Forms.Label lengthLabel;
        private System.Windows.Forms.TextBox minLengthBox;
        private System.Windows.Forms.Label lengthToLabel;
        private System.Windows.Forms.TextBox maxLengthBox;
        private System.Windows.Forms.Label widthLabel;
        private System.Windows.Forms.TextBox minWidthBox;
        private System.Windows.Forms.Label widthToLabel;
        private System.Windows.Forms.TextBox maxWidthBox;
        private System.Windows.Forms.Label holesLabel;
        private System.Windows.Forms.TextBox minHolesBox;
        private System.Windows.Forms.Label holesToLabel;
        private System.Windows.Forms.TextBox maxHolesBox;
        private System.Windows.Forms.Label savedLabel;
        private System.Windows.Forms.DateTimePicker savedFromPicker;
        private System.Windows.Forms.Label savedToLabel;
        private System.Windows.Forms.DateTimePicker savedToPicker;
        private System.Windows.Forms.Label noteLabel;
        private System.Windows.Forms.Label errorLabel;
        private System.Windows.Forms.FlowLayoutPanel buttonPanel;
        private System.Windows.Forms.Button cancelButton;
        private System.Windows.Forms.Button okButton;
        private System.Windows.Forms.Button clearButton;
    }
}
