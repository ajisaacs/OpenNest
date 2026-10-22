namespace OpenNest.Forms
{
    partial class DrawingSetsForm
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
            browser = new OpenNest.Controls.DrawingSetBrowser();
            buttonPanel = new System.Windows.Forms.FlowLayoutPanel();
            closeButton = new System.Windows.Forms.Button();
            layoutTable.SuspendLayout();
            buttonPanel.SuspendLayout();
            SuspendLayout();
            //
            // layoutTable
            //
            layoutTable.ColumnCount = 1;
            layoutTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            layoutTable.Controls.Add(browser, 0, 0);
            layoutTable.Controls.Add(buttonPanel, 0, 1);
            layoutTable.Dock = System.Windows.Forms.DockStyle.Fill;
            layoutTable.Location = new System.Drawing.Point(0, 0);
            layoutTable.Name = "layoutTable";
            layoutTable.Padding = new System.Windows.Forms.Padding(8);
            layoutTable.RowCount = 2;
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.Size = new System.Drawing.Size(984, 601);
            layoutTable.TabIndex = 0;
            //
            // browser
            //
            browser.Dock = System.Windows.Forms.DockStyle.Fill;
            browser.Margin = new System.Windows.Forms.Padding(0);
            browser.Name = "browser";
            browser.TabIndex = 0;
            //
            // buttonPanel
            //
            buttonPanel.AutoSize = true;
            buttonPanel.Controls.Add(closeButton);
            buttonPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            buttonPanel.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
            buttonPanel.Margin = new System.Windows.Forms.Padding(0, 6, 0, 0);
            buttonPanel.Name = "buttonPanel";
            buttonPanel.TabIndex = 1;
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
            // DrawingSetsForm
            //
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            CancelButton = closeButton;
            ClientSize = new System.Drawing.Size(984, 601);
            Controls.Add(layoutTable);
            Font = new System.Drawing.Font("Segoe UI", 9.75F);
            MinimizeBox = false;
            MinimumSize = new System.Drawing.Size(640, 380);
            Name = "DrawingSetsForm";
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "Drawing Sets";
            Shown += DrawingSetsForm_Shown;
            layoutTable.ResumeLayout(false);
            layoutTable.PerformLayout();
            buttonPanel.ResumeLayout(false);
            buttonPanel.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel layoutTable;
        private OpenNest.Controls.DrawingSetBrowser browser;
        private System.Windows.Forms.FlowLayoutPanel buttonPanel;
        private System.Windows.Forms.Button closeButton;
    }
}
