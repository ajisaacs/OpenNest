using System;
using System.Text.Json;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.PostSettings;

namespace OpenNest.Forms
{
    public partial class PostProcessorConfigForm : Form
    {
        private readonly IConfigurablePostProcessor postProcessor;
        private readonly string configBackup;
        private readonly PostSettingsEditor settingsEditor;

        public PostProcessorConfigForm(IConfigurablePostProcessor postProcessor)
        {
            InitializeComponent();

            this.postProcessor = postProcessor;
            this.Text = postProcessor.Name + " Settings";

            var sections = PostSettingsLayout.TryBuild(postProcessor.Config.GetType());
            if (sections != null)
            {
                // Sectioned editor: edits stay in the controls until OK applies them,
                // so Cancel needs no backup.
                settingsEditor = new PostSettingsEditor(postProcessor.Config, sections)
                {
                    Dock = DockStyle.Fill,
                };
                Controls.Remove(propertyGrid);
                propertyGrid.Dispose();
                Controls.Add(settingsEditor);
                settingsEditor.BringToFront();
                ClientSize = new System.Drawing.Size(760, 560);
                MinimumSize = new System.Drawing.Size(620, 420);
                MaximizeBox = true;
                return;
            }

            // Deep-clone config as JSON backup for cancel/restore
            configBackup = JsonSerializer.Serialize(
                postProcessor.Config,
                postProcessor.Config.GetType()
            );

            propertyGrid.SelectedObject = postProcessor.Config;
        }

        private void okButton_Click(object sender, EventArgs e)
        {
            if (settingsEditor != null && !settingsEditor.TryApply(out var error))
            {
                DialogResult = DialogResult.None;
                MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            postProcessor.SaveConfig();
        }

        private void cancelButton_Click(object sender, EventArgs e)
        {
            // The sectioned editor never wrote to the config.
            if (configBackup == null)
                return;

            // Restore config from backup
            var original = JsonSerializer.Deserialize(configBackup, postProcessor.Config.GetType());
            var properties = postProcessor.Config.GetType().GetProperties();
            foreach (var prop in properties)
            {
                if (prop.CanWrite)
                    prop.SetValue(postProcessor.Config, prop.GetValue(original));
            }
        }
    }
}
