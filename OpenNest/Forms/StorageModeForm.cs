using System;
using System.Drawing;
using System.Windows.Forms;
using OpenNest.Data;

namespace OpenNest.Forms;

/// <summary>
/// File &gt; Storage Mode...: choose File mode (local .nest files, the default and
/// current behavior) or Database mode (Save/Open go through the shared nest server).
/// </summary>
public sealed class StorageModeForm : Form
{
    private readonly RadioButton fileModeRadio;
    private readonly RadioButton databaseModeRadio;
    private readonly TextBox serverUrlBox;
    private readonly Label serverUrlLabel;

    public StorageModeForm()
    {
        Text = "Storage Mode";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(420, 190);
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;

        var info = new Label
        {
            Dock = DockStyle.Top,
            Height = 40,
            Padding = new Padding(10, 10, 10, 0),
            Text = "Database mode shares saved nests with every shop PC through a central server.",
        };

        fileModeRadio = new RadioButton
        {
            Text = "File — save/open local .nest files (default)",
            AutoSize = true,
            Location = new Point(14, 54),
            Checked = true,
        };
        databaseModeRadio = new RadioButton
        {
            Text = "Database — save/open through a shared nest server",
            AutoSize = true,
            Location = new Point(14, 80),
        };

        serverUrlLabel = new Label
        {
            Text = "Server URL:",
            AutoSize = true,
            Location = new Point(32, 112),
            Enabled = false,
        };
        serverUrlBox = new TextBox
        {
            Location = new Point(110, 108),
            Width = 280,
            PlaceholderText = "http://barge.lan:8090",
            Enabled = false,
        };

        fileModeRadio.CheckedChanged += (_, _) => UpdateServerUrlEnabled();
        databaseModeRadio.CheckedChanged += (_, _) => UpdateServerUrlEnabled();

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(6),
        };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        var ok = new Button { Text = "OK", AutoSize = true };
        ok.Click += Ok_Click;
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        Controls.Add(databaseModeRadio);
        Controls.Add(fileModeRadio);
        Controls.Add(serverUrlBox);
        Controls.Add(serverUrlLabel);
        Controls.Add(info);
        Controls.Add(buttons);

        AcceptButton = ok;
        CancelButton = cancel;

        LoadCurrentSettings();
    }

    public NestStorageMode SelectedMode =>
        databaseModeRadio.Checked ? NestStorageMode.Database : NestStorageMode.File;

    public string ServerUrl => serverUrlBox.Text.Trim();

    private void LoadCurrentSettings()
    {
        var settings = NestStorage.Settings;
        databaseModeRadio.Checked = settings.Mode == NestStorageMode.Database;
        fileModeRadio.Checked = !databaseModeRadio.Checked;
        serverUrlBox.Text = settings.ServerUrl;
        UpdateServerUrlEnabled();
    }

    private void UpdateServerUrlEnabled()
    {
        serverUrlBox.Enabled = databaseModeRadio.Checked;
        serverUrlLabel.Enabled = databaseModeRadio.Checked;
    }

    private void Ok_Click(object sender, EventArgs e)
    {
        if (databaseModeRadio.Checked)
        {
            var url = ServerUrl;
            var isValidUrl =
                Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

            if (!isValidUrl)
            {
                MessageBox.Show(
                    this,
                    "Enter a valid server URL, e.g. http://barge.lan:8090",
                    "Storage Mode",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return;
            }
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
