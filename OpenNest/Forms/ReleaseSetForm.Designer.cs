namespace OpenNest.Forms
{
    partial class ReleaseSetForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                cancellation?.Dispose();
                if (components != null)
                    components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            System.Windows.Forms.DataGridViewCellStyle rightAligned = new System.Windows.Forms.DataGridViewCellStyle();
            layoutTable = new System.Windows.Forms.TableLayoutPanel();
            setLabel = new System.Windows.Forms.Label();
            optionsTable = new System.Windows.Forms.TableLayoutPanel();
            multiplierLabel = new System.Windows.Forms.Label();
            multiplierBox = new System.Windows.Forms.NumericUpDown();
            includeArchivedCheckBox = new System.Windows.Forms.CheckBox();
            notesLabel = new System.Windows.Forms.Label();
            notesBox = new System.Windows.Forms.TextBox();
            groupGrid = new System.Windows.Forms.DataGridView();
            includeColumn = new System.Windows.Forms.DataGridViewCheckBoxColumn();
            materialColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            gradeColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            thicknessColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            unitsColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            cutTypeColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            drawingsColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            partsColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            statusColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            skippedLabel = new System.Windows.Forms.Label();
            bottomTable = new System.Windows.Forms.TableLayoutPanel();
            summaryLabel = new System.Windows.Forms.Label();
            releaseButton = new System.Windows.Forms.Button();
            closeButton = new System.Windows.Forms.Button();
            statusLabel = new System.Windows.Forms.Label();
            layoutTable.SuspendLayout();
            optionsTable.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)multiplierBox).BeginInit();
            ((System.ComponentModel.ISupportInitialize)groupGrid).BeginInit();
            bottomTable.SuspendLayout();
            SuspendLayout();
            //
            // layoutTable
            //
            layoutTable.ColumnCount = 1;
            layoutTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            layoutTable.Controls.Add(setLabel, 0, 0);
            layoutTable.Controls.Add(optionsTable, 0, 1);
            layoutTable.Controls.Add(groupGrid, 0, 2);
            layoutTable.Controls.Add(skippedLabel, 0, 3);
            layoutTable.Controls.Add(bottomTable, 0, 4);
            layoutTable.Controls.Add(statusLabel, 0, 5);
            layoutTable.Dock = System.Windows.Forms.DockStyle.Fill;
            layoutTable.Location = new System.Drawing.Point(0, 0);
            layoutTable.Name = "layoutTable";
            layoutTable.Padding = new System.Windows.Forms.Padding(8);
            layoutTable.RowCount = 6;
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.Size = new System.Drawing.Size(944, 521);
            layoutTable.TabIndex = 0;
            //
            // setLabel
            //
            setLabel.AutoEllipsis = true;
            setLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            setLabel.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Bold);
            setLabel.Margin = new System.Windows.Forms.Padding(3, 0, 3, 6);
            setLabel.Name = "setLabel";
            setLabel.Size = new System.Drawing.Size(100, 20);
            setLabel.TabIndex = 0;
            //
            // optionsTable
            //
            optionsTable.AutoSize = true;
            optionsTable.ColumnCount = 5;
            optionsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            optionsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            optionsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            optionsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            optionsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            optionsTable.Controls.Add(multiplierLabel, 0, 0);
            optionsTable.Controls.Add(multiplierBox, 1, 0);
            optionsTable.Controls.Add(includeArchivedCheckBox, 2, 0);
            optionsTable.Controls.Add(notesLabel, 3, 0);
            optionsTable.Controls.Add(notesBox, 4, 0);
            optionsTable.Dock = System.Windows.Forms.DockStyle.Fill;
            optionsTable.Margin = new System.Windows.Forms.Padding(0, 0, 0, 6);
            optionsTable.Name = "optionsTable";
            optionsTable.RowCount = 1;
            optionsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            optionsTable.TabIndex = 1;
            //
            // multiplierLabel
            //
            multiplierLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            multiplierLabel.AutoSize = true;
            multiplierLabel.Name = "multiplierLabel";
            multiplierLabel.TabIndex = 0;
            multiplierLabel.Text = "Kits (multiplier):";
            //
            // multiplierBox
            //
            multiplierBox.Anchor = System.Windows.Forms.AnchorStyles.Left;
            multiplierBox.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
            multiplierBox.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            multiplierBox.Name = "multiplierBox";
            multiplierBox.Size = new System.Drawing.Size(70, 25);
            multiplierBox.TabIndex = 1;
            multiplierBox.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            multiplierBox.Value = new decimal(new int[] { 1, 0, 0, 0 });
            multiplierBox.ValueChanged += Options_Changed;
            //
            // includeArchivedCheckBox
            //
            includeArchivedCheckBox.Anchor = System.Windows.Forms.AnchorStyles.Left;
            includeArchivedCheckBox.AutoSize = true;
            includeArchivedCheckBox.Margin = new System.Windows.Forms.Padding(12, 3, 3, 3);
            includeArchivedCheckBox.Name = "includeArchivedCheckBox";
            includeArchivedCheckBox.TabIndex = 2;
            includeArchivedCheckBox.Text = "Include archived drawings";
            includeArchivedCheckBox.UseVisualStyleBackColor = true;
            includeArchivedCheckBox.CheckedChanged += Options_Changed;
            //
            // notesLabel
            //
            notesLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            notesLabel.AutoSize = true;
            notesLabel.Margin = new System.Windows.Forms.Padding(12, 0, 3, 0);
            notesLabel.Name = "notesLabel";
            notesLabel.TabIndex = 3;
            notesLabel.Text = "Release notes:";
            //
            // notesBox
            //
            notesBox.Dock = System.Windows.Forms.DockStyle.Fill;
            notesBox.MaxLength = 4000;
            notesBox.Name = "notesBox";
            notesBox.TabIndex = 4;
            //
            // groupGrid
            //
            groupGrid.AllowUserToAddRows = false;
            groupGrid.AllowUserToDeleteRows = false;
            groupGrid.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] { includeColumn, materialColumn, gradeColumn, thicknessColumn, unitsColumn, cutTypeColumn, drawingsColumn, partsColumn, statusColumn });
            groupGrid.Dock = System.Windows.Forms.DockStyle.Fill;
            groupGrid.MultiSelect = false;
            groupGrid.Name = "groupGrid";
            groupGrid.RowHeadersVisible = false;
            groupGrid.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            groupGrid.StandardTab = true;
            groupGrid.TabIndex = 2;
            groupGrid.CellValueChanged += GroupGrid_CellValueChanged;
            groupGrid.CurrentCellDirtyStateChanged += GroupGrid_CurrentCellDirtyStateChanged;
            //
            // includeColumn
            //
            includeColumn.HeaderText = "Release";
            includeColumn.Name = "includeColumn";
            includeColumn.Width = 65;
            //
            // materialColumn
            //
            materialColumn.HeaderText = "Material";
            materialColumn.Name = "materialColumn";
            materialColumn.ReadOnly = true;
            materialColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            materialColumn.Width = 140;
            //
            // gradeColumn
            //
            gradeColumn.HeaderText = "Grade";
            gradeColumn.Name = "gradeColumn";
            gradeColumn.ReadOnly = true;
            gradeColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            gradeColumn.Width = 90;
            //
            // thicknessColumn
            //
            rightAligned.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleRight;
            thicknessColumn.DefaultCellStyle = rightAligned;
            thicknessColumn.HeaderText = "Thickness";
            thicknessColumn.Name = "thicknessColumn";
            thicknessColumn.ReadOnly = true;
            thicknessColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            thicknessColumn.Width = 80;
            //
            // unitsColumn
            //
            unitsColumn.HeaderText = "Units";
            unitsColumn.Name = "unitsColumn";
            unitsColumn.ReadOnly = true;
            unitsColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            unitsColumn.Width = 55;
            //
            // cutTypeColumn
            //
            cutTypeColumn.HeaderText = "Cut type";
            cutTypeColumn.Name = "cutTypeColumn";
            cutTypeColumn.ReadOnly = true;
            cutTypeColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            cutTypeColumn.Width = 75;
            //
            // drawingsColumn
            //
            drawingsColumn.DefaultCellStyle = rightAligned;
            drawingsColumn.HeaderText = "Drawings";
            drawingsColumn.Name = "drawingsColumn";
            drawingsColumn.ReadOnly = true;
            drawingsColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            drawingsColumn.Width = 70;
            //
            // partsColumn
            //
            partsColumn.DefaultCellStyle = rightAligned;
            partsColumn.HeaderText = "Parts";
            partsColumn.Name = "partsColumn";
            partsColumn.ReadOnly = true;
            partsColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            partsColumn.Width = 65;
            //
            // statusColumn
            //
            statusColumn.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            statusColumn.HeaderText = "Status";
            statusColumn.Name = "statusColumn";
            statusColumn.ReadOnly = true;
            statusColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            //
            // skippedLabel
            //
            skippedLabel.AutoSize = true;
            skippedLabel.ForeColor = System.Drawing.SystemColors.GrayText;
            skippedLabel.Margin = new System.Windows.Forms.Padding(3, 6, 3, 0);
            skippedLabel.MaximumSize = new System.Drawing.Size(920, 0);
            skippedLabel.Name = "skippedLabel";
            skippedLabel.TabIndex = 3;
            skippedLabel.Visible = false;
            //
            // bottomTable
            //
            bottomTable.AutoSize = true;
            bottomTable.ColumnCount = 3;
            bottomTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            bottomTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            bottomTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            bottomTable.Controls.Add(summaryLabel, 0, 0);
            bottomTable.Controls.Add(releaseButton, 1, 0);
            bottomTable.Controls.Add(closeButton, 2, 0);
            bottomTable.Dock = System.Windows.Forms.DockStyle.Fill;
            bottomTable.Margin = new System.Windows.Forms.Padding(0, 6, 0, 0);
            bottomTable.Name = "bottomTable";
            bottomTable.RowCount = 1;
            bottomTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            bottomTable.TabIndex = 4;
            //
            // summaryLabel
            //
            summaryLabel.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            summaryLabel.AutoEllipsis = true;
            summaryLabel.Name = "summaryLabel";
            summaryLabel.TabIndex = 0;
            //
            // releaseButton
            //
            releaseButton.AutoSize = true;
            releaseButton.Enabled = false;
            releaseButton.Name = "releaseButton";
            releaseButton.TabIndex = 1;
            releaseButton.Text = "Release";
            releaseButton.UseVisualStyleBackColor = true;
            releaseButton.Click += ReleaseButton_Click;
            //
            // closeButton
            //
            closeButton.AutoSize = true;
            closeButton.Name = "closeButton";
            closeButton.TabIndex = 2;
            closeButton.Text = "Close";
            closeButton.UseVisualStyleBackColor = true;
            closeButton.Click += CloseButton_Click;
            //
            // statusLabel
            //
            statusLabel.AutoEllipsis = true;
            statusLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            statusLabel.Margin = new System.Windows.Forms.Padding(3, 6, 3, 0);
            statusLabel.Name = "statusLabel";
            statusLabel.Size = new System.Drawing.Size(100, 20);
            statusLabel.TabIndex = 5;
            //
            // ReleaseSetForm
            //
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            ClientSize = new System.Drawing.Size(944, 521);
            Controls.Add(layoutTable);
            Font = new System.Drawing.Font("Segoe UI", 9.75F);
            MinimizeBox = false;
            MinimumSize = new System.Drawing.Size(760, 400);
            Name = "ReleaseSetForm";
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "Release Drawing Set";
            FormClosing += ReleaseSetForm_FormClosing;
            layoutTable.ResumeLayout(false);
            layoutTable.PerformLayout();
            optionsTable.ResumeLayout(false);
            optionsTable.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)multiplierBox).EndInit();
            ((System.ComponentModel.ISupportInitialize)groupGrid).EndInit();
            bottomTable.ResumeLayout(false);
            bottomTable.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel layoutTable;
        private System.Windows.Forms.Label setLabel;
        private System.Windows.Forms.TableLayoutPanel optionsTable;
        private System.Windows.Forms.Label multiplierLabel;
        private System.Windows.Forms.NumericUpDown multiplierBox;
        private System.Windows.Forms.CheckBox includeArchivedCheckBox;
        private System.Windows.Forms.Label notesLabel;
        private System.Windows.Forms.TextBox notesBox;
        private System.Windows.Forms.DataGridView groupGrid;
        private System.Windows.Forms.DataGridViewCheckBoxColumn includeColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn materialColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn gradeColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn thicknessColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn unitsColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn cutTypeColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn drawingsColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn partsColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn statusColumn;
        private System.Windows.Forms.Label skippedLabel;
        private System.Windows.Forms.TableLayoutPanel bottomTable;
        private System.Windows.Forms.Label summaryLabel;
        private System.Windows.Forms.Button releaseButton;
        private System.Windows.Forms.Button closeButton;
        private System.Windows.Forms.Label statusLabel;
    }
}
