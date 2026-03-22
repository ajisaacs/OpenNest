namespace OpenNest.Forms
{
    partial class NestProgressForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            phaseStepper = new OpenNest.Controls.PhaseStepperControl();
            resultsPanel = new System.Windows.Forms.Panel();
            resultsTable = new System.Windows.Forms.TableLayoutPanel();
            partsLabel = new System.Windows.Forms.Label();
            partsValue = new System.Windows.Forms.Label();
            densityLabel = new System.Windows.Forms.Label();
            densityPanel = new System.Windows.Forms.FlowLayoutPanel();
            densityValue = new System.Windows.Forms.Label();
            densityBar = new OpenNest.Controls.DensityBar();
            nestedAreaLabel = new System.Windows.Forms.Label();
            nestedAreaValue = new System.Windows.Forms.Label();
            resultsHeader = new System.Windows.Forms.Label();
            statusPanel = new System.Windows.Forms.Panel();
            statusTable = new System.Windows.Forms.TableLayoutPanel();
            plateLabel = new System.Windows.Forms.Label();
            plateValue = new System.Windows.Forms.Label();
            elapsedLabel = new System.Windows.Forms.Label();
            elapsedValue = new System.Windows.Forms.Label();
            descriptionLabel = new System.Windows.Forms.Label();
            descriptionValue = new System.Windows.Forms.Label();
            statusHeader = new System.Windows.Forms.Label();
            buttonPanel = new System.Windows.Forms.FlowLayoutPanel();
            stopButton = new System.Windows.Forms.Button();
            acceptButton = new System.Windows.Forms.Button();
            resultsPanel.SuspendLayout();
            resultsTable.SuspendLayout();
            densityPanel.SuspendLayout();
            statusPanel.SuspendLayout();
            statusTable.SuspendLayout();
            buttonPanel.SuspendLayout();
            SuspendLayout();
            // 
            // phaseStepper
            // 
            phaseStepper.ActivePhase = null;
            phaseStepper.Dock = System.Windows.Forms.DockStyle.Top;
            phaseStepper.IsComplete = false;
            phaseStepper.Location = new System.Drawing.Point(0, 0);
            phaseStepper.Name = "phaseStepper";
            phaseStepper.Size = new System.Drawing.Size(450, 60);
            phaseStepper.TabIndex = 0;
            // 
            // resultsPanel
            // 
            resultsPanel.BackColor = System.Drawing.Color.White;
            resultsPanel.Controls.Add(resultsTable);
            resultsPanel.Controls.Add(resultsHeader);
            resultsPanel.Dock = System.Windows.Forms.DockStyle.Top;
            resultsPanel.Location = new System.Drawing.Point(0, 60);
            resultsPanel.Margin = new System.Windows.Forms.Padding(10, 4, 10, 4);
            resultsPanel.Name = "resultsPanel";
            resultsPanel.Padding = new System.Windows.Forms.Padding(14, 10, 14, 10);
            resultsPanel.Size = new System.Drawing.Size(450, 120);
            resultsPanel.TabIndex = 1;
            // 
            // resultsTable
            // 
            resultsTable.AutoSize = true;
            resultsTable.ColumnCount = 2;
            resultsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 90F));
            resultsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            resultsTable.Controls.Add(partsLabel, 0, 0);
            resultsTable.Controls.Add(partsValue, 1, 0);
            resultsTable.Controls.Add(densityLabel, 0, 1);
            resultsTable.Controls.Add(densityPanel, 1, 1);
            resultsTable.Controls.Add(nestedAreaLabel, 0, 2);
            resultsTable.Controls.Add(nestedAreaValue, 1, 2);
            resultsTable.Dock = System.Windows.Forms.DockStyle.Top;
            resultsTable.Location = new System.Drawing.Point(14, 33);
            resultsTable.Name = "resultsTable";
            resultsTable.RowCount = 3;
            resultsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            resultsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            resultsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            resultsTable.Size = new System.Drawing.Size(422, 69);
            resultsTable.TabIndex = 1;
            // 
            // partsLabel
            // 
            partsLabel.AutoSize = true;
            partsLabel.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Bold);
            partsLabel.ForeColor = System.Drawing.Color.FromArgb(51, 51, 51);
            partsLabel.Location = new System.Drawing.Point(0, 3);
            partsLabel.Margin = new System.Windows.Forms.Padding(0, 3, 5, 3);
            partsLabel.Name = "partsLabel";
            partsLabel.Size = new System.Drawing.Size(43, 17);
            partsLabel.TabIndex = 0;
            partsLabel.Text = "Parts:";
            // 
            // partsValue
            // 
            partsValue.AutoSize = true;
            partsValue.Font = new System.Drawing.Font("Consolas", 9.75F);
            partsValue.Location = new System.Drawing.Point(90, 3);
            partsValue.Margin = new System.Windows.Forms.Padding(0, 3, 0, 3);
            partsValue.Name = "partsValue";
            partsValue.Size = new System.Drawing.Size(13, 15);
            partsValue.TabIndex = 1;
            partsValue.Text = "�";
            // 
            // densityLabel
            // 
            densityLabel.AutoSize = true;
            densityLabel.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Bold);
            densityLabel.ForeColor = System.Drawing.Color.FromArgb(51, 51, 51);
            densityLabel.Location = new System.Drawing.Point(0, 26);
            densityLabel.Margin = new System.Windows.Forms.Padding(0, 3, 5, 3);
            densityLabel.Name = "densityLabel";
            densityLabel.Size = new System.Drawing.Size(59, 17);
            densityLabel.TabIndex = 2;
            densityLabel.Text = "Density:";
            // 
            // densityPanel
            // 
            densityPanel.AutoSize = true;
            densityPanel.Controls.Add(densityValue);
            densityPanel.Controls.Add(densityBar);
            densityPanel.Location = new System.Drawing.Point(90, 23);
            densityPanel.Margin = new System.Windows.Forms.Padding(0);
            densityPanel.Name = "densityPanel";
            densityPanel.Size = new System.Drawing.Size(262, 21);
            densityPanel.TabIndex = 3;
            densityPanel.WrapContents = false;
            // 
            // densityValue
            // 
            densityValue.AutoSize = true;
            densityValue.Font = new System.Drawing.Font("Consolas", 9.75F);
            densityValue.Location = new System.Drawing.Point(0, 3);
            densityValue.Margin = new System.Windows.Forms.Padding(0, 3, 8, 3);
            densityValue.Name = "densityValue";
            densityValue.Size = new System.Drawing.Size(13, 15);
            densityValue.TabIndex = 0;
            densityValue.Text = "�";
            // 
            // densityBar
            // 
            densityBar.Location = new System.Drawing.Point(21, 5);
            densityBar.Margin = new System.Windows.Forms.Padding(0, 5, 0, 0);
            densityBar.Name = "densityBar";
            densityBar.Size = new System.Drawing.Size(241, 8);
            densityBar.TabIndex = 1;
            densityBar.Value = 0D;
            // 
            // nestedAreaLabel
            // 
            nestedAreaLabel.AutoSize = true;
            nestedAreaLabel.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Bold);
            nestedAreaLabel.ForeColor = System.Drawing.Color.FromArgb(51, 51, 51);
            nestedAreaLabel.Location = new System.Drawing.Point(0, 49);
            nestedAreaLabel.Margin = new System.Windows.Forms.Padding(0, 3, 5, 3);
            nestedAreaLabel.Name = "nestedAreaLabel";
            nestedAreaLabel.Size = new System.Drawing.Size(55, 17);
            nestedAreaLabel.TabIndex = 4;
            nestedAreaLabel.Text = "Nested:";
            // 
            // nestedAreaValue
            // 
            nestedAreaValue.AutoSize = true;
            nestedAreaValue.Font = new System.Drawing.Font("Consolas", 9.75F);
            nestedAreaValue.Location = new System.Drawing.Point(90, 49);
            nestedAreaValue.Margin = new System.Windows.Forms.Padding(0, 3, 0, 3);
            nestedAreaValue.Name = "nestedAreaValue";
            nestedAreaValue.Size = new System.Drawing.Size(13, 15);
            nestedAreaValue.TabIndex = 5;
            nestedAreaValue.Text = "�";
            // 
            // resultsHeader
            // 
            resultsHeader.AutoSize = true;
            resultsHeader.Dock = System.Windows.Forms.DockStyle.Top;
            resultsHeader.Font = new System.Drawing.Font("Segoe UI", 10.5F, System.Drawing.FontStyle.Bold);
            resultsHeader.ForeColor = System.Drawing.Color.FromArgb(85, 85, 85);
            resultsHeader.Location = new System.Drawing.Point(14, 10);
            resultsHeader.Name = "resultsHeader";
            resultsHeader.Padding = new System.Windows.Forms.Padding(0, 0, 0, 4);
            resultsHeader.Size = new System.Drawing.Size(65, 23);
            resultsHeader.TabIndex = 0;
            resultsHeader.Text = "RESULTS";
            // 
            // statusPanel
            // 
            statusPanel.BackColor = System.Drawing.Color.White;
            statusPanel.Controls.Add(statusTable);
            statusPanel.Controls.Add(statusHeader);
            statusPanel.Dock = System.Windows.Forms.DockStyle.Top;
            statusPanel.Location = new System.Drawing.Point(0, 180);
            statusPanel.Name = "statusPanel";
            statusPanel.Padding = new System.Windows.Forms.Padding(14, 10, 14, 10);
            statusPanel.Size = new System.Drawing.Size(450, 115);
            statusPanel.TabIndex = 2;
            // 
            // statusTable
            // 
            statusTable.AutoSize = true;
            statusTable.ColumnCount = 2;
            statusTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 90F));
            statusTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            statusTable.Controls.Add(plateLabel, 0, 0);
            statusTable.Controls.Add(plateValue, 1, 0);
            statusTable.Controls.Add(elapsedLabel, 0, 1);
            statusTable.Controls.Add(elapsedValue, 1, 1);
            statusTable.Controls.Add(descriptionLabel, 0, 2);
            statusTable.Controls.Add(descriptionValue, 1, 2);
            statusTable.Dock = System.Windows.Forms.DockStyle.Top;
            statusTable.Location = new System.Drawing.Point(14, 33);
            statusTable.Name = "statusTable";
            statusTable.RowCount = 3;
            statusTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            statusTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            statusTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            statusTable.Size = new System.Drawing.Size(422, 69);
            statusTable.TabIndex = 1;
            // 
            // plateLabel
            // 
            plateLabel.AutoSize = true;
            plateLabel.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Bold);
            plateLabel.ForeColor = System.Drawing.Color.FromArgb(51, 51, 51);
            plateLabel.Location = new System.Drawing.Point(0, 3);
            plateLabel.Margin = new System.Windows.Forms.Padding(0, 3, 5, 3);
            plateLabel.Name = "plateLabel";
            plateLabel.Size = new System.Drawing.Size(43, 17);
            plateLabel.TabIndex = 0;
            plateLabel.Text = "Plate:";
            // 
            // plateValue
            // 
            plateValue.AutoSize = true;
            plateValue.Font = new System.Drawing.Font("Consolas", 9.75F);
            plateValue.Location = new System.Drawing.Point(90, 3);
            plateValue.Margin = new System.Windows.Forms.Padding(0, 3, 0, 3);
            plateValue.Name = "plateValue";
            plateValue.Size = new System.Drawing.Size(13, 15);
            plateValue.TabIndex = 1;
            plateValue.Text = "�";
            // 
            // elapsedLabel
            // 
            elapsedLabel.AutoSize = true;
            elapsedLabel.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Bold);
            elapsedLabel.ForeColor = System.Drawing.Color.FromArgb(51, 51, 51);
            elapsedLabel.Location = new System.Drawing.Point(0, 26);
            elapsedLabel.Margin = new System.Windows.Forms.Padding(0, 3, 5, 3);
            elapsedLabel.Name = "elapsedLabel";
            elapsedLabel.Size = new System.Drawing.Size(59, 17);
            elapsedLabel.TabIndex = 2;
            elapsedLabel.Text = "Elapsed:";
            // 
            // elapsedValue
            // 
            elapsedValue.AutoSize = true;
            elapsedValue.Font = new System.Drawing.Font("Consolas", 9.75F);
            elapsedValue.Location = new System.Drawing.Point(90, 26);
            elapsedValue.Margin = new System.Windows.Forms.Padding(0, 3, 0, 3);
            elapsedValue.Name = "elapsedValue";
            elapsedValue.Size = new System.Drawing.Size(35, 15);
            elapsedValue.TabIndex = 3;
            elapsedValue.Text = "0:00";
            // 
            // descriptionLabel
            // 
            descriptionLabel.AutoSize = true;
            descriptionLabel.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Bold);
            descriptionLabel.ForeColor = System.Drawing.Color.FromArgb(51, 51, 51);
            descriptionLabel.Location = new System.Drawing.Point(0, 49);
            descriptionLabel.Margin = new System.Windows.Forms.Padding(0, 3, 5, 3);
            descriptionLabel.Name = "descriptionLabel";
            descriptionLabel.Size = new System.Drawing.Size(49, 17);
            descriptionLabel.TabIndex = 4;
            descriptionLabel.Text = "Detail:";
            // 
            // descriptionValue
            // 
            descriptionValue.AutoSize = true;
            descriptionValue.Font = new System.Drawing.Font("Segoe UI", 9.75F);
            descriptionValue.Location = new System.Drawing.Point(90, 49);
            descriptionValue.Margin = new System.Windows.Forms.Padding(0, 3, 0, 3);
            descriptionValue.Name = "descriptionValue";
            descriptionValue.Size = new System.Drawing.Size(20, 17);
            descriptionValue.TabIndex = 5;
            descriptionValue.Text = "�";
            // 
            // statusHeader
            // 
            statusHeader.AutoSize = true;
            statusHeader.Dock = System.Windows.Forms.DockStyle.Top;
            statusHeader.Font = new System.Drawing.Font("Segoe UI", 10.5F, System.Drawing.FontStyle.Bold);
            statusHeader.ForeColor = System.Drawing.Color.FromArgb(85, 85, 85);
            statusHeader.Location = new System.Drawing.Point(14, 10);
            statusHeader.Name = "statusHeader";
            statusHeader.Padding = new System.Windows.Forms.Padding(0, 0, 0, 4);
            statusHeader.Size = new System.Drawing.Size(59, 23);
            statusHeader.TabIndex = 0;
            statusHeader.Text = "STATUS";
            // 
            // buttonPanel
            // 
            buttonPanel.AutoSize = true;
            buttonPanel.Controls.Add(stopButton);
            buttonPanel.Controls.Add(acceptButton);
            buttonPanel.Dock = System.Windows.Forms.DockStyle.Top;
            buttonPanel.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
            buttonPanel.Location = new System.Drawing.Point(0, 295);
            buttonPanel.Name = "buttonPanel";
            buttonPanel.Padding = new System.Windows.Forms.Padding(9, 6, 9, 6);
            buttonPanel.Size = new System.Drawing.Size(450, 45);
            buttonPanel.TabIndex = 3;
            // 
            // stopButton
            // 
            stopButton.Enabled = false;
            stopButton.Font = new System.Drawing.Font("Segoe UI", 9.75F);
            stopButton.Location = new System.Drawing.Point(339, 9);
            stopButton.Margin = new System.Windows.Forms.Padding(0, 3, 0, 3);
            stopButton.Name = "stopButton";
            stopButton.Size = new System.Drawing.Size(93, 27);
            stopButton.TabIndex = 0;
            stopButton.Text = "Stop";
            stopButton.UseVisualStyleBackColor = true;
            stopButton.Click += StopButton_Click;
            // 
            // acceptButton
            // 
            acceptButton.Enabled = false;
            acceptButton.Font = new System.Drawing.Font("Segoe UI", 9.75F);
            acceptButton.Location = new System.Drawing.Point(246, 9);
            acceptButton.Margin = new System.Windows.Forms.Padding(6, 3, 0, 3);
            acceptButton.Name = "acceptButton";
            acceptButton.Size = new System.Drawing.Size(93, 27);
            acceptButton.TabIndex = 1;
            acceptButton.Text = "Accept";
            acceptButton.UseVisualStyleBackColor = true;
            acceptButton.Click += AcceptButton_Click;
            // 
            // NestProgressForm
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(450, 345);
            Controls.Add(buttonPanel);
            Controls.Add(statusPanel);
            Controls.Add(resultsPanel);
            Controls.Add(phaseStepper);
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "NestProgressForm";
            ShowInTaskbar = false;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "Nesting Progress";
            resultsPanel.ResumeLayout(false);
            resultsPanel.PerformLayout();
            resultsTable.ResumeLayout(false);
            resultsTable.PerformLayout();
            densityPanel.ResumeLayout(false);
            densityPanel.PerformLayout();
            statusPanel.ResumeLayout(false);
            statusPanel.PerformLayout();
            statusTable.ResumeLayout(false);
            statusTable.PerformLayout();
            buttonPanel.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Controls.PhaseStepperControl phaseStepper;
        private System.Windows.Forms.Panel resultsPanel;
        private System.Windows.Forms.Label resultsHeader;
        private System.Windows.Forms.TableLayoutPanel resultsTable;
        private System.Windows.Forms.Label partsLabel;
        private System.Windows.Forms.Label partsValue;
        private System.Windows.Forms.Label densityLabel;
        private System.Windows.Forms.FlowLayoutPanel densityPanel;
        private System.Windows.Forms.Label densityValue;
        private Controls.DensityBar densityBar;
        private System.Windows.Forms.Label nestedAreaLabel;
        private System.Windows.Forms.Label nestedAreaValue;
        private System.Windows.Forms.Panel statusPanel;
        private System.Windows.Forms.Label statusHeader;
        private System.Windows.Forms.TableLayoutPanel statusTable;
        private System.Windows.Forms.Label plateLabel;
        private System.Windows.Forms.Label plateValue;
        private System.Windows.Forms.Label elapsedLabel;
        private System.Windows.Forms.Label elapsedValue;
        private System.Windows.Forms.Label descriptionLabel;
        private System.Windows.Forms.Label descriptionValue;
        private System.Windows.Forms.FlowLayoutPanel buttonPanel;
        private System.Windows.Forms.Button acceptButton;
        private System.Windows.Forms.Button stopButton;
    }
}
