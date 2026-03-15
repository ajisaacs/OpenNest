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
            table = new System.Windows.Forms.TableLayoutPanel();
            phaseLabel = new System.Windows.Forms.Label();
            phaseValue = new System.Windows.Forms.Label();
            plateLabel = new System.Windows.Forms.Label();
            plateValue = new System.Windows.Forms.Label();
            partsLabel = new System.Windows.Forms.Label();
            partsValue = new System.Windows.Forms.Label();
            densityLabel = new System.Windows.Forms.Label();
            densityValue = new System.Windows.Forms.Label();
            nestedAreaLabel = new System.Windows.Forms.Label();
            nestedAreaValue = new System.Windows.Forms.Label();
            remnantLabel = new System.Windows.Forms.Label();
            remnantValue = new System.Windows.Forms.Label();
            elapsedLabel = new System.Windows.Forms.Label();
            elapsedValue = new System.Windows.Forms.Label();
            descriptionLabel = new System.Windows.Forms.Label();
            descriptionValue = new System.Windows.Forms.Label();
            stopButton = new System.Windows.Forms.Button();
            buttonPanel = new System.Windows.Forms.FlowLayoutPanel();
            table.SuspendLayout();
            buttonPanel.SuspendLayout();
            SuspendLayout();
            // 
            // table
            // 
            table.AutoSize = true;
            table.ColumnCount = 2;
            table.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 93F));
            table.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            table.Controls.Add(phaseLabel, 0, 0);
            table.Controls.Add(phaseValue, 1, 0);
            table.Controls.Add(plateLabel, 0, 1);
            table.Controls.Add(plateValue, 1, 1);
            table.Controls.Add(partsLabel, 0, 2);
            table.Controls.Add(partsValue, 1, 2);
            table.Controls.Add(densityLabel, 0, 3);
            table.Controls.Add(densityValue, 1, 3);
            table.Controls.Add(nestedAreaLabel, 0, 4);
            table.Controls.Add(nestedAreaValue, 1, 4);
            table.Controls.Add(remnantLabel, 0, 5);
            table.Controls.Add(remnantValue, 1, 5);
            table.Controls.Add(elapsedLabel, 0, 6);
            table.Controls.Add(elapsedValue, 1, 6);
            table.Controls.Add(descriptionLabel, 0, 7);
            table.Controls.Add(descriptionValue, 1, 7);
            table.Dock = System.Windows.Forms.DockStyle.Top;
            table.Location = new System.Drawing.Point(0, 45);
            table.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            table.Name = "table";
            table.Padding = new System.Windows.Forms.Padding(9, 9, 9, 9);
            table.RowCount = 8;
            table.RowStyles.Add(new System.Windows.Forms.RowStyle());
            table.RowStyles.Add(new System.Windows.Forms.RowStyle());
            table.RowStyles.Add(new System.Windows.Forms.RowStyle());
            table.RowStyles.Add(new System.Windows.Forms.RowStyle());
            table.RowStyles.Add(new System.Windows.Forms.RowStyle());
            table.RowStyles.Add(new System.Windows.Forms.RowStyle());
            table.RowStyles.Add(new System.Windows.Forms.RowStyle());
            table.RowStyles.Add(new System.Windows.Forms.RowStyle());
            table.Size = new System.Drawing.Size(425, 218);
            table.TabIndex = 0;
            // 
            // phaseLabel
            // 
            phaseLabel.AutoSize = true;
            phaseLabel.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold);
            phaseLabel.Location = new System.Drawing.Point(14, 14);
            phaseLabel.Margin = new System.Windows.Forms.Padding(5, 5, 5, 5);
            phaseLabel.Name = "phaseLabel";
            phaseLabel.Size = new System.Drawing.Size(46, 13);
            phaseLabel.TabIndex = 0;
            phaseLabel.Text = "Phase:";
            // 
            // phaseValue
            // 
            phaseValue.AutoSize = true;
            phaseValue.Location = new System.Drawing.Point(107, 14);
            phaseValue.Margin = new System.Windows.Forms.Padding(5, 5, 5, 5);
            phaseValue.Name = "phaseValue";
            phaseValue.Size = new System.Drawing.Size(19, 15);
            phaseValue.TabIndex = 1;
            phaseValue.Text = "—";
            // 
            // plateLabel
            // 
            plateLabel.AutoSize = true;
            plateLabel.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold);
            plateLabel.Location = new System.Drawing.Point(14, 39);
            plateLabel.Margin = new System.Windows.Forms.Padding(5, 5, 5, 5);
            plateLabel.Name = "plateLabel";
            plateLabel.Size = new System.Drawing.Size(40, 13);
            plateLabel.TabIndex = 2;
            plateLabel.Text = "Plate:";
            // 
            // plateValue
            // 
            plateValue.AutoSize = true;
            plateValue.Location = new System.Drawing.Point(107, 39);
            plateValue.Margin = new System.Windows.Forms.Padding(5, 5, 5, 5);
            plateValue.Name = "plateValue";
            plateValue.Size = new System.Drawing.Size(19, 15);
            plateValue.TabIndex = 3;
            plateValue.Text = "—";
            // 
            // partsLabel
            // 
            partsLabel.AutoSize = true;
            partsLabel.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold);
            partsLabel.Location = new System.Drawing.Point(14, 64);
            partsLabel.Margin = new System.Windows.Forms.Padding(5, 5, 5, 5);
            partsLabel.Name = "partsLabel";
            partsLabel.Size = new System.Drawing.Size(40, 13);
            partsLabel.TabIndex = 4;
            partsLabel.Text = "Parts:";
            // 
            // partsValue
            // 
            partsValue.AutoSize = true;
            partsValue.Location = new System.Drawing.Point(107, 64);
            partsValue.Margin = new System.Windows.Forms.Padding(5, 5, 5, 5);
            partsValue.Name = "partsValue";
            partsValue.Size = new System.Drawing.Size(19, 15);
            partsValue.TabIndex = 5;
            partsValue.Text = "—";
            // 
            // densityLabel
            // 
            densityLabel.AutoSize = true;
            densityLabel.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold);
            densityLabel.Location = new System.Drawing.Point(14, 89);
            densityLabel.Margin = new System.Windows.Forms.Padding(5, 5, 5, 5);
            densityLabel.Name = "densityLabel";
            densityLabel.Size = new System.Drawing.Size(53, 13);
            densityLabel.TabIndex = 6;
            densityLabel.Text = "Density:";
            // 
            // densityValue
            // 
            densityValue.AutoSize = true;
            densityValue.Location = new System.Drawing.Point(107, 89);
            densityValue.Margin = new System.Windows.Forms.Padding(5, 5, 5, 5);
            densityValue.Name = "densityValue";
            densityValue.Size = new System.Drawing.Size(19, 15);
            densityValue.TabIndex = 7;
            densityValue.Text = "—";
            // 
            // nestedAreaLabel
            // 
            nestedAreaLabel.AutoSize = true;
            nestedAreaLabel.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold);
            nestedAreaLabel.Location = new System.Drawing.Point(14, 114);
            nestedAreaLabel.Margin = new System.Windows.Forms.Padding(5, 5, 5, 5);
            nestedAreaLabel.Name = "nestedAreaLabel";
            nestedAreaLabel.Size = new System.Drawing.Size(51, 13);
            nestedAreaLabel.TabIndex = 8;
            nestedAreaLabel.Text = "Nested:";
            // 
            // nestedAreaValue
            // 
            nestedAreaValue.AutoSize = true;
            nestedAreaValue.Location = new System.Drawing.Point(107, 114);
            nestedAreaValue.Margin = new System.Windows.Forms.Padding(5, 5, 5, 5);
            nestedAreaValue.Name = "nestedAreaValue";
            nestedAreaValue.Size = new System.Drawing.Size(19, 15);
            nestedAreaValue.TabIndex = 9;
            nestedAreaValue.Text = "—";
            // 
            // remnantLabel
            // 
            remnantLabel.AutoSize = true;
            remnantLabel.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold);
            remnantLabel.Location = new System.Drawing.Point(14, 139);
            remnantLabel.Margin = new System.Windows.Forms.Padding(5, 5, 5, 5);
            remnantLabel.Name = "remnantLabel";
            remnantLabel.Size = new System.Drawing.Size(54, 13);
            remnantLabel.TabIndex = 10;
            remnantLabel.Text = "Unused:";
            // 
            // remnantValue
            // 
            remnantValue.AutoSize = true;
            remnantValue.Location = new System.Drawing.Point(107, 139);
            remnantValue.Margin = new System.Windows.Forms.Padding(5, 5, 5, 5);
            remnantValue.Name = "remnantValue";
            remnantValue.Size = new System.Drawing.Size(19, 15);
            remnantValue.TabIndex = 11;
            remnantValue.Text = "—";
            // 
            // elapsedLabel
            // 
            elapsedLabel.AutoSize = true;
            elapsedLabel.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold);
            elapsedLabel.Location = new System.Drawing.Point(14, 164);
            elapsedLabel.Margin = new System.Windows.Forms.Padding(5, 5, 5, 5);
            elapsedLabel.Name = "elapsedLabel";
            elapsedLabel.Size = new System.Drawing.Size(56, 13);
            elapsedLabel.TabIndex = 12;
            elapsedLabel.Text = "Elapsed:";
            // 
            // elapsedValue
            // 
            elapsedValue.AutoSize = true;
            elapsedValue.Location = new System.Drawing.Point(107, 164);
            elapsedValue.Margin = new System.Windows.Forms.Padding(5, 5, 5, 5);
            elapsedValue.Name = "elapsedValue";
            elapsedValue.Size = new System.Drawing.Size(28, 15);
            elapsedValue.TabIndex = 13;
            elapsedValue.Text = "0:00";
            // 
            // descriptionLabel
            // 
            descriptionLabel.AutoSize = true;
            descriptionLabel.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold);
            descriptionLabel.Location = new System.Drawing.Point(14, 189);
            descriptionLabel.Margin = new System.Windows.Forms.Padding(5, 5, 5, 5);
            descriptionLabel.Name = "descriptionLabel";
            descriptionLabel.Size = new System.Drawing.Size(44, 13);
            descriptionLabel.TabIndex = 14;
            descriptionLabel.Text = "Detail:";
            // 
            // descriptionValue
            // 
            descriptionValue.AutoSize = true;
            descriptionValue.Location = new System.Drawing.Point(107, 189);
            descriptionValue.Margin = new System.Windows.Forms.Padding(5, 5, 5, 5);
            descriptionValue.Name = "descriptionValue";
            descriptionValue.Size = new System.Drawing.Size(19, 15);
            descriptionValue.TabIndex = 15;
            descriptionValue.Text = "—";
            // 
            // stopButton
            // 
            stopButton.Anchor = System.Windows.Forms.AnchorStyles.None;
            stopButton.Location = new System.Drawing.Point(314, 9);
            stopButton.Margin = new System.Windows.Forms.Padding(0, 9, 0, 9);
            stopButton.Name = "stopButton";
            stopButton.Size = new System.Drawing.Size(93, 27);
            stopButton.TabIndex = 0;
            stopButton.Text = "Stop";
            stopButton.UseVisualStyleBackColor = true;
            stopButton.Click += StopButton_Click;
            // 
            // buttonPanel
            // 
            buttonPanel.AutoSize = true;
            buttonPanel.Controls.Add(stopButton);
            buttonPanel.Dock = System.Windows.Forms.DockStyle.Top;
            buttonPanel.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
            buttonPanel.Location = new System.Drawing.Point(0, 0);
            buttonPanel.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            buttonPanel.Name = "buttonPanel";
            buttonPanel.Padding = new System.Windows.Forms.Padding(9, 0, 9, 0);
            buttonPanel.Size = new System.Drawing.Size(425, 45);
            buttonPanel.TabIndex = 1;
            // 
            // NestProgressForm
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(425, 266);
            Controls.Add(table);
            Controls.Add(buttonPanel);
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow;
            Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "NestProgressForm";
            ShowInTaskbar = false;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "Nesting Progress";
            table.ResumeLayout(false);
            table.PerformLayout();
            buttonPanel.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
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
        private System.Windows.Forms.Label nestedAreaLabel;
        private System.Windows.Forms.Label nestedAreaValue;
        private System.Windows.Forms.Label remnantLabel;
        private System.Windows.Forms.Label remnantValue;
        private System.Windows.Forms.Label elapsedLabel;
        private System.Windows.Forms.Label elapsedValue;
        private System.Windows.Forms.Label descriptionLabel;
        private System.Windows.Forms.Label descriptionValue;
        private System.Windows.Forms.Button stopButton;
        private System.Windows.Forms.FlowLayoutPanel buttonPanel;
    }
}
