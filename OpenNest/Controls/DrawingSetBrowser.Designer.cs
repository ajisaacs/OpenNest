namespace OpenNest.Controls
{
    partial class DrawingSetBrowser
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                session?.Dispose();
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
            filterTable = new System.Windows.Forms.TableLayoutPanel();
            searchLabel = new System.Windows.Forms.Label();
            searchBox = new System.Windows.Forms.TextBox();
            showArchivedCheckBox = new System.Windows.Forms.CheckBox();
            setGrid = new System.Windows.Forms.DataGridView();
            nameColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            customerColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            membersColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            revisionColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            savedColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            savedByColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            stateColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            notesGroup = new System.Windows.Forms.GroupBox();
            notesBox = new System.Windows.Forms.TextBox();
            bottomTable = new System.Windows.Forms.TableLayoutPanel();
            previousButton = new System.Windows.Forms.Button();
            nextButton = new System.Windows.Forms.Button();
            summaryLabel = new System.Windows.Forms.Label();
            newButton = new System.Windows.Forms.Button();
            editButton = new System.Windows.Forms.Button();
            archiveButton = new System.Windows.Forms.Button();
            historyButton = new System.Windows.Forms.Button();
            statusLabel = new System.Windows.Forms.Label();
            searchTimer = new System.Windows.Forms.Timer(components);
            layoutTable.SuspendLayout();
            filterTable.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)setGrid).BeginInit();
            notesGroup.SuspendLayout();
            bottomTable.SuspendLayout();
            SuspendLayout();
            //
            // layoutTable
            //
            layoutTable.ColumnCount = 1;
            layoutTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            layoutTable.Controls.Add(filterTable, 0, 0);
            layoutTable.Controls.Add(setGrid, 0, 1);
            layoutTable.Controls.Add(notesGroup, 0, 2);
            layoutTable.Controls.Add(bottomTable, 0, 3);
            layoutTable.Controls.Add(statusLabel, 0, 4);
            layoutTable.Dock = System.Windows.Forms.DockStyle.Fill;
            layoutTable.Location = new System.Drawing.Point(0, 0);
            layoutTable.Name = "layoutTable";
            layoutTable.Padding = new System.Windows.Forms.Padding(0);
            layoutTable.RowCount = 5;
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 80F));
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.Size = new System.Drawing.Size(904, 541);
            layoutTable.TabIndex = 0;
            //
            // filterTable
            //
            filterTable.AutoSize = true;
            filterTable.ColumnCount = 4;
            filterTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            filterTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 280F));
            filterTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            filterTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            filterTable.Controls.Add(searchLabel, 0, 0);
            filterTable.Controls.Add(searchBox, 1, 0);
            filterTable.Controls.Add(showArchivedCheckBox, 2, 0);
            filterTable.Dock = System.Windows.Forms.DockStyle.Fill;
            filterTable.Margin = new System.Windows.Forms.Padding(0, 0, 0, 6);
            filterTable.Name = "filterTable";
            filterTable.RowCount = 1;
            filterTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            filterTable.TabIndex = 0;
            //
            // searchLabel
            //
            searchLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            searchLabel.AutoSize = true;
            searchLabel.Name = "searchLabel";
            searchLabel.TabIndex = 0;
            searchLabel.Text = "Search:";
            //
            // searchBox
            //
            searchBox.Dock = System.Windows.Forms.DockStyle.Fill;
            searchBox.Name = "searchBox";
            searchBox.PlaceholderText = "Name, customer or notes";
            searchBox.TabIndex = 1;
            searchBox.TextChanged += SearchBox_TextChanged;
            //
            // showArchivedCheckBox
            //
            showArchivedCheckBox.Anchor = System.Windows.Forms.AnchorStyles.Left;
            showArchivedCheckBox.AutoSize = true;
            showArchivedCheckBox.Margin = new System.Windows.Forms.Padding(12, 3, 3, 3);
            showArchivedCheckBox.Name = "showArchivedCheckBox";
            showArchivedCheckBox.TabIndex = 2;
            showArchivedCheckBox.Text = "Show archived";
            showArchivedCheckBox.UseVisualStyleBackColor = true;
            showArchivedCheckBox.CheckedChanged += ShowArchivedCheckBox_CheckedChanged;
            //
            // setGrid
            //
            setGrid.AllowUserToAddRows = false;
            setGrid.AllowUserToDeleteRows = false;
            setGrid.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] { nameColumn, customerColumn, membersColumn, revisionColumn, savedColumn, savedByColumn, stateColumn });
            setGrid.Dock = System.Windows.Forms.DockStyle.Fill;
            setGrid.MultiSelect = false;
            setGrid.Name = "setGrid";
            setGrid.ReadOnly = true;
            setGrid.RowHeadersVisible = false;
            setGrid.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            setGrid.StandardTab = true;
            setGrid.TabIndex = 1;
            setGrid.CellDoubleClick += SetGrid_CellDoubleClick;
            setGrid.ColumnHeaderMouseClick += SetGrid_ColumnHeaderMouseClick;
            setGrid.CurrentCellChanged += SetGrid_CurrentCellChanged;
            //
            // nameColumn
            //
            nameColumn.HeaderText = "Name";
            nameColumn.Name = "nameColumn";
            nameColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Programmatic;
            nameColumn.Width = 220;
            //
            // customerColumn
            //
            customerColumn.HeaderText = "Customer";
            customerColumn.Name = "customerColumn";
            customerColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Programmatic;
            customerColumn.Width = 170;
            //
            // membersColumn
            //
            rightAligned.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleRight;
            membersColumn.DefaultCellStyle = rightAligned;
            membersColumn.HeaderText = "Drawings";
            membersColumn.Name = "membersColumn";
            membersColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            membersColumn.Width = 75;
            //
            // revisionColumn
            //
            revisionColumn.DefaultCellStyle = rightAligned;
            revisionColumn.HeaderText = "Revision";
            revisionColumn.Name = "revisionColumn";
            revisionColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Programmatic;
            revisionColumn.Width = 70;
            //
            // savedColumn
            //
            savedColumn.HeaderText = "Saved";
            savedColumn.Name = "savedColumn";
            savedColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Programmatic;
            savedColumn.Width = 130;
            //
            // savedByColumn
            //
            savedByColumn.HeaderText = "Saved by";
            savedByColumn.Name = "savedByColumn";
            savedByColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            savedByColumn.Width = 110;
            //
            // stateColumn
            //
            stateColumn.HeaderText = "State";
            stateColumn.Name = "stateColumn";
            stateColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            stateColumn.Width = 80;
            //
            // notesGroup
            //
            notesGroup.Controls.Add(notesBox);
            notesGroup.Dock = System.Windows.Forms.DockStyle.Fill;
            notesGroup.Name = "notesGroup";
            notesGroup.TabIndex = 2;
            notesGroup.TabStop = false;
            notesGroup.Text = "Notes";
            //
            // notesBox
            //
            notesBox.BorderStyle = System.Windows.Forms.BorderStyle.None;
            notesBox.Dock = System.Windows.Forms.DockStyle.Fill;
            notesBox.Multiline = true;
            notesBox.Name = "notesBox";
            notesBox.ReadOnly = true;
            notesBox.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            notesBox.TabIndex = 0;
            notesBox.TabStop = false;
            //
            // bottomTable
            //
            bottomTable.AutoSize = true;
            bottomTable.ColumnCount = 7;
            bottomTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            bottomTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            bottomTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            bottomTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            bottomTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            bottomTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            bottomTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            bottomTable.Controls.Add(previousButton, 0, 0);
            bottomTable.Controls.Add(nextButton, 1, 0);
            bottomTable.Controls.Add(summaryLabel, 2, 0);
            bottomTable.Controls.Add(newButton, 3, 0);
            bottomTable.Controls.Add(editButton, 4, 0);
            bottomTable.Controls.Add(archiveButton, 5, 0);
            bottomTable.Controls.Add(historyButton, 6, 0);
            bottomTable.Dock = System.Windows.Forms.DockStyle.Fill;
            bottomTable.Margin = new System.Windows.Forms.Padding(0, 6, 0, 0);
            bottomTable.Name = "bottomTable";
            bottomTable.RowCount = 1;
            bottomTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            bottomTable.TabIndex = 3;
            //
            // previousButton
            //
            previousButton.AutoSize = true;
            previousButton.Name = "previousButton";
            previousButton.TabIndex = 0;
            previousButton.Text = "< Previous";
            previousButton.UseVisualStyleBackColor = true;
            previousButton.Click += PreviousButton_Click;
            //
            // nextButton
            //
            nextButton.AutoSize = true;
            nextButton.Name = "nextButton";
            nextButton.TabIndex = 1;
            nextButton.Text = "Next >";
            nextButton.UseVisualStyleBackColor = true;
            nextButton.Click += NextButton_Click;
            //
            // summaryLabel
            //
            summaryLabel.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            summaryLabel.AutoEllipsis = true;
            summaryLabel.Name = "summaryLabel";
            summaryLabel.TabIndex = 2;
            //
            // newButton
            //
            newButton.AutoSize = true;
            newButton.Margin = new System.Windows.Forms.Padding(12, 3, 3, 3);
            newButton.Name = "newButton";
            newButton.TabIndex = 3;
            newButton.Text = "New Set...";
            newButton.UseVisualStyleBackColor = true;
            newButton.Click += NewButton_Click;
            //
            // editButton
            //
            editButton.AutoSize = true;
            editButton.Enabled = false;
            editButton.Name = "editButton";
            editButton.TabIndex = 4;
            editButton.Text = "Edit...";
            editButton.UseVisualStyleBackColor = true;
            editButton.Click += EditButton_Click;
            //
            // archiveButton
            //
            archiveButton.AutoSize = true;
            archiveButton.Enabled = false;
            archiveButton.Name = "archiveButton";
            archiveButton.TabIndex = 5;
            archiveButton.Text = "Archive";
            archiveButton.UseVisualStyleBackColor = true;
            archiveButton.Click += ArchiveButton_Click;
            //
            // historyButton
            //
            historyButton.AutoSize = true;
            historyButton.Enabled = false;
            historyButton.Name = "historyButton";
            historyButton.TabIndex = 6;
            historyButton.Text = "History...";
            historyButton.UseVisualStyleBackColor = true;
            historyButton.Click += HistoryButton_Click;
            //
            // statusLabel
            //
            statusLabel.AutoEllipsis = true;
            statusLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            statusLabel.Margin = new System.Windows.Forms.Padding(3, 6, 3, 0);
            statusLabel.Name = "statusLabel";
            statusLabel.Size = new System.Drawing.Size(100, 20);
            statusLabel.TabIndex = 4;
            //
            // searchTimer
            //
            searchTimer.Interval = 300;
            searchTimer.Tick += SearchTimer_Tick;
            //
            // DrawingSetBrowser
            //
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            Controls.Add(layoutTable);
            Font = new System.Drawing.Font("Segoe UI", 9.75F);
            Name = "DrawingSetBrowser";
            Size = new System.Drawing.Size(904, 541);
            layoutTable.ResumeLayout(false);
            layoutTable.PerformLayout();
            filterTable.ResumeLayout(false);
            filterTable.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)setGrid).EndInit();
            notesGroup.ResumeLayout(false);
            notesGroup.PerformLayout();
            bottomTable.ResumeLayout(false);
            bottomTable.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel layoutTable;
        private System.Windows.Forms.TableLayoutPanel filterTable;
        private System.Windows.Forms.Label searchLabel;
        private System.Windows.Forms.TextBox searchBox;
        private System.Windows.Forms.CheckBox showArchivedCheckBox;
        private System.Windows.Forms.DataGridView setGrid;
        private System.Windows.Forms.DataGridViewTextBoxColumn nameColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn customerColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn membersColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn revisionColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn savedColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn savedByColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn stateColumn;
        private System.Windows.Forms.GroupBox notesGroup;
        private System.Windows.Forms.TextBox notesBox;
        private System.Windows.Forms.TableLayoutPanel bottomTable;
        private System.Windows.Forms.Button previousButton;
        private System.Windows.Forms.Button nextButton;
        private System.Windows.Forms.Label summaryLabel;
        private System.Windows.Forms.Button newButton;
        private System.Windows.Forms.Button editButton;
        private System.Windows.Forms.Button archiveButton;
        private System.Windows.Forms.Button historyButton;
        private System.Windows.Forms.Label statusLabel;
        private System.Windows.Forms.Timer searchTimer;
    }
}
