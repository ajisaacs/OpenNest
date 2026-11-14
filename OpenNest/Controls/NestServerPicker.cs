using System;
using System.Windows.Forms;
using OpenNest.Data;

namespace OpenNest.Controls
{
    /// <summary>
    /// Picks a nest server: a dropdown of the servers this PC has used, each shown with the
    /// operator's name for it, above the address and name boxes that also take a new server.
    /// The host form saves the choice; only Forget changes the saved list directly.
    /// </summary>
    public sealed class NestServerPicker : UserControl
    {
        private readonly ComboBox savedCombo;
        private readonly TextBox urlBox;
        private readonly TextBox nameBox;
        private readonly Button forgetButton;
        private string activeUrl = "";
        private bool syncing;

        public NestServerPicker()
        {
            Size = new System.Drawing.Size(360, 90);

            savedCombo = new ComboBox
            {
                Name = "savedServerCombo",
                DropDownStyle = ComboBoxStyle.DropDownList,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
            };
            urlBox = new TextBox
            {
                Name = "serverUrlBox",
                PlaceholderText = "http://server:8090",
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
            };
            nameBox = new TextBox
            {
                Name = "serverNameBox",
                PlaceholderText = "What this server is for, e.g. Shop production",
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
            };
            forgetButton = new Button
            {
                Name = "forgetServerButton",
                Text = "Forget",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Enabled = false,
            };
            var toolTip = new ToolTip();
            toolTip.SetToolTip(forgetButton,
                "Remove the selected server from this list. The server in use stays.");
            Disposed += (_, _) => toolTip.Dispose();

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 3,
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (var row = 0; row < 3; row++)
                table.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / 3));

            table.Controls.Add(RowLabel("Saved:"), 0, 0);
            table.Controls.Add(savedCombo, 1, 0);
            table.Controls.Add(forgetButton, 2, 0);
            table.Controls.Add(RowLabel("URL:"), 0, 1);
            table.Controls.Add(urlBox, 1, 1);
            table.SetColumnSpan(urlBox, 2);
            table.Controls.Add(RowLabel("Name:"), 0, 2);
            table.Controls.Add(nameBox, 1, 2);
            table.SetColumnSpan(nameBox, 2);
            Controls.Add(table);

            savedCombo.SelectedIndexChanged += (_, _) => ShowSelectedServer();
            urlBox.TextChanged += (_, _) => SelectServerMatchingUrl();
            forgetButton.Click += (_, _) => ForgetSelectedServer();
        }

        /// <summary>
        /// Persists the removal of a saved server, given its address. A throw keeps the server
        /// in the list and is shown to the operator.
        /// </summary>
        public Action<string> ForgetServer { get; set; }

        /// <summary>The address as typed or picked, trimmed; the host form validates it.</summary>
        public string ServerUrl => urlBox.Text.Trim();

        /// <summary>The operator's name for the server, trimmed; may be empty.</summary>
        public string ServerName => nameBox.Text.Trim();

        /// <summary>Lists the saved servers and shows the one in use.</summary>
        public void Bind(NestStorageSettings settings)
        {
            activeUrl = settings.ServerUrl;

            syncing = true;
            savedCombo.Items.Clear();
            foreach (var server in settings.Servers)
                savedCombo.Items.Add(server);
            syncing = false;

            ShowActiveServer();
        }

        private static Label RowLabel(string text) =>
            new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left };

        private SavedNestServer FindSaved(string url)
        {
            foreach (SavedNestServer server in savedCombo.Items)
            {
                if (SameServer(server.Url, url))
                    return server;
            }

            return null;
        }

        private static bool SameServer(string a, string b) =>
            string.Equals(a.Trim().TrimEnd('/'), b.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

        private void ShowActiveServer()
        {
            urlBox.Text = activeUrl;
            SelectServerMatchingUrl();
            nameBox.Text = FindSaved(activeUrl)?.Name ?? "";
        }

        private void ShowSelectedServer()
        {
            if (syncing || savedCombo.SelectedItem is not SavedNestServer server)
                return;

            syncing = true;
            urlBox.Text = server.Url;
            nameBox.Text = server.Name;
            syncing = false;
            UpdateForgetEnabled();
        }

        /// <summary>Typing a saved address selects it and shows its name; any other address clears the selection.</summary>
        private void SelectServerMatchingUrl()
        {
            if (syncing)
                return;

            var match = FindSaved(urlBox.Text);
            syncing = true;
            if (match == null)
                savedCombo.SelectedIndex = -1;
            else if (!ReferenceEquals(savedCombo.SelectedItem, match))
            {
                savedCombo.SelectedItem = match;
                nameBox.Text = match.Name;
            }
            syncing = false;
            UpdateForgetEnabled();
        }

        private void UpdateForgetEnabled() =>
            forgetButton.Enabled = savedCombo.SelectedItem is SavedNestServer server
                && !SameServer(server.Url, activeUrl);

        private void ForgetSelectedServer()
        {
            if (savedCombo.SelectedItem is not SavedNestServer server)
                return;

            try
            {
                ForgetServer?.Invoke(server.Url);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not forget the server: {ex.Message}",
                    "Nest server", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            syncing = true;
            savedCombo.Items.Remove(server);
            syncing = false;
            ShowActiveServer();
        }
    }
}
