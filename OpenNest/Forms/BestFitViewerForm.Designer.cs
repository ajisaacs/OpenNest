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
            this.toolbarPanel = new System.Windows.Forms.Panel();
            this.lblDrawing = new System.Windows.Forms.Label();
            this.cboDrawing = new System.Windows.Forms.ComboBox();
            this.navPanel = new System.Windows.Forms.Panel();
            this.btnPrev = new System.Windows.Forms.Button();
            this.btnNext = new System.Windows.Forms.Button();
            this.txtPage = new System.Windows.Forms.TextBox();
            this.lblPageCount = new System.Windows.Forms.Label();
            this.toolbarPanel.SuspendLayout();
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
            this.gridPanel.Location = new System.Drawing.Point(0, 32);
            this.gridPanel.Name = "gridPanel";
            this.gridPanel.RowCount = 3;
            this.gridPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 33.33F));
            this.gridPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 33.34F));
            this.gridPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 33.33F));
            this.gridPanel.Size = new System.Drawing.Size(1200, 732);
            this.gridPanel.TabIndex = 0;
            //
            // toolbarPanel
            //
            this.toolbarPanel.Controls.Add(this.lblDrawing);
            this.toolbarPanel.Controls.Add(this.cboDrawing);
            this.toolbarPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.toolbarPanel.Location = new System.Drawing.Point(0, 0);
            this.toolbarPanel.Name = "toolbarPanel";
            this.toolbarPanel.Size = new System.Drawing.Size(1200, 32);
            this.toolbarPanel.TabIndex = 2;
            //
            // lblDrawing
            //
            this.lblDrawing.Location = new System.Drawing.Point(6, 0);
            this.lblDrawing.Name = "lblDrawing";
            this.lblDrawing.Size = new System.Drawing.Size(55, 32);
            this.lblDrawing.TabIndex = 0;
            this.lblDrawing.Text = "Drawing:";
            this.lblDrawing.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // cboDrawing
            //
            this.cboDrawing.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboDrawing.Location = new System.Drawing.Point(64, 5);
            this.cboDrawing.Name = "cboDrawing";
            this.cboDrawing.Size = new System.Drawing.Size(250, 21);
            this.cboDrawing.TabIndex = 1;
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
            this.btnPrev.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnPrev.Name = "btnPrev";
            this.btnPrev.Size = new System.Drawing.Size(80, 28);
            this.btnPrev.TabIndex = 0;
            this.btnPrev.Text = "<  Prev";
            this.btnPrev.Click += new System.EventHandler(this.btnPrev_Click);
            //
            // txtPage
            //
            this.txtPage.Name = "txtPage";
            this.txtPage.Size = new System.Drawing.Size(40, 20);
            this.txtPage.TabIndex = 1;
            this.txtPage.Text = "1";
            this.txtPage.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            this.txtPage.KeyDown += new System.Windows.Forms.KeyEventHandler(this.txtPage_KeyDown);
            //
            // lblPageCount
            //
            this.lblPageCount.Name = "lblPageCount";
            this.lblPageCount.Size = new System.Drawing.Size(50, 28);
            this.lblPageCount.TabIndex = 2;
            this.lblPageCount.Text = "/ 1";
            this.lblPageCount.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // btnNext
            //
            this.btnNext.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
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
            this.Controls.Add(this.toolbarPanel);
            this.Controls.Add(this.navPanel);
            this.KeyPreview = true;
            this.Name = "BestFitViewerForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Best-Fit Viewer";
            this.WindowState = System.Windows.Forms.FormWindowState.Maximized;
            this.toolbarPanel.ResumeLayout(false);
            this.navPanel.ResumeLayout(false);
            this.navPanel.PerformLayout();
            this.ResumeLayout(false);
        }

        private System.Windows.Forms.TableLayoutPanel gridPanel;
        private System.Windows.Forms.Panel toolbarPanel;
        private System.Windows.Forms.Label lblDrawing;
        private System.Windows.Forms.ComboBox cboDrawing;
        private System.Windows.Forms.Panel navPanel;
        private System.Windows.Forms.Button btnPrev;
        private System.Windows.Forms.Button btnNext;
        private System.Windows.Forms.TextBox txtPage;
        private System.Windows.Forms.Label lblPageCount;
    }
}
