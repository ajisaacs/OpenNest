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
            tbl = new System.Windows.Forms.TableLayoutPanel();
            lblJobName = new System.Windows.Forms.Label();
            txtJobName = new System.Windows.Forms.TextBox();
            lblBomFile = new System.Windows.Forms.Label();
            txtBomFile = new System.Windows.Forms.TextBox();
            btnBrowseBom = new System.Windows.Forms.Button();
            lblDxfFolder = new System.Windows.Forms.Label();
            txtDxfFolder = new System.Windows.Forms.TextBox();
            btnBrowseDxf = new System.Windows.Forms.Button();
            lblPlateSize = new System.Windows.Forms.Label();
            platePanel = new System.Windows.Forms.FlowLayoutPanel();
            txtPlateWidth = new System.Windows.Forms.TextBox();
            lblPlateX = new System.Windows.Forms.Label();
            txtPlateLength = new System.Windows.Forms.TextBox();
            btnAnalyze = new System.Windows.Forms.Button();
            tabControl = new System.Windows.Forms.TabControl();
            tabParts = new System.Windows.Forms.TabPage();
            dgvParts = new System.Windows.Forms.DataGridView();
            tabGroups = new System.Windows.Forms.TabPage();
            dgvGroups = new System.Windows.Forms.DataGridView();
            pnlBottom = new System.Windows.Forms.Panel();
            lblSummary = new System.Windows.Forms.Label();
            btnCreateNests = new System.Windows.Forms.Button();
            btnClose = new System.Windows.Forms.Button();
            grpInput.SuspendLayout();
            tbl.SuspendLayout();
            platePanel.SuspendLayout();
            tabControl.SuspendLayout();
            tabParts.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)dgvParts).BeginInit();
            tabGroups.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)dgvGroups).BeginInit();
            pnlBottom.SuspendLayout();
            SuspendLayout();
            // 
            // grpInput
            // 
            grpInput.Controls.Add(tbl);
            grpInput.Dock = System.Windows.Forms.DockStyle.Top;
            grpInput.Location = new System.Drawing.Point(0, 0);
            grpInput.Name = "grpInput";
            grpInput.Padding = new System.Windows.Forms.Padding(6);
            grpInput.Size = new System.Drawing.Size(804, 200);
            grpInput.TabIndex = 0;
            grpInput.TabStop = false;
            grpInput.Text = "Input";
            // 
            // tbl
            // 
            tbl.ColumnCount = 3;
            tbl.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            tbl.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            tbl.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            tbl.Controls.Add(lblJobName, 0, 0);
            tbl.Controls.Add(txtJobName, 1, 0);
            tbl.Controls.Add(lblBomFile, 0, 1);
            tbl.Controls.Add(txtBomFile, 1, 1);
            tbl.Controls.Add(btnBrowseBom, 2, 1);
            tbl.Controls.Add(lblDxfFolder, 0, 2);
            tbl.Controls.Add(txtDxfFolder, 1, 2);
            tbl.Controls.Add(btnBrowseDxf, 2, 2);
            tbl.Controls.Add(lblPlateSize, 0, 3);
            tbl.Controls.Add(platePanel, 1, 3);
            tbl.Controls.Add(btnAnalyze, 1, 4);
            tbl.Dock = System.Windows.Forms.DockStyle.Fill;
            tbl.Location = new System.Drawing.Point(6, 22);
            tbl.Name = "tbl";
            tbl.Padding = new System.Windows.Forms.Padding(3);
            tbl.RowCount = 5;
            tbl.RowStyles.Add(new System.Windows.Forms.RowStyle());
            tbl.RowStyles.Add(new System.Windows.Forms.RowStyle());
            tbl.RowStyles.Add(new System.Windows.Forms.RowStyle());
            tbl.RowStyles.Add(new System.Windows.Forms.RowStyle());
            tbl.RowStyles.Add(new System.Windows.Forms.RowStyle());
            tbl.Size = new System.Drawing.Size(792, 172);
            tbl.TabIndex = 0;
            // 
            // lblJobName
            // 
            lblJobName.Anchor = System.Windows.Forms.AnchorStyles.Left;
            lblJobName.AutoSize = true;
            lblJobName.Location = new System.Drawing.Point(6, 13);
            lblJobName.Margin = new System.Windows.Forms.Padding(3, 6, 3, 3);
            lblJobName.Name = "lblJobName";
            lblJobName.Size = new System.Drawing.Size(63, 15);
            lblJobName.TabIndex = 0;
            lblJobName.Text = "Job Name:";
            // 
            // txtJobName
            // 
            tbl.SetColumnSpan(txtJobName, 2);
            txtJobName.Dock = System.Windows.Forms.DockStyle.Fill;
            txtJobName.Location = new System.Drawing.Point(79, 9);
            txtJobName.Margin = new System.Windows.Forms.Padding(3, 6, 3, 3);
            txtJobName.Name = "txtJobName";
            txtJobName.Size = new System.Drawing.Size(707, 23);
            txtJobName.TabIndex = 1;
            // 
            // lblBomFile
            // 
            lblBomFile.Anchor = System.Windows.Forms.AnchorStyles.Left;
            lblBomFile.AutoSize = true;
            lblBomFile.Location = new System.Drawing.Point(6, 45);
            lblBomFile.Margin = new System.Windows.Forms.Padding(3, 6, 3, 3);
            lblBomFile.Name = "lblBomFile";
            lblBomFile.Size = new System.Drawing.Size(58, 15);
            lblBomFile.TabIndex = 2;
            lblBomFile.Text = "BOM File:";
            // 
            // txtBomFile
            // 
            txtBomFile.Dock = System.Windows.Forms.DockStyle.Fill;
            txtBomFile.Location = new System.Drawing.Point(79, 41);
            txtBomFile.Margin = new System.Windows.Forms.Padding(3, 6, 3, 3);
            txtBomFile.Name = "txtBomFile";
            txtBomFile.ReadOnly = true;
            txtBomFile.Size = new System.Drawing.Size(669, 23);
            txtBomFile.TabIndex = 3;
            // 
            // btnBrowseBom
            // 
            btnBrowseBom.Location = new System.Drawing.Point(751, 40);
            btnBrowseBom.Margin = new System.Windows.Forms.Padding(0, 5, 3, 3);
            btnBrowseBom.Name = "btnBrowseBom";
            btnBrowseBom.Size = new System.Drawing.Size(35, 25);
            btnBrowseBom.TabIndex = 4;
            btnBrowseBom.Text = "...";
            btnBrowseBom.Click += BrowseBom_Click;
            // 
            // lblDxfFolder
            // 
            lblDxfFolder.Anchor = System.Windows.Forms.AnchorStyles.Left;
            lblDxfFolder.AutoSize = true;
            lblDxfFolder.Location = new System.Drawing.Point(6, 78);
            lblDxfFolder.Margin = new System.Windows.Forms.Padding(3, 6, 3, 3);
            lblDxfFolder.Name = "lblDxfFolder";
            lblDxfFolder.Size = new System.Drawing.Size(67, 15);
            lblDxfFolder.TabIndex = 5;
            lblDxfFolder.Text = "DXF Folder:";
            // 
            // txtDxfFolder
            // 
            txtDxfFolder.Dock = System.Windows.Forms.DockStyle.Fill;
            txtDxfFolder.Location = new System.Drawing.Point(79, 74);
            txtDxfFolder.Margin = new System.Windows.Forms.Padding(3, 6, 3, 3);
            txtDxfFolder.Name = "txtDxfFolder";
            txtDxfFolder.ReadOnly = true;
            txtDxfFolder.Size = new System.Drawing.Size(669, 23);
            txtDxfFolder.TabIndex = 6;
            // 
            // btnBrowseDxf
            // 
            btnBrowseDxf.Location = new System.Drawing.Point(751, 73);
            btnBrowseDxf.Margin = new System.Windows.Forms.Padding(0, 5, 3, 3);
            btnBrowseDxf.Name = "btnBrowseDxf";
            btnBrowseDxf.Size = new System.Drawing.Size(35, 25);
            btnBrowseDxf.TabIndex = 7;
            btnBrowseDxf.Text = "...";
            btnBrowseDxf.Click += BrowseDxf_Click;
            // 
            // lblPlateSize
            // 
            lblPlateSize.Anchor = System.Windows.Forms.AnchorStyles.Left;
            lblPlateSize.AutoSize = true;
            lblPlateSize.Location = new System.Drawing.Point(6, 112);
            lblPlateSize.Margin = new System.Windows.Forms.Padding(3, 6, 3, 3);
            lblPlateSize.Name = "lblPlateSize";
            lblPlateSize.Size = new System.Drawing.Size(59, 15);
            lblPlateSize.TabIndex = 8;
            lblPlateSize.Text = "Plate Size:";
            // 
            // platePanel
            // 
            platePanel.AutoSize = true;
            platePanel.Controls.Add(txtPlateWidth);
            platePanel.Controls.Add(lblPlateX);
            platePanel.Controls.Add(txtPlateLength);
            platePanel.Location = new System.Drawing.Point(76, 104);
            platePanel.Margin = new System.Windows.Forms.Padding(0, 3, 0, 3);
            platePanel.Name = "platePanel";
            platePanel.Size = new System.Drawing.Size(156, 29);
            platePanel.TabIndex = 9;
            platePanel.WrapContents = false;
            // 
            // txtPlateWidth
            // 
            txtPlateWidth.Location = new System.Drawing.Point(3, 3);
            txtPlateWidth.Name = "txtPlateWidth";
            txtPlateWidth.Size = new System.Drawing.Size(60, 23);
            txtPlateWidth.TabIndex = 0;
            txtPlateWidth.Text = "60";
            // 
            // lblPlateX
            // 
            lblPlateX.Anchor = System.Windows.Forms.AnchorStyles.Left;
            lblPlateX.AutoSize = true;
            lblPlateX.Location = new System.Drawing.Point(69, 7);
            lblPlateX.Name = "lblPlateX";
            lblPlateX.Size = new System.Drawing.Size(18, 15);
            lblPlateX.TabIndex = 1;
            lblPlateX.Text = " x ";
            // 
            // txtPlateLength
            // 
            txtPlateLength.Location = new System.Drawing.Point(93, 3);
            txtPlateLength.Name = "txtPlateLength";
            txtPlateLength.Size = new System.Drawing.Size(60, 23);
            txtPlateLength.TabIndex = 2;
            txtPlateLength.Text = "120";
            // 
            // btnAnalyze
            // 
            btnAnalyze.Anchor = System.Windows.Forms.AnchorStyles.Right;
            tbl.SetColumnSpan(btnAnalyze, 2);
            btnAnalyze.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            btnAnalyze.Location = new System.Drawing.Point(676, 142);
            btnAnalyze.Margin = new System.Windows.Forms.Padding(3, 6, 3, 3);
            btnAnalyze.Name = "btnAnalyze";
            btnAnalyze.Size = new System.Drawing.Size(110, 30);
            btnAnalyze.TabIndex = 10;
            btnAnalyze.Text = "Analyze";
            btnAnalyze.Click += Analyze_Click;
            //
            // tabControl
            //
            tabControl.Controls.Add(tabParts);
            tabControl.Controls.Add(tabGroups);
            tabControl.Dock = System.Windows.Forms.DockStyle.Fill;
            tabControl.Location = new System.Drawing.Point(0, 200);
            tabControl.Name = "tabControl";
            tabControl.SelectedIndex = 0;
            tabControl.Size = new System.Drawing.Size(804, 365);
            tabControl.TabIndex = 1;
            //
            // tabParts
            //
            tabParts.Controls.Add(dgvParts);
            tabParts.Name = "tabParts";
            tabParts.Padding = new System.Windows.Forms.Padding(3);
            tabParts.Text = "Parts";
            //
            // dgvParts
            //
            dgvParts.AllowUserToAddRows = false;
            dgvParts.AllowUserToDeleteRows = false;
            dgvParts.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            dgvParts.BackgroundColor = System.Drawing.SystemColors.Window;
            dgvParts.BorderStyle = System.Windows.Forms.BorderStyle.None;
            dgvParts.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            dgvParts.Dock = System.Windows.Forms.DockStyle.Fill;
            dgvParts.Name = "dgvParts";
            dgvParts.RowHeadersVisible = false;
            dgvParts.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            dgvParts.TabIndex = 0;
            //
            // tabGroups
            //
            tabGroups.Controls.Add(dgvGroups);
            tabGroups.Name = "tabGroups";
            tabGroups.Padding = new System.Windows.Forms.Padding(3);
            tabGroups.Text = "Groups";
            //
            // dgvGroups
            //
            dgvGroups.AllowUserToAddRows = false;
            dgvGroups.AllowUserToDeleteRows = false;
            dgvGroups.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            dgvGroups.BackgroundColor = System.Drawing.SystemColors.Window;
            dgvGroups.BorderStyle = System.Windows.Forms.BorderStyle.None;
            dgvGroups.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            dgvGroups.Dock = System.Windows.Forms.DockStyle.Fill;
            dgvGroups.Name = "dgvGroups";
            dgvGroups.RowHeadersVisible = false;
            dgvGroups.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            dgvGroups.TabIndex = 0;
            // 
            // pnlBottom
            // 
            pnlBottom.Controls.Add(lblSummary);
            pnlBottom.Controls.Add(btnCreateNests);
            pnlBottom.Controls.Add(btnClose);
            pnlBottom.Dock = System.Windows.Forms.DockStyle.Bottom;
            pnlBottom.Location = new System.Drawing.Point(0, 565);
            pnlBottom.Name = "pnlBottom";
            pnlBottom.Padding = new System.Windows.Forms.Padding(10);
            pnlBottom.Size = new System.Drawing.Size(804, 50);
            pnlBottom.TabIndex = 2;
            // 
            // lblSummary
            // 
            lblSummary.AutoSize = true;
            lblSummary.Dock = System.Windows.Forms.DockStyle.Left;
            lblSummary.ForeColor = System.Drawing.Color.Gray;
            lblSummary.Location = new System.Drawing.Point(10, 10);
            lblSummary.Name = "lblSummary";
            lblSummary.Size = new System.Drawing.Size(0, 15);
            lblSummary.TabIndex = 0;
            lblSummary.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // btnCreateNests
            // 
            btnCreateNests.Dock = System.Windows.Forms.DockStyle.Right;
            btnCreateNests.Enabled = false;
            btnCreateNests.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            btnCreateNests.Location = new System.Drawing.Point(604, 10);
            btnCreateNests.Margin = new System.Windows.Forms.Padding(0, 0, 6, 0);
            btnCreateNests.Name = "btnCreateNests";
            btnCreateNests.Size = new System.Drawing.Size(110, 30);
            btnCreateNests.TabIndex = 1;
            btnCreateNests.Text = "Create Nests";
            btnCreateNests.Click += CreateNests_Click;
            // 
            // btnClose
            // 
            btnClose.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            btnClose.Dock = System.Windows.Forms.DockStyle.Right;
            btnClose.Location = new System.Drawing.Point(714, 10);
            btnClose.Name = "btnClose";
            btnClose.Size = new System.Drawing.Size(80, 30);
            btnClose.TabIndex = 2;
            btnClose.Text = "Close";
            btnClose.Click += BtnClose_Click;
            // 
            // BomImportForm
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            CancelButton = btnClose;
            ClientSize = new System.Drawing.Size(804, 615);
            Controls.Add(tabControl);
            Controls.Add(pnlBottom);
            Controls.Add(grpInput);
            Font = new System.Drawing.Font("Segoe UI", 9F);
            MaximizeBox = false;
            MinimumSize = new System.Drawing.Size(400, 350);
            Name = "BomImportForm";
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "Import BOM";
            grpInput.ResumeLayout(false);
            tbl.ResumeLayout(false);
            tbl.PerformLayout();
            platePanel.ResumeLayout(false);
            platePanel.PerformLayout();
            tabParts.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)dgvParts).EndInit();
            tabGroups.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)dgvGroups).EndInit();
            tabControl.ResumeLayout(false);
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
        private System.Windows.Forms.TabControl tabControl;
        private System.Windows.Forms.TabPage tabParts;
        private System.Windows.Forms.DataGridView dgvParts;
        private System.Windows.Forms.TabPage tabGroups;
        private System.Windows.Forms.DataGridView dgvGroups;
        private System.Windows.Forms.Panel pnlBottom;
        private System.Windows.Forms.Label lblSummary;
        private System.Windows.Forms.Button btnCreateNests;
        private System.Windows.Forms.Button btnClose;
        private System.Windows.Forms.TableLayoutPanel tbl;
        private System.Windows.Forms.Label lblJobName;
        private System.Windows.Forms.Label lblBomFile;
        private System.Windows.Forms.Label lblDxfFolder;
        private System.Windows.Forms.Label lblPlateSize;
        private System.Windows.Forms.FlowLayoutPanel platePanel;
        private System.Windows.Forms.Label lblPlateX;
    }
}
