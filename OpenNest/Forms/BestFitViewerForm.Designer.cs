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
            this.gridPanel = new System.Windows.Forms.TableLayoutPanel();
            this.navPanel = new System.Windows.Forms.Panel();
            this.btnPrev = new System.Windows.Forms.Button();
            this.btnNext = new System.Windows.Forms.Button();
            this.txtPage = new System.Windows.Forms.TextBox();
            this.lblPageCount = new System.Windows.Forms.Label();
            this.navPanel.SuspendLayout();
            this.SuspendLayout();
            //
            // gridPanel
            //
            this.gridPanel.ColumnCount = 5;
            this.gridPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 20F));
            this.gridPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 20F));
            this.gridPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 20F));
            this.gridPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 20F));
            this.gridPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 20F));
            this.gridPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gridPanel.Location = new System.Drawing.Point(0, 0);
            this.gridPanel.Name = "gridPanel";
            this.gridPanel.RowCount = 3;
            this.gridPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 33.33F));
            this.gridPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 33.34F));
            this.gridPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 33.33F));
            this.gridPanel.Size = new System.Drawing.Size(1200, 764);
            this.gridPanel.TabIndex = 0;
            //
            // navPanel
            //
            this.navPanel.Controls.Add(this.btnPrev);
            this.navPanel.Controls.Add(this.txtPage);
            this.navPanel.Controls.Add(this.lblPageCount);
            this.navPanel.Controls.Add(this.btnNext);
            this.navPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.navPanel.Location = new System.Drawing.Point(0, 764);
            this.navPanel.Name = "navPanel";
            this.navPanel.Size = new System.Drawing.Size(1200, 36);
            this.navPanel.TabIndex = 1;
            //
            // btnPrev
            //
            this.btnPrev.Anchor = System.Windows.Forms.AnchorStyles.Top;
            this.btnPrev.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnPrev.Location = new System.Drawing.Point(480, 4);
            this.btnPrev.Name = "btnPrev";
            this.btnPrev.Size = new System.Drawing.Size(80, 28);
            this.btnPrev.TabIndex = 0;
            this.btnPrev.Text = "<  Prev";
            this.btnPrev.Click += new System.EventHandler(this.btnPrev_Click);
            //
            // txtPage
            //
            this.txtPage.Anchor = System.Windows.Forms.AnchorStyles.Top;
            this.txtPage.Location = new System.Drawing.Point(564, 7);
            this.txtPage.Name = "txtPage";
            this.txtPage.Size = new System.Drawing.Size(40, 20);
            this.txtPage.TabIndex = 1;
            this.txtPage.Text = "1";
            this.txtPage.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            this.txtPage.KeyDown += new System.Windows.Forms.KeyEventHandler(this.txtPage_KeyDown);
            //
            // lblPageCount
            //
            this.lblPageCount.Anchor = System.Windows.Forms.AnchorStyles.Top;
            this.lblPageCount.Location = new System.Drawing.Point(606, 4);
            this.lblPageCount.Name = "lblPageCount";
            this.lblPageCount.Size = new System.Drawing.Size(50, 28);
            this.lblPageCount.TabIndex = 2;
            this.lblPageCount.Text = "/ 1";
            this.lblPageCount.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // btnNext
            //
            this.btnNext.Anchor = System.Windows.Forms.AnchorStyles.Top;
            this.btnNext.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnNext.Location = new System.Drawing.Point(660, 4);
            this.btnNext.Name = "btnNext";
            this.btnNext.Size = new System.Drawing.Size(80, 28);
            this.btnNext.TabIndex = 3;
            this.btnNext.Text = "Next  >";
            this.btnNext.Click += new System.EventHandler(this.btnNext_Click);
            //
            // BestFitViewerForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1200, 800);
            this.Controls.Add(this.gridPanel);
            this.Controls.Add(this.navPanel);
            this.KeyPreview = true;
            this.Name = "BestFitViewerForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Best-Fit Viewer";
            this.WindowState = System.Windows.Forms.FormWindowState.Maximized;
            this.navPanel.ResumeLayout(false);
            this.navPanel.PerformLayout();
            this.ResumeLayout(false);
        }

        private System.Windows.Forms.TableLayoutPanel gridPanel;
        private System.Windows.Forms.Panel navPanel;
        private System.Windows.Forms.Button btnPrev;
        private System.Windows.Forms.Button btnNext;
        private System.Windows.Forms.TextBox txtPage;
        private System.Windows.Forms.Label lblPageCount;
    }
}
