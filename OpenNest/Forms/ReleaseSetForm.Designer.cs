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
                searchCancellation?.Dispose();
                if (components != null)
                    components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            System.Windows.Forms.DataGridViewCellStyle rightAligned = new System.Windows.Forms.DataGridViewCellStyle();
            layoutTable = new System.Windows.Forms.TableLayoutPanel();
            setLabel = new System.Windows.Forms.Label();
            addSetTable = new System.Windows.Forms.TableLayoutPanel();
            addSetLabel = new System.Windows.Forms.Label();
            addSetCombo = new System.Windows.Forms.ComboBox();
            addSetButton = new System.Windows.Forms.Button();
            addDrawingsButton = new System.Windows.Forms.Button();
            removeSetButton = new System.Windows.Forms.Button();
            setGrid = new System.Windows.Forms.DataGridView();
            lineTypeColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            setNameColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            setCustomerColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            setRevisionColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            kitsColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            optionsTable = new System.Windows.Forms.TableLayoutPanel();
            includeArchivedCheckBox = new System.Windows.Forms.CheckBox();
            nestAfterReleaseCheckBox = new System.Windows.Forms.CheckBox();
            notesLabel = new System.Windows.Forms.Label();
            notesBox = new System.Windows.Forms.TextBox();
            previewTable = new System.Windows.Forms.TableLayoutPanel();
            previewLabel = new System.Windows.Forms.Label();
            optionsButton = new System.Windows.Forms.Button();
            groupGrid = new System.Windows.Forms.DataGridView();
            includeColumn = new System.Windows.Forms.DataGridViewCheckBoxColumn();
            materialColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            gradeColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            thicknessColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            unitsColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            cutTypeColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            drawingsColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            partsColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            targetColumn = new System.Windows.Forms.DataGridViewComboBoxColumn();
            statusColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            skippedLabel = new System.Windows.Forms.Label();
            bottomTable = new System.Windows.Forms.TableLayoutPanel();
            summaryLabel = new System.Windows.Forms.Label();
            releaseButton = new System.Windows.Forms.Button();
            closeButton = new System.Windows.Forms.Button();
            statusLabel = new System.Windows.Forms.Label();
            searchTimer = new System.Windows.Forms.Timer(components);
            fixDrawingsButton = new System.Windows.Forms.Button();
            groupMenu = new System.Windows.Forms.ContextMenuStrip(components);
            layoutTable.SuspendLayout();
            addSetTable.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)setGrid).BeginInit();
            optionsTable.SuspendLayout();
            previewTable.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)groupGrid).BeginInit();
            bottomTable.SuspendLayout();
            SuspendLayout();
            //
            // layoutTable
            //
            layoutTable.ColumnCount = 1;
            layoutTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            layoutTable.Controls.Add(setLabel, 0, 0);
            layoutTable.Controls.Add(addSetTable, 0, 1);
            layoutTable.Controls.Add(setGrid, 0, 2);
            layoutTable.Controls.Add(optionsTable, 0, 3);
            layoutTable.Controls.Add(previewTable, 0, 4);
            layoutTable.Controls.Add(groupGrid, 0, 5);
            layoutTable.Controls.Add(skippedLabel, 0, 6);
            layoutTable.Controls.Add(bottomTable, 0, 7);
            layoutTable.Controls.Add(statusLabel, 0, 8);
            layoutTable.Dock = System.Windows.Forms.DockStyle.Fill;
            layoutTable.Location = new System.Drawing.Point(0, 0);
            layoutTable.Name = "layoutTable";
            layoutTable.Padding = new System.Windows.Forms.Padding(8);
            layoutTable.RowCount = 9;
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 130F));
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.Size = new System.Drawing.Size(1104, 661);
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
            setLabel.Text = "Sets and drawings to release";
            //
            // addSetTable
            //
            addSetTable.AutoSize = true;
            addSetTable.ColumnCount = 5;
            addSetTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            addSetTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 360F));
            addSetTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            addSetTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            addSetTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            addSetTable.Controls.Add(addSetLabel, 0, 0);
            addSetTable.Controls.Add(addSetCombo, 1, 0);
            addSetTable.Controls.Add(addSetButton, 2, 0);
            addSetTable.Controls.Add(addDrawingsButton, 3, 0);
            addSetTable.Controls.Add(removeSetButton, 4, 0);
            addSetTable.Dock = System.Windows.Forms.DockStyle.Fill;
            addSetTable.Margin = new System.Windows.Forms.Padding(0, 0, 0, 3);
            addSetTable.Name = "addSetTable";
            addSetTable.RowCount = 1;
            addSetTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            addSetTable.TabIndex = 1;
            //
            // addSetLabel
            //
            addSetLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            addSetLabel.AutoSize = true;
            addSetLabel.Name = "addSetLabel";
            addSetLabel.TabIndex = 0;
            addSetLabel.Text = "Add a set:";
            //
            // addSetCombo
            //
            addSetCombo.Dock = System.Windows.Forms.DockStyle.Fill;
            addSetCombo.Name = "addSetCombo";
            addSetCombo.TabIndex = 1;
            addSetCombo.TextUpdate += AddSetCombo_TextUpdate;
            addSetCombo.DropDown += AddSetCombo_DropDown;
            addSetCombo.KeyDown += AddSetCombo_KeyDown;
            //
            // addSetButton
            //
            addSetButton.AutoSize = true;
            addSetButton.Name = "addSetButton";
            addSetButton.TabIndex = 2;
            addSetButton.Text = "Add";
            addSetButton.UseVisualStyleBackColor = true;
            addSetButton.Click += AddSetButton_Click;
            //
            // addDrawingsButton
            //
            addDrawingsButton.Anchor = System.Windows.Forms.AnchorStyles.Left;
            addDrawingsButton.AutoSize = true;
            addDrawingsButton.Margin = new System.Windows.Forms.Padding(12, 3, 3, 3);
            addDrawingsButton.Name = "addDrawingsButton";
            addDrawingsButton.TabIndex = 3;
            addDrawingsButton.Text = "Add Drawings...";
            addDrawingsButton.UseVisualStyleBackColor = true;
            addDrawingsButton.Click += AddDrawingsButton_Click;
            //
            // removeSetButton
            //
            removeSetButton.Anchor = System.Windows.Forms.AnchorStyles.Left;
            removeSetButton.AutoSize = true;
            removeSetButton.Name = "removeSetButton";
            removeSetButton.TabIndex = 4;
            removeSetButton.Text = "Remove";
            removeSetButton.UseVisualStyleBackColor = true;
            removeSetButton.Click += RemoveSetButton_Click;
            //
            // setGrid
            //
            setGrid.AllowUserToAddRows = false;
            setGrid.AllowUserToDeleteRows = false;
            setGrid.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] { lineTypeColumn, setNameColumn, setCustomerColumn, setRevisionColumn, kitsColumn });
            setGrid.Dock = System.Windows.Forms.DockStyle.Fill;
            setGrid.Margin = new System.Windows.Forms.Padding(3, 3, 3, 6);
            setGrid.MultiSelect = false;
            setGrid.Name = "setGrid";
            setGrid.RowHeadersVisible = false;
            setGrid.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            setGrid.StandardTab = true;
            setGrid.TabIndex = 2;
            setGrid.CellValidating += SetGrid_CellValidating;
            setGrid.CellValueChanged += SetGrid_CellValueChanged;
            setGrid.SelectionChanged += SetGrid_SelectionChanged;
            //
            // lineTypeColumn
            //
            lineTypeColumn.HeaderText = "Type";
            lineTypeColumn.Name = "lineTypeColumn";
            lineTypeColumn.ReadOnly = true;
            lineTypeColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            lineTypeColumn.Width = 70;
            //
            // setNameColumn
            //
            setNameColumn.Width = 260;
            setNameColumn.HeaderText = "Set or drawing";
            setNameColumn.Name = "setNameColumn";
            setNameColumn.ReadOnly = true;
            setNameColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            //
            // setCustomerColumn
            //
            setCustomerColumn.HeaderText = "Customer";
            setCustomerColumn.Name = "setCustomerColumn";
            setCustomerColumn.ReadOnly = true;
            setCustomerColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            setCustomerColumn.Width = 200;
            //
            // setRevisionColumn
            //
            setRevisionColumn.DefaultCellStyle = rightAligned;
            setRevisionColumn.HeaderText = "Revision";
            setRevisionColumn.Name = "setRevisionColumn";
            setRevisionColumn.ReadOnly = true;
            setRevisionColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            setRevisionColumn.Width = 75;
            //
            // kitsColumn
            //
            kitsColumn.DefaultCellStyle = rightAligned;
            kitsColumn.HeaderText = "Quantity";
            kitsColumn.Name = "kitsColumn";
            kitsColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            kitsColumn.ToolTipText = "How many to make. For a set, every quantity in the set is multiplied by it.";
            kitsColumn.Width = 80;
            //
            // optionsTable
            //
            optionsTable.AutoSize = true;
            optionsTable.ColumnCount = 3;
            optionsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            optionsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            optionsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            optionsTable.Controls.Add(nestAfterReleaseCheckBox, 0, 0);
            optionsTable.Controls.Add(notesLabel, 1, 0);
            optionsTable.Controls.Add(notesBox, 2, 0);
            optionsTable.Dock = System.Windows.Forms.DockStyle.Fill;
            optionsTable.Margin = new System.Windows.Forms.Padding(0, 0, 0, 6);
            optionsTable.Name = "optionsTable";
            optionsTable.RowCount = 1;
            optionsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            optionsTable.TabIndex = 3;
            //
            // includeArchivedCheckBox
            //
            includeArchivedCheckBox.Anchor = System.Windows.Forms.AnchorStyles.Left;
            includeArchivedCheckBox.AutoSize = true;
            includeArchivedCheckBox.Margin = new System.Windows.Forms.Padding(12, 3, 3, 3);
            includeArchivedCheckBox.Name = "includeArchivedCheckBox";
            includeArchivedCheckBox.TabIndex = 1;
            includeArchivedCheckBox.Visible = false;
            includeArchivedCheckBox.Text = "Include archived drawings";
            includeArchivedCheckBox.UseVisualStyleBackColor = true;
            includeArchivedCheckBox.CheckedChanged += Options_Changed;
            //
            // nestAfterReleaseCheckBox
            //
            nestAfterReleaseCheckBox.Anchor = System.Windows.Forms.AnchorStyles.Left;
            nestAfterReleaseCheckBox.AutoSize = true;
            nestAfterReleaseCheckBox.Checked = true;
            nestAfterReleaseCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            nestAfterReleaseCheckBox.Name = "nestAfterReleaseCheckBox";
            nestAfterReleaseCheckBox.TabIndex = 0;
            nestAfterReleaseCheckBox.Text = "Nest after release";
            nestAfterReleaseCheckBox.UseVisualStyleBackColor = true;
            nestAfterReleaseCheckBox.CheckedChanged += NestAfterReleaseCheckBox_CheckedChanged;
            //
            // notesLabel
            //
            notesLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            notesLabel.AutoSize = true;
            notesLabel.Margin = new System.Windows.Forms.Padding(12, 0, 3, 0);
            notesLabel.Name = "notesLabel";
            notesLabel.TabIndex = 1;
            notesLabel.Text = "Release notes:";
            //
            // notesBox
            //
            notesBox.Dock = System.Windows.Forms.DockStyle.Fill;
            notesBox.MaxLength = 4000;
            notesBox.Name = "notesBox";
            notesBox.TabIndex = 2;
            //
            // previewTable
            //
            previewTable.AutoSize = true;
            previewTable.ColumnCount = 3;
            previewTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            previewTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            previewTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            previewTable.Controls.Add(previewLabel, 0, 0);
            previewTable.Controls.Add(includeArchivedCheckBox, 1, 0);
            previewTable.Controls.Add(optionsButton, 2, 0);
            previewTable.Dock = System.Windows.Forms.DockStyle.Fill;
            previewTable.Margin = new System.Windows.Forms.Padding(0, 0, 0, 3);
            previewTable.Name = "previewTable";
            previewTable.RowCount = 1;
            previewTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            previewTable.TabIndex = 4;
            //
            // previewLabel
            //
            previewLabel.AutoSize = true;
            previewLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            previewLabel.Margin = new System.Windows.Forms.Padding(3, 6, 3, 6);
            previewLabel.Name = "previewLabel";
            previewLabel.TabIndex = 0;
            //
            // optionsButton
            //
            optionsButton.Anchor = System.Windows.Forms.AnchorStyles.Right;
            optionsButton.AutoSize = true;
            optionsButton.Name = "optionsButton";
            optionsButton.TabIndex = 2;
            optionsButton.Text = "Options ▸";
            optionsButton.UseVisualStyleBackColor = true;
            optionsButton.Click += OptionsButton_Click;
            //
            // groupGrid
            //
            groupGrid.AllowUserToAddRows = false;
            groupGrid.AllowUserToDeleteRows = false;
            groupGrid.ContextMenuStrip = groupMenu;
            groupGrid.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] { includeColumn, materialColumn, gradeColumn, thicknessColumn, unitsColumn, cutTypeColumn, drawingsColumn, partsColumn, targetColumn, statusColumn });
            groupGrid.Dock = System.Windows.Forms.DockStyle.Fill;
            groupGrid.EditMode = System.Windows.Forms.DataGridViewEditMode.EditOnEnter;
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
            includeColumn.HeaderText = "Release";
            includeColumn.Name = "includeColumn";
            includeColumn.Visible = false;
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
            // targetColumn
            //
            targetColumn.DisplayStyle = System.Windows.Forms.DataGridViewComboBoxDisplayStyle.DropDownButton;
            targetColumn.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            targetColumn.HeaderText = "Nest";
            targetColumn.Name = "targetColumn";
            targetColumn.Visible = false;
            targetColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            targetColumn.ToolTipText = "Make a new Draft nest, or merge into a Draft nest of the same material and renest it.";
            targetColumn.Width = 260;
            //
            // statusColumn
            //
            statusColumn.Width = 200;
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
            skippedLabel.MaximumSize = new System.Drawing.Size(1080, 0);
            skippedLabel.Name = "skippedLabel";
            skippedLabel.TabIndex = 6;
            skippedLabel.Visible = false;
            //
            // bottomTable
            //
            bottomTable.AutoSize = true;
            bottomTable.ColumnCount = 4;
            bottomTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            bottomTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            bottomTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            bottomTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            bottomTable.Controls.Add(summaryLabel, 0, 0);
            bottomTable.Controls.Add(fixDrawingsButton, 1, 0);
            bottomTable.Controls.Add(releaseButton, 2, 0);
            bottomTable.Controls.Add(closeButton, 3, 0);
            bottomTable.Dock = System.Windows.Forms.DockStyle.Fill;
            bottomTable.Margin = new System.Windows.Forms.Padding(0, 6, 0, 0);
            bottomTable.Name = "bottomTable";
            bottomTable.RowCount = 1;
            bottomTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            bottomTable.TabIndex = 7;
            //
            // summaryLabel
            //
            summaryLabel.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            summaryLabel.AutoEllipsis = true;
            summaryLabel.Name = "summaryLabel";
            summaryLabel.TabIndex = 0;
            //
            // fixDrawingsButton
            //
            fixDrawingsButton.AutoSize = true;
            fixDrawingsButton.Enabled = false;
            fixDrawingsButton.Margin = new System.Windows.Forms.Padding(3, 3, 12, 3);
            fixDrawingsButton.Name = "fixDrawingsButton";
            fixDrawingsButton.TabIndex = 1;
            fixDrawingsButton.Text = "Fix Drawings...";
            fixDrawingsButton.UseVisualStyleBackColor = true;
            fixDrawingsButton.Click += FixDrawingsButton_Click;
            //
            // releaseButton
            //
            releaseButton.AutoSize = true;
            releaseButton.Enabled = false;
            releaseButton.Name = "releaseButton";
            releaseButton.TabIndex = 2;
            releaseButton.Text = "Make Nests...";
            releaseButton.UseVisualStyleBackColor = true;
            releaseButton.Click += ReleaseButton_Click;
            //
            // closeButton
            //
            closeButton.AutoSize = true;
            closeButton.Name = "closeButton";
            closeButton.TabIndex = 3;
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
            statusLabel.TabIndex = 8;
            //
            // searchTimer
            //
            searchTimer.Interval = 250;
            searchTimer.Tick += SearchTimer_Tick;
            //
            // groupMenu
            //
            groupMenu.Name = "groupMenu";
            groupMenu.Opening += GroupMenu_Opening;
            //
            // ReleaseSetForm
            //
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            ClientSize = new System.Drawing.Size(1104, 661);
            Controls.Add(layoutTable);
            Font = new System.Drawing.Font("Segoe UI", 9.75F);
            MinimizeBox = false;
            MinimumSize = new System.Drawing.Size(860, 520);
            Name = "ReleaseSetForm";
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "Release Drawing Sets and Drawings";
            FormClosing += ReleaseSetForm_FormClosing;
            layoutTable.ResumeLayout(false);
            layoutTable.PerformLayout();
            addSetTable.ResumeLayout(false);
            addSetTable.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)setGrid).EndInit();
            optionsTable.ResumeLayout(false);
            optionsTable.PerformLayout();
            previewTable.ResumeLayout(false);
            previewTable.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)groupGrid).EndInit();
            bottomTable.ResumeLayout(false);
            bottomTable.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel layoutTable;
        private System.Windows.Forms.Label setLabel;
        private System.Windows.Forms.TableLayoutPanel addSetTable;
        private System.Windows.Forms.Label addSetLabel;
        private System.Windows.Forms.ComboBox addSetCombo;
        private System.Windows.Forms.Button addSetButton;
        private System.Windows.Forms.Button addDrawingsButton;
        private System.Windows.Forms.Button removeSetButton;
        private System.Windows.Forms.DataGridView setGrid;
        private System.Windows.Forms.DataGridViewTextBoxColumn lineTypeColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn setNameColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn setCustomerColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn setRevisionColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn kitsColumn;
        private System.Windows.Forms.TableLayoutPanel optionsTable;
        private System.Windows.Forms.CheckBox includeArchivedCheckBox;
        private System.Windows.Forms.CheckBox nestAfterReleaseCheckBox;
        private System.Windows.Forms.Label notesLabel;
        private System.Windows.Forms.TextBox notesBox;
        private System.Windows.Forms.TableLayoutPanel previewTable;
        private System.Windows.Forms.Label previewLabel;
        private System.Windows.Forms.Button optionsButton;
        private System.Windows.Forms.DataGridView groupGrid;
        private System.Windows.Forms.DataGridViewCheckBoxColumn includeColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn materialColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn gradeColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn thicknessColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn unitsColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn cutTypeColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn drawingsColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn partsColumn;
        private System.Windows.Forms.DataGridViewComboBoxColumn targetColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn statusColumn;
        private System.Windows.Forms.Label skippedLabel;
        private System.Windows.Forms.Button fixDrawingsButton;
        private System.Windows.Forms.ContextMenuStrip groupMenu;
        private System.Windows.Forms.TableLayoutPanel bottomTable;
        private System.Windows.Forms.Label summaryLabel;
        private System.Windows.Forms.Button releaseButton;
        private System.Windows.Forms.Button closeButton;
        private System.Windows.Forms.Label statusLabel;
        private System.Windows.Forms.Timer searchTimer;
    }
}
