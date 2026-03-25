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
            mainSplitter = new System.Windows.Forms.SplitContainer();
            sidebarSplitter = new System.Windows.Forms.SplitContainer();
            fileList = new OpenNest.Controls.FileListControl();
            filterPanel = new OpenNest.Controls.FilterPanel();
            rightPanel = new System.Windows.Forms.Panel();
            entityView1 = new OpenNest.Controls.EntityView();
            detailBar = new System.Windows.Forms.Panel();
            lblDimensions = new System.Windows.Forms.Label();
            lblEntityCount = new System.Windows.Forms.Label();
            numQuantity = new System.Windows.Forms.NumericUpDown();
            txtCustomer = new System.Windows.Forms.TextBox();
            btnSplit = new System.Windows.Forms.Button();
            cboBendDetector = new System.Windows.Forms.ComboBox();
            lblQty = new System.Windows.Forms.Label();
            lblCust = new System.Windows.Forms.Label();
            lblDetect = new System.Windows.Forms.Label();
            bottomPanel1 = new OpenNest.Controls.BottomPanel();
            cancelButton = new System.Windows.Forms.Button();
            acceptButton = new System.Windows.Forms.Button();

            ((System.ComponentModel.ISupportInitialize)mainSplitter).BeginInit();
            mainSplitter.Panel1.SuspendLayout();
            mainSplitter.Panel2.SuspendLayout();
            mainSplitter.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)sidebarSplitter).BeginInit();
            sidebarSplitter.Panel1.SuspendLayout();
            sidebarSplitter.Panel2.SuspendLayout();
            sidebarSplitter.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)numQuantity).BeginInit();
            bottomPanel1.SuspendLayout();
            SuspendLayout();

            // mainSplitter (sidebar | preview)
            mainSplitter.Dock = System.Windows.Forms.DockStyle.Fill;
            mainSplitter.SplitterDistance = 260;
            mainSplitter.SplitterWidth = 3;
            mainSplitter.Panel1.Controls.Add(sidebarSplitter);
            mainSplitter.Panel2.Controls.Add(rightPanel);

            // sidebarSplitter (file list | filter panel)
            sidebarSplitter.Dock = System.Windows.Forms.DockStyle.Fill;
            sidebarSplitter.Orientation = System.Windows.Forms.Orientation.Horizontal;
            sidebarSplitter.SplitterDistance = 280;
            sidebarSplitter.SplitterWidth = 3;
            sidebarSplitter.Panel1.Controls.Add(fileList);
            sidebarSplitter.Panel2.Controls.Add(filterPanel);

            // fileList
            fileList.Dock = System.Windows.Forms.DockStyle.Fill;
            fileList.AllowDrop = true;

            // filterPanel
            filterPanel.Dock = System.Windows.Forms.DockStyle.Fill;

            // rightPanel
            rightPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            rightPanel.Controls.Add(entityView1);
            rightPanel.Controls.Add(detailBar);

            // entityView1
            entityView1.BackColor = System.Drawing.Color.FromArgb(33, 40, 48);
            entityView1.Cursor = System.Windows.Forms.Cursors.Cross;
            entityView1.Dock = System.Windows.Forms.DockStyle.Fill;

            // detailBar
            detailBar.Dock = System.Windows.Forms.DockStyle.Bottom;
            detailBar.Height = 36;
            detailBar.BackColor = System.Drawing.Color.FromArgb(245, 245, 245);
            detailBar.Padding = new System.Windows.Forms.Padding(6, 4, 6, 4);

            lblQty = new System.Windows.Forms.Label { Text = "Qty:", AutoSize = true, Location = new System.Drawing.Point(6, 9), Font = new System.Drawing.Font("Segoe UI", 9f) };
            numQuantity.Location = new System.Drawing.Point(35, 5);
            numQuantity.Size = new System.Drawing.Size(50, 24);
            numQuantity.Minimum = 1;
            numQuantity.Maximum = 9999;
            numQuantity.Value = 1;
            numQuantity.Font = new System.Drawing.Font("Segoe UI", 9f);

            lblCust = new System.Windows.Forms.Label { Text = "Customer:", AutoSize = true, Location = new System.Drawing.Point(95, 9), Font = new System.Drawing.Font("Segoe UI", 9f) };
            txtCustomer.Location = new System.Drawing.Point(165, 5);
            txtCustomer.Size = new System.Drawing.Size(120, 24);
            txtCustomer.Font = new System.Drawing.Font("Segoe UI", 9f);
            txtCustomer.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;

            lblDimensions = new System.Windows.Forms.Label { AutoSize = true, Location = new System.Drawing.Point(300, 9), Font = new System.Drawing.Font("Segoe UI", 9f), ForeColor = System.Drawing.Color.Gray };
            lblEntityCount = new System.Windows.Forms.Label { AutoSize = true, Location = new System.Drawing.Point(420, 9), Font = new System.Drawing.Font("Segoe UI", 9f), ForeColor = System.Drawing.Color.Gray };

            btnSplit = new System.Windows.Forms.Button { Text = "Split...", Location = new System.Drawing.Point(520, 4), Size = new System.Drawing.Size(60, 28), FlatStyle = System.Windows.Forms.FlatStyle.Flat, Font = new System.Drawing.Font("Segoe UI", 9f) };

            lblDetect = new System.Windows.Forms.Label { Text = "Bends:", AutoSize = true, Location = new System.Drawing.Point(590, 9), Font = new System.Drawing.Font("Segoe UI", 9f) };
            cboBendDetector.Location = new System.Drawing.Point(638, 5);
            cboBendDetector.Size = new System.Drawing.Size(100, 24);
            cboBendDetector.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            cboBendDetector.Font = new System.Drawing.Font("Segoe UI", 9f);

            detailBar.Controls.AddRange(new System.Windows.Forms.Control[] {
                lblQty, numQuantity, lblCust, txtCustomer,
                lblDimensions, lblEntityCount, btnSplit,
                lblDetect, cboBendDetector
            });

            // bottomPanel1
            bottomPanel1.Dock = System.Windows.Forms.DockStyle.Bottom;
            bottomPanel1.Height = 50;
            bottomPanel1.Controls.Add(cancelButton);
            bottomPanel1.Controls.Add(acceptButton);

            cancelButton.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
            cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            cancelButton.Location = new System.Drawing.Point(826, 10);
            cancelButton.Size = new System.Drawing.Size(90, 28);
            cancelButton.Text = "Cancel";
            cancelButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            cancelButton.Font = new System.Drawing.Font("Segoe UI", 9f);

            acceptButton.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
            acceptButton.DialogResult = System.Windows.Forms.DialogResult.OK;
            acceptButton.Location = new System.Drawing.Point(730, 10);
            acceptButton.Size = new System.Drawing.Size(90, 28);
            acceptButton.Text = "Accept";
            acceptButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            acceptButton.Font = new System.Drawing.Font("Segoe UI", 9f);

            // CadConverterForm
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            ClientSize = new System.Drawing.Size(1024, 720);
            Controls.Add(mainSplitter);
            Controls.Add(bottomPanel1);
            Font = new System.Drawing.Font("Segoe UI", 9f);
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "CAD Converter";
            AllowDrop = true;

            ((System.ComponentModel.ISupportInitialize)mainSplitter).EndInit();
            mainSplitter.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)sidebarSplitter).EndInit();
            sidebarSplitter.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)numQuantity).EndInit();
            bottomPanel1.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.SplitContainer mainSplitter;
        private System.Windows.Forms.SplitContainer sidebarSplitter;
        private Controls.FileListControl fileList;
        private Controls.FilterPanel filterPanel;
        private System.Windows.Forms.Panel rightPanel;
        private Controls.EntityView entityView1;
        private System.Windows.Forms.Panel detailBar;
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
