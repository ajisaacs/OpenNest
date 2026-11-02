namespace OpenNest.Forms
{
    partial class ServerJobsForm
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
            layoutTable = new System.Windows.Forms.TableLayoutPanel();
            filterPanel = new System.Windows.Forms.FlowLayoutPanel();
            mineCheckBox = new System.Windows.Forms.CheckBox();
            showLabel = new System.Windows.Forms.Label();
            showComboBox = new System.Windows.Forms.ComboBox();
            jobsListView = new System.Windows.Forms.ListView();
            nestColumn = new System.Windows.Forms.ColumnHeader();
            stateColumn = new System.Windows.Forms.ColumnHeader();
            resultColumn = new System.Windows.Forms.ColumnHeader();
            engineColumn = new System.Windows.Forms.ColumnHeader();
            submittedByColumn = new System.Windows.Forms.ColumnHeader();
            submittedColumn = new System.Windows.Forms.ColumnHeader();
            finishedColumn = new System.Windows.Forms.ColumnHeader();
            bottomTable = new System.Windows.Forms.TableLayoutPanel();
            statusLabel = new System.Windows.Forms.Label();
            openButton = new System.Windows.Forms.Button();
            cancelJobButton = new System.Windows.Forms.Button();
            removeButton = new System.Windows.Forms.Button();
            closeButton = new System.Windows.Forms.Button();
            pollTimer = new System.Windows.Forms.Timer(components);
            layoutTable.SuspendLayout();
            filterPanel.SuspendLayout();
            bottomTable.SuspendLayout();
            SuspendLayout();
            //
            // layoutTable
            //
            layoutTable.ColumnCount = 1;
            layoutTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            layoutTable.Controls.Add(filterPanel, 0, 0);
            layoutTable.Controls.Add(jobsListView, 0, 1);
            layoutTable.Controls.Add(bottomTable, 0, 2);
            layoutTable.Dock = System.Windows.Forms.DockStyle.Fill;
            layoutTable.Location = new System.Drawing.Point(0, 0);
            layoutTable.Name = "layoutTable";
            layoutTable.Padding = new System.Windows.Forms.Padding(8);
            layoutTable.RowCount = 3;
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.Size = new System.Drawing.Size(944, 441);
            layoutTable.TabIndex = 0;
            //
            // filterPanel
            //
            filterPanel.AutoSize = true;
            filterPanel.Controls.Add(mineCheckBox);
            filterPanel.Controls.Add(showLabel);
            filterPanel.Controls.Add(showComboBox);
            filterPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            filterPanel.Margin = new System.Windows.Forms.Padding(0, 0, 0, 6);
            filterPanel.Name = "filterPanel";
            filterPanel.TabIndex = 0;
            filterPanel.WrapContents = false;
            //
            // mineCheckBox
            //
            mineCheckBox.AutoSize = true;
            mineCheckBox.Checked = true;
            mineCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            mineCheckBox.Margin = new System.Windows.Forms.Padding(0, 6, 16, 3);
            mineCheckBox.Name = "mineCheckBox";
            mineCheckBox.TabIndex = 0;
            mineCheckBox.Text = "Only my jobs";
            mineCheckBox.UseVisualStyleBackColor = true;
            mineCheckBox.CheckedChanged += Filter_Changed;
            //
            // showLabel
            //
            showLabel.AutoSize = true;
            showLabel.Margin = new System.Windows.Forms.Padding(0, 7, 4, 3);
            showLabel.Name = "showLabel";
            showLabel.TabIndex = 1;
            showLabel.Text = "Show:";
            //
            // showComboBox
            //
            showComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            showComboBox.Items.AddRange(new object[] { "All jobs", "Active", "Finished", "Ready to review" });
            showComboBox.Name = "showComboBox";
            showComboBox.Size = new System.Drawing.Size(150, 25);
            showComboBox.TabIndex = 2;
            showComboBox.SelectedIndexChanged += Filter_Changed;
            //
            // jobsListView
            //
            jobsListView.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] { nestColumn, stateColumn, resultColumn, engineColumn, submittedByColumn, submittedColumn, finishedColumn });
            jobsListView.Dock = System.Windows.Forms.DockStyle.Fill;
            jobsListView.FullRowSelect = true;
            jobsListView.HideSelection = false;
            jobsListView.Margin = new System.Windows.Forms.Padding(0);
            jobsListView.MultiSelect = false;
            jobsListView.Name = "jobsListView";
            jobsListView.ShowItemToolTips = true;
            jobsListView.TabIndex = 1;
            jobsListView.UseCompatibleStateImageBehavior = false;
            jobsListView.View = System.Windows.Forms.View.Details;
            jobsListView.SelectedIndexChanged += JobsListView_SelectedIndexChanged;
            jobsListView.DoubleClick += JobsListView_DoubleClick;
            //
            // nestColumn
            //
            nestColumn.Text = "Nest";
            nestColumn.Width = 170;
            //
            // stateColumn
            //
            stateColumn.Text = "State";
            stateColumn.Width = 130;
            //
            // resultColumn
            //
            resultColumn.Text = "Result";
            resultColumn.Width = 240;
            //
            // engineColumn
            //
            engineColumn.Text = "Engine";
            engineColumn.Width = 90;
            //
            // submittedByColumn
            //
            submittedByColumn.Text = "Submitted by";
            submittedByColumn.Width = 120;
            //
            // submittedColumn
            //
            submittedColumn.Text = "Submitted";
            submittedColumn.Width = 120;
            //
            // finishedColumn
            //
            finishedColumn.Text = "Finished";
            finishedColumn.Width = 120;
            //
            // bottomTable
            //
            bottomTable.AutoSize = true;
            bottomTable.ColumnCount = 5;
            bottomTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            bottomTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            bottomTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            bottomTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            bottomTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            bottomTable.Controls.Add(statusLabel, 0, 0);
            bottomTable.Controls.Add(openButton, 1, 0);
            bottomTable.Controls.Add(cancelJobButton, 2, 0);
            bottomTable.Controls.Add(removeButton, 3, 0);
            bottomTable.Controls.Add(closeButton, 4, 0);
            bottomTable.Dock = System.Windows.Forms.DockStyle.Fill;
            bottomTable.Margin = new System.Windows.Forms.Padding(0, 8, 0, 0);
            bottomTable.Name = "bottomTable";
            bottomTable.RowCount = 1;
            bottomTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            bottomTable.TabIndex = 2;
            //
            // statusLabel
            //
            statusLabel.AutoEllipsis = true;
            statusLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            statusLabel.ForeColor = System.Drawing.SystemColors.GrayText;
            statusLabel.Name = "statusLabel";
            statusLabel.TabIndex = 0;
            statusLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // openButton
            //
            openButton.AutoSize = true;
            openButton.Enabled = false;
            openButton.MinimumSize = new System.Drawing.Size(93, 29);
            openButton.Name = "openButton";
            openButton.TabIndex = 1;
            openButton.Text = "&Open";
            openButton.UseVisualStyleBackColor = true;
            openButton.Click += OpenButton_Click;
            //
            // cancelJobButton
            //
            cancelJobButton.AutoSize = true;
            cancelJobButton.Enabled = false;
            cancelJobButton.MinimumSize = new System.Drawing.Size(93, 29);
            cancelJobButton.Name = "cancelJobButton";
            cancelJobButton.TabIndex = 2;
            cancelJobButton.Text = "&Cancel Job";
            cancelJobButton.UseVisualStyleBackColor = true;
            cancelJobButton.Click += CancelJobButton_Click;
            //
            // removeButton
            //
            removeButton.AutoSize = true;
            removeButton.Enabled = false;
            removeButton.MinimumSize = new System.Drawing.Size(93, 29);
            removeButton.Name = "removeButton";
            removeButton.TabIndex = 3;
            removeButton.Text = "&Remove";
            removeButton.UseVisualStyleBackColor = true;
            removeButton.Click += RemoveButton_Click;
            //
            // closeButton
            //
            closeButton.AutoSize = true;
            closeButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            closeButton.MinimumSize = new System.Drawing.Size(93, 29);
            closeButton.Name = "closeButton";
            closeButton.TabIndex = 4;
            closeButton.Text = "Close";
            closeButton.UseVisualStyleBackColor = true;
            closeButton.Click += CloseButton_Click;
            //
            // pollTimer
            //
            pollTimer.Interval = 3000;
            pollTimer.Tick += PollTimer_Tick;
            //
            // ServerJobsForm
            //
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            CancelButton = closeButton;
            ClientSize = new System.Drawing.Size(944, 441);
            Controls.Add(layoutTable);
            Font = new System.Drawing.Font("Segoe UI", 9.75F);
            MinimizeBox = false;
            MinimumSize = new System.Drawing.Size(640, 300);
            Name = "ServerJobsForm";
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "My Server Jobs";
            layoutTable.ResumeLayout(false);
            layoutTable.PerformLayout();
            filterPanel.ResumeLayout(false);
            filterPanel.PerformLayout();
            bottomTable.ResumeLayout(false);
            bottomTable.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel layoutTable;
        private System.Windows.Forms.FlowLayoutPanel filterPanel;
        private System.Windows.Forms.CheckBox mineCheckBox;
        private System.Windows.Forms.Label showLabel;
        private System.Windows.Forms.ComboBox showComboBox;
        private System.Windows.Forms.ListView jobsListView;
        private System.Windows.Forms.ColumnHeader nestColumn;
        private System.Windows.Forms.ColumnHeader stateColumn;
        private System.Windows.Forms.ColumnHeader resultColumn;
        private System.Windows.Forms.ColumnHeader engineColumn;
        private System.Windows.Forms.ColumnHeader submittedByColumn;
        private System.Windows.Forms.ColumnHeader submittedColumn;
        private System.Windows.Forms.ColumnHeader finishedColumn;
        private System.Windows.Forms.TableLayoutPanel bottomTable;
        private System.Windows.Forms.Label statusLabel;
        private System.Windows.Forms.Button openButton;
        private System.Windows.Forms.Button cancelJobButton;
        private System.Windows.Forms.Button removeButton;
        private System.Windows.Forms.Button closeButton;
        private System.Windows.Forms.Timer pollTimer;
    }
}
