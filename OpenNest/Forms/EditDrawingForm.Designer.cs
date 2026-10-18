using System.Drawing;
using System.Windows.Forms;

namespace OpenNest.Forms
{
    partial class EditDrawingForm
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
            if (disposing)
            {
                pictureBox1.Image?.Dispose();
                components?.Dispose();
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
            components = new System.ComponentModel.Container();
            layout = new TableLayoutPanel();
            previewPanel = new TableLayoutPanel();
            pictureBox1 = new PictureBox();
            colorPanel = new TableLayoutPanel();
            colorSwatch = new Panel();
            colorButton = new Button();
            infoPanel = new TableLayoutPanel();
            sizeCaption = new Label();
            sizeLabel = new Label();
            areaCaption = new Label();
            areaLabel = new Label();
            sourceCaption = new Label();
            sourceLabel = new Label();
            fieldsPanel = new TableLayoutPanel();
            drawingGroup = new GroupBox();
            drawingFields = new TableLayoutPanel();
            label1 = new Label();
            nameBox = new TextBox();
            label3 = new Label();
            customerBox = new TextBox();
            nestingGroup = new GroupBox();
            nestingFields = new TableLayoutPanel();
            label2 = new Label();
            qtyBox = new OpenNest.Controls.NumericUpDown();
            nestedLabel = new Label();
            label4 = new Label();
            priorityBox = new OpenNest.Controls.NumericUpDown();
            priorityHint = new Label();
            cutFirstBox = new CheckBox();
            cutFirstHint = new Label();
            bottomPanel1 = new OpenNest.Controls.BottomPanel();
            buttonsPanel = new FlowLayoutPanel();
            cancelButton = new Button();
            applyButton = new Button();
            colorDialog1 = new ColorDialog();
            toolTip = new ToolTip(components);
            layout.SuspendLayout();
            previewPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)pictureBox1).BeginInit();
            colorPanel.SuspendLayout();
            infoPanel.SuspendLayout();
            fieldsPanel.SuspendLayout();
            drawingGroup.SuspendLayout();
            drawingFields.SuspendLayout();
            nestingGroup.SuspendLayout();
            nestingFields.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)qtyBox).BeginInit();
            ((System.ComponentModel.ISupportInitialize)priorityBox).BeginInit();
            bottomPanel1.SuspendLayout();
            buttonsPanel.SuspendLayout();
            SuspendLayout();
            //
            // layout
            //
            layout.ColumnCount = 2;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 264F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.Controls.Add(previewPanel, 0, 0);
            layout.Controls.Add(fieldsPanel, 1, 0);
            layout.Dock = DockStyle.Fill;
            layout.Name = "layout";
            layout.Padding = new Padding(12, 12, 12, 6);
            layout.RowCount = 1;
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.TabIndex = 0;
            //
            // previewPanel
            //
            previewPanel.ColumnCount = 1;
            previewPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            previewPanel.Controls.Add(pictureBox1, 0, 0);
            previewPanel.Controls.Add(colorPanel, 0, 1);
            previewPanel.Controls.Add(infoPanel, 0, 2);
            previewPanel.Dock = DockStyle.Fill;
            previewPanel.Margin = new Padding(0, 0, 12, 0);
            previewPanel.Name = "previewPanel";
            previewPanel.RowCount = 3;
            previewPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 240F));
            previewPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            previewPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            previewPanel.TabIndex = 1;
            //
            // pictureBox1
            //
            pictureBox1.BackColor = Color.White;
            pictureBox1.BorderStyle = BorderStyle.FixedSingle;
            pictureBox1.Dock = DockStyle.Fill;
            pictureBox1.Margin = new Padding(0);
            pictureBox1.Name = "pictureBox1";
            pictureBox1.SizeMode = PictureBoxSizeMode.CenterImage;
            pictureBox1.TabStop = false;
            //
            // colorPanel
            //
            colorPanel.AutoSize = true;
            colorPanel.ColumnCount = 2;
            colorPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            colorPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            colorPanel.Controls.Add(colorSwatch, 0, 0);
            colorPanel.Controls.Add(colorButton, 1, 0);
            colorPanel.Dock = DockStyle.Fill;
            colorPanel.Margin = new Padding(0, 8, 0, 8);
            colorPanel.Name = "colorPanel";
            colorPanel.RowCount = 1;
            colorPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            colorPanel.TabIndex = 0;
            //
            // colorSwatch
            //
            colorSwatch.Anchor = AnchorStyles.Left;
            colorSwatch.BorderStyle = BorderStyle.FixedSingle;
            colorSwatch.Cursor = Cursors.Hand;
            colorSwatch.Margin = new Padding(0, 0, 8, 0);
            colorSwatch.Name = "colorSwatch";
            colorSwatch.Size = new Size(26, 26);
            colorSwatch.Click += ChangeColor_Click;
            //
            // colorButton
            //
            colorButton.Anchor = AnchorStyles.Left;
            colorButton.AutoSize = true;
            colorButton.Margin = new Padding(0);
            colorButton.Name = "colorButton";
            colorButton.Size = new Size(110, 27);
            colorButton.TabIndex = 0;
            colorButton.Text = "C&hange Color...";
            colorButton.UseVisualStyleBackColor = true;
            colorButton.Click += ChangeColor_Click;
            //
            // infoPanel
            //
            infoPanel.AutoSize = true;
            infoPanel.ColumnCount = 2;
            infoPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            infoPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            infoPanel.Controls.Add(sizeCaption, 0, 0);
            infoPanel.Controls.Add(sizeLabel, 1, 0);
            infoPanel.Controls.Add(areaCaption, 0, 1);
            infoPanel.Controls.Add(areaLabel, 1, 1);
            infoPanel.Controls.Add(sourceCaption, 0, 2);
            infoPanel.Controls.Add(sourceLabel, 1, 2);
            infoPanel.Dock = DockStyle.Top;
            infoPanel.Margin = new Padding(0);
            infoPanel.Name = "infoPanel";
            infoPanel.RowCount = 3;
            infoPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            infoPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            infoPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            //
            // sizeCaption
            //
            sizeCaption.AutoSize = true;
            sizeCaption.ForeColor = SystemColors.GrayText;
            sizeCaption.Margin = new Padding(0, 2, 8, 2);
            sizeCaption.Name = "sizeCaption";
            sizeCaption.Text = "Size";
            //
            // sizeLabel
            //
            sizeLabel.AutoEllipsis = true;
            sizeLabel.Dock = DockStyle.Fill;
            sizeLabel.Margin = new Padding(0, 2, 0, 2);
            sizeLabel.Name = "sizeLabel";
            sizeLabel.Size = new Size(200, 17);
            sizeLabel.Text = "-";
            //
            // areaCaption
            //
            areaCaption.AutoSize = true;
            areaCaption.ForeColor = SystemColors.GrayText;
            areaCaption.Margin = new Padding(0, 2, 8, 2);
            areaCaption.Name = "areaCaption";
            areaCaption.Text = "Area";
            //
            // areaLabel
            //
            areaLabel.AutoEllipsis = true;
            areaLabel.Dock = DockStyle.Fill;
            areaLabel.Margin = new Padding(0, 2, 0, 2);
            areaLabel.Name = "areaLabel";
            areaLabel.Size = new Size(200, 17);
            areaLabel.Text = "-";
            //
            // sourceCaption
            //
            sourceCaption.AutoSize = true;
            sourceCaption.ForeColor = SystemColors.GrayText;
            sourceCaption.Margin = new Padding(0, 2, 8, 2);
            sourceCaption.Name = "sourceCaption";
            sourceCaption.Text = "Source";
            //
            // sourceLabel
            //
            sourceLabel.AutoEllipsis = true;
            sourceLabel.Dock = DockStyle.Fill;
            sourceLabel.Margin = new Padding(0, 2, 0, 2);
            sourceLabel.Name = "sourceLabel";
            sourceLabel.Size = new Size(200, 17);
            sourceLabel.Text = "-";
            //
            // fieldsPanel
            //
            fieldsPanel.ColumnCount = 1;
            fieldsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            fieldsPanel.Controls.Add(drawingGroup, 0, 0);
            fieldsPanel.Controls.Add(nestingGroup, 0, 1);
            fieldsPanel.Dock = DockStyle.Fill;
            fieldsPanel.Margin = new Padding(0);
            fieldsPanel.Name = "fieldsPanel";
            fieldsPanel.RowCount = 3;
            fieldsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            fieldsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            fieldsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            fieldsPanel.TabIndex = 0;
            //
            // drawingGroup
            //
            drawingGroup.AutoSize = true;
            drawingGroup.Controls.Add(drawingFields);
            drawingGroup.Dock = DockStyle.Fill;
            drawingGroup.Margin = new Padding(0, 0, 0, 10);
            drawingGroup.Name = "drawingGroup";
            drawingGroup.Padding = new Padding(10, 6, 10, 8);
            drawingGroup.TabIndex = 0;
            drawingGroup.TabStop = false;
            drawingGroup.Text = "Drawing";
            //
            // drawingFields
            //
            drawingFields.AutoSize = true;
            drawingFields.ColumnCount = 2;
            drawingFields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80F));
            drawingFields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            drawingFields.Controls.Add(label1, 0, 0);
            drawingFields.Controls.Add(nameBox, 1, 0);
            drawingFields.Controls.Add(label3, 0, 1);
            drawingFields.Controls.Add(customerBox, 1, 1);
            drawingFields.Dock = DockStyle.Fill;
            drawingFields.Name = "drawingFields";
            drawingFields.RowCount = 2;
            drawingFields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            drawingFields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            drawingFields.TabIndex = 0;
            //
            // label1
            //
            label1.Anchor = AnchorStyles.Left;
            label1.AutoSize = true;
            label1.Name = "label1";
            label1.Text = "&Name";
            //
            // nameBox
            //
            nameBox.Dock = DockStyle.Fill;
            nameBox.Margin = new Padding(3, 4, 3, 4);
            nameBox.Name = "nameBox";
            nameBox.TabIndex = 0;
            //
            // label3
            //
            label3.Anchor = AnchorStyles.Left;
            label3.AutoSize = true;
            label3.Name = "label3";
            label3.Text = "C&ustomer";
            //
            // customerBox
            //
            customerBox.Dock = DockStyle.Fill;
            customerBox.Margin = new Padding(3, 4, 3, 4);
            customerBox.Name = "customerBox";
            customerBox.TabIndex = 1;
            //
            // nestingGroup
            //
            nestingGroup.AutoSize = true;
            nestingGroup.Controls.Add(nestingFields);
            nestingGroup.Dock = DockStyle.Fill;
            nestingGroup.Margin = new Padding(0);
            nestingGroup.Name = "nestingGroup";
            nestingGroup.Padding = new Padding(10, 6, 10, 8);
            nestingGroup.TabIndex = 1;
            nestingGroup.TabStop = false;
            nestingGroup.Text = "Nesting";
            //
            // nestingFields
            //
            nestingFields.AutoSize = true;
            nestingFields.ColumnCount = 3;
            nestingFields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80F));
            nestingFields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96F));
            nestingFields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            nestingFields.Controls.Add(label2, 0, 0);
            nestingFields.Controls.Add(qtyBox, 1, 0);
            nestingFields.Controls.Add(nestedLabel, 2, 0);
            nestingFields.Controls.Add(label4, 0, 1);
            nestingFields.Controls.Add(priorityBox, 1, 1);
            nestingFields.Controls.Add(priorityHint, 2, 1);
            nestingFields.Controls.Add(cutFirstBox, 1, 2);
            nestingFields.Controls.Add(cutFirstHint, 1, 3);
            nestingFields.SetColumnSpan(cutFirstBox, 2);
            nestingFields.SetColumnSpan(cutFirstHint, 2);
            nestingFields.Dock = DockStyle.Fill;
            nestingFields.Name = "nestingFields";
            nestingFields.RowCount = 4;
            nestingFields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            nestingFields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            nestingFields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            nestingFields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            nestingFields.TabIndex = 0;
            //
            // label2
            //
            label2.Anchor = AnchorStyles.Left;
            label2.AutoSize = true;
            label2.Name = "label2";
            label2.Text = "&Quantity";
            //
            // qtyBox
            //
            qtyBox.Dock = DockStyle.Fill;
            qtyBox.Margin = new Padding(3, 4, 3, 4);
            qtyBox.Maximum = new decimal(new int[] { 999999, 0, 0, 0 });
            qtyBox.Name = "qtyBox";
            qtyBox.Suffix = "";
            qtyBox.TabIndex = 0;
            qtyBox.ValueChanged += QtyBox_ValueChanged;
            //
            // nestedLabel
            //
            nestedLabel.Anchor = AnchorStyles.Left;
            nestedLabel.AutoSize = true;
            nestedLabel.ForeColor = SystemColors.GrayText;
            nestedLabel.Name = "nestedLabel";
            //
            // label4
            //
            label4.Anchor = AnchorStyles.Left;
            label4.AutoSize = true;
            label4.Name = "label4";
            label4.Text = "&Priority";
            //
            // priorityBox
            //
            priorityBox.Dock = DockStyle.Fill;
            priorityBox.Margin = new Padding(3, 4, 3, 4);
            priorityBox.Maximum = new decimal(new int[] { 999999, 0, 0, 0 });
            priorityBox.Name = "priorityBox";
            priorityBox.Suffix = "";
            priorityBox.TabIndex = 1;
            //
            // priorityHint
            //
            priorityHint.Anchor = AnchorStyles.Left;
            priorityHint.AutoSize = true;
            priorityHint.ForeColor = SystemColors.GrayText;
            priorityHint.Name = "priorityHint";
            priorityHint.Text = "Lower numbers nest first";
            //
            // cutFirstBox
            //
            cutFirstBox.AutoSize = true;
            cutFirstBox.Margin = new Padding(3, 10, 3, 0);
            cutFirstBox.Name = "cutFirstBox";
            cutFirstBox.TabIndex = 2;
            cutFirstBox.Text = "Cut &first";
            cutFirstBox.UseVisualStyleBackColor = true;
            //
            // cutFirstHint
            //
            cutFirstHint.AutoSize = true;
            cutFirstHint.ForeColor = SystemColors.GrayText;
            cutFirstHint.Margin = new Padding(20, 0, 3, 4);
            cutFirstHint.Name = "cutFirstHint";
            cutFirstHint.Text = "Cut before other parts on the plate";
            //
            // bottomPanel1
            //
            bottomPanel1.Controls.Add(buttonsPanel);
            bottomPanel1.Dock = DockStyle.Bottom;
            bottomPanel1.Name = "bottomPanel1";
            bottomPanel1.Padding = new Padding(12, 10, 12, 10);
            bottomPanel1.Size = new Size(640, 50);
            bottomPanel1.TabIndex = 1;
            //
            // buttonsPanel
            //
            buttonsPanel.Controls.Add(cancelButton);
            buttonsPanel.Controls.Add(applyButton);
            buttonsPanel.Dock = DockStyle.Fill;
            buttonsPanel.FlowDirection = FlowDirection.RightToLeft;
            buttonsPanel.Margin = new Padding(0);
            buttonsPanel.Name = "buttonsPanel";
            buttonsPanel.TabIndex = 0;
            buttonsPanel.WrapContents = false;
            //
            // cancelButton
            //
            cancelButton.DialogResult = DialogResult.Cancel;
            cancelButton.Margin = new Padding(6, 0, 0, 0);
            cancelButton.Name = "cancelButton";
            cancelButton.Size = new Size(85, 28);
            cancelButton.TabIndex = 1;
            cancelButton.Text = "Cancel";
            cancelButton.UseVisualStyleBackColor = true;
            //
            // applyButton
            //
            applyButton.DialogResult = DialogResult.OK;
            applyButton.Margin = new Padding(0);
            applyButton.Name = "applyButton";
            applyButton.Size = new Size(85, 28);
            applyButton.TabIndex = 0;
            applyButton.Text = "OK";
            applyButton.UseVisualStyleBackColor = true;
            //
            // colorDialog1
            //
            colorDialog1.AnyColor = true;
            colorDialog1.FullOpen = true;
            //
            // EditDrawingForm
            //
            AcceptButton = applyButton;
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            CancelButton = cancelButton;
            ClientSize = new Size(640, 430);
            Controls.Add(layout);
            Controls.Add(bottomPanel1);
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "EditDrawingForm";
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "Drawing Properties";
            layout.ResumeLayout(false);
            previewPanel.ResumeLayout(false);
            previewPanel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)pictureBox1).EndInit();
            colorPanel.ResumeLayout(false);
            colorPanel.PerformLayout();
            infoPanel.ResumeLayout(false);
            infoPanel.PerformLayout();
            fieldsPanel.ResumeLayout(false);
            fieldsPanel.PerformLayout();
            drawingGroup.ResumeLayout(false);
            drawingGroup.PerformLayout();
            drawingFields.ResumeLayout(false);
            drawingFields.PerformLayout();
            nestingGroup.ResumeLayout(false);
            nestingGroup.PerformLayout();
            nestingFields.ResumeLayout(false);
            nestingFields.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)qtyBox).EndInit();
            ((System.ComponentModel.ISupportInitialize)priorityBox).EndInit();
            bottomPanel1.ResumeLayout(false);
            buttonsPanel.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private TableLayoutPanel layout;
        private TableLayoutPanel previewPanel;
        private PictureBox pictureBox1;
        private TableLayoutPanel colorPanel;
        private Panel colorSwatch;
        private Button colorButton;
        private TableLayoutPanel infoPanel;
        private Label sizeCaption;
        private Label sizeLabel;
        private Label areaCaption;
        private Label areaLabel;
        private Label sourceCaption;
        private Label sourceLabel;
        private TableLayoutPanel fieldsPanel;
        private GroupBox drawingGroup;
        private TableLayoutPanel drawingFields;
        private Label label1;
        private TextBox nameBox;
        private Label label3;
        private TextBox customerBox;
        private GroupBox nestingGroup;
        private TableLayoutPanel nestingFields;
        private Label label2;
        private Controls.NumericUpDown qtyBox;
        private Label nestedLabel;
        private Label label4;
        private Controls.NumericUpDown priorityBox;
        private Label priorityHint;
        private CheckBox cutFirstBox;
        private Label cutFirstHint;
        private Controls.BottomPanel bottomPanel1;
        private FlowLayoutPanel buttonsPanel;
        private Button cancelButton;
        private Button applyButton;
        private ColorDialog colorDialog1;
        private ToolTip toolTip;
    }
}
