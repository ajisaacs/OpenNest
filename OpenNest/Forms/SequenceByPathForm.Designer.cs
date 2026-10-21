namespace OpenNest.Forms
{
    partial class SequenceByPathForm
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
            tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
            statusLabel = new System.Windows.Forms.Label();
            undoButton = new System.Windows.Forms.Button();
            clearButton = new System.Windows.Forms.Button();
            doneButton = new System.Windows.Forms.Button();
            tableLayoutPanel1.SuspendLayout();
            SuspendLayout();
            //
            // tableLayoutPanel1
            //
            tableLayoutPanel1.ColumnCount = 3;
            tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 33.33F));
            tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 33.33F));
            tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 33.34F));
            tableLayoutPanel1.Controls.Add(statusLabel, 0, 0);
            tableLayoutPanel1.SetColumnSpan(statusLabel, 3);
            tableLayoutPanel1.Controls.Add(undoButton, 0, 1);
            tableLayoutPanel1.Controls.Add(clearButton, 1, 1);
            tableLayoutPanel1.Controls.Add(doneButton, 2, 1);
            tableLayoutPanel1.Dock = System.Windows.Forms.DockStyle.Fill;
            tableLayoutPanel1.Location = new System.Drawing.Point(0, 0);
            tableLayoutPanel1.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            tableLayoutPanel1.Name = "tableLayoutPanel1";
            tableLayoutPanel1.Padding = new System.Windows.Forms.Padding(4, 3, 4, 3);
            tableLayoutPanel1.RowCount = 2;
            tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 35F));
            tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            tableLayoutPanel1.Size = new System.Drawing.Size(296, 74);
            tableLayoutPanel1.TabIndex = 0;
            //
            // statusLabel
            //
            statusLabel.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            statusLabel.AutoSize = true;
            statusLabel.Location = new System.Drawing.Point(8, 13);
            statusLabel.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            statusLabel.Name = "statusLabel";
            statusLabel.Size = new System.Drawing.Size(280, 15);
            statusLabel.TabIndex = 0;
            //
            // undoButton
            //
            undoButton.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            undoButton.Location = new System.Drawing.Point(8, 40);
            undoButton.Name = "undoButton";
            undoButton.Size = new System.Drawing.Size(89, 28);
            undoButton.TabIndex = 1;
            undoButton.Text = "Undo Line";
            undoButton.UseVisualStyleBackColor = true;
            undoButton.Click += undoButton_Click;
            //
            // clearButton
            //
            clearButton.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            clearButton.Location = new System.Drawing.Point(103, 40);
            clearButton.Name = "clearButton";
            clearButton.Size = new System.Drawing.Size(89, 28);
            clearButton.TabIndex = 2;
            clearButton.Text = "Clear";
            clearButton.UseVisualStyleBackColor = true;
            clearButton.Click += clearButton_Click;
            //
            // doneButton
            //
            doneButton.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            doneButton.Location = new System.Drawing.Point(198, 40);
            doneButton.Name = "doneButton";
            doneButton.Size = new System.Drawing.Size(90, 28);
            doneButton.TabIndex = 3;
            doneButton.Text = "Done";
            doneButton.UseVisualStyleBackColor = true;
            doneButton.Click += doneButton_Click;
            //
            // SequenceByPathForm
            //
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(296, 74);
            Controls.Add(tableLayoutPanel1);
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow;
            Location = new System.Drawing.Point(100, 100);
            Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "SequenceByPathForm";
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            Text = "Sequence by Path";
            tableLayoutPanel1.ResumeLayout(false);
            tableLayoutPanel1.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel1;
        private System.Windows.Forms.Label statusLabel;
        private System.Windows.Forms.Button undoButton;
        private System.Windows.Forms.Button clearButton;
        private System.Windows.Forms.Button doneButton;
    }
}
