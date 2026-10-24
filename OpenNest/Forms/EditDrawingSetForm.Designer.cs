namespace OpenNest.Forms
{
    partial class EditDrawingSetForm
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
            System.Windows.Forms.DataGridViewCellStyle rightAligned = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle quantityStyle = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle libraryRightAligned = new System.Windows.Forms.DataGridViewCellStyle();
            layoutTable = new System.Windows.Forms.TableLayoutPanel();
            detailsTable = new System.Windows.Forms.TableLayoutPanel();
            nameLabel = new System.Windows.Forms.Label();
            nameBox = new System.Windows.Forms.TextBox();
            customerLabel = new System.Windows.Forms.Label();
            customerBox = new System.Windows.Forms.TextBox();
            notesLabel = new System.Windows.Forms.Label();
            notesBox = new System.Windows.Forms.TextBox();
            splitContainer = new System.Windows.Forms.SplitContainer();
            libraryGroup = new System.Windows.Forms.GroupBox();
            libraryTable = new System.Windows.Forms.TableLayoutPanel();
            searchBox = new System.Windows.Forms.TextBox();
            libraryGrid = new System.Windows.Forms.DataGridView();
            libraryNameColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            libraryMaterialColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            libraryGradeColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            libraryThicknessColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            libraryCustomerColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            libraryNavTable = new System.Windows.Forms.TableLayoutPanel();
            previousButton = new System.Windows.Forms.Button();
            nextButton = new System.Windows.Forms.Button();
            librarySummaryLabel = new System.Windows.Forms.Label();
            addTable = new System.Windows.Forms.TableLayoutPanel();
            quantityLabel = new System.Windows.Forms.Label();
            quantityBox = new System.Windows.Forms.NumericUpDown();
            addButton = new System.Windows.Forms.Button();
            memberGroup = new System.Windows.Forms.GroupBox();
            memberTable = new System.Windows.Forms.TableLayoutPanel();
            memberGrid = new System.Windows.Forms.DataGridView();
            drawingColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            materialColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            gradeColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            thicknessColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            unitsColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            cutTypeColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            quantityColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            warningColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            memberButtons = new System.Windows.Forms.FlowLayoutPanel();
            editDrawingButton = new System.Windows.Forms.Button();
            removeButton = new System.Windows.Forms.Button();
            upButton = new System.Windows.Forms.Button();
            downButton = new System.Windows.Forms.Button();
            bottomTable = new System.Windows.Forms.TableLayoutPanel();
            summaryLabel = new System.Windows.Forms.Label();
            saveButton = new System.Windows.Forms.Button();
            cancelButton = new System.Windows.Forms.Button();
            statusLabel = new System.Windows.Forms.Label();
            searchTimer = new System.Windows.Forms.Timer(components);
            memberMenu = new System.Windows.Forms.ContextMenuStrip(components);
            editDrawingMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            removeMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            layoutTable.SuspendLayout();
            detailsTable.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)splitContainer).BeginInit();
            splitContainer.Panel1.SuspendLayout();
            splitContainer.Panel2.SuspendLayout();
            splitContainer.SuspendLayout();
            libraryGroup.SuspendLayout();
            libraryTable.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)libraryGrid).BeginInit();
            libraryNavTable.SuspendLayout();
            addTable.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)quantityBox).BeginInit();
            memberGroup.SuspendLayout();
            memberTable.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)memberGrid).BeginInit();
            memberButtons.SuspendLayout();
            bottomTable.SuspendLayout();
            SuspendLayout();
            //
            // layoutTable
            //
            layoutTable.ColumnCount = 1;
            layoutTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            layoutTable.Controls.Add(detailsTable, 0, 0);
            layoutTable.Controls.Add(splitContainer, 0, 1);
            layoutTable.Controls.Add(bottomTable, 0, 2);
            layoutTable.Controls.Add(statusLabel, 0, 3);
            layoutTable.Dock = System.Windows.Forms.DockStyle.Fill;
            layoutTable.Location = new System.Drawing.Point(0, 0);
            layoutTable.Name = "layoutTable";
            layoutTable.Padding = new System.Windows.Forms.Padding(8);
            layoutTable.RowCount = 4;
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.Size = new System.Drawing.Size(1264, 641);
            layoutTable.TabIndex = 0;
            //
            // detailsTable
            //
            detailsTable.AutoSize = true;
            detailsTable.ColumnCount = 4;
            detailsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            detailsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            detailsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            detailsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            detailsTable.Controls.Add(nameLabel, 0, 0);
            detailsTable.Controls.Add(nameBox, 1, 0);
            detailsTable.Controls.Add(customerLabel, 2, 0);
            detailsTable.Controls.Add(customerBox, 3, 0);
            detailsTable.Controls.Add(notesLabel, 0, 1);
            detailsTable.Controls.Add(notesBox, 1, 1);
            detailsTable.SetColumnSpan(notesBox, 3);
            detailsTable.Dock = System.Windows.Forms.DockStyle.Fill;
            detailsTable.Margin = new System.Windows.Forms.Padding(0, 0, 0, 6);
            detailsTable.Name = "detailsTable";
            detailsTable.RowCount = 2;
            detailsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            detailsTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 48F));
            detailsTable.TabIndex = 0;
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
            nameBox.TextChanged += Details_TextChanged;
            //
            // customerLabel
            //
            customerLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            customerLabel.AutoSize = true;
            customerLabel.Margin = new System.Windows.Forms.Padding(12, 0, 3, 0);
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
            customerBox.TextChanged += Details_TextChanged;
            //
            // notesLabel
            //
            notesLabel.AutoSize = true;
            notesLabel.Margin = new System.Windows.Forms.Padding(3, 6, 3, 0);
            notesLabel.Name = "notesLabel";
            notesLabel.TabIndex = 4;
            notesLabel.Text = "Notes:";
            //
            // notesBox
            //
            notesBox.Dock = System.Windows.Forms.DockStyle.Fill;
            notesBox.MaxLength = 4000;
            notesBox.Multiline = true;
            notesBox.Name = "notesBox";
            notesBox.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            notesBox.TabIndex = 5;
            notesBox.TextChanged += Details_TextChanged;
            //
            // splitContainer
            //
            splitContainer.Dock = System.Windows.Forms.DockStyle.Fill;
            splitContainer.Margin = new System.Windows.Forms.Padding(0);
            splitContainer.Name = "splitContainer";
            splitContainer.Panel1.Controls.Add(libraryGroup);
            splitContainer.Panel2.Controls.Add(memberGroup);
            splitContainer.Size = new System.Drawing.Size(1248, 470);
            splitContainer.SplitterDistance = 450;
            splitContainer.SplitterWidth = 6;
            splitContainer.Panel1MinSize = 320;
            splitContainer.Panel2MinSize = 420;
            splitContainer.TabIndex = 1;
            //
            // libraryGroup
            //
            libraryGroup.Controls.Add(libraryTable);
            libraryGroup.Dock = System.Windows.Forms.DockStyle.Fill;
            libraryGroup.Name = "libraryGroup";
            libraryGroup.Padding = new System.Windows.Forms.Padding(6);
            libraryGroup.TabIndex = 0;
            libraryGroup.TabStop = false;
            libraryGroup.Text = "Drawing library";
            //
            // libraryTable
            //
            libraryTable.ColumnCount = 1;
            libraryTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            libraryTable.Controls.Add(searchBox, 0, 0);
            libraryTable.Controls.Add(libraryGrid, 0, 1);
            libraryTable.Controls.Add(libraryNavTable, 0, 2);
            libraryTable.Controls.Add(addTable, 0, 3);
            libraryTable.Dock = System.Windows.Forms.DockStyle.Fill;
            libraryTable.Margin = new System.Windows.Forms.Padding(0);
            libraryTable.Name = "libraryTable";
            libraryTable.RowCount = 4;
            libraryTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            libraryTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            libraryTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            libraryTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            libraryTable.TabIndex = 0;
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
            libraryGrid.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] { libraryNameColumn, libraryMaterialColumn, libraryGradeColumn, libraryThicknessColumn, libraryCustomerColumn });
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
            libraryNameColumn.Width = 140;
            //
            // libraryMaterialColumn
            //
            libraryMaterialColumn.HeaderText = "Material";
            libraryMaterialColumn.Name = "libraryMaterialColumn";
            libraryMaterialColumn.ReadOnly = true;
            libraryMaterialColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Programmatic;
            libraryMaterialColumn.Width = 100;
            //
            // libraryGradeColumn
            //
            libraryGradeColumn.HeaderText = "Grade";
            libraryGradeColumn.Name = "libraryGradeColumn";
            libraryGradeColumn.ReadOnly = true;
            libraryGradeColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Programmatic;
            libraryGradeColumn.Width = 60;
            //
            // libraryThicknessColumn
            //
            libraryRightAligned.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleRight;
            libraryThicknessColumn.DefaultCellStyle = libraryRightAligned;
            libraryThicknessColumn.HeaderText = "Thickness";
            libraryThicknessColumn.Name = "libraryThicknessColumn";
            libraryThicknessColumn.ReadOnly = true;
            libraryThicknessColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Programmatic;
            libraryThicknessColumn.Width = 70;
            //
            // libraryCustomerColumn
            //
            libraryCustomerColumn.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
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
            addTable.ColumnCount = 4;
            addTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            addTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            addTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            addTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            addTable.Controls.Add(quantityLabel, 1, 0);
            addTable.Controls.Add(quantityBox, 2, 0);
            addTable.Controls.Add(addButton, 3, 0);
            addTable.Dock = System.Windows.Forms.DockStyle.Fill;
            addTable.Margin = new System.Windows.Forms.Padding(0, 6, 0, 0);
            addTable.Name = "addTable";
            addTable.RowCount = 1;
            addTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            addTable.TabIndex = 3;
            //
            // quantityLabel
            //
            quantityLabel.Anchor = System.Windows.Forms.AnchorStyles.Right;
            quantityLabel.AutoSize = true;
            quantityLabel.Name = "quantityLabel";
            quantityLabel.TabIndex = 0;
            quantityLabel.Text = "Qty per set:";
            //
            // quantityBox
            //
            quantityBox.Anchor = System.Windows.Forms.AnchorStyles.Left;
            quantityBox.Maximum = new decimal(new int[] { 100000, 0, 0, 0 });
            quantityBox.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            quantityBox.Name = "quantityBox";
            quantityBox.Size = new System.Drawing.Size(70, 25);
            quantityBox.TabIndex = 1;
            quantityBox.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            quantityBox.Value = new decimal(new int[] { 1, 0, 0, 0 });
            //
            // addButton
            //
            addButton.Enabled = false;
            addButton.Margin = new System.Windows.Forms.Padding(6, 0, 0, 0);
            addButton.Name = "addButton";
            addButton.Size = new System.Drawing.Size(120, 28);
            addButton.TabIndex = 2;
            addButton.Text = "Add to Set  →";
            addButton.UseVisualStyleBackColor = true;
            addButton.Click += AddButton_Click;
            //
            // memberGroup
            //
            memberGroup.Controls.Add(memberTable);
            memberGroup.Dock = System.Windows.Forms.DockStyle.Fill;
            memberGroup.Name = "memberGroup";
            memberGroup.Padding = new System.Windows.Forms.Padding(6);
            memberGroup.TabIndex = 0;
            memberGroup.TabStop = false;
            memberGroup.Text = "In this set";
            //
            // memberTable
            //
            memberTable.ColumnCount = 2;
            memberTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            memberTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            memberTable.Controls.Add(memberGrid, 0, 0);
            memberTable.Controls.Add(memberButtons, 1, 0);
            memberTable.Dock = System.Windows.Forms.DockStyle.Fill;
            memberTable.Margin = new System.Windows.Forms.Padding(0);
            memberTable.Name = "memberTable";
            memberTable.RowCount = 1;
            memberTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            memberTable.TabIndex = 0;
            //
            // memberGrid
            //
            memberGrid.AllowUserToAddRows = false;
            memberGrid.AllowUserToDeleteRows = false;
            memberGrid.AllowUserToOrderColumns = false;
            memberGrid.ContextMenuStrip = memberMenu;
            memberGrid.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] { drawingColumn, materialColumn, gradeColumn, thicknessColumn, unitsColumn, cutTypeColumn, quantityColumn, warningColumn });
            memberGrid.Dock = System.Windows.Forms.DockStyle.Fill;
            memberGrid.EditMode = System.Windows.Forms.DataGridViewEditMode.EditOnKeystrokeOrF2;
            memberGrid.Margin = new System.Windows.Forms.Padding(0);
            memberGrid.MultiSelect = false;
            memberGrid.Name = "memberGrid";
            memberGrid.RowHeadersVisible = false;
            memberGrid.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            memberGrid.StandardTab = true;
            memberGrid.TabIndex = 0;
            memberGrid.CellValidating += MemberGrid_CellValidating;
            memberGrid.DataError += MemberGrid_DataError;
            memberGrid.CellEndEdit += MemberGrid_CellEndEdit;
            memberGrid.CurrentCellChanged += MemberGrid_CurrentCellChanged;
            memberGrid.CellDoubleClick += MemberGrid_CellDoubleClick;
            memberGrid.CellMouseDown += MemberGrid_CellMouseDown;
            //
            // drawingColumn
            //
            drawingColumn.HeaderText = "Drawing";
            drawingColumn.Name = "drawingColumn";
            drawingColumn.ReadOnly = true;
            drawingColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            drawingColumn.Width = 150;
            //
            // materialColumn
            //
            materialColumn.HeaderText = "Material";
            materialColumn.Name = "materialColumn";
            materialColumn.ReadOnly = true;
            materialColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            materialColumn.Width = 100;
            //
            // gradeColumn
            //
            gradeColumn.HeaderText = "Grade";
            gradeColumn.Name = "gradeColumn";
            gradeColumn.ReadOnly = true;
            gradeColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            gradeColumn.Width = 60;
            //
            // thicknessColumn
            //
            rightAligned.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleRight;
            thicknessColumn.DefaultCellStyle = rightAligned;
            thicknessColumn.HeaderText = "Thickness";
            thicknessColumn.Name = "thicknessColumn";
            thicknessColumn.ReadOnly = true;
            thicknessColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            thicknessColumn.Width = 70;
            //
            // unitsColumn
            //
            unitsColumn.HeaderText = "Units";
            unitsColumn.Name = "unitsColumn";
            unitsColumn.ReadOnly = true;
            unitsColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            unitsColumn.Width = 50;
            //
            // cutTypeColumn
            //
            cutTypeColumn.HeaderText = "Cut type";
            cutTypeColumn.Name = "cutTypeColumn";
            cutTypeColumn.ReadOnly = true;
            cutTypeColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            cutTypeColumn.Width = 65;
            //
            // quantityColumn
            //
            quantityStyle.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleRight;
            quantityStyle.BackColor = System.Drawing.SystemColors.Info;
            quantityColumn.DefaultCellStyle = quantityStyle;
            quantityColumn.HeaderText = "Qty";
            quantityColumn.Name = "quantityColumn";
            quantityColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            quantityColumn.ToolTipText = "Quantity per set; type to change it";
            quantityColumn.ValueType = typeof(int);
            quantityColumn.Width = 50;
            //
            // warningColumn
            //
            warningColumn.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            warningColumn.HeaderText = "Note";
            warningColumn.MinimumWidth = 60;
            warningColumn.Name = "warningColumn";
            warningColumn.ReadOnly = true;
            warningColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            //
            // memberButtons
            //
            memberButtons.AutoSize = true;
            memberButtons.Controls.Add(editDrawingButton);
            memberButtons.Controls.Add(removeButton);
            memberButtons.Controls.Add(upButton);
            memberButtons.Controls.Add(downButton);
            memberButtons.Dock = System.Windows.Forms.DockStyle.Fill;
            memberButtons.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            memberButtons.Margin = new System.Windows.Forms.Padding(6, 0, 0, 0);
            memberButtons.Name = "memberButtons";
            memberButtons.TabIndex = 1;
            memberButtons.WrapContents = false;
            //
            // editDrawingButton
            //
            editDrawingButton.Enabled = false;
            editDrawingButton.Margin = new System.Windows.Forms.Padding(0, 0, 0, 12);
            editDrawingButton.Name = "editDrawingButton";
            editDrawingButton.Size = new System.Drawing.Size(100, 28);
            editDrawingButton.TabIndex = 0;
            editDrawingButton.Text = "Edit Drawing...";
            editDrawingButton.UseVisualStyleBackColor = true;
            editDrawingButton.Click += EditDrawingButton_Click;
            //
            // removeButton
            //
            removeButton.Enabled = false;
            removeButton.Margin = new System.Windows.Forms.Padding(0, 0, 0, 3);
            removeButton.Name = "removeButton";
            removeButton.Size = new System.Drawing.Size(100, 28);
            removeButton.TabIndex = 1;
            removeButton.Text = "Remove";
            removeButton.UseVisualStyleBackColor = true;
            removeButton.Click += RemoveButton_Click;
            //
            // upButton
            //
            upButton.Enabled = false;
            upButton.Margin = new System.Windows.Forms.Padding(0, 12, 0, 3);
            upButton.Name = "upButton";
            upButton.Size = new System.Drawing.Size(100, 28);
            upButton.TabIndex = 2;
            upButton.Text = "Move Up";
            upButton.UseVisualStyleBackColor = true;
            upButton.Click += UpButton_Click;
            //
            // downButton
            //
            downButton.Enabled = false;
            downButton.Margin = new System.Windows.Forms.Padding(0, 0, 0, 3);
            downButton.Name = "downButton";
            downButton.Size = new System.Drawing.Size(100, 28);
            downButton.TabIndex = 3;
            downButton.Text = "Move Down";
            downButton.UseVisualStyleBackColor = true;
            downButton.Click += DownButton_Click;
            //
            // bottomTable
            //
            bottomTable.AutoSize = true;
            bottomTable.ColumnCount = 3;
            bottomTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            bottomTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            bottomTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            bottomTable.Controls.Add(summaryLabel, 0, 0);
            bottomTable.Controls.Add(saveButton, 1, 0);
            bottomTable.Controls.Add(cancelButton, 2, 0);
            bottomTable.Dock = System.Windows.Forms.DockStyle.Fill;
            bottomTable.Margin = new System.Windows.Forms.Padding(0, 6, 0, 0);
            bottomTable.Name = "bottomTable";
            bottomTable.RowCount = 1;
            bottomTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            bottomTable.TabIndex = 2;
            //
            // summaryLabel
            //
            summaryLabel.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            summaryLabel.AutoEllipsis = true;
            summaryLabel.Name = "summaryLabel";
            summaryLabel.TabIndex = 0;
            //
            // saveButton
            //
            saveButton.AutoSize = true;
            saveButton.Enabled = false;
            saveButton.Name = "saveButton";
            saveButton.TabIndex = 1;
            saveButton.Text = "Save";
            saveButton.UseVisualStyleBackColor = true;
            saveButton.Click += SaveButton_Click;
            //
            // cancelButton
            //
            cancelButton.AutoSize = true;
            cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            cancelButton.Name = "cancelButton";
            cancelButton.TabIndex = 2;
            cancelButton.Text = "Cancel";
            cancelButton.UseVisualStyleBackColor = true;
            //
            // statusLabel
            //
            statusLabel.AutoEllipsis = true;
            statusLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            statusLabel.Margin = new System.Windows.Forms.Padding(3, 6, 3, 0);
            statusLabel.Name = "statusLabel";
            statusLabel.Size = new System.Drawing.Size(100, 20);
            statusLabel.TabIndex = 3;
            //
            // searchTimer
            //
            searchTimer.Interval = 300;
            searchTimer.Tick += SearchTimer_Tick;
            //
            // memberMenu
            //
            memberMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { editDrawingMenuItem, removeMenuItem });
            memberMenu.Name = "memberMenu";
            memberMenu.Opening += MemberMenu_Opening;
            //
            // editDrawingMenuItem
            //
            editDrawingMenuItem.Name = "editDrawingMenuItem";
            editDrawingMenuItem.Text = "Edit Drawing...";
            editDrawingMenuItem.Click += EditDrawingButton_Click;
            //
            // removeMenuItem
            //
            removeMenuItem.Name = "removeMenuItem";
            removeMenuItem.Text = "Remove";
            removeMenuItem.Click += RemoveButton_Click;
            //
            // EditDrawingSetForm
            //
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            CancelButton = cancelButton;
            ClientSize = new System.Drawing.Size(1264, 641);
            Controls.Add(layoutTable);
            Font = new System.Drawing.Font("Segoe UI", 9.75F);
            MinimizeBox = false;
            MinimumSize = new System.Drawing.Size(1000, 480);
            Name = "EditDrawingSetForm";
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "Drawing Set";
            FormClosing += EditDrawingSetForm_FormClosing;
            Shown += EditDrawingSetForm_Shown;
            layoutTable.ResumeLayout(false);
            layoutTable.PerformLayout();
            detailsTable.ResumeLayout(false);
            detailsTable.PerformLayout();
            splitContainer.Panel1.ResumeLayout(false);
            splitContainer.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitContainer).EndInit();
            splitContainer.ResumeLayout(false);
            libraryGroup.ResumeLayout(false);
            libraryTable.ResumeLayout(false);
            libraryTable.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)libraryGrid).EndInit();
            libraryNavTable.ResumeLayout(false);
            addTable.ResumeLayout(false);
            addTable.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)quantityBox).EndInit();
            memberGroup.ResumeLayout(false);
            memberTable.ResumeLayout(false);
            memberTable.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)memberGrid).EndInit();
            memberButtons.ResumeLayout(false);
            bottomTable.ResumeLayout(false);
            bottomTable.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel layoutTable;
        private System.Windows.Forms.TableLayoutPanel detailsTable;
        private System.Windows.Forms.Label nameLabel;
        private System.Windows.Forms.TextBox nameBox;
        private System.Windows.Forms.Label customerLabel;
        private System.Windows.Forms.TextBox customerBox;
        private System.Windows.Forms.Label notesLabel;
        private System.Windows.Forms.TextBox notesBox;
        private System.Windows.Forms.SplitContainer splitContainer;
        private System.Windows.Forms.GroupBox libraryGroup;
        private System.Windows.Forms.TableLayoutPanel libraryTable;
        private System.Windows.Forms.TextBox searchBox;
        private System.Windows.Forms.DataGridView libraryGrid;
        private System.Windows.Forms.DataGridViewTextBoxColumn libraryNameColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn libraryMaterialColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn libraryGradeColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn libraryThicknessColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn libraryCustomerColumn;
        private System.Windows.Forms.TableLayoutPanel libraryNavTable;
        private System.Windows.Forms.Button previousButton;
        private System.Windows.Forms.Button nextButton;
        private System.Windows.Forms.Label librarySummaryLabel;
        private System.Windows.Forms.TableLayoutPanel addTable;
        private System.Windows.Forms.Label quantityLabel;
        private System.Windows.Forms.NumericUpDown quantityBox;
        private System.Windows.Forms.Button addButton;
        private System.Windows.Forms.GroupBox memberGroup;
        private System.Windows.Forms.TableLayoutPanel memberTable;
        private System.Windows.Forms.DataGridView memberGrid;
        private System.Windows.Forms.DataGridViewTextBoxColumn drawingColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn materialColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn gradeColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn thicknessColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn unitsColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn cutTypeColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn quantityColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn warningColumn;
        private System.Windows.Forms.FlowLayoutPanel memberButtons;
        private System.Windows.Forms.Button removeButton;
        private System.Windows.Forms.Button upButton;
        private System.Windows.Forms.Button downButton;
        private System.Windows.Forms.TableLayoutPanel bottomTable;
        private System.Windows.Forms.Label summaryLabel;
        private System.Windows.Forms.Button saveButton;
        private System.Windows.Forms.Button cancelButton;
        private System.Windows.Forms.Label statusLabel;
        private System.Windows.Forms.Timer searchTimer;
        private System.Windows.Forms.Button editDrawingButton;
        private System.Windows.Forms.ContextMenuStrip memberMenu;
        private System.Windows.Forms.ToolStripMenuItem editDrawingMenuItem;
        private System.Windows.Forms.ToolStripMenuItem removeMenuItem;
    }
}
