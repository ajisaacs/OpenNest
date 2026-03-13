namespace OpenNest.Forms
{
    partial class NestProgressForm
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
            this.table = new System.Windows.Forms.TableLayoutPanel();
            this.phaseLabel = new System.Windows.Forms.Label();
            this.phaseValue = new System.Windows.Forms.Label();
            this.plateLabel = new System.Windows.Forms.Label();
            this.plateValue = new System.Windows.Forms.Label();
            this.partsLabel = new System.Windows.Forms.Label();
            this.partsValue = new System.Windows.Forms.Label();
            this.densityLabel = new System.Windows.Forms.Label();
            this.densityValue = new System.Windows.Forms.Label();
            this.remnantLabel = new System.Windows.Forms.Label();
            this.remnantValue = new System.Windows.Forms.Label();
            this.stopButton = new System.Windows.Forms.Button();
            this.buttonPanel = new System.Windows.Forms.FlowLayoutPanel();
            this.table.SuspendLayout();
            this.buttonPanel.SuspendLayout();
            this.SuspendLayout();
            //
            // table
            //
            this.table.ColumnCount = 2;
            this.table.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 80F));
            this.table.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
            this.table.Controls.Add(this.phaseLabel, 0, 0);
            this.table.Controls.Add(this.phaseValue, 1, 0);
            this.table.Controls.Add(this.plateLabel, 0, 1);
            this.table.Controls.Add(this.plateValue, 1, 1);
            this.table.Controls.Add(this.partsLabel, 0, 2);
            this.table.Controls.Add(this.partsValue, 1, 2);
            this.table.Controls.Add(this.densityLabel, 0, 3);
            this.table.Controls.Add(this.densityValue, 1, 3);
            this.table.Controls.Add(this.remnantLabel, 0, 4);
            this.table.Controls.Add(this.remnantValue, 1, 4);
            this.table.Dock = System.Windows.Forms.DockStyle.Top;
            this.table.Location = new System.Drawing.Point(0, 0);
            this.table.Name = "table";
            this.table.Padding = new System.Windows.Forms.Padding(8);
            this.table.RowCount = 5;
            this.table.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.table.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.table.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.table.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.table.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.table.AutoSize = true;
            this.table.Size = new System.Drawing.Size(264, 130);
            this.table.TabIndex = 0;
            //
            // phaseLabel
            //
            this.phaseLabel.AutoSize = true;
            this.phaseLabel.Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold);
            this.phaseLabel.Margin = new System.Windows.Forms.Padding(4);
            this.phaseLabel.Name = "phaseLabel";
            this.phaseLabel.Text = "Phase:";
            //
            // phaseValue
            //
            this.phaseValue.AutoSize = true;
            this.phaseValue.Margin = new System.Windows.Forms.Padding(4);
            this.phaseValue.Name = "phaseValue";
            this.phaseValue.Text = "\u2014";
            //
            // plateLabel
            //
            this.plateLabel.AutoSize = true;
            this.plateLabel.Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold);
            this.plateLabel.Margin = new System.Windows.Forms.Padding(4);
            this.plateLabel.Name = "plateLabel";
            this.plateLabel.Text = "Plate:";
            //
            // plateValue
            //
            this.plateValue.AutoSize = true;
            this.plateValue.Margin = new System.Windows.Forms.Padding(4);
            this.plateValue.Name = "plateValue";
            this.plateValue.Text = "\u2014";
            //
            // partsLabel
            //
            this.partsLabel.AutoSize = true;
            this.partsLabel.Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold);
            this.partsLabel.Margin = new System.Windows.Forms.Padding(4);
            this.partsLabel.Name = "partsLabel";
            this.partsLabel.Text = "Parts:";
            //
            // partsValue
            //
            this.partsValue.AutoSize = true;
            this.partsValue.Margin = new System.Windows.Forms.Padding(4);
            this.partsValue.Name = "partsValue";
            this.partsValue.Text = "\u2014";
            //
            // densityLabel
            //
            this.densityLabel.AutoSize = true;
            this.densityLabel.Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold);
            this.densityLabel.Margin = new System.Windows.Forms.Padding(4);
            this.densityLabel.Name = "densityLabel";
            this.densityLabel.Text = "Density:";
            //
            // densityValue
            //
            this.densityValue.AutoSize = true;
            this.densityValue.Margin = new System.Windows.Forms.Padding(4);
            this.densityValue.Name = "densityValue";
            this.densityValue.Text = "\u2014";
            //
            // remnantLabel
            //
            this.remnantLabel.AutoSize = true;
            this.remnantLabel.Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold);
            this.remnantLabel.Margin = new System.Windows.Forms.Padding(4);
            this.remnantLabel.Name = "remnantLabel";
            this.remnantLabel.Text = "Remnant:";
            //
            // remnantValue
            //
            this.remnantValue.AutoSize = true;
            this.remnantValue.Margin = new System.Windows.Forms.Padding(4);
            this.remnantValue.Name = "remnantValue";
            this.remnantValue.Text = "\u2014";
            //
            // stopButton
            //
            this.stopButton.Anchor = System.Windows.Forms.AnchorStyles.None;
            this.stopButton.Margin = new System.Windows.Forms.Padding(0, 8, 0, 8);
            this.stopButton.Name = "stopButton";
            this.stopButton.Size = new System.Drawing.Size(80, 23);
            this.stopButton.TabIndex = 0;
            this.stopButton.Text = "Stop";
            this.stopButton.UseVisualStyleBackColor = true;
            this.stopButton.Click += new System.EventHandler(this.StopButton_Click);
            //
            // buttonPanel
            //
            this.buttonPanel.AutoSize = true;
            this.buttonPanel.Controls.Add(this.stopButton);
            this.buttonPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.buttonPanel.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
            this.buttonPanel.Name = "buttonPanel";
            this.buttonPanel.Padding = new System.Windows.Forms.Padding(8, 0, 8, 0);
            this.buttonPanel.TabIndex = 1;
            //
            // NestProgressForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(264, 181);
            this.Controls.Add(this.buttonPanel);
            this.Controls.Add(this.table);
            this.Controls.SetChildIndex(this.table, 0);
            this.Controls.SetChildIndex(this.buttonPanel, 1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "NestProgressForm";
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Nesting Progress";
            this.table.ResumeLayout(false);
            this.table.PerformLayout();
            this.buttonPanel.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel table;
        private System.Windows.Forms.Label phaseLabel;
        private System.Windows.Forms.Label phaseValue;
        private System.Windows.Forms.Label plateLabel;
        private System.Windows.Forms.Label plateValue;
        private System.Windows.Forms.Label partsLabel;
        private System.Windows.Forms.Label partsValue;
        private System.Windows.Forms.Label densityLabel;
        private System.Windows.Forms.Label densityValue;
        private System.Windows.Forms.Label remnantLabel;
        private System.Windows.Forms.Label remnantValue;
        private System.Windows.Forms.Button stopButton;
        private System.Windows.Forms.FlowLayoutPanel buttonPanel;
    }
}
