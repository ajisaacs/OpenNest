namespace OpenNest.Controls
{
    partial class ReleasePanel
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

        #region Component Designer generated code

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            System.Windows.Forms.DataGridViewCellStyle rightAligned = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle wrapped = new System.Windows.Forms.DataGridViewCellStyle();
            layoutTable = new System.Windows.Forms.TableLayoutPanel();
            headingLabel = new System.Windows.Forms.Label();
            hintLabel = new System.Windows.Forms.Label();
            lineGrid = new System.Windows.Forms.DataGridView();
            lineNameColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            lineQuantityColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            lineUnitColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            lineRemoveColumn = new System.Windows.Forms.DataGridViewButtonColumn();
            detailsTable = new System.Windows.Forms.TableLayoutPanel();
            customerLabel = new System.Windows.Forms.Label();
            customerValue = new System.Windows.Forms.Label();
            notesLabel = new System.Windows.Forms.Label();
            notesBox = new System.Windows.Forms.TextBox();
            nestsTable = new System.Windows.Forms.TableLayoutPanel();
            nestsLabel = new System.Windows.Forms.Label();
            includeArchivedCheckBox = new System.Windows.Forms.CheckBox();
            groupGrid = new System.Windows.Forms.DataGridView();
            includeColumn = new System.Windows.Forms.DataGridViewCheckBoxColumn();
            groupColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            partsColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            targetColumn = new System.Windows.Forms.DataGridViewComboBoxColumn();
            statusColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            previewLabel = new System.Windows.Forms.Label();
            bottomTable = new System.Windows.Forms.TableLayoutPanel();
            fixDrawingsButton = new System.Windows.Forms.Button();
            nestAfterReleaseCheckBox = new System.Windows.Forms.CheckBox();
            clearButton = new System.Windows.Forms.Button();
            releaseButton = new System.Windows.Forms.Button();
            statusLabel = new System.Windows.Forms.Label();
            groupMenu = new System.Windows.Forms.ContextMenuStrip(components);
            layoutTable.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)lineGrid).BeginInit();
            detailsTable.SuspendLayout();
            nestsTable.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)groupGrid).BeginInit();
            bottomTable.SuspendLayout();
            SuspendLayout();
            //
            // layoutTable
            //
            layoutTable.ColumnCount = 1;
            layoutTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            layoutTable.Controls.Add(headingLabel, 0, 0);
            layoutTable.Controls.Add(hintLabel, 0, 1);
            layoutTable.Controls.Add(lineGrid, 0, 2);
            layoutTable.Controls.Add(detailsTable, 0, 3);
            layoutTable.Controls.Add(nestsTable, 0, 4);
            layoutTable.Controls.Add(groupGrid, 0, 5);
            layoutTable.Controls.Add(previewLabel, 0, 6);
            layoutTable.Controls.Add(bottomTable, 0, 7);
            layoutTable.Controls.Add(statusLabel, 0, 8);
            layoutTable.Dock = System.Windows.Forms.DockStyle.Fill;
            layoutTable.Location = new System.Drawing.Point(0, 0);
            layoutTable.Name = "layoutTable";
            layoutTable.RowCount = 9;
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 40F));
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 60F));
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.Size = new System.Drawing.Size(560, 600);
            layoutTable.TabIndex = 0;
            //
            // headingLabel
            //
            headingLabel.AutoEllipsis = true;
            headingLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            headingLabel.Font = new System.Drawing.Font("Segoe UI", 11.25F, System.Drawing.FontStyle.Bold);
            headingLabel.Margin = new System.Windows.Forms.Padding(3, 0, 3, 3);
            headingLabel.Name = "headingLabel";
            headingLabel.Size = new System.Drawing.Size(100, 24);
            headingLabel.TabIndex = 0;
            headingLabel.Text = "Release";
            //
            // hintLabel
            //
            hintLabel.AutoSize = true;
            hintLabel.ForeColor = System.Drawing.SystemColors.GrayText;
            hintLabel.Margin = new System.Windows.Forms.Padding(3, 0, 3, 6);
            hintLabel.Name = "hintLabel";
            hintLabel.TabIndex = 1;
            hintLabel.Text = "Double-click a set or a drawing to add it.";
            //
            // lineGrid
            //
            lineGrid.AllowUserToAddRows = false;
            lineGrid.AllowUserToDeleteRows = false;
            lineGrid.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            lineGrid.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] { lineNameColumn, lineQuantityColumn, lineUnitColumn, lineRemoveColumn });
            lineGrid.Dock = System.Windows.Forms.DockStyle.Fill;
            lineGrid.EditMode = System.Windows.Forms.DataGridViewEditMode.EditOnEnter;
            lineGrid.Margin = new System.Windows.Forms.Padding(3, 0, 3, 6);
            lineGrid.MultiSelect = false;
            lineGrid.Name = "lineGrid";
            lineGrid.RowHeadersVisible = false;
            lineGrid.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            lineGrid.StandardTab = true;
            lineGrid.TabIndex = 2;
            lineGrid.CellContentClick += LineGrid_CellContentClick;
            lineGrid.CellValidating += LineGrid_CellValidating;
            lineGrid.CellValueChanged += LineGrid_CellValueChanged;
            lineGrid.KeyDown += LineGrid_KeyDown;
            //
            // lineNameColumn
            //
            lineNameColumn.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            lineNameColumn.HeaderText = "Set or drawing";
            lineNameColumn.Name = "lineNameColumn";
            lineNameColumn.ReadOnly = true;
            lineNameColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            //
            // lineQuantityColumn
            //
            rightAligned.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleRight;
            lineQuantityColumn.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            lineQuantityColumn.DefaultCellStyle = rightAligned;
            lineQuantityColumn.HeaderText = "Qty";
            lineQuantityColumn.Name = "lineQuantityColumn";
            lineQuantityColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            lineQuantityColumn.ToolTipText = "How many of a set (every quantity in it is multiplied) or pieces of a drawing.";
            lineQuantityColumn.Width = 55;
            //
            // lineUnitColumn
            //
            lineUnitColumn.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            lineUnitColumn.HeaderText = "";
            lineUnitColumn.Name = "lineUnitColumn";
            lineUnitColumn.ReadOnly = true;
            lineUnitColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            lineUnitColumn.Width = 42;
            //
            // lineRemoveColumn
            //
            lineRemoveColumn.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            lineRemoveColumn.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            lineRemoveColumn.HeaderText = "";
            lineRemoveColumn.Name = "lineRemoveColumn";
            lineRemoveColumn.Text = "✕";
            lineRemoveColumn.ToolTipText = "Remove from the release";
            lineRemoveColumn.UseColumnTextForButtonValue = true;
            lineRemoveColumn.Width = 30;
            //
            // detailsTable
            //
            detailsTable.AutoSize = true;
            detailsTable.ColumnCount = 2;
            detailsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            detailsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            detailsTable.Controls.Add(customerLabel, 0, 0);
            detailsTable.Controls.Add(customerValue, 1, 0);
            detailsTable.Controls.Add(notesLabel, 0, 1);
            detailsTable.Controls.Add(notesBox, 1, 1);
            detailsTable.Dock = System.Windows.Forms.DockStyle.Fill;
            detailsTable.Margin = new System.Windows.Forms.Padding(0, 0, 0, 6);
            detailsTable.Name = "detailsTable";
            detailsTable.RowCount = 2;
            detailsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            detailsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            detailsTable.TabIndex = 3;
            //
            // customerLabel
            //
            customerLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            customerLabel.AutoSize = true;
            customerLabel.Name = "customerLabel";
            customerLabel.TabIndex = 0;
            customerLabel.Text = "Customer:";
            //
            // customerValue
            //
            customerValue.AutoEllipsis = true;
            customerValue.Dock = System.Windows.Forms.DockStyle.Fill;
            customerValue.Name = "customerValue";
            customerValue.Size = new System.Drawing.Size(100, 22);
            customerValue.TabIndex = 1;
            customerValue.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // notesLabel
            //
            notesLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            notesLabel.AutoSize = true;
            notesLabel.Name = "notesLabel";
            notesLabel.TabIndex = 2;
            notesLabel.Text = "Notes:";
            //
            // notesBox
            //
            notesBox.Dock = System.Windows.Forms.DockStyle.Fill;
            notesBox.MaxLength = 4000;
            notesBox.Name = "notesBox";
            notesBox.TabIndex = 3;
            //
            // nestsTable
            //
            nestsTable.AutoSize = true;
            nestsTable.ColumnCount = 2;
            nestsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            nestsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            nestsTable.Controls.Add(nestsLabel, 0, 0);
            nestsTable.Controls.Add(includeArchivedCheckBox, 1, 0);
            nestsTable.Dock = System.Windows.Forms.DockStyle.Fill;
            nestsTable.Margin = new System.Windows.Forms.Padding(0);
            nestsTable.Name = "nestsTable";
            nestsTable.RowCount = 1;
            nestsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            nestsTable.TabIndex = 4;
            //
            // nestsLabel
            //
            nestsLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            nestsLabel.AutoSize = true;
            nestsLabel.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Bold);
            nestsLabel.Name = "nestsLabel";
            nestsLabel.TabIndex = 0;
            nestsLabel.Text = "Nests it will make";
            //
            // includeArchivedCheckBox
            //
            includeArchivedCheckBox.Anchor = System.Windows.Forms.AnchorStyles.Right;
            includeArchivedCheckBox.AutoSize = true;
            includeArchivedCheckBox.Name = "includeArchivedCheckBox";
            includeArchivedCheckBox.TabIndex = 1;
            includeArchivedCheckBox.Text = "Include archived drawings";
            includeArchivedCheckBox.UseVisualStyleBackColor = true;
            includeArchivedCheckBox.Visible = false;
            includeArchivedCheckBox.CheckedChanged += IncludeArchivedCheckBox_CheckedChanged;
            //
            // groupGrid
            //
            groupGrid.AllowUserToAddRows = false;
            groupGrid.AllowUserToDeleteRows = false;
            groupGrid.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            groupGrid.AutoSizeRowsMode = System.Windows.Forms.DataGridViewAutoSizeRowsMode.AllCells;
            groupGrid.ContextMenuStrip = groupMenu;
            groupGrid.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] { includeColumn, groupColumn, partsColumn, targetColumn, statusColumn });
            groupGrid.Dock = System.Windows.Forms.DockStyle.Fill;
            groupGrid.EditMode = System.Windows.Forms.DataGridViewEditMode.EditOnEnter;
            groupGrid.Margin = new System.Windows.Forms.Padding(3, 3, 3, 3);
            groupGrid.MultiSelect = false;
            groupGrid.Name = "groupGrid";
            groupGrid.RowHeadersVisible = false;
            groupGrid.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            groupGrid.StandardTab = true;
            groupGrid.TabIndex = 5;
            groupGrid.CellValueChanged += GroupGrid_CellValueChanged;
            groupGrid.CurrentCellDirtyStateChanged += GroupGrid_CurrentCellDirtyStateChanged;
            groupGrid.DataError += GroupGrid_DataError;
            groupGrid.CellDoubleClick += GroupGrid_CellDoubleClick;
            groupGrid.CellMouseDown += GroupGrid_CellMouseDown;
            groupGrid.CurrentCellChanged += GroupGrid_CurrentCellChanged;
            //
            // includeColumn
            //
            includeColumn.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            includeColumn.HeaderText = "";
            includeColumn.Name = "includeColumn";
            includeColumn.ToolTipText = "Untick to leave this material out of the release.";
            includeColumn.Width = 28;
            //
            // groupColumn
            //
            wrapped.WrapMode = System.Windows.Forms.DataGridViewTriState.True;
            groupColumn.DefaultCellStyle = wrapped;
            groupColumn.FillWeight = 40F;
            groupColumn.HeaderText = "Material";
            groupColumn.Name = "groupColumn";
            groupColumn.ReadOnly = true;
            groupColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            //
            // partsColumn
            //
            partsColumn.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            partsColumn.DefaultCellStyle = rightAligned;
            partsColumn.HeaderText = "Parts";
            partsColumn.Name = "partsColumn";
            partsColumn.ReadOnly = true;
            partsColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            partsColumn.Width = 50;
            //
            // targetColumn
            //
            targetColumn.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            targetColumn.DisplayStyle = System.Windows.Forms.DataGridViewComboBoxDisplayStyle.DropDownButton;
            targetColumn.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            targetColumn.HeaderText = "Nest";
            targetColumn.Name = "targetColumn";
            targetColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            targetColumn.ToolTipText = "Merge into a Draft nest of the same material (it is renested), or make a new one.";
            targetColumn.Width = 150;
            //
            // statusColumn
            //
            statusColumn.DefaultCellStyle = wrapped;
            statusColumn.FillWeight = 60F;
            statusColumn.HeaderText = "What happens";
            statusColumn.Name = "statusColumn";
            statusColumn.ReadOnly = true;
            statusColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            //
            // previewLabel
            //
            previewLabel.AutoSize = true;
            previewLabel.Margin = new System.Windows.Forms.Padding(3, 3, 3, 3);
            previewLabel.Name = "previewLabel";
            previewLabel.TabIndex = 6;
            //
            // bottomTable
            //
            bottomTable.AutoSize = true;
            bottomTable.ColumnCount = 5;
            bottomTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            bottomTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            bottomTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            bottomTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            bottomTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            bottomTable.Controls.Add(fixDrawingsButton, 0, 0);
            bottomTable.Controls.Add(nestAfterReleaseCheckBox, 2, 0);
            bottomTable.Controls.Add(clearButton, 3, 0);
            bottomTable.Controls.Add(releaseButton, 4, 0);
            bottomTable.Dock = System.Windows.Forms.DockStyle.Fill;
            bottomTable.Margin = new System.Windows.Forms.Padding(0, 3, 0, 0);
            bottomTable.Name = "bottomTable";
            bottomTable.RowCount = 1;
            bottomTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            bottomTable.TabIndex = 7;
            //
            // fixDrawingsButton
            //
            fixDrawingsButton.AutoSize = true;
            fixDrawingsButton.Enabled = false;
            fixDrawingsButton.Name = "fixDrawingsButton";
            fixDrawingsButton.TabIndex = 0;
            fixDrawingsButton.Text = "Fix Drawings...";
            fixDrawingsButton.UseVisualStyleBackColor = true;
            fixDrawingsButton.Visible = false;
            fixDrawingsButton.Click += FixDrawingsButton_Click;
            //
            // nestAfterReleaseCheckBox
            //
            nestAfterReleaseCheckBox.Anchor = System.Windows.Forms.AnchorStyles.Right;
            nestAfterReleaseCheckBox.AutoSize = true;
            nestAfterReleaseCheckBox.Checked = true;
            nestAfterReleaseCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            nestAfterReleaseCheckBox.Margin = new System.Windows.Forms.Padding(3, 3, 9, 3);
            nestAfterReleaseCheckBox.Name = "nestAfterReleaseCheckBox";
            nestAfterReleaseCheckBox.TabIndex = 1;
            nestAfterReleaseCheckBox.Text = "Nest after release";
            nestAfterReleaseCheckBox.UseVisualStyleBackColor = true;
            nestAfterReleaseCheckBox.CheckedChanged += NestAfterReleaseCheckBox_CheckedChanged;
            //
            // clearButton
            //
            clearButton.AutoSize = true;
            clearButton.Enabled = false;
            clearButton.Name = "clearButton";
            clearButton.TabIndex = 2;
            clearButton.Text = "Clear";
            clearButton.UseVisualStyleBackColor = true;
            clearButton.Click += ClearButton_Click;
            //
            // releaseButton
            //
            releaseButton.AutoSize = true;
            releaseButton.Enabled = false;
            releaseButton.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Bold);
            releaseButton.MinimumSize = new System.Drawing.Size(0, 32);
            releaseButton.Name = "releaseButton";
            releaseButton.TabIndex = 3;
            releaseButton.Text = "Make Nests";
            releaseButton.UseVisualStyleBackColor = true;
            releaseButton.Click += ReleaseButton_Click;
            //
            // statusLabel
            //
            statusLabel.AutoSize = true;
            statusLabel.Margin = new System.Windows.Forms.Padding(3, 6, 3, 0);
            statusLabel.Name = "statusLabel";
            statusLabel.TabIndex = 8;
            //
            // groupMenu
            //
            groupMenu.Name = "groupMenu";
            groupMenu.Opening += GroupMenu_Opening;
            //
            // ReleasePanel
            //
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            Controls.Add(layoutTable);
            Font = new System.Drawing.Font("Segoe UI", 9.75F);
            MinimumSize = new System.Drawing.Size(420, 360);
            Name = "ReleasePanel";
            Size = new System.Drawing.Size(560, 600);
            Resize += ReleasePanel_Resize;
            layoutTable.ResumeLayout(false);
            layoutTable.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)lineGrid).EndInit();
            detailsTable.ResumeLayout(false);
            detailsTable.PerformLayout();
            nestsTable.ResumeLayout(false);
            nestsTable.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)groupGrid).EndInit();
            bottomTable.ResumeLayout(false);
            bottomTable.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel layoutTable;
        private System.Windows.Forms.Label headingLabel;
        private System.Windows.Forms.Label hintLabel;
        private System.Windows.Forms.DataGridView lineGrid;
        private System.Windows.Forms.DataGridViewTextBoxColumn lineNameColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn lineQuantityColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn lineUnitColumn;
        private System.Windows.Forms.DataGridViewButtonColumn lineRemoveColumn;
        private System.Windows.Forms.TableLayoutPanel detailsTable;
        private System.Windows.Forms.Label customerLabel;
        private System.Windows.Forms.Label customerValue;
        private System.Windows.Forms.Label notesLabel;
        private System.Windows.Forms.TextBox notesBox;
        private System.Windows.Forms.TableLayoutPanel nestsTable;
        private System.Windows.Forms.Label nestsLabel;
        private System.Windows.Forms.CheckBox includeArchivedCheckBox;
        private System.Windows.Forms.DataGridView groupGrid;
        private System.Windows.Forms.DataGridViewCheckBoxColumn includeColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn groupColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn partsColumn;
        private System.Windows.Forms.DataGridViewComboBoxColumn targetColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn statusColumn;
        private System.Windows.Forms.Label previewLabel;
        private System.Windows.Forms.TableLayoutPanel bottomTable;
        private System.Windows.Forms.Button fixDrawingsButton;
        private System.Windows.Forms.CheckBox nestAfterReleaseCheckBox;
        private System.Windows.Forms.Button clearButton;
        private System.Windows.Forms.Button releaseButton;
        private System.Windows.Forms.Label statusLabel;
        private System.Windows.Forms.ContextMenuStrip groupMenu;
    }
}
