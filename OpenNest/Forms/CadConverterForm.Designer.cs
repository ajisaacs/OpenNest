namespace OpenNest.Forms
{
    partial class CadConverterForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            sidebarPanel = new System.Windows.Forms.Panel();
            fileList = new OpenNest.Controls.FileListControl();
            filterPanel = new OpenNest.Controls.FilterPanel();
            splitterSidebar = new System.Windows.Forms.Splitter();
            entityView1 = new OpenNest.Controls.EntityView();
            detailBar = new System.Windows.Forms.FlowLayoutPanel();
            lblQty = new System.Windows.Forms.Label();
            numQuantity = new System.Windows.Forms.NumericUpDown();
            lblCust = new System.Windows.Forms.Label();
            txtCustomer = new System.Windows.Forms.TextBox();
            lblDimensions = new System.Windows.Forms.Label();
            lblEntityCount = new System.Windows.Forms.Label();
            btnSplit = new System.Windows.Forms.Button();
            lblDetect = new System.Windows.Forms.Label();
            cboBendDetector = new System.Windows.Forms.ComboBox();
            bottomPanel1 = new OpenNest.Controls.BottomPanel();
            cancelButton = new System.Windows.Forms.Button();
            acceptButton = new System.Windows.Forms.Button();

            ((System.ComponentModel.ISupportInitialize)numQuantity).BeginInit();
            sidebarPanel.SuspendLayout();
            bottomPanel1.SuspendLayout();
            SuspendLayout();

            //
            // sidebarPanel (Left dock — contains file list + filter panel)
            //
            sidebarPanel.Dock = System.Windows.Forms.DockStyle.Left;
            sidebarPanel.Name = "sidebarPanel";
            sidebarPanel.Size = new System.Drawing.Size(260, 670);
            sidebarPanel.Controls.Add(filterPanel);
            sidebarPanel.Controls.Add(fileList);

            //
            // fileList (Top of sidebar)
            //
            fileList.Dock = System.Windows.Forms.DockStyle.Top;
            fileList.AllowDrop = true;
            fileList.Name = "fileList";
            fileList.Size = new System.Drawing.Size(260, 300);

            //
            // filterPanel (Fill remainder of sidebar)
            //
            filterPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            filterPanel.Name = "filterPanel";
            filterPanel.Size = new System.Drawing.Size(260, 370);

            //
            // splitterSidebar (between sidebar and preview)
            //
            splitterSidebar.Location = new System.Drawing.Point(260, 0);
            splitterSidebar.Name = "splitterSidebar";
            splitterSidebar.Size = new System.Drawing.Size(3, 670);
            splitterSidebar.TabStop = false;

            //
            // entityView1 (Fill — main preview area)
            //
            entityView1.BackColor = System.Drawing.Color.FromArgb(33, 40, 48);
            entityView1.Cursor = System.Windows.Forms.Cursors.Cross;
            entityView1.Dock = System.Windows.Forms.DockStyle.Fill;
            entityView1.Name = "entityView1";
            entityView1.Size = new System.Drawing.Size(761, 634);

            //
            // detailBar (Bottom of preview area)
            //
            detailBar.Dock = System.Windows.Forms.DockStyle.Bottom;
            detailBar.Name = "detailBar";
            detailBar.Size = new System.Drawing.Size(761, 36);
            detailBar.BackColor = System.Drawing.Color.FromArgb(245, 245, 245);
            detailBar.Padding = new System.Windows.Forms.Padding(4, 6, 4, 4);
            detailBar.WrapContents = false;

            //
            // lblQty
            //
            lblQty.Text = "Qty:";
            lblQty.AutoSize = true;
            lblQty.Font = new System.Drawing.Font("Segoe UI", 9f);
            lblQty.Margin = new System.Windows.Forms.Padding(2, 3, 0, 0);

            //
            // numQuantity
            //
            numQuantity.Size = new System.Drawing.Size(50, 24);
            numQuantity.Minimum = 1;
            numQuantity.Maximum = 9999;
            numQuantity.Value = 1;
            numQuantity.Font = new System.Drawing.Font("Segoe UI", 9f);
            numQuantity.Margin = new System.Windows.Forms.Padding(2, 0, 8, 0);

            //
            // lblCust
            //
            lblCust.Text = "Customer:";
            lblCust.AutoSize = true;
            lblCust.Font = new System.Drawing.Font("Segoe UI", 9f);
            lblCust.Margin = new System.Windows.Forms.Padding(2, 3, 0, 0);

            //
            // txtCustomer
            //
            txtCustomer.Size = new System.Drawing.Size(100, 24);
            txtCustomer.Font = new System.Drawing.Font("Segoe UI", 9f);
            txtCustomer.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            txtCustomer.Margin = new System.Windows.Forms.Padding(2, 0, 8, 0);

            //
            // lblDimensions
            //
            lblDimensions.AutoSize = true;
            lblDimensions.Font = new System.Drawing.Font("Segoe UI", 9f);
            lblDimensions.ForeColor = System.Drawing.Color.Gray;
            lblDimensions.Margin = new System.Windows.Forms.Padding(2, 3, 8, 0);

            //
            // lblEntityCount
            //
            lblEntityCount.AutoSize = true;
            lblEntityCount.Font = new System.Drawing.Font("Segoe UI", 9f);
            lblEntityCount.ForeColor = System.Drawing.Color.Gray;
            lblEntityCount.Margin = new System.Windows.Forms.Padding(2, 3, 8, 0);

            //
            // btnSplit
            //
            btnSplit.Text = "Split...";
            btnSplit.Size = new System.Drawing.Size(60, 24);
            btnSplit.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            btnSplit.Font = new System.Drawing.Font("Segoe UI", 9f);
            btnSplit.Margin = new System.Windows.Forms.Padding(2, 0, 8, 0);

            //
            // lblDetect
            //
            lblDetect.Text = "Bends:";
            lblDetect.AutoSize = true;
            lblDetect.Font = new System.Drawing.Font("Segoe UI", 9f);
            lblDetect.Margin = new System.Windows.Forms.Padding(2, 3, 0, 0);

            //
            // cboBendDetector
            //
            cboBendDetector.Size = new System.Drawing.Size(90, 24);
            cboBendDetector.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            cboBendDetector.Font = new System.Drawing.Font("Segoe UI", 9f);
            cboBendDetector.Margin = new System.Windows.Forms.Padding(2, 0, 0, 0);

            detailBar.Controls.AddRange(new System.Windows.Forms.Control[] {
                lblQty, numQuantity, lblCust, txtCustomer,
                lblDimensions, lblEntityCount, btnSplit,
                lblDetect, cboBendDetector
            });

            //
            // bottomPanel1
            //
            bottomPanel1.Dock = System.Windows.Forms.DockStyle.Bottom;
            bottomPanel1.Name = "bottomPanel1";
            bottomPanel1.Size = new System.Drawing.Size(1024, 50);
            bottomPanel1.Controls.Add(cancelButton);
            bottomPanel1.Controls.Add(acceptButton);

            //
            // cancelButton
            //
            cancelButton.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
            cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            cancelButton.Location = new System.Drawing.Point(922, 10);
            cancelButton.Size = new System.Drawing.Size(90, 28);
            cancelButton.Text = "Cancel";
            cancelButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            cancelButton.Font = new System.Drawing.Font("Segoe UI", 9f);

            //
            // acceptButton
            //
            acceptButton.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
            acceptButton.DialogResult = System.Windows.Forms.DialogResult.OK;
            acceptButton.Location = new System.Drawing.Point(826, 10);
            acceptButton.Size = new System.Drawing.Size(90, 28);
            acceptButton.Text = "Accept";
            acceptButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            acceptButton.Font = new System.Drawing.Font("Segoe UI", 9f);

            //
            // CadConverterForm
            // Add order: Fill last so it gets remaining space
            //
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            ClientSize = new System.Drawing.Size(1024, 720);
            Controls.Add(entityView1);
            Controls.Add(detailBar);
            Controls.Add(splitterSidebar);
            Controls.Add(sidebarPanel);
            Controls.Add(bottomPanel1);
            Font = new System.Drawing.Font("Segoe UI", 9f);
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "CAD Converter";
            AllowDrop = true;

            ((System.ComponentModel.ISupportInitialize)numQuantity).EndInit();
            sidebarPanel.ResumeLayout(false);
            bottomPanel1.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.Panel sidebarPanel;
        private System.Windows.Forms.Splitter splitterSidebar;
        private Controls.FileListControl fileList;
        private Controls.FilterPanel filterPanel;
        private Controls.EntityView entityView1;
        private System.Windows.Forms.FlowLayoutPanel detailBar;
        private System.Windows.Forms.Label lblDimensions;
        private System.Windows.Forms.Label lblEntityCount;
        private System.Windows.Forms.NumericUpDown numQuantity;
        private System.Windows.Forms.TextBox txtCustomer;
        private System.Windows.Forms.Button btnSplit;
        private System.Windows.Forms.ComboBox cboBendDetector;
        private System.Windows.Forms.Label lblQty;
        private System.Windows.Forms.Label lblCust;
        private System.Windows.Forms.Label lblDetect;
        private Controls.BottomPanel bottomPanel1;
        private System.Windows.Forms.Button acceptButton;
        private System.Windows.Forms.Button cancelButton;
    }
}
