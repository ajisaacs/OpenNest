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
            mainSplit = new System.Windows.Forms.SplitContainer();
            fileList = new OpenNest.Controls.FileListControl();
            cadViewSplit = new System.Windows.Forms.SplitContainer();
            filterPanel = new OpenNest.Controls.FilterPanel();
            entityView1 = new OpenNest.Controls.EntityView();
            detailBar = new System.Windows.Forms.FlowLayoutPanel();
            viewTabs = new System.Windows.Forms.TabControl();
            tabCadView = new System.Windows.Forms.TabPage();
            tabProgram = new System.Windows.Forms.TabPage();
            programEditor = new OpenNest.Controls.ProgramEditorControl();
            lblQty = new System.Windows.Forms.Label();
            numQuantity = new System.Windows.Forms.NumericUpDown();
            lblCust = new System.Windows.Forms.Label();
            txtCustomer = new System.Windows.Forms.TextBox();
            lblDimensions = new System.Windows.Forms.Label();
            lblEntityCount = new System.Windows.Forms.Label();
            btnSplit = new System.Windows.Forms.Button();
            btnSimplify = new System.Windows.Forms.Button();
            btnExportDxf = new System.Windows.Forms.Button();
            chkShowOriginal = new System.Windows.Forms.CheckBox();
            chkLabels = new System.Windows.Forms.CheckBox();
            lblDetect = new System.Windows.Forms.Label();
            cboBendDetector = new System.Windows.Forms.ComboBox();
            bottomPanel1 = new OpenNest.Controls.BottomPanel();
            cancelButton = new System.Windows.Forms.Button();
            acceptButton = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)mainSplit).BeginInit();
            mainSplit.Panel1.SuspendLayout();
            mainSplit.Panel2.SuspendLayout();
            mainSplit.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)cadViewSplit).BeginInit();
            cadViewSplit.Panel1.SuspendLayout();
            cadViewSplit.Panel2.SuspendLayout();
            cadViewSplit.SuspendLayout();
            detailBar.SuspendLayout();
            viewTabs.SuspendLayout();
            tabCadView.SuspendLayout();
            tabProgram.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)numQuantity).BeginInit();
            bottomPanel1.SuspendLayout();
            SuspendLayout();
            //
            // mainSplit
            //
            mainSplit.Dock = System.Windows.Forms.DockStyle.Fill;
            mainSplit.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
            mainSplit.Location = new System.Drawing.Point(0, 0);
            mainSplit.Name = "mainSplit";
            //
            // mainSplit.Panel1
            //
            mainSplit.Panel1.Controls.Add(fileList);
            mainSplit.Panel1MinSize = 200;
            //
            // mainSplit.Panel2
            //
            mainSplit.Panel2.Controls.Add(viewTabs);
            mainSplit.Size = new System.Drawing.Size(1024, 670);
            mainSplit.SplitterDistance = 260;
            mainSplit.SplitterWidth = 5;
            mainSplit.TabIndex = 2;
            //
            // fileList
            //
            fileList.AllowDrop = true;
            fileList.BackColor = System.Drawing.Color.White;
            fileList.Dock = System.Windows.Forms.DockStyle.Fill;
            fileList.Font = new System.Drawing.Font("Segoe UI", 9F);
            fileList.Location = new System.Drawing.Point(0, 0);
            fileList.Name = "fileList";
            fileList.Size = new System.Drawing.Size(260, 670);
            fileList.TabIndex = 0;
            //
            // cadViewSplit
            //
            cadViewSplit.Dock = System.Windows.Forms.DockStyle.Fill;
            cadViewSplit.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
            cadViewSplit.Location = new System.Drawing.Point(0, 0);
            cadViewSplit.Name = "cadViewSplit";
            //
            // cadViewSplit.Panel1 — filter panel
            //
            cadViewSplit.Panel1.Controls.Add(filterPanel);
            cadViewSplit.Panel1MinSize = 150;
            //
            // cadViewSplit.Panel2 — entity view + detail bar
            //
            cadViewSplit.Panel2.Controls.Add(entityView1);
            cadViewSplit.Panel2.Controls.Add(detailBar);
            cadViewSplit.Size = new System.Drawing.Size(751, 642);
            cadViewSplit.SplitterDistance = 200;
            cadViewSplit.SplitterWidth = 5;
            cadViewSplit.TabIndex = 0;
            //
            // filterPanel
            //
            filterPanel.AutoScroll = true;
            filterPanel.BackColor = System.Drawing.Color.White;
            filterPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            filterPanel.Location = new System.Drawing.Point(0, 0);
            filterPanel.Name = "filterPanel";
            filterPanel.Size = new System.Drawing.Size(200, 642);
            filterPanel.TabIndex = 0;
            // 
            // entityView1
            // 
            entityView1.BackColor = System.Drawing.Color.FromArgb(33, 40, 48);
            entityView1.Cursor = System.Windows.Forms.Cursors.Cross;
            entityView1.Dock = System.Windows.Forms.DockStyle.Fill;
            entityView1.IsPickingBendLine = false;
            entityView1.Location = new System.Drawing.Point(0, 0);
            entityView1.Name = "entityView1";
            entityView1.OriginalEntities = null;
            entityView1.ShowEntityLabels = false;
            entityView1.SimplifierHighlight = null;
            entityView1.SimplifierPreview = null;
            entityView1.SimplifierToleranceLeft = null;
            entityView1.SimplifierToleranceRight = null;
            entityView1.Size = new System.Drawing.Size(546, 606);
            entityView1.TabIndex = 0;
            // 
            // detailBar
            // 
            detailBar.BackColor = System.Drawing.Color.FromArgb(245, 245, 245);
            detailBar.Controls.Add(lblQty);
            detailBar.Controls.Add(numQuantity);
            detailBar.Controls.Add(lblCust);
            detailBar.Controls.Add(txtCustomer);
            detailBar.Controls.Add(lblDimensions);
            detailBar.Controls.Add(lblEntityCount);
            detailBar.Controls.Add(btnSplit);
            detailBar.Controls.Add(btnSimplify);
            detailBar.Controls.Add(btnExportDxf);
            detailBar.Controls.Add(chkShowOriginal);
            detailBar.Controls.Add(chkLabels);
            detailBar.Controls.Add(lblDetect);
            detailBar.Controls.Add(cboBendDetector);
            detailBar.Dock = System.Windows.Forms.DockStyle.Bottom;
            detailBar.Location = new System.Drawing.Point(0, 634);
            detailBar.Name = "detailBar";
            detailBar.Padding = new System.Windows.Forms.Padding(4, 6, 4, 4);
            detailBar.Size = new System.Drawing.Size(546, 36);
            detailBar.TabIndex = 1;
            detailBar.WrapContents = false;
            // 
            // lblQty
            // 
            lblQty.AutoSize = true;
            lblQty.Font = new System.Drawing.Font("Segoe UI", 9F);
            lblQty.Location = new System.Drawing.Point(6, 9);
            lblQty.Margin = new System.Windows.Forms.Padding(2, 3, 0, 0);
            lblQty.Name = "lblQty";
            lblQty.Size = new System.Drawing.Size(29, 15);
            lblQty.TabIndex = 0;
            lblQty.Text = "Qty:";
            // 
            // numQuantity
            // 
            numQuantity.Font = new System.Drawing.Font("Segoe UI", 9F);
            numQuantity.Location = new System.Drawing.Point(37, 6);
            numQuantity.Margin = new System.Windows.Forms.Padding(2, 0, 8, 0);
            numQuantity.Maximum = new decimal(new int[] { 9999, 0, 0, 0 });
            numQuantity.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numQuantity.Name = "numQuantity";
            numQuantity.Size = new System.Drawing.Size(50, 23);
            numQuantity.TabIndex = 1;
            numQuantity.Value = new decimal(new int[] { 1, 0, 0, 0 });
            // 
            // lblCust
            // 
            lblCust.AutoSize = true;
            lblCust.Font = new System.Drawing.Font("Segoe UI", 9F);
            lblCust.Location = new System.Drawing.Point(97, 9);
            lblCust.Margin = new System.Windows.Forms.Padding(2, 3, 0, 0);
            lblCust.Name = "lblCust";
            lblCust.Size = new System.Drawing.Size(62, 15);
            lblCust.TabIndex = 2;
            lblCust.Text = "Customer:";
            // 
            // txtCustomer
            // 
            txtCustomer.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            txtCustomer.Font = new System.Drawing.Font("Segoe UI", 9F);
            txtCustomer.Location = new System.Drawing.Point(161, 6);
            txtCustomer.Margin = new System.Windows.Forms.Padding(2, 0, 8, 0);
            txtCustomer.Name = "txtCustomer";
            txtCustomer.Size = new System.Drawing.Size(100, 23);
            txtCustomer.TabIndex = 3;
            // 
            // lblDimensions
            // 
            lblDimensions.AutoSize = true;
            lblDimensions.Font = new System.Drawing.Font("Segoe UI", 9F);
            lblDimensions.ForeColor = System.Drawing.Color.Gray;
            lblDimensions.Location = new System.Drawing.Point(271, 9);
            lblDimensions.Margin = new System.Windows.Forms.Padding(2, 3, 8, 0);
            lblDimensions.Name = "lblDimensions";
            lblDimensions.Size = new System.Drawing.Size(0, 15);
            lblDimensions.TabIndex = 4;
            // 
            // lblEntityCount
            // 
            lblEntityCount.AutoSize = true;
            lblEntityCount.Font = new System.Drawing.Font("Segoe UI", 9F);
            lblEntityCount.ForeColor = System.Drawing.Color.Gray;
            lblEntityCount.Location = new System.Drawing.Point(281, 9);
            lblEntityCount.Margin = new System.Windows.Forms.Padding(2, 3, 8, 0);
            lblEntityCount.Name = "lblEntityCount";
            lblEntityCount.Size = new System.Drawing.Size(0, 15);
            lblEntityCount.TabIndex = 5;
            // 
            // btnSplit
            // 
            btnSplit.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            btnSplit.Font = new System.Drawing.Font("Segoe UI", 9F);
            btnSplit.Location = new System.Drawing.Point(289, 6);
            btnSplit.Margin = new System.Windows.Forms.Padding(0, 0, 10, 0);
            btnSplit.Name = "btnSplit";
            btnSplit.Size = new System.Drawing.Size(60, 27);
            btnSplit.TabIndex = 6;
            btnSplit.Text = "Split...";
            // 
            // btnSimplify
            // 
            btnSimplify.AutoSize = true;
            btnSimplify.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            btnSimplify.Font = new System.Drawing.Font("Segoe UI", 9F);
            btnSimplify.Location = new System.Drawing.Point(359, 6);
            btnSimplify.Margin = new System.Windows.Forms.Padding(0, 0, 10, 0);
            btnSimplify.Name = "btnSimplify";
            btnSimplify.Size = new System.Drawing.Size(75, 27);
            btnSimplify.TabIndex = 7;
            btnSimplify.Text = "Simplify...";
            btnSimplify.Click += OnSimplifyClick;
            // 
            // btnExportDxf
            // 
            btnExportDxf.AutoSize = true;
            btnExportDxf.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            btnExportDxf.Font = new System.Drawing.Font("Segoe UI", 9F);
            btnExportDxf.Location = new System.Drawing.Point(444, 6);
            btnExportDxf.Margin = new System.Windows.Forms.Padding(0, 0, 10, 0);
            btnExportDxf.Name = "btnExportDxf";
            btnExportDxf.Size = new System.Drawing.Size(76, 27);
            btnExportDxf.TabIndex = 8;
            btnExportDxf.Text = "Export DXF";
            btnExportDxf.Click += OnExportDxfClick;
            // 
            // chkShowOriginal
            // 
            chkShowOriginal.AutoSize = true;
            chkShowOriginal.Font = new System.Drawing.Font("Segoe UI", 9F);
            chkShowOriginal.Location = new System.Drawing.Point(536, 9);
            chkShowOriginal.Margin = new System.Windows.Forms.Padding(6, 3, 0, 0);
            chkShowOriginal.Name = "chkShowOriginal";
            chkShowOriginal.Size = new System.Drawing.Size(68, 19);
            chkShowOriginal.TabIndex = 9;
            chkShowOriginal.Text = "Original";
            chkShowOriginal.CheckedChanged += OnShowOriginalChanged;
            // 
            // chkLabels
            // 
            chkLabels.AutoSize = true;
            chkLabels.Font = new System.Drawing.Font("Segoe UI", 9F);
            chkLabels.Location = new System.Drawing.Point(610, 9);
            chkLabels.Margin = new System.Windows.Forms.Padding(6, 3, 0, 0);
            chkLabels.Name = "chkLabels";
            chkLabels.Size = new System.Drawing.Size(59, 19);
            chkLabels.TabIndex = 10;
            chkLabels.Text = "Labels";
            chkLabels.CheckedChanged += OnLabelsChanged;
            // 
            // lblDetect
            // 
            lblDetect.AutoSize = true;
            lblDetect.Font = new System.Drawing.Font("Segoe UI", 9F);
            lblDetect.Location = new System.Drawing.Point(671, 9);
            lblDetect.Margin = new System.Windows.Forms.Padding(2, 3, 0, 0);
            lblDetect.Name = "lblDetect";
            lblDetect.Size = new System.Drawing.Size(42, 15);
            lblDetect.TabIndex = 7;
            lblDetect.Text = "Bends:";
            // 
            // cboBendDetector
            // 
            cboBendDetector.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            cboBendDetector.Font = new System.Drawing.Font("Segoe UI", 9F);
            cboBendDetector.Location = new System.Drawing.Point(715, 6);
            cboBendDetector.Margin = new System.Windows.Forms.Padding(2, 0, 0, 0);
            cboBendDetector.Name = "cboBendDetector";
            cboBendDetector.Size = new System.Drawing.Size(90, 23);
            cboBendDetector.TabIndex = 8;
            // 
            // bottomPanel1
            // 
            bottomPanel1.Controls.Add(cancelButton);
            bottomPanel1.Controls.Add(acceptButton);
            bottomPanel1.Dock = System.Windows.Forms.DockStyle.Bottom;
            bottomPanel1.Location = new System.Drawing.Point(0, 670);
            bottomPanel1.Name = "bottomPanel1";
            bottomPanel1.Size = new System.Drawing.Size(1024, 50);
            bottomPanel1.TabIndex = 3;
            // 
            // cancelButton
            // 
            cancelButton.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
            cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            cancelButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            cancelButton.Font = new System.Drawing.Font("Segoe UI", 9F);
            cancelButton.Location = new System.Drawing.Point(922, 10);
            cancelButton.Name = "cancelButton";
            cancelButton.Size = new System.Drawing.Size(90, 28);
            cancelButton.TabIndex = 0;
            cancelButton.Text = "Cancel";
            // 
            // acceptButton
            // 
            acceptButton.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
            acceptButton.DialogResult = System.Windows.Forms.DialogResult.OK;
            acceptButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            acceptButton.Font = new System.Drawing.Font("Segoe UI", 9F);
            acceptButton.Location = new System.Drawing.Point(826, 10);
            acceptButton.Name = "acceptButton";
            acceptButton.Size = new System.Drawing.Size(90, 28);
            acceptButton.TabIndex = 1;
            acceptButton.Text = "Accept";
            //
            // viewTabs
            //
            viewTabs.Controls.Add(tabCadView);
            viewTabs.Controls.Add(tabProgram);
            viewTabs.Dock = System.Windows.Forms.DockStyle.Fill;
            viewTabs.Location = new System.Drawing.Point(0, 0);
            viewTabs.Name = "viewTabs";
            viewTabs.SelectedIndex = 0;
            viewTabs.Size = new System.Drawing.Size(759, 670);
            viewTabs.TabIndex = 0;
            //
            // tabCadView
            //
            tabCadView.Controls.Add(cadViewSplit);
            tabCadView.Location = new System.Drawing.Point(4, 24);
            tabCadView.Name = "tabCadView";
            tabCadView.Padding = new System.Windows.Forms.Padding(0);
            tabCadView.Size = new System.Drawing.Size(751, 642);
            tabCadView.TabIndex = 0;
            tabCadView.Text = "CAD View";
            tabCadView.UseVisualStyleBackColor = true;
            //
            // tabProgram
            //
            tabProgram.Controls.Add(programEditor);
            tabProgram.Location = new System.Drawing.Point(4, 24);
            tabProgram.Name = "tabProgram";
            tabProgram.Padding = new System.Windows.Forms.Padding(0);
            tabProgram.Size = new System.Drawing.Size(751, 642);
            tabProgram.TabIndex = 1;
            tabProgram.Text = "Program";
            tabProgram.UseVisualStyleBackColor = true;
            //
            // programEditor
            //
            programEditor.Dock = System.Windows.Forms.DockStyle.Fill;
            programEditor.Location = new System.Drawing.Point(0, 0);
            programEditor.Name = "programEditor";
            programEditor.Size = new System.Drawing.Size(751, 642);
            programEditor.TabIndex = 0;
            //
            // CadConverterForm
            //
            AllowDrop = true;
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            ClientSize = new System.Drawing.Size(1024, 720);
            Controls.Add(mainSplit);
            Controls.Add(bottomPanel1);
            Font = new System.Drawing.Font("Segoe UI", 9F);
            MinimizeBox = false;
            Name = "CadConverterForm";
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "CAD Converter";
            WindowState = System.Windows.Forms.FormWindowState.Maximized;
            mainSplit.Panel1.ResumeLayout(false);
            mainSplit.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)mainSplit).EndInit();
            mainSplit.ResumeLayout(false);
            cadViewSplit.Panel1.ResumeLayout(false);
            cadViewSplit.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)cadViewSplit).EndInit();
            cadViewSplit.ResumeLayout(false);
            detailBar.ResumeLayout(false);
            detailBar.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)numQuantity).EndInit();
            viewTabs.ResumeLayout(false);
            tabCadView.ResumeLayout(false);
            tabProgram.ResumeLayout(false);
            bottomPanel1.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.SplitContainer mainSplit;
        private System.Windows.Forms.SplitContainer cadViewSplit;
        private Controls.FileListControl fileList;
        private Controls.FilterPanel filterPanel;
        private Controls.EntityView entityView1;
        private System.Windows.Forms.FlowLayoutPanel detailBar;
        private System.Windows.Forms.Label lblDimensions;
        private System.Windows.Forms.Label lblEntityCount;
        private System.Windows.Forms.NumericUpDown numQuantity;
        private System.Windows.Forms.TextBox txtCustomer;
        private System.Windows.Forms.Button btnSplit;
        private System.Windows.Forms.Button btnSimplify;
        private System.Windows.Forms.Button btnExportDxf;
        private System.Windows.Forms.CheckBox chkShowOriginal;
        private System.Windows.Forms.CheckBox chkLabels;
        private System.Windows.Forms.ComboBox cboBendDetector;
        private System.Windows.Forms.Label lblQty;
        private System.Windows.Forms.Label lblCust;
        private System.Windows.Forms.Label lblDetect;
        private Controls.BottomPanel bottomPanel1;
        private System.Windows.Forms.Button acceptButton;
        private System.Windows.Forms.Button cancelButton;
        private System.Windows.Forms.TabControl viewTabs;
        private System.Windows.Forms.TabPage tabCadView;
        private System.Windows.Forms.TabPage tabProgram;
        private Controls.ProgramEditorControl programEditor;
    }
}
