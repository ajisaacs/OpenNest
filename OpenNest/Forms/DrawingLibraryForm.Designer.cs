namespace OpenNest.Forms
{
    partial class DrawingLibraryForm
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
            rootTable = new System.Windows.Forms.TableLayoutPanel();
            tabControl = new System.Windows.Forms.TabControl();
            drawingsTab = new System.Windows.Forms.TabPage();
            setsTab = new System.Windows.Forms.TabPage();
            setBrowser = new OpenNest.Controls.DrawingSetBrowser();
            footerPanel = new System.Windows.Forms.FlowLayoutPanel();
            layoutTable = new System.Windows.Forms.TableLayoutPanel();
            filterTable = new System.Windows.Forms.TableLayoutPanel();
            searchLabel = new System.Windows.Forms.Label();
            searchBox = new System.Windows.Forms.TextBox();
            showAllCheckBox = new System.Windows.Forms.CheckBox();
            filterLabel = new System.Windows.Forms.Label();
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
            notesGroup = new System.Windows.Forms.GroupBox();
            notesBox = new System.Windows.Forms.TextBox();
            bottomTable = new System.Windows.Forms.TableLayoutPanel();
            previousButton = new System.Windows.Forms.Button();
            nextButton = new System.Windows.Forms.Button();
            summaryLabel = new System.Windows.Forms.Label();
            quantityLabel = new System.Windows.Forms.Label();
            quantityBox = new System.Windows.Forms.NumericUpDown();
            editButton = new System.Windows.Forms.Button();
            addButton = new System.Windows.Forms.Button();
            closeButton = new System.Windows.Forms.Button();
            statusLabel = new System.Windows.Forms.Label();
            searchTimer = new System.Windows.Forms.Timer(components);
            drawingMenu = new System.Windows.Forms.ContextMenuStrip(components);
            usedInSetsMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            addToSetMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            rootTable.SuspendLayout();
            tabControl.SuspendLayout();
            drawingsTab.SuspendLayout();
            setsTab.SuspendLayout();
            footerPanel.SuspendLayout();
            layoutTable.SuspendLayout();
            filterTable.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)drawingGrid).BeginInit();
            notesGroup.SuspendLayout();
            bottomTable.SuspendLayout();
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
            rootTable.Size = new System.Drawing.Size(984, 641);
            rootTable.TabIndex = 0;
            //
            // tabControl
            //
            tabControl.Controls.Add(drawingsTab);
            tabControl.Controls.Add(setsTab);
            tabControl.Dock = System.Windows.Forms.DockStyle.Fill;
            tabControl.Margin = new System.Windows.Forms.Padding(0);
            tabControl.Name = "tabControl";
            tabControl.SelectedIndex = 0;
            tabControl.TabIndex = 0;
            tabControl.SelectedIndexChanged += TabControl_SelectedIndexChanged;
            //
            // drawingsTab
            //
            drawingsTab.Controls.Add(layoutTable);
            drawingsTab.Name = "drawingsTab";
            drawingsTab.Text = "Drawings";
            drawingsTab.UseVisualStyleBackColor = true;
            //
            // setsTab
            //
            setsTab.Controls.Add(setBrowser);
            setsTab.Name = "setsTab";
            setsTab.Padding = new System.Windows.Forms.Padding(8);
            setsTab.Text = "Sets";
            setsTab.UseVisualStyleBackColor = true;
            //
            // setBrowser
            //
            setBrowser.Dock = System.Windows.Forms.DockStyle.Fill;
            setBrowser.Name = "setBrowser";
            setBrowser.TabIndex = 0;
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
            layoutTable.Controls.Add(drawingGrid, 0, 1);
            layoutTable.Controls.Add(notesGroup, 0, 2);
            layoutTable.Controls.Add(bottomTable, 0, 3);
            layoutTable.Controls.Add(statusLabel, 0, 4);
            layoutTable.Dock = System.Windows.Forms.DockStyle.Fill;
            layoutTable.Location = new System.Drawing.Point(0, 0);
            layoutTable.Name = "layoutTable";
            layoutTable.Padding = new System.Windows.Forms.Padding(8);
            layoutTable.RowCount = 5;
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 96F));
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.Size = new System.Drawing.Size(984, 601);
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
            filterTable.Controls.Add(showAllCheckBox, 2, 0);
            filterTable.Controls.Add(filterLabel, 3, 0);
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
            // showAllCheckBox
            //
            showAllCheckBox.Anchor = System.Windows.Forms.AnchorStyles.Left;
            showAllCheckBox.AutoSize = true;
            showAllCheckBox.Margin = new System.Windows.Forms.Padding(12, 3, 3, 3);
            showAllCheckBox.Name = "showAllCheckBox";
            showAllCheckBox.TabIndex = 2;
            showAllCheckBox.Text = "Show all";
            showAllCheckBox.UseVisualStyleBackColor = true;
            showAllCheckBox.CheckedChanged += ShowAllCheckBox_CheckedChanged;
            //
            // filterLabel
            //
            filterLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            filterLabel.AutoSize = true;
            filterLabel.ForeColor = System.Drawing.SystemColors.GrayText;
            filterLabel.Name = "filterLabel";
            filterLabel.TabIndex = 3;
            //
            // drawingGrid
            //
            drawingGrid.AllowUserToAddRows = false;
            drawingGrid.AllowUserToDeleteRows = false;
            drawingGrid.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] { nameColumn, customerColumn, materialColumn, gradeColumn, thicknessColumn, cutTypeColumn, cutFirstColumn, revisionColumn, savedColumn });
            drawingGrid.Dock = System.Windows.Forms.DockStyle.Fill;
            drawingGrid.MultiSelect = false;
            drawingGrid.Name = "drawingGrid";
            drawingGrid.ReadOnly = true;
            drawingGrid.RowHeadersVisible = false;
            drawingGrid.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            drawingGrid.StandardTab = true;
            drawingGrid.TabIndex = 1;
            drawingGrid.CellDoubleClick += DrawingGrid_CellDoubleClick;
            drawingGrid.CellMouseDown += DrawingGrid_CellMouseDown;
            drawingGrid.ColumnHeaderMouseClick += DrawingGrid_ColumnHeaderMouseClick;
            drawingGrid.SelectionChanged += DrawingGrid_SelectionChanged;
            //
            // nameColumn
            //
            nameColumn.HeaderText = "Name";
            nameColumn.Name = "nameColumn";
            nameColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Programmatic;
            nameColumn.Width = 200;
            //
            // customerColumn
            //
            customerColumn.HeaderText = "Customer";
            customerColumn.Name = "customerColumn";
            customerColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Programmatic;
            customerColumn.Width = 160;
            //
            // materialColumn
            //
            materialColumn.HeaderText = "Material";
            materialColumn.Name = "materialColumn";
            materialColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Programmatic;
            materialColumn.Width = 140;
            //
            // gradeColumn
            //
            gradeColumn.HeaderText = "Grade";
            gradeColumn.Name = "gradeColumn";
            gradeColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Programmatic;
            gradeColumn.Width = 100;
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
            cutTypeColumn.Width = 80;
            //
            // cutFirstColumn
            //
            cutFirstColumn.HeaderText = "Cut first";
            cutFirstColumn.Name = "cutFirstColumn";
            cutFirstColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            cutFirstColumn.Width = 70;
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
            bottomTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            bottomTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            bottomTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            bottomTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            bottomTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            bottomTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            bottomTable.Controls.Add(previousButton, 0, 0);
            bottomTable.Controls.Add(nextButton, 1, 0);
            bottomTable.Controls.Add(summaryLabel, 2, 0);
            bottomTable.Controls.Add(quantityLabel, 3, 0);
            bottomTable.Controls.Add(quantityBox, 4, 0);
            bottomTable.Controls.Add(editButton, 5, 0);
            bottomTable.Controls.Add(addButton, 6, 0);
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
            // quantityLabel
            //
            quantityLabel.Anchor = System.Windows.Forms.AnchorStyles.Right;
            quantityLabel.AutoSize = true;
            quantityLabel.Name = "quantityLabel";
            quantityLabel.TabIndex = 3;
            quantityLabel.Text = "Quantity:";
            //
            // quantityBox
            //
            quantityBox.Anchor = System.Windows.Forms.AnchorStyles.Left;
            quantityBox.Maximum = new decimal(new int[] { 99999, 0, 0, 0 });
            quantityBox.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            quantityBox.Name = "quantityBox";
            quantityBox.Size = new System.Drawing.Size(70, 25);
            quantityBox.TabIndex = 4;
            quantityBox.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            quantityBox.Value = new decimal(new int[] { 1, 0, 0, 0 });
            //
            // editButton
            //
            editButton.AutoSize = true;
            editButton.Enabled = false;
            editButton.Margin = new System.Windows.Forms.Padding(12, 3, 3, 3);
            editButton.Name = "editButton";
            editButton.TabIndex = 5;
            editButton.Text = "Edit Settings...";
            editButton.UseVisualStyleBackColor = true;
            editButton.Click += EditButton_Click;
            //
            // addButton
            //
            addButton.AutoSize = true;
            addButton.Enabled = false;
            addButton.Name = "addButton";
            addButton.TabIndex = 6;
            addButton.Text = "Add to Nest";
            addButton.UseVisualStyleBackColor = true;
            addButton.Click += AddButton_Click;
            //
            // closeButton
            //
            closeButton.AutoSize = true;
            closeButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            closeButton.Name = "closeButton";
            closeButton.TabIndex = 7;
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
            statusLabel.TabIndex = 4;
            //
            // searchTimer
            //
            searchTimer.Interval = 300;
            searchTimer.Tick += SearchTimer_Tick;
            //
            // drawingMenu
            //
            drawingMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { usedInSetsMenuItem, addToSetMenuItem });
            drawingMenu.Name = "drawingMenu";
            drawingMenu.Opening += DrawingMenu_Opening;
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
            // DrawingLibraryForm
            //
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            CancelButton = closeButton;
            ClientSize = new System.Drawing.Size(984, 641);
            Controls.Add(rootTable);
            Font = new System.Drawing.Font("Segoe UI", 9.75F);
            MinimizeBox = false;
            MinimumSize = new System.Drawing.Size(720, 420);
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
            setsTab.ResumeLayout(false);
            footerPanel.ResumeLayout(false);
            footerPanel.PerformLayout();
            layoutTable.ResumeLayout(false);
            layoutTable.PerformLayout();
            filterTable.ResumeLayout(false);
            filterTable.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)drawingGrid).EndInit();
            notesGroup.ResumeLayout(false);
            notesGroup.PerformLayout();
            bottomTable.ResumeLayout(false);
            bottomTable.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)quantityBox).EndInit();
            drawingMenu.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel rootTable;
        private System.Windows.Forms.TabControl tabControl;
        private System.Windows.Forms.TabPage drawingsTab;
        private System.Windows.Forms.TabPage setsTab;
        private OpenNest.Controls.DrawingSetBrowser setBrowser;
        private System.Windows.Forms.FlowLayoutPanel footerPanel;
        private System.Windows.Forms.TableLayoutPanel layoutTable;
        private System.Windows.Forms.TableLayoutPanel filterTable;
        private System.Windows.Forms.Label searchLabel;
        private System.Windows.Forms.TextBox searchBox;
        private System.Windows.Forms.CheckBox showAllCheckBox;
        private System.Windows.Forms.Label filterLabel;
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
        private System.Windows.Forms.GroupBox notesGroup;
        private System.Windows.Forms.TextBox notesBox;
        private System.Windows.Forms.TableLayoutPanel bottomTable;
        private System.Windows.Forms.Button previousButton;
        private System.Windows.Forms.Button nextButton;
        private System.Windows.Forms.Label summaryLabel;
        private System.Windows.Forms.Label quantityLabel;
        private System.Windows.Forms.NumericUpDown quantityBox;
        private System.Windows.Forms.Button editButton;
        private System.Windows.Forms.Button addButton;
        private System.Windows.Forms.Button closeButton;
        private System.Windows.Forms.Label statusLabel;
        private System.Windows.Forms.Timer searchTimer;
        private System.Windows.Forms.ContextMenuStrip drawingMenu;
        private System.Windows.Forms.ToolStripMenuItem usedInSetsMenuItem;
        private System.Windows.Forms.ToolStripMenuItem addToSetMenuItem;
    }
}
