namespace OpenNest.Forms
{
    partial class BestFitViewerForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            splitContainer = new System.Windows.Forms.SplitContainer();
            drawingListBox = new OpenNest.Controls.DrawingListBox();
            gridPanel = new System.Windows.Forms.TableLayoutPanel();
            navPanel = new System.Windows.Forms.Panel();
            btnPrev = new System.Windows.Forms.Button();
            txtPage = new System.Windows.Forms.TextBox();
            lblPageCount = new System.Windows.Forms.Label();
            btnNext = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)splitContainer).BeginInit();
            splitContainer.Panel1.SuspendLayout();
            splitContainer.Panel2.SuspendLayout();
            splitContainer.SuspendLayout();
            navPanel.SuspendLayout();
            SuspendLayout();
            // 
            // splitContainer
            // 
            splitContainer.Dock = System.Windows.Forms.DockStyle.Fill;
            splitContainer.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
            splitContainer.Location = new System.Drawing.Point(0, 0);
            splitContainer.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            splitContainer.Name = "splitContainer";
            // 
            // splitContainer.Panel1
            // 
            splitContainer.Panel1.Controls.Add(drawingListBox);
            splitContainer.Panel1MinSize = 180;
            // 
            // splitContainer.Panel2
            // 
            splitContainer.Panel2.Controls.Add(gridPanel);
            splitContainer.Panel2.Controls.Add(navPanel);
            splitContainer.Size = new System.Drawing.Size(792, 486);
            splitContainer.SplitterDistance = 280;
            splitContainer.SplitterWidth = 6;
            splitContainer.TabIndex = 0;
            // 
            // drawingListBox
            // 
            drawingListBox.BorderStyle = System.Windows.Forms.BorderStyle.None;
            drawingListBox.Dock = System.Windows.Forms.DockStyle.Fill;
            drawingListBox.DrawMode = System.Windows.Forms.DrawMode.OwnerDrawVariable;
            drawingListBox.FormattingEnabled = true;
            drawingListBox.HideDepletedParts = false;
            drawingListBox.ItemHeight = 85;
            drawingListBox.Location = new System.Drawing.Point(0, 0);
            drawingListBox.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            drawingListBox.Name = "drawingListBox";
            drawingListBox.Size = new System.Drawing.Size(280, 486);
            drawingListBox.TabIndex = 0;
            drawingListBox.Units = Units.Inches;
            // 
            // gridPanel
            // 
            gridPanel.ColumnCount = 5;
            gridPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 20F));
            gridPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 20F));
            gridPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 20F));
            gridPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 20F));
            gridPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 20F));
            gridPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            gridPanel.Location = new System.Drawing.Point(0, 0);
            gridPanel.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            gridPanel.Name = "gridPanel";
            gridPanel.RowCount = 3;
            gridPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 33.33F));
            gridPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 33.34F));
            gridPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 33.33F));
            gridPanel.Size = new System.Drawing.Size(506, 444);
            gridPanel.TabIndex = 0;
            // 
            // navPanel
            // 
            navPanel.Controls.Add(btnPrev);
            navPanel.Controls.Add(txtPage);
            navPanel.Controls.Add(lblPageCount);
            navPanel.Controls.Add(btnNext);
            navPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
            navPanel.Location = new System.Drawing.Point(0, 444);
            navPanel.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            navPanel.Name = "navPanel";
            navPanel.Size = new System.Drawing.Size(506, 42);
            navPanel.TabIndex = 1;
            // 
            // btnPrev
            // 
            btnPrev.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            btnPrev.Location = new System.Drawing.Point(0, 0);
            btnPrev.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            btnPrev.Name = "btnPrev";
            btnPrev.Size = new System.Drawing.Size(93, 32);
            btnPrev.TabIndex = 0;
            btnPrev.Text = "<  Prev";
            btnPrev.Click += btnPrev_Click;
            // 
            // txtPage
            // 
            txtPage.Location = new System.Drawing.Point(0, 0);
            txtPage.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            txtPage.Name = "txtPage";
            txtPage.Size = new System.Drawing.Size(46, 23);
            txtPage.TabIndex = 1;
            txtPage.Text = "1";
            txtPage.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            txtPage.KeyDown += txtPage_KeyDown;
            // 
            // lblPageCount
            // 
            lblPageCount.Location = new System.Drawing.Point(0, 0);
            lblPageCount.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            lblPageCount.Name = "lblPageCount";
            lblPageCount.Size = new System.Drawing.Size(58, 32);
            lblPageCount.TabIndex = 2;
            lblPageCount.Text = "/ 1";
            lblPageCount.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // btnNext
            // 
            btnNext.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            btnNext.Location = new System.Drawing.Point(0, 0);
            btnNext.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            btnNext.Name = "btnNext";
            btnNext.Size = new System.Drawing.Size(93, 32);
            btnNext.TabIndex = 3;
            btnNext.Text = "Next  >";
            btnNext.Click += btnNext_Click;
            // 
            // BestFitViewerForm
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(792, 486);
            Controls.Add(splitContainer);
            KeyPreview = true;
            Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            Name = "BestFitViewerForm";
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "Best-Fit Viewer";
            WindowState = System.Windows.Forms.FormWindowState.Maximized;
            splitContainer.Panel1.ResumeLayout(false);
            splitContainer.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitContainer).EndInit();
            splitContainer.ResumeLayout(false);
            navPanel.ResumeLayout(false);
            navPanel.PerformLayout();
            ResumeLayout(false);
        }

        private System.Windows.Forms.SplitContainer splitContainer;
        private Controls.DrawingListBox drawingListBox;
        private System.Windows.Forms.TableLayoutPanel gridPanel;
        private System.Windows.Forms.Panel navPanel;
        private System.Windows.Forms.Button btnPrev;
        private System.Windows.Forms.Button btnNext;
        private System.Windows.Forms.TextBox txtPage;
        private System.Windows.Forms.Label lblPageCount;
    }
}
