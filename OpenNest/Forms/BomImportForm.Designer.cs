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
            lblJobName = new System.Windows.Forms.Label();
            txtJobName = new System.Windows.Forms.TextBox();
            lblBomFile = new System.Windows.Forms.Label();
            txtBomFile = new System.Windows.Forms.TextBox();
            btnBrowseBom = new System.Windows.Forms.Button();
            lblDxfFolder = new System.Windows.Forms.Label();
            txtDxfFolder = new System.Windows.Forms.TextBox();
            btnBrowseDxf = new System.Windows.Forms.Button();
            lblPlateSize = new System.Windows.Forms.Label();
            txtPlateWidth = new System.Windows.Forms.TextBox();
            lblPlateX = new System.Windows.Forms.Label();
            txtPlateLength = new System.Windows.Forms.TextBox();
            btnAnalyze = new System.Windows.Forms.Button();
            grpGroups = new System.Windows.Forms.GroupBox();
            dgvGroups = new System.Windows.Forms.DataGridView();
            pnlBottom = new System.Windows.Forms.Panel();
            lblSummary = new System.Windows.Forms.Label();
            btnClose = new System.Windows.Forms.Button();
            btnCreateNests = new System.Windows.Forms.Button();
            grpInput.SuspendLayout();
            grpGroups.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)dgvGroups).BeginInit();
            pnlBottom.SuspendLayout();
            SuspendLayout();
            //
            // grpInput
            //
            grpInput.Controls.Add(lblJobName);
            grpInput.Controls.Add(txtJobName);
            grpInput.Controls.Add(lblBomFile);
            grpInput.Controls.Add(txtBomFile);
            grpInput.Controls.Add(btnBrowseBom);
            grpInput.Controls.Add(lblDxfFolder);
            grpInput.Controls.Add(txtDxfFolder);
            grpInput.Controls.Add(btnBrowseDxf);
            grpInput.Controls.Add(lblPlateSize);
            grpInput.Controls.Add(txtPlateWidth);
            grpInput.Controls.Add(lblPlateX);
            grpInput.Controls.Add(txtPlateLength);
            grpInput.Controls.Add(btnAnalyze);
            grpInput.Dock = System.Windows.Forms.DockStyle.Top;
            grpInput.Font = new System.Drawing.Font("Segoe UI", 9F);
            grpInput.Location = new System.Drawing.Point(0, 0);
            grpInput.Name = "grpInput";
            grpInput.Height = 180;
            grpInput.Padding = new System.Windows.Forms.Padding(10, 6, 10, 6);
            grpInput.TabIndex = 0;
            grpInput.TabStop = false;
            grpInput.Text = "Input";
            //
            // lblJobName
            //
            lblJobName.AutoSize = true;
            lblJobName.Location = new System.Drawing.Point(13, 28);
            lblJobName.Name = "lblJobName";
            lblJobName.Size = new System.Drawing.Size(65, 15);
            lblJobName.TabIndex = 0;
            lblJobName.Text = "Job Name:";
            //
            // txtJobName
            //
            txtJobName.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            txtJobName.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            txtJobName.Location = new System.Drawing.Point(90, 25);
            txtJobName.Name = "txtJobName";
            txtJobName.Size = new System.Drawing.Size(378, 23);
            txtJobName.TabIndex = 1;
            //
            // lblBomFile
            //
            lblBomFile.AutoSize = true;
            lblBomFile.Location = new System.Drawing.Point(13, 58);
            lblBomFile.Name = "lblBomFile";
            lblBomFile.Size = new System.Drawing.Size(56, 15);
            lblBomFile.TabIndex = 2;
            lblBomFile.Text = "BOM File:";
            //
            // txtBomFile
            //
            txtBomFile.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            txtBomFile.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            txtBomFile.Location = new System.Drawing.Point(90, 55);
            txtBomFile.Name = "txtBomFile";
            txtBomFile.ReadOnly = true;
            txtBomFile.Size = new System.Drawing.Size(343, 23);
            txtBomFile.TabIndex = 3;
            //
            // btnBrowseBom
            //
            btnBrowseBom.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            btnBrowseBom.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            btnBrowseBom.Location = new System.Drawing.Point(438, 54);
            btnBrowseBom.Name = "btnBrowseBom";
            btnBrowseBom.Size = new System.Drawing.Size(30, 25);
            btnBrowseBom.TabIndex = 4;
            btnBrowseBom.Text = "...";
            btnBrowseBom.Click += new System.EventHandler(BrowseBom_Click);
            //
            // lblDxfFolder
            //
            lblDxfFolder.AutoSize = true;
            lblDxfFolder.Location = new System.Drawing.Point(13, 90);
            lblDxfFolder.Name = "lblDxfFolder";
            lblDxfFolder.Size = new System.Drawing.Size(65, 15);
            lblDxfFolder.TabIndex = 5;
            lblDxfFolder.Text = "DXF Folder:";
            //
            // txtDxfFolder
            //
            txtDxfFolder.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            txtDxfFolder.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            txtDxfFolder.Location = new System.Drawing.Point(90, 87);
            txtDxfFolder.Name = "txtDxfFolder";
            txtDxfFolder.ReadOnly = true;
            txtDxfFolder.Size = new System.Drawing.Size(343, 23);
            txtDxfFolder.TabIndex = 6;
            //
            // btnBrowseDxf
            //
            btnBrowseDxf.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            btnBrowseDxf.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            btnBrowseDxf.Location = new System.Drawing.Point(438, 86);
            btnBrowseDxf.Name = "btnBrowseDxf";
            btnBrowseDxf.Size = new System.Drawing.Size(30, 25);
            btnBrowseDxf.TabIndex = 7;
            btnBrowseDxf.Text = "...";
            btnBrowseDxf.Click += new System.EventHandler(BrowseDxf_Click);
            //
            // lblPlateSize
            //
            lblPlateSize.AutoSize = true;
            lblPlateSize.Location = new System.Drawing.Point(13, 122);
            lblPlateSize.Name = "lblPlateSize";
            lblPlateSize.Size = new System.Drawing.Size(62, 15);
            lblPlateSize.TabIndex = 8;
            lblPlateSize.Text = "Plate Size:";
            //
            // txtPlateWidth
            //
            txtPlateWidth.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            txtPlateWidth.Location = new System.Drawing.Point(90, 119);
            txtPlateWidth.Name = "txtPlateWidth";
            txtPlateWidth.Size = new System.Drawing.Size(60, 23);
            txtPlateWidth.TabIndex = 9;
            txtPlateWidth.Text = "60";
            //
            // lblPlateX
            //
            lblPlateX.AutoSize = true;
            lblPlateX.Location = new System.Drawing.Point(155, 122);
            lblPlateX.Name = "lblPlateX";
            lblPlateX.Size = new System.Drawing.Size(13, 15);
            lblPlateX.TabIndex = 10;
            lblPlateX.Text = "x";
            //
            // txtPlateLength
            //
            txtPlateLength.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            txtPlateLength.Location = new System.Drawing.Point(173, 119);
            txtPlateLength.Name = "txtPlateLength";
            txtPlateLength.Size = new System.Drawing.Size(60, 23);
            txtPlateLength.TabIndex = 11;
            txtPlateLength.Text = "120";
            //
            // btnAnalyze
            //
            btnAnalyze.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            btnAnalyze.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            btnAnalyze.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            btnAnalyze.Location = new System.Drawing.Point(358, 118);
            btnAnalyze.Name = "btnAnalyze";
            btnAnalyze.Size = new System.Drawing.Size(110, 27);
            btnAnalyze.TabIndex = 12;
            btnAnalyze.Text = "Analyze";
            btnAnalyze.Click += new System.EventHandler(Analyze_Click);
            //
            // grpGroups
            //
            grpGroups.Controls.Add(dgvGroups);
            grpGroups.Dock = System.Windows.Forms.DockStyle.Fill;
            grpGroups.Font = new System.Drawing.Font("Segoe UI", 9F);
            grpGroups.Location = new System.Drawing.Point(0, 180);
            grpGroups.Name = "grpGroups";
            grpGroups.Padding = new System.Windows.Forms.Padding(10, 6, 10, 6);
            grpGroups.TabIndex = 1;
            grpGroups.TabStop = false;
            grpGroups.Text = "Material Groups";
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
            dgvGroups.Location = new System.Drawing.Point(10, 22);
            dgvGroups.Name = "dgvGroups";
            dgvGroups.ReadOnly = true;
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
            pnlBottom.Height = 60;
            pnlBottom.Name = "pnlBottom";
            pnlBottom.TabIndex = 2;
            //
            // lblSummary
            //
            lblSummary.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom;
            lblSummary.AutoSize = true;
            lblSummary.Font = new System.Drawing.Font("Segoe UI", 9F);
            lblSummary.ForeColor = System.Drawing.Color.Gray;
            lblSummary.Location = new System.Drawing.Point(10, 20);
            lblSummary.Name = "lblSummary";
            lblSummary.Size = new System.Drawing.Size(0, 15);
            lblSummary.TabIndex = 0;
            //
            // btnCreateNests
            //
            btnCreateNests.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
            btnCreateNests.Enabled = false;
            btnCreateNests.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            btnCreateNests.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            btnCreateNests.Location = new System.Drawing.Point(294, 16);
            btnCreateNests.Name = "btnCreateNests";
            btnCreateNests.Size = new System.Drawing.Size(110, 28);
            btnCreateNests.TabIndex = 1;
            btnCreateNests.Text = "Create Nests";
            btnCreateNests.Click += new System.EventHandler(CreateNests_Click);
            //
            // btnClose
            //
            btnClose.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
            btnClose.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            btnClose.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            btnClose.Font = new System.Drawing.Font("Segoe UI", 9F);
            btnClose.Location = new System.Drawing.Point(410, 16);
            btnClose.Name = "btnClose";
            btnClose.Size = new System.Drawing.Size(75, 28);
            btnClose.TabIndex = 2;
            btnClose.Text = "Close";
            btnClose.Click += new System.EventHandler(BtnClose_Click);
            //
            // BomImportForm
            //
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            CancelButton = btnClose;
            ClientSize = new System.Drawing.Size(530, 560);
            Controls.Add(pnlBottom);
            Controls.Add(grpInput);
            Controls.Add(grpGroups);
            Font = new System.Drawing.Font("Segoe UI", 9F);
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.Sizable;
            MinimumSize = new System.Drawing.Size(450, 400);
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "BomImportForm";
            ShowIcon = false;
            ShowInTaskbar = false;
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
        private System.Windows.Forms.Label lblJobName;
        private System.Windows.Forms.TextBox txtJobName;
        private System.Windows.Forms.Label lblBomFile;
        private System.Windows.Forms.TextBox txtBomFile;
        private System.Windows.Forms.Button btnBrowseBom;
        private System.Windows.Forms.Label lblDxfFolder;
        private System.Windows.Forms.TextBox txtDxfFolder;
        private System.Windows.Forms.Button btnBrowseDxf;
        private System.Windows.Forms.Label lblPlateSize;
        private System.Windows.Forms.TextBox txtPlateWidth;
        private System.Windows.Forms.Label lblPlateX;
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
