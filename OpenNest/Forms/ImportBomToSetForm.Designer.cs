namespace OpenNest.Forms
{
    partial class ImportBomToSetForm
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
            layoutTable = new System.Windows.Forms.TableLayoutPanel();
            inputTable = new System.Windows.Forms.TableLayoutPanel();
            bomLabel = new System.Windows.Forms.Label();
            bomBox = new System.Windows.Forms.TextBox();
            browseBomButton = new System.Windows.Forms.Button();
            folderLabel = new System.Windows.Forms.Label();
            folderBox = new System.Windows.Forms.TextBox();
            browseFolderButton = new System.Windows.Forms.Button();
            nameLabel = new System.Windows.Forms.Label();
            nameBox = new System.Windows.Forms.TextBox();
            customerLabel = new System.Windows.Forms.Label();
            customerBox = new System.Windows.Forms.TextBox();
            readButton = new System.Windows.Forms.Button();
            drawingGrid = new System.Windows.Forms.DataGridView();
            drawingColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            materialColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            thicknessColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            quantityColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            libraryColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            noteColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            summaryLabel = new System.Windows.Forms.Label();
            buttonPanel = new System.Windows.Forms.FlowLayoutPanel();
            cancelButton = new System.Windows.Forms.Button();
            createButton = new System.Windows.Forms.Button();
            layoutTable.SuspendLayout();
            inputTable.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)drawingGrid).BeginInit();
            buttonPanel.SuspendLayout();
            SuspendLayout();
            //
            // layoutTable
            //
            layoutTable.ColumnCount = 1;
            layoutTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            layoutTable.Controls.Add(inputTable, 0, 0);
            layoutTable.Controls.Add(drawingGrid, 0, 1);
            layoutTable.Controls.Add(summaryLabel, 0, 2);
            layoutTable.Controls.Add(buttonPanel, 0, 3);
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
            // inputTable
            //
            inputTable.AutoSize = true;
            inputTable.ColumnCount = 5;
            inputTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            inputTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            inputTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            inputTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            inputTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            inputTable.Controls.Add(bomLabel, 0, 0);
            inputTable.Controls.Add(bomBox, 1, 0);
            inputTable.Controls.Add(browseBomButton, 4, 0);
            inputTable.Controls.Add(folderLabel, 0, 1);
            inputTable.Controls.Add(folderBox, 1, 1);
            inputTable.Controls.Add(browseFolderButton, 4, 1);
            inputTable.Controls.Add(nameLabel, 0, 2);
            inputTable.Controls.Add(nameBox, 1, 2);
            inputTable.Controls.Add(customerLabel, 2, 2);
            inputTable.Controls.Add(customerBox, 3, 2);
            inputTable.Controls.Add(readButton, 4, 2);
            inputTable.SetColumnSpan(bomBox, 3);
            inputTable.SetColumnSpan(folderBox, 3);
            inputTable.Dock = System.Windows.Forms.DockStyle.Fill;
            inputTable.Margin = new System.Windows.Forms.Padding(0, 0, 0, 6);
            inputTable.Name = "inputTable";
            inputTable.RowCount = 3;
            inputTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            inputTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            inputTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            inputTable.TabIndex = 0;
            //
            // bomLabel
            //
            bomLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            bomLabel.AutoSize = true;
            bomLabel.Name = "bomLabel";
            bomLabel.TabIndex = 0;
            bomLabel.Text = "&BOM:";
            //
            // bomBox
            //
            bomBox.Dock = System.Windows.Forms.DockStyle.Fill;
            bomBox.Name = "bomBox";
            bomBox.TabIndex = 1;
            bomBox.TextChanged += Inputs_Changed;
            //
            // browseBomButton
            //
            browseBomButton.AutoSize = true;
            browseBomButton.Name = "browseBomButton";
            browseBomButton.TabIndex = 2;
            browseBomButton.Text = "Bro&wse...";
            browseBomButton.UseVisualStyleBackColor = true;
            browseBomButton.Click += BrowseBomButton_Click;
            //
            // folderLabel
            //
            folderLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            folderLabel.AutoSize = true;
            folderLabel.Name = "folderLabel";
            folderLabel.TabIndex = 3;
            folderLabel.Text = "&Drawing folder:";
            //
            // folderBox
            //
            folderBox.Dock = System.Windows.Forms.DockStyle.Fill;
            folderBox.Name = "folderBox";
            folderBox.TabIndex = 4;
            folderBox.TextChanged += Inputs_Changed;
            //
            // browseFolderButton
            //
            browseFolderButton.AutoSize = true;
            browseFolderButton.Name = "browseFolderButton";
            browseFolderButton.TabIndex = 5;
            browseFolderButton.Text = "Brow&se...";
            browseFolderButton.UseVisualStyleBackColor = true;
            browseFolderButton.Click += BrowseFolderButton_Click;
            //
            // nameLabel
            //
            nameLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            nameLabel.AutoSize = true;
            nameLabel.Name = "nameLabel";
            nameLabel.TabIndex = 6;
            nameLabel.Text = "Set &name:";
            //
            // nameBox
            //
            nameBox.Dock = System.Windows.Forms.DockStyle.Fill;
            nameBox.Name = "nameBox";
            nameBox.TabIndex = 7;
            //
            // customerLabel
            //
            customerLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            customerLabel.AutoSize = true;
            customerLabel.Margin = new System.Windows.Forms.Padding(12, 0, 3, 0);
            customerLabel.Name = "customerLabel";
            customerLabel.TabIndex = 8;
            customerLabel.Text = "C&ustomer:";
            //
            // customerBox
            //
            customerBox.Dock = System.Windows.Forms.DockStyle.Fill;
            customerBox.Name = "customerBox";
            customerBox.TabIndex = 9;
            customerBox.TextChanged += Inputs_Changed;
            //
            // readButton
            //
            readButton.AutoSize = true;
            readButton.Name = "readButton";
            readButton.TabIndex = 10;
            readButton.Text = "&Read BOM";
            readButton.UseVisualStyleBackColor = true;
            readButton.Click += ReadButton_Click;
            //
            // drawingGrid
            //
            drawingGrid.AllowUserToAddRows = false;
            drawingGrid.AllowUserToDeleteRows = false;
            drawingGrid.AllowUserToResizeRows = false;
            drawingGrid.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            drawingGrid.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            drawingGrid.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] { drawingColumn, materialColumn, thicknessColumn, quantityColumn, libraryColumn, noteColumn });
            drawingGrid.Dock = System.Windows.Forms.DockStyle.Fill;
            drawingGrid.Name = "drawingGrid";
            drawingGrid.ReadOnly = true;
            drawingGrid.RowHeadersVisible = false;
            drawingGrid.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            drawingGrid.ShowCellToolTips = true;
            drawingGrid.TabIndex = 1;
            //
            // drawingColumn
            //
            drawingColumn.FillWeight = 130F;
            drawingColumn.HeaderText = "Drawing";
            drawingColumn.Name = "drawingColumn";
            drawingColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            //
            // materialColumn
            //
            materialColumn.FillWeight = 80F;
            materialColumn.HeaderText = "Material";
            materialColumn.Name = "materialColumn";
            materialColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            //
            // thicknessColumn
            //
            thicknessColumn.FillWeight = 50F;
            thicknessColumn.HeaderText = "Thickness";
            thicknessColumn.Name = "thicknessColumn";
            thicknessColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            //
            // quantityColumn
            //
            quantityColumn.DefaultCellStyle = new System.Windows.Forms.DataGridViewCellStyle { Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleRight };
            quantityColumn.FillWeight = 40F;
            quantityColumn.HeaderText = "Qty";
            quantityColumn.Name = "quantityColumn";
            quantityColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            quantityColumn.ToolTipText = "Quantity per set: the BOM quantity, summed over rows that name the drawing";
            //
            // libraryColumn
            //
            libraryColumn.FillWeight = 120F;
            libraryColumn.HeaderText = "Drawing library";
            libraryColumn.Name = "libraryColumn";
            libraryColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            //
            // noteColumn
            //
            noteColumn.FillWeight = 120F;
            noteColumn.HeaderText = "Note";
            noteColumn.Name = "noteColumn";
            noteColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            //
            // summaryLabel
            //
            summaryLabel.AutoSize = true;
            summaryLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            summaryLabel.Margin = new System.Windows.Forms.Padding(3, 6, 3, 0);
            summaryLabel.Name = "summaryLabel";
            summaryLabel.TabIndex = 2;
            summaryLabel.Text = "Choose a BOM and the folder of its drawings, then Read BOM. Nothing is stored until you create the set.";
            //
            // buttonPanel
            //
            buttonPanel.AutoSize = true;
            buttonPanel.Controls.Add(cancelButton);
            buttonPanel.Controls.Add(createButton);
            buttonPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            buttonPanel.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
            buttonPanel.Margin = new System.Windows.Forms.Padding(0, 6, 0, 0);
            buttonPanel.Name = "buttonPanel";
            buttonPanel.TabIndex = 3;
            //
            // cancelButton
            //
            cancelButton.AutoSize = true;
            cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            cancelButton.Name = "cancelButton";
            cancelButton.TabIndex = 1;
            cancelButton.Text = "Cancel";
            cancelButton.UseVisualStyleBackColor = true;
            //
            // createButton
            //
            createButton.AutoSize = true;
            createButton.Enabled = false;
            createButton.Name = "createButton";
            createButton.TabIndex = 0;
            createButton.Text = "&Create Set...";
            createButton.UseVisualStyleBackColor = true;
            createButton.Click += CreateButton_Click;
            //
            // ImportBomToSetForm
            //
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            CancelButton = cancelButton;
            ClientSize = new System.Drawing.Size(904, 561);
            Controls.Add(layoutTable);
            MinimizeBox = false;
            MinimumSize = new System.Drawing.Size(700, 420);
            Name = "ImportBomToSetForm";
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "Import BOM to Set";
            FormClosing += ImportBomToSetForm_FormClosing;
            layoutTable.ResumeLayout(false);
            layoutTable.PerformLayout();
            inputTable.ResumeLayout(false);
            inputTable.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)drawingGrid).EndInit();
            buttonPanel.ResumeLayout(false);
            buttonPanel.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel layoutTable;
        private System.Windows.Forms.TableLayoutPanel inputTable;
        private System.Windows.Forms.Label bomLabel;
        private System.Windows.Forms.TextBox bomBox;
        private System.Windows.Forms.Button browseBomButton;
        private System.Windows.Forms.Label folderLabel;
        private System.Windows.Forms.TextBox folderBox;
        private System.Windows.Forms.Button browseFolderButton;
        private System.Windows.Forms.Label nameLabel;
        private System.Windows.Forms.TextBox nameBox;
        private System.Windows.Forms.Label customerLabel;
        private System.Windows.Forms.TextBox customerBox;
        private System.Windows.Forms.Button readButton;
        private System.Windows.Forms.DataGridView drawingGrid;
        private System.Windows.Forms.DataGridViewTextBoxColumn drawingColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn materialColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn thicknessColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn quantityColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn libraryColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn noteColumn;
        private System.Windows.Forms.Label summaryLabel;
        private System.Windows.Forms.FlowLayoutPanel buttonPanel;
        private System.Windows.Forms.Button cancelButton;
        private System.Windows.Forms.Button createButton;
    }
}
