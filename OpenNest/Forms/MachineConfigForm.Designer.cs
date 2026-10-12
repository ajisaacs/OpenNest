namespace OpenNest.Forms
{
    partial class MachineConfigForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            splitContainer = new System.Windows.Forms.SplitContainer();
            _tree = new System.Windows.Forms.TreeView();
            _detailPanel = new System.Windows.Forms.Panel();
            bottomPanel = new System.Windows.Forms.FlowLayoutPanel();
            _saveButton = new System.Windows.Forms.Button();
            exportButton = new System.Windows.Forms.Button();
            importButton = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)splitContainer).BeginInit();
            splitContainer.Panel1.SuspendLayout();
            splitContainer.Panel2.SuspendLayout();
            splitContainer.SuspendLayout();
            bottomPanel.SuspendLayout();
            SuspendLayout();
            // 
            // splitContainer
            // 
            splitContainer.Dock = System.Windows.Forms.DockStyle.Fill;
            splitContainer.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
            splitContainer.Location = new System.Drawing.Point(0, 0);
            splitContainer.Name = "splitContainer";
            // 
            // splitContainer.Panel1
            // 
            splitContainer.Panel1.Controls.Add(_tree);
            // 
            // splitContainer.Panel2
            // 
            splitContainer.Panel2.Controls.Add(_detailPanel);
            splitContainer.Size = new System.Drawing.Size(1194, 562);
            splitContainer.SplitterDistance = 500;
            splitContainer.TabIndex = 0;
            // 
            // _tree
            // 
            _tree.Dock = System.Windows.Forms.DockStyle.Fill;
            _tree.HideSelection = false;
            _tree.Location = new System.Drawing.Point(0, 0);
            _tree.Name = "_tree";
            _tree.Size = new System.Drawing.Size(256, 562);
            _tree.TabIndex = 0;
            // 
            // _detailPanel
            // 
            _detailPanel.AutoScroll = true;
            _detailPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            _detailPanel.Location = new System.Drawing.Point(0, 0);
            _detailPanel.Name = "_detailPanel";
            _detailPanel.Size = new System.Drawing.Size(934, 562);
            _detailPanel.TabIndex = 0;
            // 
            // bottomPanel
            // 
            bottomPanel.AutoSize = true;
            bottomPanel.Controls.Add(_saveButton);
            bottomPanel.Controls.Add(exportButton);
            bottomPanel.Controls.Add(importButton);
            bottomPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
            bottomPanel.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
            bottomPanel.Location = new System.Drawing.Point(0, 562);
            bottomPanel.Name = "bottomPanel";
            bottomPanel.Padding = new System.Windows.Forms.Padding(4);
            bottomPanel.Size = new System.Drawing.Size(1194, 39);
            bottomPanel.TabIndex = 1;
            // 
            // _saveButton
            // 
            _saveButton.AutoSize = true;
            _saveButton.Location = new System.Drawing.Point(1108, 7);
            _saveButton.Name = "_saveButton";
            _saveButton.Size = new System.Drawing.Size(75, 25);
            _saveButton.TabIndex = 0;
            _saveButton.Text = "Save";
            _saveButton.Click += Save_Click;
            // 
            // exportButton
            // 
            exportButton.AutoSize = true;
            exportButton.Location = new System.Drawing.Point(1027, 7);
            exportButton.Name = "exportButton";
            exportButton.Size = new System.Drawing.Size(75, 25);
            exportButton.TabIndex = 1;
            exportButton.Text = "Export...";
            exportButton.Click += Export_Click;
            // 
            // importButton
            // 
            importButton.AutoSize = true;
            importButton.Location = new System.Drawing.Point(946, 7);
            importButton.Name = "importButton";
            importButton.Size = new System.Drawing.Size(75, 25);
            importButton.TabIndex = 2;
            importButton.Text = "Import...";
            importButton.Click += Import_Click;
            // 
            // MachineConfigForm
            // 
            ClientSize = new System.Drawing.Size(1194, 601);
            Controls.Add(splitContainer);
            Controls.Add(bottomPanel);
            MinimumSize = new System.Drawing.Size(1010, 440);
            Name = "MachineConfigForm";
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "Machine Configuration";
            splitContainer.Panel1.ResumeLayout(false);
            splitContainer.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitContainer).EndInit();
            splitContainer.ResumeLayout(false);
            bottomPanel.ResumeLayout(false);
            bottomPanel.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private System.Windows.Forms.SplitContainer splitContainer;
        private System.Windows.Forms.TreeView _tree;
        private System.Windows.Forms.Panel _detailPanel;
        private System.Windows.Forms.FlowLayoutPanel bottomPanel;
        private System.Windows.Forms.Button _saveButton;
        private System.Windows.Forms.Button exportButton;
        private System.Windows.Forms.Button importButton;
    }
}
