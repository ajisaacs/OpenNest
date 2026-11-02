namespace OpenNest.Forms
{
    partial class DrawingLibraryGridForm
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
            toolPanel = new System.Windows.Forms.FlowLayoutPanel();
            searchLabel = new System.Windows.Forms.Label();
            searchBox = new System.Windows.Forms.TextBox();
            previousButton = new System.Windows.Forms.Button();
            pageLabel = new System.Windows.Forms.Label();
            nextButton = new System.Windows.Forms.Button();
            checkAllButton = new System.Windows.Forms.Button();
            stopButton = new System.Windows.Forms.Button();
            copyButton = new System.Windows.Forms.Button();
            splitContainer = new System.Windows.Forms.SplitContainer();
            tilePanel = new System.Windows.Forms.FlowLayoutPanel();
            problemList = new System.Windows.Forms.ListView();
            problemNameColumn = new System.Windows.Forms.ColumnHeader();
            problemRevisionColumn = new System.Windows.Forms.ColumnHeader();
            problemColumn = new System.Windows.Forms.ColumnHeader();
            problemTimeColumn = new System.Windows.Forms.ColumnHeader();
            statusStrip = new System.Windows.Forms.StatusStrip();
            statusLabel = new System.Windows.Forms.ToolStripStatusLabel();
            toolPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)splitContainer).BeginInit();
            splitContainer.Panel1.SuspendLayout();
            splitContainer.Panel2.SuspendLayout();
            splitContainer.SuspendLayout();
            statusStrip.SuspendLayout();
            SuspendLayout();
            //
            // toolPanel
            //
            toolPanel.AutoSize = true;
            toolPanel.Controls.Add(searchLabel);
            toolPanel.Controls.Add(searchBox);
            toolPanel.Controls.Add(previousButton);
            toolPanel.Controls.Add(pageLabel);
            toolPanel.Controls.Add(nextButton);
            toolPanel.Controls.Add(checkAllButton);
            toolPanel.Controls.Add(stopButton);
            toolPanel.Controls.Add(copyButton);
            toolPanel.Dock = System.Windows.Forms.DockStyle.Top;
            toolPanel.Name = "toolPanel";
            toolPanel.Padding = new System.Windows.Forms.Padding(6);
            toolPanel.TabIndex = 0;
            toolPanel.WrapContents = false;
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
            searchBox.Anchor = System.Windows.Forms.AnchorStyles.Left;
            searchBox.Name = "searchBox";
            searchBox.Size = new System.Drawing.Size(260, 25);
            searchBox.TabIndex = 1;
            searchBox.KeyDown += SearchBox_KeyDown;
            //
            // previousButton
            //
            previousButton.AutoSize = true;
            previousButton.Enabled = false;
            previousButton.Margin = new System.Windows.Forms.Padding(12, 3, 3, 3);
            previousButton.Name = "previousButton";
            previousButton.TabIndex = 2;
            previousButton.Text = "< Previous";
            previousButton.UseVisualStyleBackColor = true;
            previousButton.Click += PreviousButton_Click;
            //
            // pageLabel
            //
            pageLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            pageLabel.AutoSize = true;
            pageLabel.MinimumSize = new System.Drawing.Size(140, 0);
            pageLabel.Name = "pageLabel";
            pageLabel.TabIndex = 3;
            pageLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            //
            // nextButton
            //
            nextButton.AutoSize = true;
            nextButton.Enabled = false;
            nextButton.Name = "nextButton";
            nextButton.TabIndex = 4;
            nextButton.Text = "Next >";
            nextButton.UseVisualStyleBackColor = true;
            nextButton.Click += NextButton_Click;
            //
            // checkAllButton
            //
            checkAllButton.AutoSize = true;
            checkAllButton.Margin = new System.Windows.Forms.Padding(24, 3, 3, 3);
            checkAllButton.Name = "checkAllButton";
            checkAllButton.TabIndex = 5;
            checkAllButton.Text = "Check All Pages";
            checkAllButton.UseVisualStyleBackColor = true;
            checkAllButton.Click += CheckAllButton_Click;
            //
            // stopButton
            //
            stopButton.AutoSize = true;
            stopButton.Enabled = false;
            stopButton.Name = "stopButton";
            stopButton.TabIndex = 6;
            stopButton.Text = "Stop";
            stopButton.UseVisualStyleBackColor = true;
            stopButton.Click += StopButton_Click;
            //
            // copyButton
            //
            copyButton.AutoSize = true;
            copyButton.Name = "copyButton";
            copyButton.TabIndex = 7;
            copyButton.Text = "Copy Problems";
            copyButton.UseVisualStyleBackColor = true;
            copyButton.Click += CopyButton_Click;
            //
            // splitContainer
            //
            splitContainer.Dock = System.Windows.Forms.DockStyle.Fill;
            splitContainer.FixedPanel = System.Windows.Forms.FixedPanel.Panel2;
            splitContainer.Name = "splitContainer";
            splitContainer.Orientation = System.Windows.Forms.Orientation.Horizontal;
            splitContainer.Panel1.Controls.Add(tilePanel);
            splitContainer.Panel2.Controls.Add(problemList);
            splitContainer.Size = new System.Drawing.Size(1184, 650);
            splitContainer.SplitterDistance = 480;
            splitContainer.TabIndex = 1;
            //
            // tilePanel
            //
            tilePanel.AutoScroll = true;
            tilePanel.Dock = System.Windows.Forms.DockStyle.Fill;
            tilePanel.Name = "tilePanel";
            tilePanel.Padding = new System.Windows.Forms.Padding(4);
            tilePanel.TabIndex = 0;
            //
            // problemList
            //
            problemList.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] { problemNameColumn, problemRevisionColumn, problemColumn, problemTimeColumn });
            problemList.Dock = System.Windows.Forms.DockStyle.Fill;
            problemList.FullRowSelect = true;
            problemList.Name = "problemList";
            problemList.TabIndex = 0;
            problemList.UseCompatibleStateImageBehavior = false;
            problemList.View = System.Windows.Forms.View.Details;
            problemList.DoubleClick += ProblemList_DoubleClick;
            //
            // problemNameColumn
            //
            problemNameColumn.Text = "Drawing";
            problemNameColumn.Width = 260;
            //
            // problemRevisionColumn
            //
            problemRevisionColumn.Text = "Rev";
            problemRevisionColumn.Width = 50;
            //
            // problemColumn
            //
            problemColumn.Text = "Problem";
            problemColumn.Width = 420;
            //
            // problemTimeColumn
            //
            problemTimeColumn.Text = "ms";
            problemTimeColumn.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            problemTimeColumn.Width = 80;
            //
            // statusStrip
            //
            statusStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { statusLabel });
            statusStrip.Name = "statusStrip";
            statusStrip.TabIndex = 2;
            //
            // statusLabel
            //
            statusLabel.Name = "statusLabel";
            statusLabel.Spring = true;
            statusLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // DrawingLibraryGridForm
            //
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            ClientSize = new System.Drawing.Size(1184, 721);
            Controls.Add(splitContainer);
            Controls.Add(statusStrip);
            Controls.Add(toolPanel);
            Font = new System.Drawing.Font("Segoe UI", 9.75F);
            MinimumSize = new System.Drawing.Size(860, 520);
            Name = "DrawingLibraryGridForm";
            ShowIcon = false;
            StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            Text = "Drawing Library Grid";
            Shown += DrawingLibraryGridForm_Shown;
            toolPanel.ResumeLayout(false);
            toolPanel.PerformLayout();
            splitContainer.Panel1.ResumeLayout(false);
            splitContainer.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitContainer).EndInit();
            splitContainer.ResumeLayout(false);
            statusStrip.ResumeLayout(false);
            statusStrip.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private System.Windows.Forms.FlowLayoutPanel toolPanel;
        private System.Windows.Forms.Label searchLabel;
        private System.Windows.Forms.TextBox searchBox;
        private System.Windows.Forms.Button previousButton;
        private System.Windows.Forms.Label pageLabel;
        private System.Windows.Forms.Button nextButton;
        private System.Windows.Forms.Button checkAllButton;
        private System.Windows.Forms.Button stopButton;
        private System.Windows.Forms.Button copyButton;
        private System.Windows.Forms.SplitContainer splitContainer;
        private System.Windows.Forms.FlowLayoutPanel tilePanel;
        private System.Windows.Forms.ListView problemList;
        private System.Windows.Forms.ColumnHeader problemNameColumn;
        private System.Windows.Forms.ColumnHeader problemRevisionColumn;
        private System.Windows.Forms.ColumnHeader problemColumn;
        private System.Windows.Forms.ColumnHeader problemTimeColumn;
        private System.Windows.Forms.StatusStrip statusStrip;
        private System.Windows.Forms.ToolStripStatusLabel statusLabel;
    }
}
