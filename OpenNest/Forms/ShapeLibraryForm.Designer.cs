namespace OpenNest.Forms
{
    partial class ShapeLibraryForm
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
            ColorScheme colorScheme1 = new ColorScheme();
            CutOffSettings cutOffSettings1 = new CutOffSettings();
            Plate plate1 = new Plate();
            Collections.ObservableList<CutOff> observableList_11 = new Collections.ObservableList<CutOff>();
            Collections.ObservableList<Part> observableList_12 = new Collections.ObservableList<Part>();
            splitContainer = new System.Windows.Forms.SplitContainer();
            shapeListBox = new System.Windows.Forms.ListBox();
            layoutTable = new System.Windows.Forms.TableLayoutPanel();
            fieldsTable = new System.Windows.Forms.TableLayoutPanel();
            nameLabel = new System.Windows.Forms.Label();
            nameTextBox = new System.Windows.Forms.TextBox();
            qtyLabel = new System.Windows.Forms.Label();
            quantityUpDown = new OpenNest.Controls.NumericUpDown();
            configLabel = new System.Windows.Forms.Label();
            configComboBox = new System.Windows.Forms.ComboBox();
            contentPanel = new System.Windows.Forms.Panel();
            previewBox = new OpenNest.Controls.ShapePreviewControl();
            parametersPanel = new System.Windows.Forms.Panel();
            buttonPanel = new System.Windows.Forms.Panel();
            addButton = new System.Windows.Forms.Button();
            closeButton = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)splitContainer).BeginInit();
            splitContainer.Panel1.SuspendLayout();
            splitContainer.Panel2.SuspendLayout();
            splitContainer.SuspendLayout();
            layoutTable.SuspendLayout();
            fieldsTable.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)quantityUpDown).BeginInit();
            contentPanel.SuspendLayout();
            buttonPanel.SuspendLayout();
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
            splitContainer.Panel1.Controls.Add(shapeListBox);
            // 
            // splitContainer.Panel2
            // 
            splitContainer.Panel2.Controls.Add(layoutTable);
            splitContainer.Size = new System.Drawing.Size(750, 520);
            splitContainer.SplitterDistance = 150;
            splitContainer.TabIndex = 0;
            // 
            // shapeListBox
            // 
            shapeListBox.BorderStyle = System.Windows.Forms.BorderStyle.None;
            shapeListBox.Dock = System.Windows.Forms.DockStyle.Fill;
            shapeListBox.DrawMode = System.Windows.Forms.DrawMode.OwnerDrawFixed;
            shapeListBox.Font = new System.Drawing.Font("Segoe UI", 10F);
            shapeListBox.IntegralHeight = false;
            shapeListBox.ItemHeight = 32;
            shapeListBox.Location = new System.Drawing.Point(0, 0);
            shapeListBox.Name = "shapeListBox";
            shapeListBox.Size = new System.Drawing.Size(150, 520);
            shapeListBox.TabIndex = 0;
            // 
            // layoutTable
            // 
            layoutTable.ColumnCount = 1;
            layoutTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            layoutTable.Controls.Add(fieldsTable, 0, 0);
            layoutTable.Controls.Add(contentPanel, 0, 1);
            layoutTable.Controls.Add(buttonPanel, 0, 2);
            layoutTable.Dock = System.Windows.Forms.DockStyle.Fill;
            layoutTable.Location = new System.Drawing.Point(0, 0);
            layoutTable.Name = "layoutTable";
            layoutTable.Padding = new System.Windows.Forms.Padding(6, 4, 6, 0);
            layoutTable.RowCount = 3;
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 44F));
            layoutTable.Size = new System.Drawing.Size(596, 520);
            layoutTable.TabIndex = 0;
            // 
            // fieldsTable
            // 
            fieldsTable.AutoSize = true;
            fieldsTable.ColumnCount = 2;
            fieldsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            fieldsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            fieldsTable.Controls.Add(nameLabel, 0, 0);
            fieldsTable.Controls.Add(nameTextBox, 1, 0);
            fieldsTable.Controls.Add(qtyLabel, 0, 1);
            fieldsTable.Controls.Add(quantityUpDown, 1, 1);
            fieldsTable.Controls.Add(configLabel, 0, 2);
            fieldsTable.Controls.Add(configComboBox, 1, 2);
            fieldsTable.Dock = System.Windows.Forms.DockStyle.Fill;
            fieldsTable.Location = new System.Drawing.Point(6, 4);
            fieldsTable.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
            fieldsTable.Name = "fieldsTable";
            fieldsTable.RowCount = 3;
            fieldsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            fieldsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            fieldsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            fieldsTable.Size = new System.Drawing.Size(584, 97);
            fieldsTable.TabIndex = 0;
            // 
            // nameLabel
            // 
            nameLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            nameLabel.AutoSize = true;
            nameLabel.Location = new System.Drawing.Point(4, 8);
            nameLabel.Margin = new System.Windows.Forms.Padding(4, 4, 8, 4);
            nameLabel.Name = "nameLabel";
            nameLabel.Size = new System.Drawing.Size(46, 17);
            nameLabel.TabIndex = 0;
            nameLabel.Text = "Name:";
            // 
            // nameTextBox
            // 
            nameTextBox.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            nameTextBox.Location = new System.Drawing.Point(102, 4);
            nameTextBox.Margin = new System.Windows.Forms.Padding(0, 4, 4, 4);
            nameTextBox.Name = "nameTextBox";
            nameTextBox.Size = new System.Drawing.Size(478, 25);
            nameTextBox.TabIndex = 1;
            // 
            // qtyLabel
            // 
            qtyLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            qtyLabel.AutoSize = true;
            qtyLabel.Location = new System.Drawing.Point(4, 41);
            qtyLabel.Margin = new System.Windows.Forms.Padding(4, 4, 8, 4);
            qtyLabel.Name = "qtyLabel";
            qtyLabel.Size = new System.Drawing.Size(59, 17);
            qtyLabel.TabIndex = 2;
            qtyLabel.Text = "Quantity:";
            // 
            // quantityUpDown
            // 
            quantityUpDown.Location = new System.Drawing.Point(102, 37);
            quantityUpDown.Margin = new System.Windows.Forms.Padding(0, 4, 4, 4);
            quantityUpDown.Maximum = new decimal(new int[] { 999999, 0, 0, 0 });
            quantityUpDown.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            quantityUpDown.Name = "quantityUpDown";
            quantityUpDown.Size = new System.Drawing.Size(100, 25);
            quantityUpDown.Suffix = "";
            quantityUpDown.TabIndex = 2;
            quantityUpDown.Value = new decimal(new int[] { 1, 0, 0, 0 });
            // 
            // configLabel
            // 
            configLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            configLabel.AutoSize = true;
            configLabel.Location = new System.Drawing.Point(4, 73);
            configLabel.Margin = new System.Windows.Forms.Padding(4, 4, 8, 4);
            configLabel.Name = "configLabel";
            configLabel.Size = new System.Drawing.Size(90, 17);
            configLabel.TabIndex = 3;
            configLabel.Text = "Configuration:";
            configLabel.Visible = false;
            // 
            // configComboBox
            // 
            configComboBox.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            configComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            configComboBox.Location = new System.Drawing.Point(102, 70);
            configComboBox.Margin = new System.Windows.Forms.Padding(0, 4, 4, 4);
            configComboBox.Name = "configComboBox";
            configComboBox.Size = new System.Drawing.Size(478, 25);
            configComboBox.TabIndex = 3;
            configComboBox.Visible = false;
            // 
            // contentPanel
            // 
            contentPanel.Controls.Add(previewBox);
            contentPanel.Controls.Add(parametersPanel);
            contentPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            contentPanel.Location = new System.Drawing.Point(9, 108);
            contentPanel.Name = "contentPanel";
            contentPanel.Size = new System.Drawing.Size(578, 365);
            contentPanel.TabIndex = 1;
            // 
            // previewBox
            // 
            previewBox.ActiveWorkArea = null;
            previewBox.AllowPan = false;
            previewBox.AllowSelect = false;
            previewBox.AllowZoom = false;
            previewBox.BackColor = System.Drawing.Color.White;
            colorScheme1.BackgroundColor = System.Drawing.Color.DarkGray;
            colorScheme1.BoundingBoxColor = System.Drawing.Color.FromArgb(128, 128, 255);
            colorScheme1.EdgeSpacingColor = System.Drawing.Color.FromArgb(180, 180, 180);
            colorScheme1.LayoutFillColor = System.Drawing.Color.WhiteSmoke;
            colorScheme1.LayoutOutlineColor = System.Drawing.Color.Gray;
            colorScheme1.OriginColor = System.Drawing.Color.Gray;
            colorScheme1.PreviewPartColor = System.Drawing.Color.FromArgb(255, 140, 0);
            colorScheme1.RapidColor = System.Drawing.Color.DodgerBlue;
            previewBox.ColorScheme = colorScheme1;
            cutOffSettings1.CutDirection = CutDirection.AwayFromOrigin;
            cutOffSettings1.MinSegmentLength = 0.05D;
            cutOffSettings1.Overtravel = 0D;
            cutOffSettings1.PartClearance = 0.02D;
            previewBox.CutOffSettings = cutOffSettings1;
            previewBox.DebugRemnantPriorities = null;
            previewBox.DebugRemnants = null;
            previewBox.Dock = System.Windows.Forms.DockStyle.Fill;
            previewBox.DrawBounds = false;
            previewBox.DrawCutDirection = false;
            previewBox.DrawOffset = false;
            previewBox.DrawOrigin = false;
            previewBox.DrawPiercePoints = false;
            previewBox.DrawRapid = false;
            previewBox.FillParts = true;
            previewBox.Location = new System.Drawing.Point(0, 0);
            previewBox.Name = "previewBox";
            previewBox.OffsetIncrementDistance = 10D;
            previewBox.OffsetTolerance = 0.001D;
            plate1.CutOffs = observableList_11;
            plate1.CuttingParameters = null;
            plate1.GrainAngle = 0D;
            plate1.Parts = observableList_12;
            plate1.PartSpacing = 0D;
            plate1.Quadrant = 1;
            plate1.Quantity = 0;
            previewBox.Plate = plate1;
            previewBox.RotateIncrementAngle = 10D;
            previewBox.ShowBendLines = false;
            previewBox.Size = new System.Drawing.Size(318, 365);
            previewBox.Status = "Select";
            previewBox.TabIndex = 4;
            previewBox.TabStop = false;
            // 
            // parametersPanel
            // 
            parametersPanel.AutoScroll = true;
            parametersPanel.Dock = System.Windows.Forms.DockStyle.Right;
            parametersPanel.Location = new System.Drawing.Point(318, 0);
            parametersPanel.Name = "parametersPanel";
            parametersPanel.Padding = new System.Windows.Forms.Padding(8, 0, 0, 0);
            parametersPanel.Size = new System.Drawing.Size(260, 365);
            parametersPanel.TabIndex = 5;
            // 
            // buttonPanel
            // 
            buttonPanel.Controls.Add(addButton);
            buttonPanel.Controls.Add(closeButton);
            buttonPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            buttonPanel.Location = new System.Drawing.Point(9, 479);
            buttonPanel.Name = "buttonPanel";
            buttonPanel.Size = new System.Drawing.Size(578, 38);
            buttonPanel.TabIndex = 2;
            // 
            // addButton
            // 
            addButton.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            addButton.Location = new System.Drawing.Point(379, 5);
            addButton.Name = "addButton";
            addButton.Size = new System.Drawing.Size(100, 30);
            addButton.TabIndex = 0;
            addButton.Text = "Add to Nest";
            addButton.UseVisualStyleBackColor = true;
            // 
            // closeButton
            // 
            closeButton.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            closeButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            closeButton.Location = new System.Drawing.Point(485, 5);
            closeButton.Name = "closeButton";
            closeButton.Size = new System.Drawing.Size(90, 30);
            closeButton.TabIndex = 1;
            closeButton.Text = "Close";
            closeButton.UseVisualStyleBackColor = true;
            // 
            // ShapeLibraryForm
            // 
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            CancelButton = closeButton;
            ClientSize = new System.Drawing.Size(750, 520);
            Controls.Add(splitContainer);
            Font = new System.Drawing.Font("Segoe UI", 9.75F);
            MinimizeBox = false;
            MinimumSize = new System.Drawing.Size(600, 400);
            Name = "ShapeLibraryForm";
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "Shape Library";
            splitContainer.Panel1.ResumeLayout(false);
            splitContainer.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitContainer).EndInit();
            splitContainer.ResumeLayout(false);
            layoutTable.ResumeLayout(false);
            layoutTable.PerformLayout();
            fieldsTable.ResumeLayout(false);
            fieldsTable.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)quantityUpDown).EndInit();
            contentPanel.ResumeLayout(false);
            buttonPanel.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.SplitContainer splitContainer;
        private System.Windows.Forms.ListBox shapeListBox;
        private System.Windows.Forms.TableLayoutPanel layoutTable;
        private System.Windows.Forms.TableLayoutPanel fieldsTable;
        private System.Windows.Forms.Label nameLabel;
        private System.Windows.Forms.TextBox nameTextBox;
        private System.Windows.Forms.Label qtyLabel;
        private Controls.NumericUpDown quantityUpDown;
        private System.Windows.Forms.Label configLabel;
        private System.Windows.Forms.ComboBox configComboBox;
        private System.Windows.Forms.Panel contentPanel;
        private Controls.ShapePreviewControl previewBox;
        private System.Windows.Forms.Panel parametersPanel;
        private System.Windows.Forms.Panel buttonPanel;
        private System.Windows.Forms.Button addButton;
        private System.Windows.Forms.Button closeButton;
    }
}
