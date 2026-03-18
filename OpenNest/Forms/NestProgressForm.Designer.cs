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
            phaseStepper = new Controls.PhaseStepperControl();
            resultsPanel = new System.Windows.Forms.Panel();
            resultsHeader = new System.Windows.Forms.Label();
            resultsTable = new System.Windows.Forms.TableLayoutPanel();
            partsLabel = new System.Windows.Forms.Label();
            partsValue = new System.Windows.Forms.Label();
            densityLabel = new System.Windows.Forms.Label();
            densityPanel = new System.Windows.Forms.FlowLayoutPanel();
            densityValue = new System.Windows.Forms.Label();
            densityBar = new Controls.DensityBar();
            nestedAreaLabel = new System.Windows.Forms.Label();
            nestedAreaValue = new System.Windows.Forms.Label();
            statusPanel = new System.Windows.Forms.Panel();
            statusHeader = new System.Windows.Forms.Label();
            statusTable = new System.Windows.Forms.TableLayoutPanel();
            plateLabel = new System.Windows.Forms.Label();
            plateValue = new System.Windows.Forms.Label();
            elapsedLabel = new System.Windows.Forms.Label();
            elapsedValue = new System.Windows.Forms.Label();
            descriptionLabel = new System.Windows.Forms.Label();
            descriptionValue = new System.Windows.Forms.Label();
            buttonPanel = new System.Windows.Forms.FlowLayoutPanel();
            acceptButton = new System.Windows.Forms.Button();
            stopButton = new System.Windows.Forms.Button();

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
            phaseStepper.Dock = System.Windows.Forms.DockStyle.Top;
            phaseStepper.Height = 60;
            phaseStepper.Name = "phaseStepper";
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
            resultsPanel.Size = new System.Drawing.Size(450, 105);
            resultsPanel.TabIndex = 1;

            //
            // resultsHeader
            //
            resultsHeader.AutoSize = true;
            resultsHeader.Dock = System.Windows.Forms.DockStyle.Top;
            resultsHeader.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            resultsHeader.ForeColor = System.Drawing.Color.FromArgb(85, 85, 85);
            resultsHeader.Name = "resultsHeader";
            resultsHeader.Padding = new System.Windows.Forms.Padding(0, 0, 0, 4);
            resultsHeader.Size = new System.Drawing.Size(63, 19);
            resultsHeader.TabIndex = 0;
            resultsHeader.Text = "RESULTS";

            //
            // resultsTable
            //
            resultsTable.AutoSize = true;
            resultsTable.ColumnCount = 2;
            resultsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 80F));
            resultsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            resultsTable.Controls.Add(partsLabel, 0, 0);
            resultsTable.Controls.Add(partsValue, 1, 0);
            resultsTable.Controls.Add(densityLabel, 0, 1);
            resultsTable.Controls.Add(densityPanel, 1, 1);
            resultsTable.Controls.Add(nestedAreaLabel, 0, 2);
            resultsTable.Controls.Add(nestedAreaValue, 1, 2);
            resultsTable.Dock = System.Windows.Forms.DockStyle.Top;
            resultsTable.Name = "resultsTable";
            resultsTable.RowCount = 3;
            resultsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            resultsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            resultsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            resultsTable.TabIndex = 1;

            //
            // partsLabel
            //
            partsLabel.AutoSize = true;
            partsLabel.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Bold);
            partsLabel.ForeColor = System.Drawing.Color.FromArgb(51, 51, 51);
            partsLabel.Margin = new System.Windows.Forms.Padding(0, 3, 5, 3);
            partsLabel.Name = "partsLabel";
            partsLabel.Text = "Parts:";

            //
            // partsValue
            //
            partsValue.AutoSize = true;
            partsValue.Font = new System.Drawing.Font("Consolas", 8.25F);
            partsValue.Margin = new System.Windows.Forms.Padding(0, 3, 0, 3);
            partsValue.Name = "partsValue";
            partsValue.Text = "\u2014";

            //
            // densityLabel
            //
            densityLabel.AutoSize = true;
            densityLabel.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Bold);
            densityLabel.ForeColor = System.Drawing.Color.FromArgb(51, 51, 51);
            densityLabel.Margin = new System.Windows.Forms.Padding(0, 3, 5, 3);
            densityLabel.Name = "densityLabel";
            densityLabel.Text = "Density:";

            //
            // densityPanel
            //
            densityPanel.AutoSize = true;
            densityPanel.Controls.Add(densityValue);
            densityPanel.Controls.Add(densityBar);
            densityPanel.FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight;
            densityPanel.Margin = new System.Windows.Forms.Padding(0);
            densityPanel.Name = "densityPanel";
            densityPanel.WrapContents = false;

            //
            // densityValue
            //
            densityValue.AutoSize = true;
            densityValue.Font = new System.Drawing.Font("Consolas", 8.25F);
            densityValue.Margin = new System.Windows.Forms.Padding(0, 3, 8, 3);
            densityValue.Name = "densityValue";
            densityValue.Text = "\u2014";

            //
            // densityBar
            //
            densityBar.Margin = new System.Windows.Forms.Padding(0, 5, 0, 0);
            densityBar.Name = "densityBar";
            densityBar.Size = new System.Drawing.Size(60, 8);

            //
            // nestedAreaLabel
            //
            nestedAreaLabel.AutoSize = true;
            nestedAreaLabel.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Bold);
            nestedAreaLabel.ForeColor = System.Drawing.Color.FromArgb(51, 51, 51);
            nestedAreaLabel.Margin = new System.Windows.Forms.Padding(0, 3, 5, 3);
            nestedAreaLabel.Name = "nestedAreaLabel";
            nestedAreaLabel.Text = "Nested:";

            //
            // nestedAreaValue
            //
            nestedAreaValue.AutoSize = true;
            nestedAreaValue.Font = new System.Drawing.Font("Consolas", 8.25F);
            nestedAreaValue.Margin = new System.Windows.Forms.Padding(0, 3, 0, 3);
            nestedAreaValue.Name = "nestedAreaValue";
            nestedAreaValue.Text = "\u2014";

            //
            // statusPanel
            //
            statusPanel.BackColor = System.Drawing.Color.White;
            statusPanel.Controls.Add(statusTable);
            statusPanel.Controls.Add(statusHeader);
            statusPanel.Dock = System.Windows.Forms.DockStyle.Top;
            statusPanel.Location = new System.Drawing.Point(0, 169);
            statusPanel.Name = "statusPanel";
            statusPanel.Padding = new System.Windows.Forms.Padding(14, 10, 14, 10);
            statusPanel.Size = new System.Drawing.Size(450, 100);
            statusPanel.TabIndex = 2;

            //
            // statusHeader
            //
            statusHeader.AutoSize = true;
            statusHeader.Dock = System.Windows.Forms.DockStyle.Top;
            statusHeader.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            statusHeader.ForeColor = System.Drawing.Color.FromArgb(85, 85, 85);
            statusHeader.Name = "statusHeader";
            statusHeader.Padding = new System.Windows.Forms.Padding(0, 0, 0, 4);
            statusHeader.Size = new System.Drawing.Size(55, 19);
            statusHeader.TabIndex = 0;
            statusHeader.Text = "STATUS";

            //
            // statusTable
            //
            statusTable.AutoSize = true;
            statusTable.ColumnCount = 2;
            statusTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 80F));
            statusTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            statusTable.Controls.Add(plateLabel, 0, 0);
            statusTable.Controls.Add(plateValue, 1, 0);
            statusTable.Controls.Add(elapsedLabel, 0, 1);
            statusTable.Controls.Add(elapsedValue, 1, 1);
            statusTable.Controls.Add(descriptionLabel, 0, 2);
            statusTable.Controls.Add(descriptionValue, 1, 2);
            statusTable.Dock = System.Windows.Forms.DockStyle.Top;
            statusTable.Name = "statusTable";
            statusTable.RowCount = 3;
            statusTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            statusTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            statusTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            statusTable.TabIndex = 1;

            //
            // plateLabel
            //
            plateLabel.AutoSize = true;
            plateLabel.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Bold);
            plateLabel.ForeColor = System.Drawing.Color.FromArgb(51, 51, 51);
            plateLabel.Margin = new System.Windows.Forms.Padding(0, 3, 5, 3);
            plateLabel.Name = "plateLabel";
            plateLabel.Text = "Plate:";

            //
            // plateValue
            //
            plateValue.AutoSize = true;
            plateValue.Font = new System.Drawing.Font("Consolas", 8.25F);
            plateValue.Margin = new System.Windows.Forms.Padding(0, 3, 0, 3);
            plateValue.Name = "plateValue";
            plateValue.Text = "\u2014";

            //
            // elapsedLabel
            //
            elapsedLabel.AutoSize = true;
            elapsedLabel.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Bold);
            elapsedLabel.ForeColor = System.Drawing.Color.FromArgb(51, 51, 51);
            elapsedLabel.Margin = new System.Windows.Forms.Padding(0, 3, 5, 3);
            elapsedLabel.Name = "elapsedLabel";
            elapsedLabel.Text = "Elapsed:";

            //
            // elapsedValue
            //
            elapsedValue.AutoSize = true;
            elapsedValue.Font = new System.Drawing.Font("Consolas", 8.25F);
            elapsedValue.Margin = new System.Windows.Forms.Padding(0, 3, 0, 3);
            elapsedValue.Name = "elapsedValue";
            elapsedValue.Text = "0:00";

            //
            // descriptionLabel
            //
            descriptionLabel.AutoSize = true;
            descriptionLabel.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Bold);
            descriptionLabel.ForeColor = System.Drawing.Color.FromArgb(51, 51, 51);
            descriptionLabel.Margin = new System.Windows.Forms.Padding(0, 3, 5, 3);
            descriptionLabel.Name = "descriptionLabel";
            descriptionLabel.Text = "Detail:";

            //
            // descriptionValue
            //
            descriptionValue.AutoSize = true;
            descriptionValue.Font = new System.Drawing.Font("Segoe UI", 8.25F);
            descriptionValue.Margin = new System.Windows.Forms.Padding(0, 3, 0, 3);
            descriptionValue.Name = "descriptionValue";
            descriptionValue.Text = "\u2014";

            //
            // buttonPanel
            //
            buttonPanel.AutoSize = true;
            buttonPanel.Controls.Add(stopButton);
            buttonPanel.Controls.Add(acceptButton);
            buttonPanel.Dock = System.Windows.Forms.DockStyle.Top;
            buttonPanel.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
            buttonPanel.Name = "buttonPanel";
            buttonPanel.Padding = new System.Windows.Forms.Padding(9, 6, 9, 6);
            buttonPanel.Size = new System.Drawing.Size(450, 45);
            buttonPanel.TabIndex = 3;

            //
            // acceptButton
            //
            acceptButton.Enabled = false;
            acceptButton.Font = new System.Drawing.Font("Segoe UI", 8.25F);
            acceptButton.Margin = new System.Windows.Forms.Padding(6, 3, 0, 3);
            acceptButton.Name = "acceptButton";
            acceptButton.Size = new System.Drawing.Size(93, 27);
            acceptButton.TabIndex = 1;
            acceptButton.Text = "Accept";
            acceptButton.UseVisualStyleBackColor = true;
            acceptButton.Click += AcceptButton_Click;

            //
            // stopButton
            //
            stopButton.Enabled = false;
            stopButton.Font = new System.Drawing.Font("Segoe UI", 8.25F);
            stopButton.Margin = new System.Windows.Forms.Padding(0, 3, 0, 3);
            stopButton.Name = "stopButton";
            stopButton.Size = new System.Drawing.Size(93, 27);
            stopButton.TabIndex = 0;
            stopButton.Text = "Stop";
            stopButton.UseVisualStyleBackColor = true;
            stopButton.Click += StopButton_Click;

            //
            // NestProgressForm
            //
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(450, 315);
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
