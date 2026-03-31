using System;
using System.Text.Json;
using System.Windows.Forms;

namespace OpenNest.Forms
{
    public partial class PostProcessorConfigForm : Form
    {
        private readonly IConfigurablePostProcessor postProcessor;
        private readonly string configBackup;

        public PostProcessorConfigForm(IConfigurablePostProcessor postProcessor)
        {
            InitializeComponent();

            this.postProcessor = postProcessor;
            this.Text = postProcessor.Name + " Settings";

            // Deep-clone config as JSON backup for cancel/restore
            configBackup = JsonSerializer.Serialize(postProcessor.Config, postProcessor.Config.GetType());

            propertyGrid.SelectedObject = postProcessor.Config;
        }

        private void okButton_Click(object sender, EventArgs e)
        {
            postProcessor.SaveConfig();
        }

        private void cancelButton_Click(object sender, EventArgs e)
        {
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
