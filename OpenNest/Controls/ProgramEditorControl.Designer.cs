namespace OpenNest.Controls
{
    partial class ProgramEditorControl
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        private void InitializeComponent()
        {
            mainSplit = new System.Windows.Forms.SplitContainer();
            contourList = new System.Windows.Forms.ListBox();
            reverseButton = new System.Windows.Forms.Button();
            contourPanel = new System.Windows.Forms.Panel();
            rightSplit = new System.Windows.Forms.SplitContainer();
            preview = new OpenNest.Controls.EntityView();
            editorPanel = new System.Windows.Forms.Panel();
            gcodeEditor = new System.Windows.Forms.TextBox();
            editorToolbar = new System.Windows.Forms.Panel();
            applyButton = new System.Windows.Forms.Button();
            lblGcode = new System.Windows.Forms.Label();
            contourMenu = new System.Windows.Forms.ContextMenuStrip();
            menuReverse = new System.Windows.Forms.ToolStripMenuItem();
            ((System.ComponentModel.ISupportInitialize)mainSplit).BeginInit();
            mainSplit.Panel1.SuspendLayout();
            mainSplit.Panel2.SuspendLayout();
            mainSplit.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)rightSplit).BeginInit();
            rightSplit.Panel1.SuspendLayout();
            rightSplit.Panel2.SuspendLayout();
            rightSplit.SuspendLayout();
            contourPanel.SuspendLayout();
            editorPanel.SuspendLayout();
            editorToolbar.SuspendLayout();
            SuspendLayout();
            //
            // mainSplit
            //
            mainSplit.Dock = System.Windows.Forms.DockStyle.Fill;
            mainSplit.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
            mainSplit.Location = new System.Drawing.Point(0, 0);
            mainSplit.Name = "mainSplit";
            //
            // mainSplit.Panel1 — contour list panel
            //
            mainSplit.Panel1.Controls.Add(contourPanel);
            mainSplit.Panel1MinSize = 150;
            //
            // mainSplit.Panel2 — preview + editor
            //
            mainSplit.Panel2.Controls.Add(rightSplit);
            mainSplit.Size = new System.Drawing.Size(800, 500);
            mainSplit.SplitterDistance = 180;
            mainSplit.SplitterWidth = 5;
            mainSplit.TabIndex = 0;
            //
            // contourPanel
            //
            contourPanel.Controls.Add(contourList);
            contourPanel.Controls.Add(reverseButton);
            contourPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            contourPanel.Location = new System.Drawing.Point(0, 0);
            contourPanel.Name = "contourPanel";
            contourPanel.Size = new System.Drawing.Size(180, 500);
            contourPanel.TabIndex = 0;
            //
            // contourList
            //
            contourList.BackColor = System.Drawing.Color.White;
            contourList.BorderStyle = System.Windows.Forms.BorderStyle.None;
            contourList.Dock = System.Windows.Forms.DockStyle.Fill;
            contourList.DrawMode = System.Windows.Forms.DrawMode.OwnerDrawVariable;
            contourList.Font = new System.Drawing.Font("Segoe UI", 9F);
            contourList.IntegralHeight = false;
            contourList.Location = new System.Drawing.Point(0, 0);
            contourList.Name = "contourList";
            contourList.SelectionMode = System.Windows.Forms.SelectionMode.MultiExtended;
            contourList.Size = new System.Drawing.Size(180, 462);
            contourList.TabIndex = 0;
            contourList.ContextMenuStrip = contourMenu;
            //
            // reverseButton
            //
            reverseButton.Dock = System.Windows.Forms.DockStyle.Bottom;
            reverseButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            reverseButton.Font = new System.Drawing.Font("Segoe UI", 9F);
            reverseButton.Location = new System.Drawing.Point(0, 462);
            reverseButton.Name = "reverseButton";
            reverseButton.Size = new System.Drawing.Size(180, 38);
            reverseButton.TabIndex = 1;
            reverseButton.Text = "\u21C5 Reverse Direction";
            //
            // rightSplit
            //
            rightSplit.Dock = System.Windows.Forms.DockStyle.Fill;
            rightSplit.Location = new System.Drawing.Point(0, 0);
            rightSplit.Name = "rightSplit";
            //
            // rightSplit.Panel1 — preview
            //
            rightSplit.Panel1.Controls.Add(preview);
            //
            // rightSplit.Panel2 — gcode editor
            //
            rightSplit.Panel2.Controls.Add(editorPanel);
            rightSplit.Size = new System.Drawing.Size(615, 500);
            rightSplit.SplitterDistance = 350;
            rightSplit.SplitterWidth = 5;
            rightSplit.TabIndex = 0;
            //
            // preview
            //
            preview.BackColor = System.Drawing.Color.FromArgb(33, 40, 48);
            preview.Cursor = System.Windows.Forms.Cursors.Cross;
            preview.Dock = System.Windows.Forms.DockStyle.Fill;
            preview.IsPickingBendLine = false;
            preview.Location = new System.Drawing.Point(0, 0);
            preview.Name = "preview";
            preview.ShowEntityLabels = false;
            preview.Size = new System.Drawing.Size(615, 300);
            preview.TabIndex = 0;
            //
            // editorPanel
            //
            editorPanel.Controls.Add(gcodeEditor);
            editorPanel.Controls.Add(editorToolbar);
            editorPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            editorPanel.Location = new System.Drawing.Point(0, 0);
            editorPanel.Name = "editorPanel";
            editorPanel.Size = new System.Drawing.Size(615, 195);
            editorPanel.TabIndex = 0;
            //
            // editorToolbar
            //
            editorToolbar.BackColor = System.Drawing.Color.FromArgb(245, 245, 245);
            editorToolbar.Controls.Add(applyButton);
            editorToolbar.Controls.Add(lblGcode);
            editorToolbar.Dock = System.Windows.Forms.DockStyle.Top;
            editorToolbar.Location = new System.Drawing.Point(0, 0);
            editorToolbar.Name = "editorToolbar";
            editorToolbar.Padding = new System.Windows.Forms.Padding(6, 4, 6, 4);
            editorToolbar.Size = new System.Drawing.Size(615, 30);
            editorToolbar.TabIndex = 0;
            //
            // lblGcode
            //
            lblGcode.AutoSize = true;
            lblGcode.Dock = System.Windows.Forms.DockStyle.Left;
            lblGcode.Font = new System.Drawing.Font("Segoe UI", 9F);
            lblGcode.ForeColor = System.Drawing.Color.Gray;
            lblGcode.Location = new System.Drawing.Point(6, 4);
            lblGcode.Name = "lblGcode";
            lblGcode.Padding = new System.Windows.Forms.Padding(0, 3, 0, 0);
            lblGcode.Size = new System.Drawing.Size(50, 18);
            lblGcode.TabIndex = 0;
            lblGcode.Text = "G-Code";
            //
            // applyButton
            //
            applyButton.Dock = System.Windows.Forms.DockStyle.Right;
            applyButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            applyButton.Font = new System.Drawing.Font("Segoe UI", 9F);
            applyButton.Location = new System.Drawing.Point(539, 4);
            applyButton.Name = "applyButton";
            applyButton.Size = new System.Drawing.Size(70, 22);
            applyButton.TabIndex = 1;
            applyButton.Text = "Apply";
            //
            // gcodeEditor
            //
            gcodeEditor.BackColor = System.Drawing.Color.FromArgb(30, 30, 45);
            gcodeEditor.BorderStyle = System.Windows.Forms.BorderStyle.None;
            gcodeEditor.Dock = System.Windows.Forms.DockStyle.Fill;
            gcodeEditor.Font = new System.Drawing.Font("Consolas", 10F);
            gcodeEditor.ForeColor = System.Drawing.Color.FromArgb(180, 200, 180);
            gcodeEditor.Location = new System.Drawing.Point(0, 30);
            gcodeEditor.Multiline = true;
            gcodeEditor.Name = "gcodeEditor";
            gcodeEditor.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            gcodeEditor.Size = new System.Drawing.Size(615, 165);
            gcodeEditor.TabIndex = 1;
            gcodeEditor.WordWrap = false;
            //
            // contourMenu
            //
            contourMenu.Items.Add(menuReverse);
            contourMenu.Name = "contourMenu";
            contourMenu.Size = new System.Drawing.Size(180, 26);
            //
            // menuReverse
            //
            menuReverse.Name = "menuReverse";
            menuReverse.Size = new System.Drawing.Size(179, 22);
            menuReverse.Text = "Reverse Direction";
            //
            // ProgramEditorControl
            //
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            Controls.Add(mainSplit);
            Name = "ProgramEditorControl";
            Size = new System.Drawing.Size(800, 500);
            mainSplit.Panel1.ResumeLayout(false);
            mainSplit.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)mainSplit).EndInit();
            mainSplit.ResumeLayout(false);
            rightSplit.Panel1.ResumeLayout(false);
            rightSplit.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)rightSplit).EndInit();
            rightSplit.ResumeLayout(false);
            contourPanel.ResumeLayout(false);
            editorPanel.ResumeLayout(false);
            editorPanel.PerformLayout();
            editorToolbar.ResumeLayout(false);
            editorToolbar.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.SplitContainer mainSplit;
        private System.Windows.Forms.Panel contourPanel;
        private System.Windows.Forms.ListBox contourList;
        private System.Windows.Forms.Button reverseButton;
        private System.Windows.Forms.SplitContainer rightSplit;
        private Controls.EntityView preview;
        private System.Windows.Forms.Panel editorPanel;
        private System.Windows.Forms.Panel editorToolbar;
        private System.Windows.Forms.Label lblGcode;
        private System.Windows.Forms.Button applyButton;
        private System.Windows.Forms.TextBox gcodeEditor;
        private System.Windows.Forms.ContextMenuStrip contourMenu;
        private System.Windows.Forms.ToolStripMenuItem menuReverse;
    }
}
