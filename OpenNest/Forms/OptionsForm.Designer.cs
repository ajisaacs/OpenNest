using System.Drawing;
using System.Windows.Forms;

namespace OpenNest.Forms
{
    partial class OptionsForm
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

        // The window is a category list beside one page per category, built in code: each page
        // is a heading, a sentence on what it holds and a label/field table, so rows line up and
        // follow the font at any DPI.
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            toolTip1 = new ToolTip(components);

            createNestOnStartupCheck = new CheckBox
            {
                Name = "createNestOnStartupCheck",
                Text = "Create a new nest on startup",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                UseVisualStyleBackColor = true,
            };
            colorSchemeCombo = NewCombo("colorSchemeCombo", 200);
            clonePlacementCombo = NewCombo("clonePlacementCombo", 200);
            toolTip1.SetToolTip(clonePlacementCombo, "Where Clone Parts pushes the copy on Shift+click (part geometry) "
                + "or Ctrl+click (bounding boxes). Every push goes toward the plate's origin corner.");

            autoSizeFactorBox = new Controls.NumericUpDown
            {
                Name = "autoSizeFactorBox",
                DecimalPlaces = 4,
                Increment = 0.125m,
                Width = 120,
                Anchor = AnchorStyles.Left,
                Suffix = "",
            };
            toolTip1.SetToolTip(autoSizeFactorBox, "The amount to round the plate size up.");
            planningEffortBox = new Controls.NumericUpDown
            {
                Name = "planningEffortBox",
                DecimalPlaces = 1,
                Increment = 0.5m,
                Width = 120,
                Anchor = AnchorStyles.Left,
                Suffix = "",
            };
            toolTip1.SetToolTip(planningEffortBox, "Multiplies the cut planner's search limit (1 = standard). "
                + "Higher values search longer on hard plates; results are the same on every computer.");

            strategyGrid = new DataGridView
            {
                Name = "strategyGrid",
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
            };

            activeServerCombo = NewCombo("activeServerCombo", 360);
            activeServerCombo.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            serverGrid = new DataGridView
            {
                Name = "serverGrid",
                Dock = DockStyle.Fill,
                AllowUserToAddRows = true,
                AllowUserToDeleteRows = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                MultiSelect = false,
                EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2,
            };
            removeServerButton = NewButton("removeServerButton", "Remove Server");
            removeServerButton.Click += RemoveServer_Click;

            ledgerUrlBox = NewTextBox("ledgerUrlBox", "http://ledger:5000");
            ledgerKeyBox = NewTextBox("ledgerKeyBox", "");
            ledgerKeyBox.UseSystemPasswordChar = true;
            toolTip1.SetToolTip(ledgerKeyBox, "MaterialLedger's Api:Keys:OpenNest key. Stored encrypted for this Windows user.");
            testLedgerButton = NewButton("testLedgerButton", "Test Connection");
            testLedgerButton.Click += TestLedger_Click;
            saveLedgerButton = NewButton("saveLedgerButton", "Save Ledger");
            saveLedgerButton.Click += SaveLedger_Click;

            updateChannelCombo = NewCombo("updateChannelCombo", 260);
            toolTip1.SetToolTip(updateChannelCombo,
                "Development builds are made from every finished change. Once a day or once "
                + "a week waits that long after this copy last updated; Help > Check for "
                + "Updates... always looks now. Takes effect after a restart.");
            updateFeedUrlBox = NewTextBox("updateFeedUrlBox", "Blank: the default feed");
            setUpdateFeedButton = NewButton("setUpdateFeedButton", "Save Feed URL");
            setUpdateFeedButton.Click += SetUpdateFeed_Click;

            pageHost = new Panel
            {
                Name = "pageHost",
                Dock = DockStyle.Fill,
                Padding = new Padding(24, 18, 24, 12),
            };
            pages = new[]
            {
                BuildGeneralPage(),
                BuildNestingPage(),
                BuildStrategyPage(),
                serverPage = BuildServerPage(),
                BuildLedgerPage(),
                BuildUpdatesPage(),
            };
            foreach (var page in pages)
            {
                page.Visible = false;
                pageHost.Controls.Add(page);
            }

            navList = new ListBox
            {
                Name = "navList",
                Dock = DockStyle.Left,
                Width = 180,
                BorderStyle = BorderStyle.None,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 34,
                IntegralHeight = false,
                BackColor = NavBackColor,
                TabIndex = 0,
            };
            foreach (var page in pages)
                navList.Items.Add(page.Text);
            navList.DrawItem += NavList_DrawItem;
            navList.SelectedIndexChanged += NavList_SelectedIndexChanged;

            navDivider = new Panel
            {
                Dock = DockStyle.Left,
                Width = 1,
                BackColor = Color.FromArgb(218, 218, 218),
            };

