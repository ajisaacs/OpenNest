namespace OpenNest.Forms
{
    partial class DrawingSetHistoryForm
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
            revisionGrid = new System.Windows.Forms.DataGridView();
            revisionColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            savedColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            savedByColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            nameColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            customerColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            membersColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            stateColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            membersGroup = new System.Windows.Forms.GroupBox();
            memberGrid = new System.Windows.Forms.DataGridView();
            drawingColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            quantityColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            materialColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            thicknessColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            cutTypeColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            bottomTable = new System.Windows.Forms.TableLayoutPanel();
            previousButton = new System.Windows.Forms.Button();
            nextButton = new System.Windows.Forms.Button();
            summaryLabel = new System.Windows.Forms.Label();
            closeButton = new System.Windows.Forms.Button();
            layoutTable.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)revisionGrid).BeginInit();
            membersGroup.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)memberGrid).BeginInit();
            bottomTable.SuspendLayout();
            SuspendLayout();
            //
            // layoutTable
            //
            layoutTable.ColumnCount = 1;
            layoutTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            layoutTable.Controls.Add(revisionGrid, 0, 0);
            layoutTable.Controls.Add(membersGroup, 0, 1);
            layoutTable.Controls.Add(bottomTable, 0, 2);
            layoutTable.Dock = System.Windows.Forms.DockStyle.Fill;
            layoutTable.Location = new System.Drawing.Point(0, 0);
            layoutTable.Name = "layoutTable";
            layoutTable.Padding = new System.Windows.Forms.Padding(8);
            layoutTable.RowCount = 3;
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50F));
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50F));
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.Size = new System.Drawing.Size(824, 541);
            layoutTable.TabIndex = 0;
            //
            // revisionGrid
            //
            revisionGrid.AllowUserToAddRows = false;
            revisionGrid.AllowUserToDeleteRows = false;
            revisionGrid.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] { revisionColumn, savedColumn, savedByColumn, nameColumn, customerColumn, membersColumn, stateColumn });
            revisionGrid.Dock = System.Windows.Forms.DockStyle.Fill;
            revisionGrid.MultiSelect = false;
            revisionGrid.Name = "revisionGrid";
            revisionGrid.ReadOnly = true;
            revisionGrid.RowHeadersVisible = false;
            revisionGrid.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            revisionGrid.StandardTab = true;
            revisionGrid.TabIndex = 0;
            revisionGrid.CurrentCellChanged += RevisionGrid_CurrentCellChanged;
            //
            // revisionColumn
            //
            rightAligned.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleRight;
            revisionColumn.DefaultCellStyle = rightAligned;
            revisionColumn.HeaderText = "Revision";
            revisionColumn.Name = "revisionColumn";
            revisionColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            revisionColumn.Width = 70;
            //
            // savedColumn
            //
            savedColumn.HeaderText = "Saved";
            savedColumn.Name = "savedColumn";
            savedColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            savedColumn.Width = 130;
            //
            // savedByColumn
            //
            savedByColumn.HeaderText = "Saved by";
            savedByColumn.Name = "savedByColumn";
            savedByColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            savedByColumn.Width = 110;
            //
            // nameColumn
            //
            nameColumn.HeaderText = "Name";
            nameColumn.Name = "nameColumn";
            nameColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            nameColumn.Width = 200;
            //
            // customerColumn
            //
            customerColumn.HeaderText = "Customer";
            customerColumn.Name = "customerColumn";
            customerColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            customerColumn.Width = 140;
            //
            // membersColumn
            //
            membersColumn.DefaultCellStyle = rightAligned;
            membersColumn.HeaderText = "Drawings";
            membersColumn.Name = "membersColumn";
            membersColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            membersColumn.Width = 75;
            //
            // stateColumn
            //
            stateColumn.HeaderText = "State";
            stateColumn.Name = "stateColumn";
            stateColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            stateColumn.Width = 75;
            //
            // membersGroup
            //
            membersGroup.Controls.Add(memberGrid);
            membersGroup.Dock = System.Windows.Forms.DockStyle.Fill;
            membersGroup.Margin = new System.Windows.Forms.Padding(0, 6, 0, 0);
            membersGroup.Name = "membersGroup";
            membersGroup.TabIndex = 1;
            membersGroup.TabStop = false;
            membersGroup.Text = "Drawings in the selected revision";
            //
            // memberGrid
            //
            memberGrid.AllowUserToAddRows = false;
            memberGrid.AllowUserToDeleteRows = false;
            memberGrid.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] { drawingColumn, quantityColumn, materialColumn, thicknessColumn, cutTypeColumn });
            memberGrid.Dock = System.Windows.Forms.DockStyle.Fill;
            memberGrid.MultiSelect = false;
            memberGrid.Name = "memberGrid";
            memberGrid.ReadOnly = true;
            memberGrid.RowHeadersVisible = false;
            memberGrid.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            memberGrid.StandardTab = true;
            memberGrid.TabIndex = 0;
            //
            // drawingColumn
            //
            drawingColumn.HeaderText = "Drawing";
            drawingColumn.Name = "drawingColumn";
            drawingColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            drawingColumn.Width = 220;
            //
            // quantityColumn
            //
            quantityColumn.DefaultCellStyle = rightAligned;
            quantityColumn.HeaderText = "Qty";
            quantityColumn.Name = "quantityColumn";
            quantityColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            quantityColumn.Width = 60;
            //
            // materialColumn
            //
            materialColumn.HeaderText = "Material (current)";
            materialColumn.Name = "materialColumn";
            materialColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            materialColumn.Width = 200;
            //
            // thicknessColumn
            //
            thicknessColumn.DefaultCellStyle = rightAligned;
            thicknessColumn.HeaderText = "Thickness";
            thicknessColumn.Name = "thicknessColumn";
            thicknessColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            thicknessColumn.Width = 80;
            //
            // cutTypeColumn
            //
            cutTypeColumn.HeaderText = "Cut type";
            cutTypeColumn.Name = "cutTypeColumn";
            cutTypeColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            cutTypeColumn.Width = 80;
            //
            // bottomTable
            //
            bottomTable.AutoSize = true;
            bottomTable.ColumnCount = 4;
            bottomTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            bottomTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            bottomTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            bottomTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            bottomTable.Controls.Add(previousButton, 0, 0);
            bottomTable.Controls.Add(nextButton, 1, 0);
            bottomTable.Controls.Add(summaryLabel, 2, 0);
            bottomTable.Controls.Add(closeButton, 3, 0);
            bottomTable.Dock = System.Windows.Forms.DockStyle.Fill;
            bottomTable.Margin = new System.Windows.Forms.Padding(0, 6, 0, 0);
            bottomTable.Name = "bottomTable";
            bottomTable.RowCount = 1;
            bottomTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            bottomTable.TabIndex = 2;
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
            // closeButton
            //
            closeButton.AutoSize = true;
            closeButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            closeButton.Name = "closeButton";
            closeButton.TabIndex = 3;
            closeButton.Text = "Close";
            closeButton.UseVisualStyleBackColor = true;
            //
            // DrawingSetHistoryForm
            //
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            CancelButton = closeButton;
            ClientSize = new System.Drawing.Size(824, 541);
            Controls.Add(layoutTable);
            Font = new System.Drawing.Font("Segoe UI", 9.75F);
            MinimizeBox = false;
            MinimumSize = new System.Drawing.Size(600, 400);
            Name = "DrawingSetHistoryForm";
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "Set History";
            Shown += DrawingSetHistoryForm_Shown;
            ((System.ComponentModel.ISupportInitialize)revisionGrid).EndInit();
            ((System.ComponentModel.ISupportInitialize)memberGrid).EndInit();
            membersGroup.ResumeLayout(false);
            bottomTable.ResumeLayout(false);
            bottomTable.PerformLayout();
            layoutTable.ResumeLayout(false);
            layoutTable.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel layoutTable;
        private System.Windows.Forms.DataGridView revisionGrid;
        private System.Windows.Forms.DataGridViewTextBoxColumn revisionColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn savedColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn savedByColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn nameColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn customerColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn membersColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn stateColumn;
        private System.Windows.Forms.GroupBox membersGroup;
        private System.Windows.Forms.DataGridView memberGrid;
        private System.Windows.Forms.DataGridViewTextBoxColumn drawingColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn quantityColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn materialColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn thicknessColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn cutTypeColumn;
        private System.Windows.Forms.TableLayoutPanel bottomTable;
        private System.Windows.Forms.Button previousButton;
        private System.Windows.Forms.Button nextButton;
        private System.Windows.Forms.Label summaryLabel;
        private System.Windows.Forms.Button closeButton;
    }
}
