namespace OpenNest.Forms
{
    partial class CuttingParametersForm
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
            this.tabControl = new System.Windows.Forms.TabControl();
            this.tabExternal = new System.Windows.Forms.TabPage();
            this.tabInternal = new System.Windows.Forms.TabPage();
            this.tabArcCircle = new System.Windows.Forms.TabPage();
            this.acceptButton = new System.Windows.Forms.Button();
            this.cancelButton = new System.Windows.Forms.Button();
            this.bottomPanel = new OpenNest.Controls.BottomPanel();
            this.tabControl.SuspendLayout();
            this.bottomPanel.SuspendLayout();
            this.SuspendLayout();
            //
            // tabControl
            //
            this.tabControl.Controls.Add(this.tabExternal);
            this.tabControl.Controls.Add(this.tabInternal);
            this.tabControl.Controls.Add(this.tabArcCircle);
            this.tabControl.Dock = System.Windows.Forms.DockStyle.Top;
            this.tabControl.Location = new System.Drawing.Point(0, 0);
            this.tabControl.Margin = new System.Windows.Forms.Padding(4);
            this.tabControl.Name = "tabControl";
            this.tabControl.SelectedIndex = 0;
            this.tabControl.Size = new System.Drawing.Size(380, 348);
            this.tabControl.TabIndex = 0;
            //
            // tabExternal
            //
            this.tabExternal.Location = new System.Drawing.Point(4, 25);
            this.tabExternal.Margin = new System.Windows.Forms.Padding(4);
            this.tabExternal.Name = "tabExternal";
            this.tabExternal.Padding = new System.Windows.Forms.Padding(8);
            this.tabExternal.Size = new System.Drawing.Size(372, 319);
            this.tabExternal.TabIndex = 0;
            this.tabExternal.Text = "External";
            this.tabExternal.UseVisualStyleBackColor = true;
            //
            // tabInternal
            //
            this.tabInternal.Location = new System.Drawing.Point(4, 25);
            this.tabInternal.Margin = new System.Windows.Forms.Padding(4);
            this.tabInternal.Name = "tabInternal";
            this.tabInternal.Padding = new System.Windows.Forms.Padding(8);
            this.tabInternal.Size = new System.Drawing.Size(372, 319);
            this.tabInternal.TabIndex = 1;
            this.tabInternal.Text = "Internal";
            this.tabInternal.UseVisualStyleBackColor = true;
            //
            // tabArcCircle
            //
            this.tabArcCircle.Location = new System.Drawing.Point(4, 25);
            this.tabArcCircle.Margin = new System.Windows.Forms.Padding(4);
            this.tabArcCircle.Name = "tabArcCircle";
            this.tabArcCircle.Padding = new System.Windows.Forms.Padding(8);
            this.tabArcCircle.Size = new System.Drawing.Size(372, 319);
            this.tabArcCircle.TabIndex = 2;
            this.tabArcCircle.Text = "Arc / Circle";
            this.tabArcCircle.UseVisualStyleBackColor = true;
            //
            // acceptButton
            //
            this.acceptButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.acceptButton.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.acceptButton.Location = new System.Drawing.Point(165, 11);
            this.acceptButton.Margin = new System.Windows.Forms.Padding(4);
            this.acceptButton.Name = "acceptButton";
            this.acceptButton.Size = new System.Drawing.Size(90, 28);
            this.acceptButton.TabIndex = 8;
            this.acceptButton.Text = "OK";
            this.acceptButton.UseVisualStyleBackColor = true;
            //
            // cancelButton
            //
            this.cancelButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.cancelButton.Location = new System.Drawing.Point(263, 11);
            this.cancelButton.Margin = new System.Windows.Forms.Padding(4);
            this.cancelButton.Name = "cancelButton";
            this.cancelButton.Size = new System.Drawing.Size(90, 28);
            this.cancelButton.TabIndex = 9;
            this.cancelButton.Text = "Cancel";
            this.cancelButton.UseVisualStyleBackColor = true;
            //
            // bottomPanel
            //
            this.bottomPanel.Controls.Add(this.acceptButton);
            this.bottomPanel.Controls.Add(this.cancelButton);
            this.bottomPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.bottomPanel.Location = new System.Drawing.Point(0, 406);
            this.bottomPanel.Name = "bottomPanel";
            this.bottomPanel.Size = new System.Drawing.Size(380, 50);
            this.bottomPanel.TabIndex = 1;
            //
            // CuttingParametersForm
            //
            this.AcceptButton = this.acceptButton;
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.cancelButton;
            this.ClientSize = new System.Drawing.Size(380, 456);
            this.Controls.Add(this.tabControl);
            this.Controls.Add(this.bottomPanel);
            this.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.Margin = new System.Windows.Forms.Padding(4);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "CuttingParametersForm";
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Cutting Parameters";
            this.tabControl.ResumeLayout(false);
            this.bottomPanel.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.TabControl tabControl;
        private System.Windows.Forms.TabPage tabExternal;
        private System.Windows.Forms.TabPage tabInternal;
        private System.Windows.Forms.TabPage tabArcCircle;
        private Controls.BottomPanel bottomPanel;
        private System.Windows.Forms.Button acceptButton;
        private System.Windows.Forms.Button cancelButton;
    }
}