            saveButton = new Button
            {
                Name = "saveButton",
                Text = "Save",
                DialogResult = DialogResult.OK,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Size = new Size(90, 28),
                UseVisualStyleBackColor = true,
                TabIndex = 0,
            };
            saveButton.Click += SaveSettings_Click;
            cancelButton = new Button
            {
                Name = "cancelButton",
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Size = new Size(90, 28),
                UseVisualStyleBackColor = true,
                TabIndex = 1,
            };
            bottomPanel1 = new Controls.BottomPanel { Name = "bottomPanel1", Height = 50 };
            bottomPanel1.Controls.Add(saveButton);
            bottomPanel1.Controls.Add(cancelButton);
            bottomPanel1.Resize += (_, _) => LayoutDialogButtons();

            SuspendLayout();
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            ClientSize = new Size(800, 540);
            MinimumSize = new Size(700, 460);
            Controls.Add(pageHost);
            Controls.Add(navDivider);
            Controls.Add(navList);
            Controls.Add(bottomPanel1);
            AcceptButton = saveButton;
            CancelButton = cancelButton;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "OptionsForm";
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "Options";
            ResumeLayout(false);
            LayoutDialogButtons();
        }

        private Panel BuildGeneralPage()
        {
            var fields = NewFieldTable();
            AddRow(fields, createNestOnStartupCheck, "Opens an empty nest with your nest defaults each time OpenNest starts.");
            AddField(fields, "Color scheme:", colorSchemeCombo, "Colors for plates, parts and cut paths.");
            AddField(fields, "Clone push placement:", clonePlacementCombo,
                "Where Clone Parts pushes each copy. Every push goes toward the plate's origin corner.");
            return NewPage("General", "Startup and how nests look and behave while you edit them.", fields);
        }

        private Panel BuildNestingPage()
        {
            var fields = NewFieldTable();
            AddField(fields, "Auto-size plate factor:", autoSizeFactorBox,
                "Auto-sized plates round their size up to a multiple of this amount.");
            AddField(fields, "Cut planning effort:", planningEffortBox,
                "Multiplies the cut planner's search limit (1 = standard). Higher values search longer "
                + "on hard plates; results are the same on every computer.");
            return NewPage("Nesting && Cutting", "How plates are sized and how hard Plan Cutting searches.", fields);
        }

        private Panel BuildStrategyPage()
        {
            var frame = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(1),
                BackColor = Color.FromArgb(218, 218, 218),
            };
            frame.Controls.Add(strategyGrid);
            return NewPage("Fill Strategies",
                "The strategies Fill tries, in order. Clear a strategy's box to skip it.", frame);
        }

        private Panel BuildServerPage()
        {
            var active = NewFieldTable();
            active.Dock = DockStyle.Top;
            active.AutoSize = true;
            active.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            AddField(active, "Active server:", activeServerCombo,
                "Database mode uses this server. Choose File or Database mode under File > Storage Mode.");

            var listHeading = new Label
            {
                Text = "Saved servers",
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = 24,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point),
            };
            var frame = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(1),
                BackColor = Color.FromArgb(218, 218, 218),
            };
            frame.Controls.Add(serverGrid);

            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Padding = new Padding(0, 8, 0, 0),
            };
            removeServerButton.Margin = new Padding(0, 0, 12, 0);
            footer.Controls.Add(removeServerButton);
            footer.Controls.Add(new Label
            {
                Text = "Type in the blank last row to add a server.",
                AutoSize = true,
                ForeColor = HintColor,
                Margin = new Padding(0, 6, 0, 0),
            });

            var body = new Panel { Dock = DockStyle.Fill };
            body.Controls.Add(frame);
            body.Controls.Add(footer);
            body.Controls.Add(listHeading);
            body.Controls.Add(active);
            return NewPage("Nest Server",
                "The nest servers this PC uses. Changes here save with Save.", body);
        }

        private Panel BuildLedgerPage()
        {
            var fields = NewFieldTable();
            ledgerUrlBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            ledgerKeyBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            AddField(fields, "URL:", ledgerUrlBox, "Leave blank to turn the ledger off.");
            AddField(fields, "API key:", ledgerKeyBox, "Stored encrypted for this Windows user.");
            AddButtons(fields, testLedgerButton, saveLedgerButton);
            return NewPage("MaterialLedger",
                "Optional. When set, Auto Nest loads stock from MaterialLedger. "
                + "Save Ledger saves these right away, for this Windows user.", fields);
        }

        private Panel BuildUpdatesPage()
        {
            var fields = NewFieldTable();
            updateFeedUrlBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            AddField(fields, "Install:", updateChannelCombo,
                "Takes effect after a restart. Help > Check for Updates... always looks now.");
            AddField(fields, "Feed URL:", updateFeedUrlBox,
                "Blank uses the nest server's mirror in Database mode, otherwise GitHub Releases.");
            AddButtons(fields, setUpdateFeedButton);
            return NewPage("Updates", "Which builds OpenNest installs and where it looks for them.", fields);
        }

        private static Panel NewPage(string title, string description, Control body)
        {
            var page = new Panel { Dock = DockStyle.Fill, Text = title.Replace("&&", "&") };
            var heading = new Label
            {
                Text = title,
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = 30,
                Font = new Font("Segoe UI Semibold", 13F, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = Color.FromArgb(32, 32, 32),
            };
            var summary = new Label
            {
                Text = description,
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = 40,
                ForeColor = HintColor,
                Padding = new Padding(0, 0, 0, 8),
            };
            summary.SizeChanged += (_, _) => FitLabelHeight(summary);
            if (body is TableLayoutPanel fields)
            {
                // A last row takes the spare height, so the fields stay packed at the top.
                fields.RowCount++;
                fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            }
            body.Dock = DockStyle.Fill;
            page.Controls.Add(body);
            page.Controls.Add(summary);
            page.Controls.Add(heading);
            return page;
        }

        private static void FitLabelHeight(Label label)
        {
            if (label.Width <= 0)
                return;
            var size = TextRenderer.MeasureText(label.Text, label.Font, new Size(label.Width, int.MaxValue),
                TextFormatFlags.WordBreak);
            var height = size.Height + label.Padding.Vertical + 2;
            if (label.Height != height)
                label.Height = height;
        }

        private static TableLayoutPanel NewFieldTable()
        {
            var table = new TableLayoutPanel
            {
                ColumnCount = 2,
                Padding = new Padding(0, 6, 0, 0),
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            return table;
        }

        private static void AddField(TableLayoutPanel table, string label, Control field, string hint)
        {
            var row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(new Label
            {
                Text = label,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 6, 16, 3),
            }, 0, row);
            field.Margin = new Padding(0, 3, 0, 3);
            table.Controls.Add(field, 1, row);
            AddHint(table, hint, 1);
        }

        private static void AddRow(TableLayoutPanel table, Control control, string hint)
        {
            var row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            control.Margin = new Padding(0, 6, 0, 3);
            table.Controls.Add(control, 0, row);
            table.SetColumnSpan(control, 2);
            AddHint(table, hint, 0);
        }

        // Buttons sit under the fields they act on, in the field column.
        private static void AddButtons(TableLayoutPanel table, params Button[] buttons)
        {
            var row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var buttonRow = NewButtonRow(buttons);
            buttonRow.Dock = DockStyle.None;
            buttonRow.Anchor = AnchorStyles.Left;
            buttonRow.Margin = new Padding(0, 0, 0, 3);
            table.Controls.Add(buttonRow, 1, row);
        }

        private static void AddHint(TableLayoutPanel table, string hint, int column)
        {
            var row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var label = new Label
            {
                Text = hint,
                AutoSize = true,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                ForeColor = HintColor,
                Margin = new Padding(column == 0 ? 20 : 0, 0, 0, 12),
            };
            table.Controls.Add(label, column, row);
            table.SetColumnSpan(label, 2 - column);
        }

        private static FlowLayoutPanel NewButtonRow(params Button[] buttons)
        {
            var row = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                WrapContents = false,
                Padding = new Padding(0, 4, 0, 0),
            };
            foreach (var button in buttons)
            {
                button.Margin = new Padding(0, 0, 8, 0);
                row.Controls.Add(button);
            }
            return row;
        }

        private static Button NewButton(string name, string text) => new()
        {
            Name = name,
            Text = text,
            AutoSize = true,
            MinimumSize = new Size(120, 28),
            Padding = new Padding(8, 0, 8, 0),
            UseVisualStyleBackColor = true,
        };

        private static TextBox NewTextBox(string name, string placeholder) => new()
        {
            Name = name,
            PlaceholderText = placeholder,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
        };

        private static ComboBox NewCombo(string name, int width) => new()
        {
            Name = name,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = width,
            Anchor = AnchorStyles.Left,
        };

        private void LayoutDialogButtons()
        {
            var top = (bottomPanel1.Height - cancelButton.Height) / 2 + 1;
            cancelButton.Location = new Point(bottomPanel1.Width - cancelButton.Width - 12, top);
            saveButton.Location = new Point(cancelButton.Left - saveButton.Width - 8, top);
        }

        private static readonly Color NavBackColor = Color.FromArgb(243, 243, 243);
        private static readonly Color HintColor = Color.FromArgb(100, 100, 100);

        private ToolTip toolTip1;
        private ListBox navList;
        private Panel navDivider;
        private Panel pageHost;
        private Panel[] pages;
        private Panel serverPage;
        private Controls.BottomPanel bottomPanel1;
        private Button saveButton;
        private Button cancelButton;
        private CheckBox createNestOnStartupCheck;
        private ComboBox colorSchemeCombo;
        private ComboBox clonePlacementCombo;
        private Controls.NumericUpDown autoSizeFactorBox;
        private Controls.NumericUpDown planningEffortBox;
        private DataGridView strategyGrid;
        private ComboBox activeServerCombo;
        private DataGridView serverGrid;
        private Button removeServerButton;
        private TextBox ledgerUrlBox;
        private TextBox ledgerKeyBox;
        private Button testLedgerButton;
        private Button saveLedgerButton;
        private ComboBox updateChannelCombo;
        private TextBox updateFeedUrlBox;
        private Button setUpdateFeedButton;
    }
}
