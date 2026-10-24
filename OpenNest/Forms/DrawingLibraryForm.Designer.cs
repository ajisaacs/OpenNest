namespace OpenNest.Forms
{
    partial class DrawingLibraryForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                previewCancel?.Cancel();
                previewCancel?.Dispose();
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
            System.Windows.Forms.DataGridViewCellStyle editableQuantity = new System.Windows.Forms.DataGridViewCellStyle();
            rootTable = new System.Windows.Forms.TableLayoutPanel();
            tabControl = new System.Windows.Forms.TabControl();
            drawingsTab = new System.Windows.Forms.TabPage();
            nestTab = new System.Windows.Forms.TabPage();
            nestTable = new System.Windows.Forms.TableLayoutPanel();
            nestHintLabel = new System.Windows.Forms.Label();
            nestGrid = new System.Windows.Forms.DataGridView();
            nestNameColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            nestLibraryColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            nestRequiredColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            nestNestedColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            nestRemainingColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            nestSummaryLabel = new System.Windows.Forms.Label();
            footerPanel = new System.Windows.Forms.FlowLayoutPanel();
            layoutTable = new System.Windows.Forms.TableLayoutPanel();
            filterTable = new System.Windows.Forms.TableLayoutPanel();
            searchLabel = new System.Windows.Forms.Label();
            searchBox = new System.Windows.Forms.TextBox();
            showAllCheckBox = new System.Windows.Forms.CheckBox();
            filterLabel = new System.Windows.Forms.Label();
            splitContainer = new System.Windows.Forms.SplitContainer();
            browseTable = new System.Windows.Forms.TableLayoutPanel();
            drawingGrid = new System.Windows.Forms.DataGridView();
            nameColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            customerColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            materialColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            gradeColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            thicknessColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            cutTypeColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            cutFirstColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            revisionColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            savedColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            pagerTable = new System.Windows.Forms.TableLayoutPanel();
            previousButton = new System.Windows.Forms.Button();
            summaryLabel = new System.Windows.Forms.Label();
            nextButton = new System.Windows.Forms.Button();
            detailsTable = new System.Windows.Forms.TableLayoutPanel();
            previewPanel = new System.Windows.Forms.Panel();
            previewBox = new OpenNest.Controls.ShapePreviewControl();
            detailNameLabel = new System.Windows.Forms.Label();
            factsTable = new System.Windows.Forms.TableLayoutPanel();
            customerCaption = new System.Windows.Forms.Label();
            customerValue = new System.Windows.Forms.Label();
            materialCaption = new System.Windows.Forms.Label();
            materialValue = new System.Windows.Forms.Label();
            thicknessCaption = new System.Windows.Forms.Label();
            thicknessValue = new System.Windows.Forms.Label();
            cuttingCaption = new System.Windows.Forms.Label();
            cuttingValue = new System.Windows.Forms.Label();
            revisionCaption = new System.Windows.Forms.Label();
            revisionValue = new System.Windows.Forms.Label();
            notesGroup = new System.Windows.Forms.GroupBox();
            notesBox = new System.Windows.Forms.TextBox();
            actionTable = new System.Windows.Forms.TableLayoutPanel();
            quantityLabel = new System.Windows.Forms.Label();
            quantityBox = new System.Windows.Forms.NumericUpDown();
            addButton = new System.Windows.Forms.Button();
            editButton = new System.Windows.Forms.Button();
            closeButton = new System.Windows.Forms.Button();
            statusLabel = new System.Windows.Forms.Label();
            searchTimer = new System.Windows.Forms.Timer(components);
            previewTimer = new System.Windows.Forms.Timer(components);
            drawingMenu = new System.Windows.Forms.ContextMenuStrip(components);
            editMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            editSeparator = new System.Windows.Forms.ToolStripSeparator();
            usedInSetsMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            addToSetMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            releaseSeparator = new System.Windows.Forms.ToolStripSeparator();
            releaseMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            releasesMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            rootTable.SuspendLayout();
            tabControl.SuspendLayout();
            drawingsTab.SuspendLayout();
            nestTab.SuspendLayout();
            nestTable.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)nestGrid).BeginInit();
            footerPanel.SuspendLayout();
            layoutTable.SuspendLayout();
            filterTable.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)splitContainer).BeginInit();
            splitContainer.Panel1.SuspendLayout();
            splitContainer.Panel2.SuspendLayout();
            splitContainer.SuspendLayout();
            browseTable.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)drawingGrid).BeginInit();
            pagerTable.SuspendLayout();
            detailsTable.SuspendLayout();
            previewPanel.SuspendLayout();
            factsTable.SuspendLayout();
            notesGroup.SuspendLayout();
            actionTable.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)quantityBox).BeginInit();
            drawingMenu.SuspendLayout();
            SuspendLayout();
            //
            // rootTable
            //
            rootTable.ColumnCount = 1;
            rootTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            rootTable.Controls.Add(tabControl, 0, 0);
            rootTable.Controls.Add(footerPanel, 0, 1);
            rootTable.Dock = System.Windows.Forms.DockStyle.Fill;
            rootTable.Location = new System.Drawing.Point(0, 0);
            rootTable.Name = "rootTable";
            rootTable.Padding = new System.Windows.Forms.Padding(8);
            rootTable.RowCount = 2;
            rootTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            rootTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            rootTable.Size = new System.Drawing.Size(1184, 721);
            rootTable.TabIndex = 0;
            //
            // tabControl
            //
            tabControl.Controls.Add(drawingsTab);
            tabControl.Controls.Add(nestTab);
            tabControl.Dock = System.Windows.Forms.DockStyle.Fill;
            tabControl.Margin = new System.Windows.Forms.Padding(0);
            tabControl.Name = "tabControl";
            tabControl.SelectedIndex = 0;
            tabControl.TabIndex = 0;
            //
            // drawingsTab
            //
            drawingsTab.Controls.Add(layoutTable);
            drawingsTab.Name = "drawingsTab";
            drawingsTab.Text = "Drawings";
            drawingsTab.UseVisualStyleBackColor = true;
            //
            // nestTab
            //
            nestTab.Controls.Add(nestTable);
            nestTab.Name = "nestTab";
            nestTab.Text = "In This Nest";
            nestTab.UseVisualStyleBackColor = true;
            //
            // nestTable
            //
            nestTable.ColumnCount = 1;
            nestTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            nestTable.Controls.Add(nestHintLabel, 0, 0);
            nestTable.Controls.Add(nestGrid, 0, 1);
            nestTable.Controls.Add(nestSummaryLabel, 0, 2);
            nestTable.Dock = System.Windows.Forms.DockStyle.Fill;
            nestTable.Name = "nestTable";
            nestTable.Padding = new System.Windows.Forms.Padding(8);
            nestTable.RowCount = 3;
            nestTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            nestTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            nestTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            nestTable.TabIndex = 0;
            //
            // nestHintLabel
            //
            nestHintLabel.AutoSize = true;
            nestHintLabel.ForeColor = System.Drawing.SystemColors.GrayText;
            nestHintLabel.Margin = new System.Windows.Forms.Padding(3, 0, 3, 6);
            nestHintLabel.Name = "nestHintLabel";
            nestHintLabel.TabIndex = 0;
            nestHintLabel.Text = "The parts this nest needs. Edit Required to change a quantity; it applies to the nest right away.";
            //
            // nestGrid
            //
            nestGrid.AllowUserToAddRows = false;
            nestGrid.AllowUserToDeleteRows = false;
            nestGrid.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] { nestNameColumn, nestLibraryColumn, nestRequiredColumn, nestNestedColumn, nestRemainingColumn });
            nestGrid.Dock = System.Windows.Forms.DockStyle.Fill;
            nestGrid.EditMode = System.Windows.Forms.DataGridViewEditMode.EditOnKeystrokeOrF2;
            nestGrid.Margin = new System.Windows.Forms.Padding(0);
            nestGrid.MultiSelect = false;
            nestGrid.Name = "nestGrid";
            nestGrid.RowHeadersVisible = false;
            nestGrid.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.CellSelect;
            nestGrid.StandardTab = true;
            nestGrid.TabIndex = 1;
            nestGrid.CellClick += NestGrid_CellClick;
            nestGrid.CellValidating += NestGrid_CellValidating;
            nestGrid.CellValueChanged += NestGrid_CellValueChanged;
            //
            // nestNameColumn
            //
            nestNameColumn.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            nestNameColumn.HeaderText = "Name";
            nestNameColumn.MinimumWidth = 160;
            nestNameColumn.Name = "nestNameColumn";
            nestNameColumn.ReadOnly = true;
            //
            // nestLibraryColumn
            //
            nestLibraryColumn.HeaderText = "Library";
            nestLibraryColumn.Name = "nestLibraryColumn";
            nestLibraryColumn.ReadOnly = true;
            nestLibraryColumn.Width = 160;
            //
            // nestRequiredColumn
            //
            editableQuantity.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleRight;
            editableQuantity.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Bold);
            nestRequiredColumn.DefaultCellStyle = editableQuantity;
            nestRequiredColumn.HeaderText = "Required";
            nestRequiredColumn.Name = "nestRequiredColumn";
            nestRequiredColumn.ValueType = typeof(int);
            nestRequiredColumn.Width = 90;
            //
            // nestNestedColumn
            //
            nestNestedColumn.DefaultCellStyle = rightAligned;
            nestNestedColumn.HeaderText = "Nested";
            nestNestedColumn.Name = "nestNestedColumn";
            nestNestedColumn.ReadOnly = true;
            nestNestedColumn.Width = 80;
            //
            // nestRemainingColumn
            //
            nestRemainingColumn.DefaultCellStyle = rightAligned;
            nestRemainingColumn.HeaderText = "Remaining";
            nestRemainingColumn.Name = "nestRemainingColumn";
            nestRemainingColumn.ReadOnly = true;
            nestRemainingColumn.Width = 90;
            //
            // nestSummaryLabel
            //
            nestSummaryLabel.AutoEllipsis = true;
            nestSummaryLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            nestSummaryLabel.ForeColor = System.Drawing.SystemColors.GrayText;
            nestSummaryLabel.Margin = new System.Windows.Forms.Padding(3, 6, 3, 0);
            nestSummaryLabel.Name = "nestSummaryLabel";
            nestSummaryLabel.Size = new System.Drawing.Size(100, 20);
            nestSummaryLabel.TabIndex = 2;
            //
            // footerPanel
            //
            footerPanel.AutoSize = true;
            footerPanel.Controls.Add(closeButton);
            footerPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            footerPanel.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
            footerPanel.Margin = new System.Windows.Forms.Padding(0, 6, 0, 0);
            footerPanel.Name = "footerPanel";
            footerPanel.TabIndex = 1;
            //
            // layoutTable
            //
            layoutTable.ColumnCount = 1;
            layoutTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            layoutTable.Controls.Add(filterTable, 0, 0);
            layoutTable.Controls.Add(splitContainer, 0, 1);
            layoutTable.Controls.Add(statusLabel, 0, 2);
            layoutTable.Dock = System.Windows.Forms.DockStyle.Fill;
            layoutTable.Margin = new System.Windows.Forms.Padding(0);
            layoutTable.Padding = new System.Windows.Forms.Padding(8);
            layoutTable.Name = "layoutTable";
            layoutTable.RowCount = 3;
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.TabIndex = 0;
            //
            // filterTable
            //
            filterTable.AutoSize = true;
            filterTable.ColumnCount = 3;
            filterTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            filterTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            filterTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            filterTable.Controls.Add(searchLabel, 0, 0);
            filterTable.Controls.Add(searchBox, 1, 0);
            filterTable.Controls.Add(showAllCheckBox, 2, 0);
            filterTable.Controls.Add(filterLabel, 1, 1);
            filterTable.SetColumnSpan(filterLabel, 2);
            filterTable.Dock = System.Windows.Forms.DockStyle.Fill;
            filterTable.Margin = new System.Windows.Forms.Padding(0, 0, 0, 6);
            filterTable.Name = "filterTable";
            filterTable.RowCount = 2;
            filterTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
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
            searchBox.PlaceholderText = "Type a drawing name, customer or note";
            searchBox.TabIndex = 1;
            searchBox.TextChanged += SearchBox_TextChanged;
            //
            // showAllCheckBox
            //
            showAllCheckBox.Anchor = System.Windows.Forms.AnchorStyles.Left;
            showAllCheckBox.AutoSize = true;
            showAllCheckBox.Margin = new System.Windows.Forms.Padding(12, 3, 3, 3);
            showAllCheckBox.Name = "showAllCheckBox";
            showAllCheckBox.TabIndex = 2;
            showAllCheckBox.Text = "Show all materials";
            showAllCheckBox.UseVisualStyleBackColor = true;
            showAllCheckBox.CheckedChanged += ShowAllCheckBox_CheckedChanged;
            //
            // filterLabel
            //
            filterLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            filterLabel.AutoSize = true;
            filterLabel.ForeColor = System.Drawing.SystemColors.GrayText;
            filterLabel.Margin = new System.Windows.Forms.Padding(3, 2, 3, 0);
            filterLabel.Name = "filterLabel";
            filterLabel.TabIndex = 3;
            //
            // splitContainer
            //
            splitContainer.Dock = System.Windows.Forms.DockStyle.Fill;
            splitContainer.FixedPanel = System.Windows.Forms.FixedPanel.Panel2;
            splitContainer.Margin = new System.Windows.Forms.Padding(0);
            splitContainer.Name = "splitContainer";
            splitContainer.Panel1.Controls.Add(browseTable);
            splitContainer.Panel2.Controls.Add(detailsTable);
            splitContainer.TabIndex = 1;
            //
            // browseTable
            //
            browseTable.ColumnCount = 1;
            browseTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            browseTable.Controls.Add(drawingGrid, 0, 0);
            browseTable.Controls.Add(pagerTable, 0, 1);
            browseTable.Dock = System.Windows.Forms.DockStyle.Fill;
            browseTable.Margin = new System.Windows.Forms.Padding(0);
            browseTable.Name = "browseTable";
            browseTable.RowCount = 2;
            browseTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            browseTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            browseTable.TabIndex = 0;
            //
            // drawingGrid
            //
            drawingGrid.AllowUserToAddRows = false;
            drawingGrid.AllowUserToDeleteRows = false;
            drawingGrid.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] { nameColumn, customerColumn, materialColumn, gradeColumn, thicknessColumn, cutTypeColumn, cutFirstColumn, revisionColumn, savedColumn });
            drawingGrid.Dock = System.Windows.Forms.DockStyle.Fill;
            drawingGrid.Margin = new System.Windows.Forms.Padding(0);
            drawingGrid.MultiSelect = false;
            drawingGrid.Name = "drawingGrid";
            drawingGrid.ReadOnly = true;
            drawingGrid.RowHeadersVisible = false;
            drawingGrid.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            drawingGrid.StandardTab = true;
            drawingGrid.TabIndex = 0;
            drawingGrid.CellDoubleClick += DrawingGrid_CellDoubleClick;
            drawingGrid.CellMouseDown += DrawingGrid_CellMouseDown;
            drawingGrid.ColumnHeaderMouseClick += DrawingGrid_ColumnHeaderMouseClick;
            drawingGrid.SelectionChanged += DrawingGrid_SelectionChanged;
            //
            // nameColumn
            //
            nameColumn.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            nameColumn.HeaderText = "Name";
            nameColumn.MinimumWidth = 160;
            nameColumn.Name = "nameColumn";
            nameColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Programmatic;
            //
            // customerColumn
            //
            customerColumn.HeaderText = "Customer";
            customerColumn.Name = "customerColumn";
            customerColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Programmatic;
            customerColumn.Width = 150;
            //
            // materialColumn
            //
            materialColumn.HeaderText = "Material";
            materialColumn.Name = "materialColumn";
            materialColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Programmatic;
            materialColumn.Width = 120;
            //
            // gradeColumn
            //
            gradeColumn.HeaderText = "Grade";
            gradeColumn.Name = "gradeColumn";
            gradeColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Programmatic;
            gradeColumn.Width = 90;
            //
            // thicknessColumn
            //
            rightAligned.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleRight;
            thicknessColumn.DefaultCellStyle = rightAligned;
            thicknessColumn.HeaderText = "Thickness";
            thicknessColumn.Name = "thicknessColumn";
            thicknessColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Programmatic;
            thicknessColumn.Width = 80;
            //
            // cutTypeColumn
            //
            cutTypeColumn.HeaderText = "Cut type";
            cutTypeColumn.Name = "cutTypeColumn";
            cutTypeColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Programmatic;
            cutTypeColumn.Width = 85;
            //
            // cutFirstColumn
            //
            cutFirstColumn.HeaderText = "Cut first";
            cutFirstColumn.Name = "cutFirstColumn";
            cutFirstColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            cutFirstColumn.Width = 65;
            //
            // revisionColumn
            //
            revisionColumn.DefaultCellStyle = rightAligned;
            revisionColumn.HeaderText = "Rev";
            revisionColumn.Name = "revisionColumn";
            revisionColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Programmatic;
            revisionColumn.Width = 50;
            //
            // savedColumn
            //
            savedColumn.HeaderText = "Saved";
            savedColumn.Name = "savedColumn";
            savedColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Programmatic;
            savedColumn.Width = 130;
            //
            // pagerTable
            //
            pagerTable.AutoSize = true;
            pagerTable.ColumnCount = 3;
            pagerTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            pagerTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            pagerTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            pagerTable.Controls.Add(previousButton, 0, 0);
            pagerTable.Controls.Add(summaryLabel, 1, 0);
            pagerTable.Controls.Add(nextButton, 2, 0);
            pagerTable.Dock = System.Windows.Forms.DockStyle.Fill;
            pagerTable.Margin = new System.Windows.Forms.Padding(0, 6, 0, 0);
            pagerTable.Name = "pagerTable";
            pagerTable.RowCount = 1;
            pagerTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            pagerTable.TabIndex = 1;
            //
            // previousButton
            //
            previousButton.AutoSize = true;
            previousButton.Margin = new System.Windows.Forms.Padding(0, 3, 3, 3);
            previousButton.Name = "previousButton";
            previousButton.TabIndex = 0;
            previousButton.Text = "< Previous";
            previousButton.UseVisualStyleBackColor = true;
            previousButton.Click += PreviousButton_Click;
            //
            // summaryLabel
            //
            summaryLabel.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            summaryLabel.AutoEllipsis = true;
            summaryLabel.ForeColor = System.Drawing.SystemColors.GrayText;
            summaryLabel.Name = "summaryLabel";
            summaryLabel.TabIndex = 1;
            summaryLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            //
            // nextButton
            //
            nextButton.AutoSize = true;
            nextButton.Margin = new System.Windows.Forms.Padding(3, 3, 0, 3);
            nextButton.Name = "nextButton";
            nextButton.TabIndex = 2;
            nextButton.Text = "Next >";
            nextButton.UseVisualStyleBackColor = true;
            nextButton.Click += NextButton_Click;
            //
            // detailsTable
            //
            detailsTable.ColumnCount = 1;
            detailsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            detailsTable.Controls.Add(previewPanel, 0, 0);
            detailsTable.Controls.Add(detailNameLabel, 0, 1);
            detailsTable.Controls.Add(factsTable, 0, 2);
            detailsTable.Controls.Add(notesGroup, 0, 3);
            detailsTable.Controls.Add(actionTable, 0, 4);
            detailsTable.Dock = System.Windows.Forms.DockStyle.Fill;
            detailsTable.Margin = new System.Windows.Forms.Padding(0);
            detailsTable.Name = "detailsTable";
            detailsTable.Padding = new System.Windows.Forms.Padding(6, 0, 0, 0);
            detailsTable.RowCount = 5;
            detailsTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            detailsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            detailsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            detailsTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 104F));
            detailsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            detailsTable.TabIndex = 0;
            //
            // previewPanel
            //
            previewPanel.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            previewPanel.Controls.Add(previewBox);
            previewPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            previewPanel.Margin = new System.Windows.Forms.Padding(0, 0, 0, 6);
            previewPanel.MinimumSize = new System.Drawing.Size(0, 160);
            previewPanel.Name = "previewPanel";
            previewPanel.TabIndex = 0;
            //
            // previewBox
            //
            previewBox.ActiveWorkArea = null;
            previewBox.AllowPan = false;
            previewBox.AllowSelect = false;
            previewBox.AllowZoom = false;
            previewBox.BackColor = System.Drawing.Color.White;
            previewBox.Cursor = System.Windows.Forms.Cursors.Default;
            previewBox.Dock = System.Windows.Forms.DockStyle.Fill;
            previewBox.Name = "previewBox";
            previewBox.TabIndex = 0;
            previewBox.TabStop = false;
            //
            // detailNameLabel
            //
            detailNameLabel.AutoEllipsis = true;
            detailNameLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            detailNameLabel.Font = new System.Drawing.Font("Segoe UI Semibold", 12F, System.Drawing.FontStyle.Bold);
            detailNameLabel.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
            detailNameLabel.Name = "detailNameLabel";
            detailNameLabel.Size = new System.Drawing.Size(100, 26);
            detailNameLabel.TabIndex = 1;
            detailNameLabel.Text = "No drawing selected";
            //
            // factsTable
            //
            factsTable.AutoSize = true;
            factsTable.ColumnCount = 2;
            factsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            factsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            factsTable.Controls.Add(customerCaption, 0, 0);
            factsTable.Controls.Add(customerValue, 1, 0);
            factsTable.Controls.Add(materialCaption, 0, 1);
            factsTable.Controls.Add(materialValue, 1, 1);
            factsTable.Controls.Add(thicknessCaption, 0, 2);
            factsTable.Controls.Add(thicknessValue, 1, 2);
            factsTable.Controls.Add(cuttingCaption, 0, 3);
            factsTable.Controls.Add(cuttingValue, 1, 3);
            factsTable.Controls.Add(revisionCaption, 0, 4);
            factsTable.Controls.Add(revisionValue, 1, 4);
            factsTable.Dock = System.Windows.Forms.DockStyle.Fill;
            factsTable.Margin = new System.Windows.Forms.Padding(0, 0, 0, 6);
            factsTable.Name = "factsTable";
            factsTable.RowCount = 5;
            factsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            factsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            factsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            factsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            factsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            factsTable.TabIndex = 2;
            //
            // customerCaption
            //
            customerCaption.AutoSize = true;
            customerCaption.ForeColor = System.Drawing.SystemColors.GrayText;
            customerCaption.Margin = new System.Windows.Forms.Padding(0, 2, 12, 2);
            customerCaption.Name = "customerCaption";
            customerCaption.Text = "Customer";
            //
            // customerValue
            //
            customerValue.AutoEllipsis = true;
            customerValue.Dock = System.Windows.Forms.DockStyle.Fill;
            customerValue.Margin = new System.Windows.Forms.Padding(0, 2, 0, 2);
            customerValue.Name = "customerValue";
            customerValue.Size = new System.Drawing.Size(100, 18);
            //
            // materialCaption
            //
            materialCaption.AutoSize = true;
            materialCaption.ForeColor = System.Drawing.SystemColors.GrayText;
            materialCaption.Margin = new System.Windows.Forms.Padding(0, 2, 12, 2);
            materialCaption.Name = "materialCaption";
            materialCaption.Text = "Material";
            //
            // materialValue
            //
            materialValue.AutoEllipsis = true;
            materialValue.Dock = System.Windows.Forms.DockStyle.Fill;
            materialValue.Margin = new System.Windows.Forms.Padding(0, 2, 0, 2);
            materialValue.Name = "materialValue";
            materialValue.Size = new System.Drawing.Size(100, 18);
            //
            // thicknessCaption
            //
            thicknessCaption.AutoSize = true;
            thicknessCaption.ForeColor = System.Drawing.SystemColors.GrayText;
            thicknessCaption.Margin = new System.Windows.Forms.Padding(0, 2, 12, 2);
            thicknessCaption.Name = "thicknessCaption";
            thicknessCaption.Text = "Thickness";
            //
            // thicknessValue
            //
            thicknessValue.AutoEllipsis = true;
            thicknessValue.Dock = System.Windows.Forms.DockStyle.Fill;
            thicknessValue.Margin = new System.Windows.Forms.Padding(0, 2, 0, 2);
            thicknessValue.Name = "thicknessValue";
            thicknessValue.Size = new System.Drawing.Size(100, 18);
            //
            // cuttingCaption
            //
            cuttingCaption.AutoSize = true;
            cuttingCaption.ForeColor = System.Drawing.SystemColors.GrayText;
            cuttingCaption.Margin = new System.Windows.Forms.Padding(0, 2, 12, 2);
            cuttingCaption.Name = "cuttingCaption";
            cuttingCaption.Text = "Cutting";
            //
            // cuttingValue
            //
            cuttingValue.AutoEllipsis = true;
            cuttingValue.Dock = System.Windows.Forms.DockStyle.Fill;
            cuttingValue.Margin = new System.Windows.Forms.Padding(0, 2, 0, 2);
            cuttingValue.Name = "cuttingValue";
            cuttingValue.Size = new System.Drawing.Size(100, 18);
            //
            // revisionCaption
            //
            revisionCaption.AutoSize = true;
            revisionCaption.ForeColor = System.Drawing.SystemColors.GrayText;
            revisionCaption.Margin = new System.Windows.Forms.Padding(0, 2, 12, 2);
            revisionCaption.Name = "revisionCaption";
            revisionCaption.Text = "Revision";
            //
            // revisionValue
            //
            revisionValue.AutoEllipsis = true;
            revisionValue.Dock = System.Windows.Forms.DockStyle.Fill;
            revisionValue.Margin = new System.Windows.Forms.Padding(0, 2, 0, 2);
            revisionValue.Name = "revisionValue";
            revisionValue.Size = new System.Drawing.Size(100, 18);
            //
            // notesGroup
            //
            notesGroup.Controls.Add(notesBox);
            notesGroup.Dock = System.Windows.Forms.DockStyle.Fill;
            notesGroup.Margin = new System.Windows.Forms.Padding(0, 0, 0, 6);
            notesGroup.Name = "notesGroup";
            notesGroup.TabIndex = 3;
            notesGroup.TabStop = false;
            notesGroup.Text = "Notes";
            //
            // notesBox
            //
            notesBox.BorderStyle = System.Windows.Forms.BorderStyle.None;
            notesBox.Dock = System.Windows.Forms.DockStyle.Fill;
            notesBox.Multiline = true;
            notesBox.Name = "notesBox";
            notesBox.PlaceholderText = "No notes";
            notesBox.ReadOnly = true;
            notesBox.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            notesBox.TabIndex = 0;
            notesBox.TabStop = false;
            //
            // actionTable
            //
            actionTable.AutoSize = true;
            actionTable.ColumnCount = 3;
            actionTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            actionTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            actionTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            actionTable.Controls.Add(quantityLabel, 0, 0);
            actionTable.Controls.Add(quantityBox, 1, 0);
            actionTable.Controls.Add(addButton, 2, 0);
            actionTable.Controls.Add(editButton, 2, 1);
            actionTable.Dock = System.Windows.Forms.DockStyle.Fill;
            actionTable.Margin = new System.Windows.Forms.Padding(0);
            actionTable.Name = "actionTable";
            actionTable.RowCount = 2;
            actionTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            actionTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            actionTable.TabIndex = 4;
            //
            // quantityLabel
            //
            quantityLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            quantityLabel.AutoSize = true;
            quantityLabel.Margin = new System.Windows.Forms.Padding(0, 3, 3, 3);
            quantityLabel.Name = "quantityLabel";
            quantityLabel.TabIndex = 0;
            quantityLabel.Text = "Quantity:";
            //
            // quantityBox
            //
            quantityBox.Anchor = System.Windows.Forms.AnchorStyles.Left;
            quantityBox.Maximum = new decimal(new int[] { 99999, 0, 0, 0 });
            quantityBox.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            quantityBox.Name = "quantityBox";
            quantityBox.Size = new System.Drawing.Size(70, 25);
            quantityBox.TabIndex = 1;
            quantityBox.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            quantityBox.Value = new decimal(new int[] { 1, 0, 0, 0 });
            //
            // addButton
            //
            addButton.Dock = System.Windows.Forms.DockStyle.Fill;
            addButton.Enabled = false;
            addButton.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Bold);
            addButton.Margin = new System.Windows.Forms.Padding(9, 3, 0, 3);
            addButton.MinimumSize = new System.Drawing.Size(0, 32);
            addButton.Name = "addButton";
            addButton.TabIndex = 2;
            addButton.Text = "Add to Nest";
            addButton.UseVisualStyleBackColor = true;
            addButton.Click += AddButton_Click;
            //
            // editButton
            //
            editButton.AutoSize = true;
            editButton.Anchor = System.Windows.Forms.AnchorStyles.Right;
            editButton.Enabled = false;
            editButton.Margin = new System.Windows.Forms.Padding(9, 3, 0, 0);
            editButton.Name = "editButton";
            editButton.TabIndex = 3;
            editButton.Text = "Edit Drawing...";
            editButton.UseVisualStyleBackColor = true;
            editButton.Click += EditButton_Click;
            //
            // closeButton
            //
            closeButton.AutoSize = true;
            closeButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            closeButton.Name = "closeButton";
            closeButton.TabIndex = 0;
            closeButton.Text = "Close";
            closeButton.UseVisualStyleBackColor = true;
            //
            // statusLabel
            //
            statusLabel.AutoEllipsis = true;
            statusLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            statusLabel.Margin = new System.Windows.Forms.Padding(3, 6, 3, 0);
            statusLabel.Name = "statusLabel";
            statusLabel.Size = new System.Drawing.Size(100, 20);
            statusLabel.TabIndex = 2;
            //
            // searchTimer
            //
            searchTimer.Interval = 300;
            searchTimer.Tick += SearchTimer_Tick;
            //
            // previewTimer
            //
            previewTimer.Interval = 150;
            previewTimer.Tick += PreviewTimer_Tick;
            //
            // drawingMenu
            //
            drawingMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { editMenuItem, editSeparator, usedInSetsMenuItem, addToSetMenuItem, releaseSeparator, releaseMenuItem, releasesMenuItem });
            drawingMenu.Name = "drawingMenu";
            drawingMenu.Opening += DrawingMenu_Opening;
            //
            // editMenuItem
            //
            editMenuItem.Name = "editMenuItem";
            editMenuItem.Text = "Edit Drawing...";
            editMenuItem.Click += EditButton_Click;
            //
            // editSeparator
            //
            editSeparator.Name = "editSeparator";
            //
            // usedInSetsMenuItem
            //
            usedInSetsMenuItem.Name = "usedInSetsMenuItem";
            usedInSetsMenuItem.Text = "Used in Sets...";
            usedInSetsMenuItem.Click += UsedInSetsMenuItem_Click;
            //
            // addToSetMenuItem
            //
            addToSetMenuItem.Name = "addToSetMenuItem";
            addToSetMenuItem.Text = "Add to Set...";
            addToSetMenuItem.Click += AddToSetMenuItem_Click;
            //
            // releaseSeparator
            //
            releaseSeparator.Name = "releaseSeparator";
            releaseSeparator.Visible = false;
            //
            // releaseMenuItem
            //
            releaseMenuItem.Name = "releaseMenuItem";
            releaseMenuItem.Text = "Release...";
            releaseMenuItem.ToolTipText = "Release this drawing on its own, at the entered quantity, with any other drawings or sets";
            releaseMenuItem.Visible = false;
            releaseMenuItem.Click += ReleaseMenuItem_Click;
            //
            // releasesMenuItem
            //
            releasesMenuItem.Name = "releasesMenuItem";
            releasesMenuItem.Text = "Releases...";
            releasesMenuItem.ToolTipText = "The releases that had this drawing on its own";
            releasesMenuItem.Click += ReleasesMenuItem_Click;
            //
            // DrawingLibraryForm
            //
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            CancelButton = closeButton;
            ClientSize = new System.Drawing.Size(1184, 721);
            Controls.Add(rootTable);
            Font = new System.Drawing.Font("Segoe UI", 9.75F);
            MinimizeBox = false;
            MinimumSize = new System.Drawing.Size(860, 520);
            Name = "DrawingLibraryForm";
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            Text = "Add from Drawing Library";
            Shown += DrawingLibraryForm_Shown;
            rootTable.ResumeLayout(false);
            rootTable.PerformLayout();
            tabControl.ResumeLayout(false);
            drawingsTab.ResumeLayout(false);
            nestTab.ResumeLayout(false);
            nestTable.ResumeLayout(false);
            nestTable.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)nestGrid).EndInit();
            footerPanel.ResumeLayout(false);
            footerPanel.PerformLayout();
            layoutTable.ResumeLayout(false);
            layoutTable.PerformLayout();
            filterTable.ResumeLayout(false);
            filterTable.PerformLayout();
            splitContainer.Panel1.ResumeLayout(false);
            splitContainer.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitContainer).EndInit();
            splitContainer.ResumeLayout(false);
            browseTable.ResumeLayout(false);
            browseTable.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)drawingGrid).EndInit();
            pagerTable.ResumeLayout(false);
            pagerTable.PerformLayout();
            detailsTable.ResumeLayout(false);
            detailsTable.PerformLayout();
            previewPanel.ResumeLayout(false);
            factsTable.ResumeLayout(false);
            factsTable.PerformLayout();
            notesGroup.ResumeLayout(false);
            notesGroup.PerformLayout();
            actionTable.ResumeLayout(false);
            actionTable.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)quantityBox).EndInit();
            drawingMenu.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel rootTable;
        private System.Windows.Forms.TabControl tabControl;
        private System.Windows.Forms.TabPage drawingsTab;
        private System.Windows.Forms.TabPage nestTab;
        private System.Windows.Forms.TableLayoutPanel nestTable;
        private System.Windows.Forms.Label nestHintLabel;
        private System.Windows.Forms.DataGridView nestGrid;
        private System.Windows.Forms.DataGridViewTextBoxColumn nestNameColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn nestLibraryColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn nestRequiredColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn nestNestedColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn nestRemainingColumn;
        private System.Windows.Forms.Label nestSummaryLabel;
        private System.Windows.Forms.FlowLayoutPanel footerPanel;
        private System.Windows.Forms.TableLayoutPanel layoutTable;
        private System.Windows.Forms.TableLayoutPanel filterTable;
        private System.Windows.Forms.Label searchLabel;
        private System.Windows.Forms.TextBox searchBox;
        private System.Windows.Forms.CheckBox showAllCheckBox;
        private System.Windows.Forms.Label filterLabel;
        private System.Windows.Forms.SplitContainer splitContainer;
        private System.Windows.Forms.TableLayoutPanel browseTable;
        private System.Windows.Forms.DataGridView drawingGrid;
        private System.Windows.Forms.DataGridViewTextBoxColumn nameColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn customerColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn materialColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn gradeColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn thicknessColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn cutTypeColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn cutFirstColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn revisionColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn savedColumn;
        private System.Windows.Forms.TableLayoutPanel pagerTable;
        private System.Windows.Forms.Button previousButton;
        private System.Windows.Forms.Label summaryLabel;
        private System.Windows.Forms.Button nextButton;
        private System.Windows.Forms.TableLayoutPanel detailsTable;
        private System.Windows.Forms.Panel previewPanel;
        private OpenNest.Controls.ShapePreviewControl previewBox;
        private System.Windows.Forms.Label detailNameLabel;
        private System.Windows.Forms.TableLayoutPanel factsTable;
        private System.Windows.Forms.Label customerCaption;
        private System.Windows.Forms.Label customerValue;
        private System.Windows.Forms.Label materialCaption;
        private System.Windows.Forms.Label materialValue;
        private System.Windows.Forms.Label thicknessCaption;
        private System.Windows.Forms.Label thicknessValue;
        private System.Windows.Forms.Label cuttingCaption;
        private System.Windows.Forms.Label cuttingValue;
        private System.Windows.Forms.Label revisionCaption;
        private System.Windows.Forms.Label revisionValue;
        private System.Windows.Forms.GroupBox notesGroup;
        private System.Windows.Forms.TextBox notesBox;
        private System.Windows.Forms.TableLayoutPanel actionTable;
        private System.Windows.Forms.Label quantityLabel;
        private System.Windows.Forms.NumericUpDown quantityBox;
        private System.Windows.Forms.Button addButton;
        private System.Windows.Forms.Button editButton;
        private System.Windows.Forms.Button closeButton;
        private System.Windows.Forms.Label statusLabel;
        private System.Windows.Forms.Timer searchTimer;
        private System.Windows.Forms.Timer previewTimer;
        private System.Windows.Forms.ContextMenuStrip drawingMenu;
        private System.Windows.Forms.ToolStripMenuItem editMenuItem;
        private System.Windows.Forms.ToolStripSeparator editSeparator;
        private System.Windows.Forms.ToolStripMenuItem usedInSetsMenuItem;
        private System.Windows.Forms.ToolStripMenuItem addToSetMenuItem;
        private System.Windows.Forms.ToolStripSeparator releaseSeparator;
        private System.Windows.Forms.ToolStripMenuItem releaseMenuItem;
        private System.Windows.Forms.ToolStripMenuItem releasesMenuItem;
    }
}
