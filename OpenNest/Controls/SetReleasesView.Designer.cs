namespace OpenNest.Controls
{
    partial class SetReleasesView
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
            System.Windows.Forms.DataGridViewCellStyle rightAligned = new System.Windows.Forms.DataGridViewCellStyle();
            layoutTable = new System.Windows.Forms.TableLayoutPanel();
            filterTable = new System.Windows.Forms.TableLayoutPanel();
            filterLabel = new System.Windows.Forms.Label();
            clearFilterButton = new System.Windows.Forms.Button();
            releaseGrid = new System.Windows.Forms.DataGridView();
            revisionColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            releasedColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            releasedByColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            multiplierColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            nestsColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            progressColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            releaseNotesColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            nestsGroup = new System.Windows.Forms.GroupBox();
            nestGrid = new System.Windows.Forms.DataGridView();
            nestNameColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            groupColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            statusColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            nestCustomerColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            modifiedColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            changeColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            bottomTable = new System.Windows.Forms.TableLayoutPanel();
            previousButton = new System.Windows.Forms.Button();
            nextButton = new System.Windows.Forms.Button();
            summaryLabel = new System.Windows.Forms.Label();
            newReleaseButton = new System.Windows.Forms.Button();
            undoButton = new System.Windows.Forms.Button();
            layoutTable.SuspendLayout();
            filterTable.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)releaseGrid).BeginInit();
            nestsGroup.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)nestGrid).BeginInit();
            bottomTable.SuspendLayout();
            SuspendLayout();
            //
            // layoutTable
            //
            layoutTable.ColumnCount = 1;
            layoutTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            layoutTable.Controls.Add(filterTable, 0, 0);
            layoutTable.Controls.Add(releaseGrid, 0, 1);
            layoutTable.Controls.Add(nestsGroup, 0, 2);
            layoutTable.Controls.Add(bottomTable, 0, 3);
            layoutTable.Dock = System.Windows.Forms.DockStyle.Fill;
            layoutTable.Location = new System.Drawing.Point(0, 0);
            layoutTable.Name = "layoutTable";
            layoutTable.Padding = new System.Windows.Forms.Padding(0);
            layoutTable.RowCount = 4;
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50F));
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50F));
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.Size = new System.Drawing.Size(984, 541);
            layoutTable.TabIndex = 0;
            //
            // filterTable
            //
            filterTable.AutoSize = true;
            filterTable.ColumnCount = 3;
            filterTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            filterTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            filterTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            filterTable.Controls.Add(filterLabel, 0, 0);
            filterTable.Controls.Add(clearFilterButton, 1, 0);
            filterTable.Dock = System.Windows.Forms.DockStyle.Fill;
            filterTable.Margin = new System.Windows.Forms.Padding(0, 0, 0, 6);
            filterTable.Name = "filterTable";
            filterTable.RowCount = 1;
            filterTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            filterTable.TabIndex = 0;
            //
            // filterLabel
            //
            filterLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            filterLabel.AutoSize = true;
            filterLabel.BackColor = System.Drawing.SystemColors.Info;
            filterLabel.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            filterLabel.ForeColor = System.Drawing.SystemColors.InfoText;
            filterLabel.Name = "filterLabel";
            filterLabel.Padding = new System.Windows.Forms.Padding(4, 2, 4, 2);
            filterLabel.TabIndex = 0;
            //
            // clearFilterButton
            //
            clearFilterButton.AutoSize = true;
            clearFilterButton.Margin = new System.Windows.Forms.Padding(6, 3, 3, 3);
            clearFilterButton.Name = "clearFilterButton";
            clearFilterButton.TabIndex = 1;
            clearFilterButton.Text = "Clear Filter";
            clearFilterButton.UseVisualStyleBackColor = true;
            clearFilterButton.Click += ClearFilterButton_Click;
            //
            // releaseGrid
            //
            releaseGrid.AllowUserToAddRows = false;
            releaseGrid.AllowUserToDeleteRows = false;
            releaseGrid.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] { releasedColumn, releasedByColumn, revisionColumn, multiplierColumn, nestsColumn, progressColumn, releaseNotesColumn });
            releaseGrid.Dock = System.Windows.Forms.DockStyle.Fill;
            releaseGrid.MultiSelect = false;
            releaseGrid.Name = "releaseGrid";
            releaseGrid.ReadOnly = true;
            releaseGrid.RowHeadersVisible = false;
            releaseGrid.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            releaseGrid.StandardTab = true;
            releaseGrid.TabIndex = 1;
            releaseGrid.CurrentCellChanged += ReleaseGrid_CurrentCellChanged;
            //
            // revisionColumn
            //
            rightAligned.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleRight;
            revisionColumn.DefaultCellStyle = rightAligned;
            revisionColumn.HeaderText = "Revision";
            revisionColumn.ToolTipText = "Which saved revision of the set was released; see the set's History.";
            revisionColumn.Name = "revisionColumn";
            revisionColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            revisionColumn.Width = 70;
            //
            // releasedColumn
            //
            releasedColumn.HeaderText = "Released";
            releasedColumn.Name = "releasedColumn";
            releasedColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            releasedColumn.Width = 130;
            //
            // releasedByColumn
            //
            releasedByColumn.HeaderText = "Released by";
            releasedByColumn.Name = "releasedByColumn";
            releasedByColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            releasedByColumn.Width = 110;
            //
            // multiplierColumn
            //
            multiplierColumn.HeaderText = "Quantity";
            multiplierColumn.Name = "multiplierColumn";
            multiplierColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            multiplierColumn.Width = 50;
            multiplierColumn.DefaultCellStyle = rightAligned;
            //
            // nestsColumn
            //
            nestsColumn.HeaderText = "Nests";
            nestsColumn.Name = "nestsColumn";
            nestsColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            nestsColumn.Width = 55;
            nestsColumn.DefaultCellStyle = rightAligned;
            //
            // progressColumn
            //
            progressColumn.HeaderText = "Progress";
            progressColumn.Name = "progressColumn";
            progressColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            progressColumn.Width = 160;
            //
            // releaseNotesColumn
            //
            releaseNotesColumn.HeaderText = "Notes";
            releaseNotesColumn.Name = "releaseNotesColumn";
            releaseNotesColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            releaseNotesColumn.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            //
            // nestsGroup
            //
            nestsGroup.Controls.Add(nestGrid);
            nestsGroup.Dock = System.Windows.Forms.DockStyle.Fill;
            nestsGroup.Margin = new System.Windows.Forms.Padding(0, 6, 0, 0);
            nestsGroup.Name = "nestsGroup";
            nestsGroup.TabIndex = 2;
            nestsGroup.TabStop = false;
            nestsGroup.Text = "Nests of the selected release";
            //
            // nestGrid
            //
            nestGrid.AllowUserToAddRows = false;
            nestGrid.AllowUserToDeleteRows = false;
            nestGrid.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] { nestNameColumn, groupColumn, changeColumn, statusColumn, nestCustomerColumn, modifiedColumn });
            nestGrid.Dock = System.Windows.Forms.DockStyle.Fill;
            nestGrid.MultiSelect = false;
            nestGrid.Name = "nestGrid";
            nestGrid.ReadOnly = true;
            nestGrid.RowHeadersVisible = false;
            nestGrid.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            nestGrid.StandardTab = true;
            nestGrid.TabIndex = 0;
            //
            // nestNameColumn
            //
            nestNameColumn.HeaderText = "Nest";
            nestNameColumn.Name = "nestNameColumn";
            nestNameColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            nestNameColumn.Width = 160;
            //
            // groupColumn
            //
            groupColumn.HeaderText = "Material";
            groupColumn.Name = "groupColumn";
            groupColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            groupColumn.Width = 260;
            //
            // changeColumn
            //
            changeColumn.HeaderText = "Action";
            changeColumn.Name = "changeColumn";
            changeColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            changeColumn.ToolTipText = "Whether the release created this nest or merged into it, and what Undo did.";
            changeColumn.Width = 190;
            //
            // statusColumn
            //
            statusColumn.HeaderText = "Status";
            statusColumn.Name = "statusColumn";
            statusColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            statusColumn.Width = 110;
            //
            // nestCustomerColumn
            //
            nestCustomerColumn.HeaderText = "Customer";
            nestCustomerColumn.Name = "nestCustomerColumn";
            nestCustomerColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            nestCustomerColumn.Width = 140;
            //
            // modifiedColumn
            //
            modifiedColumn.HeaderText = "Modified";
            modifiedColumn.Name = "modifiedColumn";
            modifiedColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            modifiedColumn.Width = 130;
            //
            // bottomTable
            //
            bottomTable.AutoSize = true;
            bottomTable.ColumnCount = 5;
            bottomTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            bottomTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            bottomTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            bottomTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            bottomTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            bottomTable.Controls.Add(previousButton, 0, 0);
            bottomTable.Controls.Add(nextButton, 1, 0);
            bottomTable.Controls.Add(summaryLabel, 2, 0);
            bottomTable.Controls.Add(newReleaseButton, 3, 0);
            bottomTable.Controls.Add(undoButton, 4, 0);
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
            previousButton.Enabled = false;
            previousButton.Name = "previousButton";
            previousButton.TabIndex = 0;
            previousButton.Text = "< Newer";
            previousButton.UseVisualStyleBackColor = true;
            previousButton.Click += PreviousButton_Click;
            //
            // nextButton
            //
            nextButton.AutoSize = true;
            nextButton.Enabled = false;
            nextButton.Name = "nextButton";
            nextButton.TabIndex = 1;
            nextButton.Text = "Older >";
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
            // newReleaseButton
            //
            newReleaseButton.AutoSize = true;
            newReleaseButton.Name = "newReleaseButton";
            newReleaseButton.TabIndex = 3;
            newReleaseButton.Text = "New Release...";
            newReleaseButton.UseVisualStyleBackColor = true;
            newReleaseButton.Visible = false;
            newReleaseButton.Click += NewReleaseButton_Click;
            //
            // undoButton
            //
            undoButton.AutoSize = true;
            undoButton.Enabled = false;
            undoButton.Name = "undoButton";
            undoButton.TabIndex = 4;
            undoButton.Text = "Undo Release...";
            undoButton.UseVisualStyleBackColor = true;
            undoButton.Click += UndoButton_Click;
            //
            // SetReleasesView
            //
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            Controls.Add(layoutTable);
            Font = new System.Drawing.Font("Segoe UI", 9.75F);
            Name = "SetReleasesView";
            Size = new System.Drawing.Size(984, 541);
            layoutTable.ResumeLayout(false);
            layoutTable.PerformLayout();
            filterTable.ResumeLayout(false);
            filterTable.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)releaseGrid).EndInit();
            nestsGroup.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)nestGrid).EndInit();
            bottomTable.ResumeLayout(false);
            bottomTable.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel layoutTable;
        private System.Windows.Forms.TableLayoutPanel filterTable;
        private System.Windows.Forms.Label filterLabel;
        private System.Windows.Forms.Button clearFilterButton;
        private System.Windows.Forms.DataGridView releaseGrid;
        private System.Windows.Forms.DataGridViewTextBoxColumn revisionColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn releasedColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn releasedByColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn multiplierColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn nestsColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn progressColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn releaseNotesColumn;
        private System.Windows.Forms.GroupBox nestsGroup;
        private System.Windows.Forms.DataGridView nestGrid;
        private System.Windows.Forms.DataGridViewTextBoxColumn nestNameColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn groupColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn changeColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn statusColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn nestCustomerColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn modifiedColumn;
        private System.Windows.Forms.TableLayoutPanel bottomTable;
        private System.Windows.Forms.Button previousButton;
        private System.Windows.Forms.Button nextButton;
        private System.Windows.Forms.Label summaryLabel;
        private System.Windows.Forms.Button newReleaseButton;
        private System.Windows.Forms.Button undoButton;
    }
}
