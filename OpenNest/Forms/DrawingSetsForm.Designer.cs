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
            splitContainer = new System.Windows.Forms.SplitContainer();
            releasePanel = new OpenNest.Controls.ReleasePanel();
            tabs = new System.Windows.Forms.TabControl();
            libraryTab = new System.Windows.Forms.TabPage();
            libraryView = new OpenNest.Controls.DrawingLibraryView();
            setsTab = new System.Windows.Forms.TabPage();
            browser = new OpenNest.Controls.DrawingSetBrowser();
            releasesTab = new System.Windows.Forms.TabPage();
            releasesView = new OpenNest.Controls.SetReleasesView();
            buttonPanel = new System.Windows.Forms.FlowLayoutPanel();
            closeButton = new System.Windows.Forms.Button();
            layoutTable.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)splitContainer).BeginInit();
            splitContainer.Panel1.SuspendLayout();
            splitContainer.Panel2.SuspendLayout();
            splitContainer.SuspendLayout();
            tabs.SuspendLayout();
            libraryTab.SuspendLayout();
            setsTab.SuspendLayout();
            releasesTab.SuspendLayout();
            buttonPanel.SuspendLayout();
            SuspendLayout();
            //
            // layoutTable
            //
            layoutTable.ColumnCount = 1;
            layoutTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            layoutTable.Controls.Add(splitContainer, 0, 0);
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
            // splitContainer
            //
            splitContainer.Dock = System.Windows.Forms.DockStyle.Fill;
            splitContainer.FixedPanel = System.Windows.Forms.FixedPanel.Panel2;
            splitContainer.Margin = new System.Windows.Forms.Padding(0);
            splitContainer.Name = "splitContainer";
            splitContainer.Panel1.Controls.Add(tabs);
            splitContainer.Panel2.Controls.Add(releasePanel);
            splitContainer.Panel2.Padding = new System.Windows.Forms.Padding(6, 0, 0, 0);
            splitContainer.Panel2Collapsed = true;
            splitContainer.Size = new System.Drawing.Size(1008, 580);
            splitContainer.SplitterDistance = 560;
            splitContainer.TabIndex = 0;
            //
            // releasePanel
            //
            releasePanel.Dock = System.Windows.Forms.DockStyle.Fill;
            releasePanel.Margin = new System.Windows.Forms.Padding(0);
            releasePanel.Name = "releasePanel";
            releasePanel.TabIndex = 0;
            //
            // tabs
            //
            tabs.Controls.Add(libraryTab);
            tabs.Controls.Add(setsTab);
            tabs.Controls.Add(releasesTab);
            tabs.Dock = System.Windows.Forms.DockStyle.Fill;
            tabs.Margin = new System.Windows.Forms.Padding(0);
            tabs.Name = "tabs";
            tabs.SelectedIndex = 1;
            tabs.TabIndex = 0;
            tabs.SelectedIndexChanged += Tabs_SelectedIndexChanged;
            //
            // libraryTab
            //
            libraryTab.Controls.Add(libraryView);
            libraryTab.Name = "libraryTab";
            libraryTab.Padding = new System.Windows.Forms.Padding(6);
            libraryTab.TabIndex = 0;
            libraryTab.Text = "Library";
            libraryTab.UseVisualStyleBackColor = true;
            //
            // libraryView
            //
            libraryView.Dock = System.Windows.Forms.DockStyle.Fill;
            libraryView.Margin = new System.Windows.Forms.Padding(0);
            libraryView.Name = "libraryView";
            libraryView.TabIndex = 0;
            //
            // setsTab
            //
            setsTab.Controls.Add(browser);
            setsTab.Name = "setsTab";
            setsTab.Padding = new System.Windows.Forms.Padding(6);
            setsTab.TabIndex = 1;
            setsTab.Text = "Sets";
            setsTab.UseVisualStyleBackColor = true;
            //
            // browser
            //
            browser.Dock = System.Windows.Forms.DockStyle.Fill;
            browser.Margin = new System.Windows.Forms.Padding(0);
            browser.Name = "browser";
            browser.TabIndex = 0;
            //
            // releasesTab
            //
            releasesTab.Controls.Add(releasesView);
            releasesTab.Name = "releasesTab";
            releasesTab.Padding = new System.Windows.Forms.Padding(6);
            releasesTab.TabIndex = 2;
            releasesTab.Text = "Releases";
            releasesTab.UseVisualStyleBackColor = true;
            //
            // releasesView
            //
            releasesView.Dock = System.Windows.Forms.DockStyle.Fill;
            releasesView.Margin = new System.Windows.Forms.Padding(0);
            releasesView.Name = "releasesView";
            releasesView.TabIndex = 0;
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
            ClientSize = new System.Drawing.Size(1024, 641);
            Controls.Add(layoutTable);
            Font = new System.Drawing.Font("Segoe UI", 9.75F);
            MinimumSize = new System.Drawing.Size(720, 440);
            Name = "DrawingSetsForm";
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Activated += DrawingSetsForm_Activated;
            FormClosing += DrawingSetsForm_FormClosing;
            Text = "Drawing Sets";
            Shown += DrawingSetsForm_Shown;
            layoutTable.ResumeLayout(false);
            layoutTable.PerformLayout();
            splitContainer.Panel1.ResumeLayout(false);
            splitContainer.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitContainer).EndInit();
            splitContainer.ResumeLayout(false);
            tabs.ResumeLayout(false);
            libraryTab.ResumeLayout(false);
            setsTab.ResumeLayout(false);
            releasesTab.ResumeLayout(false);
            buttonPanel.ResumeLayout(false);
            buttonPanel.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel layoutTable;
        private System.Windows.Forms.SplitContainer splitContainer;
        private OpenNest.Controls.ReleasePanel releasePanel;
        private System.Windows.Forms.TabControl tabs;
        private System.Windows.Forms.TabPage libraryTab;
        private OpenNest.Controls.DrawingLibraryView libraryView;
        private System.Windows.Forms.TabPage setsTab;
        private OpenNest.Controls.DrawingSetBrowser browser;
        private System.Windows.Forms.TabPage releasesTab;
        private OpenNest.Controls.SetReleasesView releasesView;
        private System.Windows.Forms.FlowLayoutPanel buttonPanel;
        private System.Windows.Forms.Button closeButton;
    }
}
