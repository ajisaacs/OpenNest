namespace OpenNest.Forms
{
    partial class BomImportForm
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
            grpInput = new System.Windows.Forms.GroupBox();
            txtJobName = new System.Windows.Forms.TextBox();
            txtBomFile = new System.Windows.Forms.TextBox();
            btnBrowseBom = new System.Windows.Forms.Button();
            txtDxfFolder = new System.Windows.Forms.TextBox();
            btnBrowseDxf = new System.Windows.Forms.Button();
            txtPlateWidth = new System.Windows.Forms.TextBox();
            txtPlateLength = new System.Windows.Forms.TextBox();
            btnAnalyze = new System.Windows.Forms.Button();
            grpGroups = new System.Windows.Forms.GroupBox();
            dgvGroups = new System.Windows.Forms.DataGridView();
            pnlBottom = new System.Windows.Forms.Panel();
            lblSummary = new System.Windows.Forms.Label();
            btnClose = new System.Windows.Forms.Button();
            btnCreateNests = new System.Windows.Forms.Button();

            var tbl = new System.Windows.Forms.TableLayoutPanel();
            var lblJobName = new System.Windows.Forms.Label();
            var lblBomFile = new System.Windows.Forms.Label();
            var lblDxfFolder = new System.Windows.Forms.Label();
            var lblPlateSize = new System.Windows.Forms.Label();
            var lblPlateX = new System.Windows.Forms.Label();
            var platePanel = new System.Windows.Forms.FlowLayoutPanel();

            grpInput.SuspendLayout();
            grpGroups.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)dgvGroups).BeginInit();
            pnlBottom.SuspendLayout();
            SuspendLayout();

            // ---- TableLayoutPanel for input fields ----
            tbl.ColumnCount = 3;
            tbl.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
            tbl.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            tbl.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
            tbl.RowCount = 5;
            tbl.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            tbl.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            tbl.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            tbl.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            tbl.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            tbl.Dock = System.Windows.Forms.DockStyle.Fill;
            tbl.Padding = new System.Windows.Forms.Padding(3);

            // Row 0 — Job Name
            lblJobName.Text = "Job Name:";
            lblJobName.AutoSize = true;
            lblJobName.Anchor = System.Windows.Forms.AnchorStyles.Left;
            lblJobName.Margin = new System.Windows.Forms.Padding(3, 6, 3, 3);
            tbl.Controls.Add(lblJobName, 0, 0);

            txtJobName.Dock = System.Windows.Forms.DockStyle.Fill;
            txtJobName.Margin = new System.Windows.Forms.Padding(3, 6, 3, 3);
            tbl.Controls.Add(txtJobName, 1, 0);
            tbl.SetColumnSpan(txtJobName, 2);

            // Row 1 — BOM File
            lblBomFile.Text = "BOM File:";
            lblBomFile.AutoSize = true;
            lblBomFile.Anchor = System.Windows.Forms.AnchorStyles.Left;
            lblBomFile.Margin = new System.Windows.Forms.Padding(3, 6, 3, 3);
            tbl.Controls.Add(lblBomFile, 0, 1);

            txtBomFile.Dock = System.Windows.Forms.DockStyle.Fill;
            txtBomFile.ReadOnly = true;
            txtBomFile.Margin = new System.Windows.Forms.Padding(3, 6, 3, 3);
            tbl.Controls.Add(txtBomFile, 1, 1);

            btnBrowseBom.Text = "...";
            btnBrowseBom.Size = new System.Drawing.Size(35, 25);
            btnBrowseBom.Margin = new System.Windows.Forms.Padding(0, 5, 3, 3);
            btnBrowseBom.Click += new System.EventHandler(BrowseBom_Click);
            tbl.Controls.Add(btnBrowseBom, 2, 1);

            // Row 2 — DXF Folder
            lblDxfFolder.Text = "DXF Folder:";
            lblDxfFolder.AutoSize = true;
            lblDxfFolder.Anchor = System.Windows.Forms.AnchorStyles.Left;
            lblDxfFolder.Margin = new System.Windows.Forms.Padding(3, 6, 3, 3);
            tbl.Controls.Add(lblDxfFolder, 0, 2);

            txtDxfFolder.Dock = System.Windows.Forms.DockStyle.Fill;
            txtDxfFolder.ReadOnly = true;
            txtDxfFolder.Margin = new System.Windows.Forms.Padding(3, 6, 3, 3);
            tbl.Controls.Add(txtDxfFolder, 1, 2);

            btnBrowseDxf.Text = "...";
            btnBrowseDxf.Size = new System.Drawing.Size(35, 25);
            btnBrowseDxf.Margin = new System.Windows.Forms.Padding(0, 5, 3, 3);
            btnBrowseDxf.Click += new System.EventHandler(BrowseDxf_Click);
            tbl.Controls.Add(btnBrowseDxf, 2, 2);

            // Row 3 — Plate Size
            lblPlateSize.Text = "Plate Size:";
            lblPlateSize.AutoSize = true;
            lblPlateSize.Anchor = System.Windows.Forms.AnchorStyles.Left;
            lblPlateSize.Margin = new System.Windows.Forms.Padding(3, 6, 3, 3);
            tbl.Controls.Add(lblPlateSize, 0, 3);

            platePanel.AutoSize = true;
            platePanel.WrapContents = false;
            platePanel.Margin = new System.Windows.Forms.Padding(0, 3, 0, 3);
            txtPlateWidth.Size = new System.Drawing.Size(60, 23);
            txtPlateWidth.Text = "60";
            lblPlateX.Text = " x ";
            lblPlateX.AutoSize = true;
            lblPlateX.Anchor = System.Windows.Forms.AnchorStyles.Left;
            txtPlateLength.Size = new System.Drawing.Size(60, 23);
            txtPlateLength.Text = "120";
            platePanel.Controls.Add(txtPlateWidth);
            platePanel.Controls.Add(lblPlateX);
            platePanel.Controls.Add(txtPlateLength);
            tbl.Controls.Add(platePanel, 1, 3);

            // Row 4 — Analyze button
            btnAnalyze.Text = "Analyze";
            btnAnalyze.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            btnAnalyze.Size = new System.Drawing.Size(110, 30);
            btnAnalyze.Margin = new System.Windows.Forms.Padding(3, 6, 3, 3);
            btnAnalyze.Anchor = System.Windows.Forms.AnchorStyles.Right;
            btnAnalyze.Click += new System.EventHandler(Analyze_Click);
            tbl.Controls.Add(btnAnalyze, 1, 4);
            tbl.SetColumnSpan(btnAnalyze, 2);

            // ---- grpInput ----
            grpInput.Controls.Add(tbl);
            grpInput.Dock = System.Windows.Forms.DockStyle.Top;
            grpInput.Name = "grpInput";
            grpInput.Height = 200;
            grpInput.Padding = new System.Windows.Forms.Padding(6);
            grpInput.TabIndex = 0;
            grpInput.TabStop = false;
            grpInput.Text = "Input";

            // ---- grpGroups ----
            grpGroups.Controls.Add(dgvGroups);
            grpGroups.Dock = System.Windows.Forms.DockStyle.Fill;
            grpGroups.Name = "grpGroups";
            grpGroups.Padding = new System.Windows.Forms.Padding(10, 6, 10, 6);
            grpGroups.TabIndex = 1;
            grpGroups.TabStop = false;
            grpGroups.Text = "Material Groups";

            // ---- dgvGroups ----
            dgvGroups.AllowUserToAddRows = false;
            dgvGroups.AllowUserToDeleteRows = false;
            dgvGroups.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            dgvGroups.BackgroundColor = System.Drawing.SystemColors.Window;
            dgvGroups.BorderStyle = System.Windows.Forms.BorderStyle.None;
            dgvGroups.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            dgvGroups.Dock = System.Windows.Forms.DockStyle.Fill;
            dgvGroups.Name = "dgvGroups";
            dgvGroups.ReadOnly = true;
            dgvGroups.RowHeadersVisible = false;
            dgvGroups.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            dgvGroups.TabIndex = 0;

            // ---- pnlBottom ----
            pnlBottom.Controls.Add(lblSummary);
            pnlBottom.Controls.Add(btnCreateNests);
            pnlBottom.Controls.Add(btnClose);
            pnlBottom.Dock = System.Windows.Forms.DockStyle.Bottom;
            pnlBottom.Height = 50;
            pnlBottom.Name = "pnlBottom";
            pnlBottom.Padding = new System.Windows.Forms.Padding(10, 10, 10, 10);
            pnlBottom.TabIndex = 2;

            // ---- lblSummary ----
            lblSummary.AutoSize = true;
            lblSummary.ForeColor = System.Drawing.Color.Gray;
            lblSummary.Dock = System.Windows.Forms.DockStyle.Left;
            lblSummary.Name = "lblSummary";
            lblSummary.TabIndex = 0;
            lblSummary.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            // ---- btnClose ----
            btnClose.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            btnClose.Dock = System.Windows.Forms.DockStyle.Right;
            btnClose.Name = "btnClose";
            btnClose.Size = new System.Drawing.Size(80, 30);
            btnClose.TabIndex = 2;
            btnClose.Text = "Close";
            btnClose.Click += new System.EventHandler(BtnClose_Click);

            // ---- btnCreateNests ----
            btnCreateNests.Enabled = false;
            btnCreateNests.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            btnCreateNests.Dock = System.Windows.Forms.DockStyle.Right;
            btnCreateNests.Name = "btnCreateNests";
            btnCreateNests.Size = new System.Drawing.Size(110, 30);
            btnCreateNests.TabIndex = 1;
            btnCreateNests.Text = "Create Nests";
            btnCreateNests.Margin = new System.Windows.Forms.Padding(0, 0, 6, 0);
            btnCreateNests.Click += new System.EventHandler(CreateNests_Click);

            // ---- BomImportForm ----
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            CancelButton = btnClose;
            ClientSize = new System.Drawing.Size(520, 500);
            Controls.Add(grpGroups);
            Controls.Add(pnlBottom);
            Controls.Add(grpInput);
            Font = new System.Drawing.Font("Segoe UI", 9F);
            MinimumSize = new System.Drawing.Size(400, 350);
            MaximizeBox = false;
            Name = "BomImportForm";
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "Import BOM";
            grpInput.ResumeLayout(false);
            grpInput.PerformLayout();
            grpGroups.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)dgvGroups).EndInit();
            pnlBottom.ResumeLayout(false);
            pnlBottom.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.GroupBox grpInput;
        private System.Windows.Forms.TextBox txtJobName;
        private System.Windows.Forms.TextBox txtBomFile;
        private System.Windows.Forms.Button btnBrowseBom;
        private System.Windows.Forms.TextBox txtDxfFolder;
        private System.Windows.Forms.Button btnBrowseDxf;
        private System.Windows.Forms.TextBox txtPlateWidth;
        private System.Windows.Forms.TextBox txtPlateLength;
        private System.Windows.Forms.Button btnAnalyze;
        private System.Windows.Forms.GroupBox grpGroups;
        private System.Windows.Forms.DataGridView dgvGroups;
        private System.Windows.Forms.Panel pnlBottom;
        private System.Windows.Forms.Label lblSummary;
        private System.Windows.Forms.Button btnCreateNests;
        private System.Windows.Forms.Button btnClose;
    }
}
