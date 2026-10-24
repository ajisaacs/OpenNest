namespace OpenNest.Forms
{
    partial class AddReleaseDrawingsForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                librarySession?.Dispose();
                if (components != null)
                    components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            System.Windows.Forms.DataGridViewCellStyle libraryRightAligned = new System.Windows.Forms.DataGridViewCellStyle();
            layoutTable = new System.Windows.Forms.TableLayoutPanel();
            searchBox = new System.Windows.Forms.TextBox();
            libraryGrid = new System.Windows.Forms.DataGridView();
            libraryNameColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            libraryMaterialColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            libraryGradeColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            libraryThicknessColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            libraryCutTypeColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            libraryCustomerColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            libraryNavTable = new System.Windows.Forms.TableLayoutPanel();
            previousButton = new System.Windows.Forms.Button();
            nextButton = new System.Windows.Forms.Button();
            librarySummaryLabel = new System.Windows.Forms.Label();
            addTable = new System.Windows.Forms.TableLayoutPanel();
            statusLabel = new System.Windows.Forms.Label();
            quantityLabel = new System.Windows.Forms.Label();
            quantityBox = new System.Windows.Forms.NumericUpDown();
            addButton = new System.Windows.Forms.Button();
            closeButton = new System.Windows.Forms.Button();
            searchTimer = new System.Windows.Forms.Timer(components);
            layoutTable.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)libraryGrid).BeginInit();
            libraryNavTable.SuspendLayout();
            addTable.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)quantityBox).BeginInit();
            SuspendLayout();
            //
            // layoutTable
            //
            layoutTable.ColumnCount = 1;
            layoutTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            layoutTable.Controls.Add(searchBox, 0, 0);
            layoutTable.Controls.Add(libraryGrid, 0, 1);
            layoutTable.Controls.Add(libraryNavTable, 0, 2);
            layoutTable.Controls.Add(addTable, 0, 3);
            layoutTable.Dock = System.Windows.Forms.DockStyle.Fill;
            layoutTable.Name = "layoutTable";
            layoutTable.Padding = new System.Windows.Forms.Padding(8);
            layoutTable.RowCount = 4;
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.TabIndex = 0;
            //
            // searchBox
            //
            searchBox.Dock = System.Windows.Forms.DockStyle.Fill;
            searchBox.Margin = new System.Windows.Forms.Padding(0, 0, 0, 6);
            searchBox.Name = "searchBox";
            searchBox.PlaceholderText = "Search by name, customer or notes";
            searchBox.TabIndex = 0;
            searchBox.TextChanged += SearchBox_TextChanged;
            //
            // libraryGrid
            //
            libraryGrid.AllowUserToAddRows = false;
            libraryGrid.AllowUserToDeleteRows = false;
            libraryGrid.AllowUserToOrderColumns = false;
            libraryGrid.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] { libraryNameColumn, libraryMaterialColumn, libraryGradeColumn, libraryThicknessColumn, libraryCutTypeColumn, libraryCustomerColumn });
            libraryGrid.Dock = System.Windows.Forms.DockStyle.Fill;
            libraryGrid.Margin = new System.Windows.Forms.Padding(0);
            libraryGrid.MultiSelect = true;
            libraryGrid.Name = "libraryGrid";
            libraryGrid.ReadOnly = true;
            libraryGrid.RowHeadersVisible = false;
            libraryGrid.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            libraryGrid.StandardTab = true;
            libraryGrid.TabIndex = 1;
            libraryGrid.CellDoubleClick += LibraryGrid_CellDoubleClick;
            libraryGrid.ColumnHeaderMouseClick += LibraryGrid_ColumnHeaderMouseClick;
            libraryGrid.SelectionChanged += LibraryGrid_SelectionChanged;
            //
            // libraryNameColumn
            //
            libraryNameColumn.HeaderText = "Drawing";
            libraryNameColumn.Name = "libraryNameColumn";
            libraryNameColumn.ReadOnly = true;
            libraryNameColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Programmatic;
            libraryNameColumn.Width = 180;
            //
            // libraryMaterialColumn
            //
            libraryMaterialColumn.HeaderText = "Material";
            libraryMaterialColumn.Name = "libraryMaterialColumn";
            libraryMaterialColumn.ReadOnly = true;
            libraryMaterialColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Programmatic;
            libraryMaterialColumn.Width = 130;
            //
            // libraryGradeColumn
            //
            libraryGradeColumn.HeaderText = "Grade";
            libraryGradeColumn.Name = "libraryGradeColumn";
            libraryGradeColumn.ReadOnly = true;
            libraryGradeColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Programmatic;
            libraryGradeColumn.Width = 70;
            //
            // libraryThicknessColumn
            //
            libraryRightAligned.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleRight;
            libraryThicknessColumn.DefaultCellStyle = libraryRightAligned;
            libraryThicknessColumn.HeaderText = "Thickness";
            libraryThicknessColumn.Name = "libraryThicknessColumn";
            libraryThicknessColumn.ReadOnly = true;
            libraryThicknessColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Programmatic;
            libraryThicknessColumn.Width = 80;
            //
            // libraryCutTypeColumn
            //
            libraryCutTypeColumn.HeaderText = "Cut type";
            libraryCutTypeColumn.Name = "libraryCutTypeColumn";
            libraryCutTypeColumn.ReadOnly = true;
            libraryCutTypeColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Programmatic;
            libraryCutTypeColumn.Width = 80;
            //
            // libraryCustomerColumn
            //
            libraryCustomerColumn.Width = 150;
            libraryCustomerColumn.HeaderText = "Customer";
            libraryCustomerColumn.MinimumWidth = 60;
            libraryCustomerColumn.Name = "libraryCustomerColumn";
            libraryCustomerColumn.ReadOnly = true;
            libraryCustomerColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Programmatic;
            //
            // libraryNavTable
            //
            libraryNavTable.AutoSize = true;
            libraryNavTable.ColumnCount = 3;
            libraryNavTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            libraryNavTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            libraryNavTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            libraryNavTable.Controls.Add(previousButton, 0, 0);
            libraryNavTable.Controls.Add(nextButton, 1, 0);
            libraryNavTable.Controls.Add(librarySummaryLabel, 2, 0);
            libraryNavTable.Dock = System.Windows.Forms.DockStyle.Fill;
            libraryNavTable.Margin = new System.Windows.Forms.Padding(0, 4, 0, 0);
            libraryNavTable.Name = "libraryNavTable";
            libraryNavTable.RowCount = 1;
            libraryNavTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            libraryNavTable.TabIndex = 2;
            //
            // previousButton
            //
            previousButton.Enabled = false;
            previousButton.Margin = new System.Windows.Forms.Padding(0, 0, 3, 0);
            previousButton.Name = "previousButton";
            previousButton.Size = new System.Drawing.Size(34, 26);
            previousButton.TabIndex = 0;
            previousButton.Text = "<";
            previousButton.UseVisualStyleBackColor = true;
            previousButton.Click += PreviousButton_Click;
            //
            // nextButton
            //
            nextButton.Enabled = false;
            nextButton.Margin = new System.Windows.Forms.Padding(0, 0, 6, 0);
            nextButton.Name = "nextButton";
            nextButton.Size = new System.Drawing.Size(34, 26);
            nextButton.TabIndex = 1;
            nextButton.Text = ">";
            nextButton.UseVisualStyleBackColor = true;
            nextButton.Click += NextButton_Click;
            //
            // librarySummaryLabel
            //
            librarySummaryLabel.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            librarySummaryLabel.AutoEllipsis = true;
            librarySummaryLabel.ForeColor = System.Drawing.SystemColors.GrayText;
            librarySummaryLabel.Name = "librarySummaryLabel";
            librarySummaryLabel.TabIndex = 2;
            //
            // addTable
            //
            addTable.AutoSize = true;
            addTable.ColumnCount = 5;
            addTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            addTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            addTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            addTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            addTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            addTable.Controls.Add(statusLabel, 0, 0);
            addTable.Controls.Add(quantityLabel, 1, 0);
            addTable.Controls.Add(quantityBox, 2, 0);
            addTable.Controls.Add(addButton, 3, 0);
            addTable.Controls.Add(closeButton, 4, 0);
            addTable.Dock = System.Windows.Forms.DockStyle.Fill;
            addTable.Margin = new System.Windows.Forms.Padding(0, 6, 0, 0);
            addTable.Name = "addTable";
            addTable.RowCount = 1;
            addTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            addTable.TabIndex = 3;
            //
            // statusLabel
            //
            statusLabel.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            statusLabel.AutoEllipsis = true;
            statusLabel.Name = "statusLabel";
            statusLabel.TabIndex = 0;
            //
            // quantityLabel
            //
            quantityLabel.Anchor = System.Windows.Forms.AnchorStyles.Right;
            quantityLabel.AutoSize = true;
            quantityLabel.Name = "quantityLabel";
            quantityLabel.TabIndex = 1;
            quantityLabel.Text = "Quantity:";
            //
            // quantityBox
            //
            quantityBox.Anchor = System.Windows.Forms.AnchorStyles.Left;
            quantityBox.Maximum = new decimal(new int[] { 100000, 0, 0, 0 });
            quantityBox.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            quantityBox.Name = "quantityBox";
            quantityBox.Size = new System.Drawing.Size(80, 25);
            quantityBox.TabIndex = 2;
            quantityBox.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            quantityBox.Value = new decimal(new int[] { 1, 0, 0, 0 });
            //
            // addButton
            //
            addButton.Enabled = false;
            addButton.Margin = new System.Windows.Forms.Padding(6, 0, 0, 0);
            addButton.Name = "addButton";
            addButton.Size = new System.Drawing.Size(140, 28);
            addButton.TabIndex = 3;
            addButton.Text = "Add to Release";
            addButton.UseVisualStyleBackColor = true;
            addButton.Click += AddButton_Click;
            //
            // closeButton
            //
            closeButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            closeButton.Margin = new System.Windows.Forms.Padding(6, 0, 0, 0);
            closeButton.Name = "closeButton";
            closeButton.Size = new System.Drawing.Size(90, 28);
            closeButton.TabIndex = 4;
            closeButton.Text = "Close";
            closeButton.UseVisualStyleBackColor = true;
            //
            // searchTimer
            //
            searchTimer.Interval = 300;
            searchTimer.Tick += SearchTimer_Tick;
            //
            // AddReleaseDrawingsForm
            //
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            CancelButton = closeButton;
            ClientSize = new System.Drawing.Size(860, 560);
            Controls.Add(layoutTable);
            Font = new System.Drawing.Font("Segoe UI", 9.75F);
            MinimizeBox = false;
            MinimumSize = new System.Drawing.Size(640, 400);
            Name = "AddReleaseDrawingsForm";
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "Add Drawings to the Release";
            Shown += AddReleaseDrawingsForm_Shown;
            layoutTable.ResumeLayout(false);
            layoutTable.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)libraryGrid).EndInit();
            libraryNavTable.ResumeLayout(false);
            addTable.ResumeLayout(false);
            addTable.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)quantityBox).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel layoutTable;
        private System.Windows.Forms.TextBox searchBox;
        private System.Windows.Forms.DataGridView libraryGrid;
        private System.Windows.Forms.DataGridViewTextBoxColumn libraryNameColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn libraryMaterialColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn libraryGradeColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn libraryThicknessColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn libraryCutTypeColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn libraryCustomerColumn;
        private System.Windows.Forms.TableLayoutPanel libraryNavTable;
        private System.Windows.Forms.Button previousButton;
        private System.Windows.Forms.Button nextButton;
        private System.Windows.Forms.Label librarySummaryLabel;
        private System.Windows.Forms.TableLayoutPanel addTable;
        private System.Windows.Forms.Label statusLabel;
        private System.Windows.Forms.Label quantityLabel;
        private System.Windows.Forms.NumericUpDown quantityBox;
        private System.Windows.Forms.Button addButton;
        private System.Windows.Forms.Button closeButton;
        private System.Windows.Forms.Timer searchTimer;
    }
}
