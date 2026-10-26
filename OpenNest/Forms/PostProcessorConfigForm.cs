using System;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Data;
using OpenNest.PostSettings;

namespace OpenNest.Forms
{
    public partial class PostProcessorConfigForm : Form
    {
        private readonly IConfigurablePostProcessor postProcessor;
        private readonly string configBackup;
        private readonly string configBefore;
        private readonly PostSettingsEditor settingsEditor;

        public PostProcessorConfigForm(IConfigurablePostProcessor postProcessor)
        {
            InitializeComponent();

            this.postProcessor = postProcessor;
            this.Text = postProcessor.Name + " Settings";
            configBefore = (postProcessor as IJsonConfigPostProcessor)?.SerializeConfig();

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

            try
            {
                SharedSettings.SavePostSettings(postProcessor, configBefore);
            }
            catch (SharedSettingSaveConflictException conflict)
            {
                // Post with the settings the other workstation saved, never a mix of both.
                ((IJsonConfigPostProcessor)postProcessor).LoadConfig(conflict.Current.Json);
                DialogResult = DialogResult.Cancel;
                MessageBox.Show(this, conflict.Message + "\n\nTheir settings are now loaded and nothing was posted. "
                    + "Post again to review them and make your change.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                DialogResult = DialogResult.None;
                MessageBox.Show(this, "The post settings could not be saved.\n\n" + ex.Message
                    + "\n\nCancel to post nothing and discard the changes.", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void cancelButton_Click(object sender, EventArgs e)
        {
            // The sectioned editor writes to the config only on OK; an OK whose save failed is undone here.
            if (configBackup == null)
            {
                if (configBefore != null && postProcessor is IJsonConfigPostProcessor json
                    && json.SerializeConfig() != configBefore)
                    json.LoadConfig(configBefore);
                return;
            }

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
