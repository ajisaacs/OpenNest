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
            System.Windows.Forms.DataGridViewCellStyle rightAligned = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle quantityStyle = new System.Windows.Forms.DataGridViewCellStyle();
            layoutTable = new System.Windows.Forms.TableLayoutPanel();
            detailsTable = new System.Windows.Forms.TableLayoutPanel();
            nameLabel = new System.Windows.Forms.Label();
            nameBox = new System.Windows.Forms.TextBox();
            customerLabel = new System.Windows.Forms.Label();
            customerBox = new System.Windows.Forms.TextBox();
            notesLabel = new System.Windows.Forms.Label();
            notesBox = new System.Windows.Forms.TextBox();
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
            addButton = new System.Windows.Forms.Button();
            removeButton = new System.Windows.Forms.Button();
            upButton = new System.Windows.Forms.Button();
            downButton = new System.Windows.Forms.Button();
            bottomTable = new System.Windows.Forms.TableLayoutPanel();
            summaryLabel = new System.Windows.Forms.Label();
            saveButton = new System.Windows.Forms.Button();
            cancelButton = new System.Windows.Forms.Button();
            statusLabel = new System.Windows.Forms.Label();
            layoutTable.SuspendLayout();
            detailsTable.SuspendLayout();
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
            layoutTable.Controls.Add(memberTable, 0, 1);
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
            layoutTable.Size = new System.Drawing.Size(904, 561);
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
            detailsTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 60F));
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
            memberTable.TabIndex = 1;
            //
            // memberGrid
            //
            memberGrid.AllowUserToAddRows = false;
            memberGrid.AllowUserToDeleteRows = false;
            memberGrid.AllowUserToOrderColumns = false;
            memberGrid.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] { drawingColumn, materialColumn, gradeColumn, thicknessColumn, unitsColumn, cutTypeColumn, quantityColumn, warningColumn });
            memberGrid.Dock = System.Windows.Forms.DockStyle.Fill;
            memberGrid.EditMode = System.Windows.Forms.DataGridViewEditMode.EditOnKeystrokeOrF2;
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
            //
            // drawingColumn
            //
            drawingColumn.HeaderText = "Drawing";
            drawingColumn.Name = "drawingColumn";
            drawingColumn.ReadOnly = true;
            drawingColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            drawingColumn.Width = 200;
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
            unitsColumn.Width = 60;
            //
            // cutTypeColumn
            //
            cutTypeColumn.HeaderText = "Cut type";
            cutTypeColumn.Name = "cutTypeColumn";
            cutTypeColumn.ReadOnly = true;
            cutTypeColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            cutTypeColumn.Width = 75;
            //
            // quantityColumn
            //
            quantityStyle.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleRight;
            quantityStyle.BackColor = System.Drawing.SystemColors.Info;
            quantityColumn.DefaultCellStyle = quantityStyle;
            quantityColumn.HeaderText = "Qty";
            quantityColumn.Name = "quantityColumn";
            quantityColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            quantityColumn.ToolTipText = "Quantity per kit; type to change it";
            quantityColumn.ValueType = typeof(int);
            quantityColumn.Width = 60;
            //
            // warningColumn
            //
            warningColumn.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            warningColumn.HeaderText = "Note";
            warningColumn.Name = "warningColumn";
            warningColumn.ReadOnly = true;
            warningColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            //
            // memberButtons
            //
            memberButtons.AutoSize = true;
            memberButtons.Controls.Add(addButton);
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
            // addButton
            //
            addButton.Name = "addButton";
            addButton.Size = new System.Drawing.Size(130, 28);
            addButton.TabIndex = 0;
            addButton.Text = "Add Drawings...";
            addButton.UseVisualStyleBackColor = true;
            addButton.Click += AddButton_Click;
            //
            // removeButton
            //
            removeButton.Enabled = false;
            removeButton.Name = "removeButton";
            removeButton.Size = new System.Drawing.Size(130, 28);
            removeButton.TabIndex = 1;
            removeButton.Text = "Remove";
            removeButton.UseVisualStyleBackColor = true;
            removeButton.Click += RemoveButton_Click;
            //
            // upButton
            //
            upButton.Enabled = false;
            upButton.Margin = new System.Windows.Forms.Padding(3, 12, 3, 3);
            upButton.Name = "upButton";
            upButton.Size = new System.Drawing.Size(130, 28);
            upButton.TabIndex = 2;
            upButton.Text = "Move Up";
            upButton.UseVisualStyleBackColor = true;
            upButton.Click += UpButton_Click;
            //
            // downButton
            //
            downButton.Enabled = false;
            downButton.Name = "downButton";
            downButton.Size = new System.Drawing.Size(130, 28);
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
            // EditDrawingSetForm
            //
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            CancelButton = cancelButton;
            ClientSize = new System.Drawing.Size(904, 561);
            Controls.Add(layoutTable);
            Font = new System.Drawing.Font("Segoe UI", 9.75F);
            MinimizeBox = false;
            MinimumSize = new System.Drawing.Size(720, 420);
            Name = "EditDrawingSetForm";
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "Drawing Set";
            FormClosing += EditDrawingSetForm_FormClosing;
            layoutTable.ResumeLayout(false);
            layoutTable.PerformLayout();
            detailsTable.ResumeLayout(false);
            detailsTable.PerformLayout();
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
        private System.Windows.Forms.Button addButton;
        private System.Windows.Forms.Button removeButton;
        private System.Windows.Forms.Button upButton;
        private System.Windows.Forms.Button downButton;
        private System.Windows.Forms.TableLayoutPanel bottomTable;
        private System.Windows.Forms.Label summaryLabel;
        private System.Windows.Forms.Button saveButton;
        private System.Windows.Forms.Button cancelButton;
        private System.Windows.Forms.Label statusLabel;
    }
}
