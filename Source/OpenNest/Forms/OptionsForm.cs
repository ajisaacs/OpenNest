using System.Windows.Forms;
using OpenNest.Properties;

namespace OpenNest.Forms
{
    public partial class OptionsForm : Form
    {
        public OptionsForm()
        {
            InitializeComponent();
        }

        protected override void OnLoad(System.EventArgs e)
        {
            base.OnLoad(e);
            LoadSettings();
        }

        private void LoadSettings()
        {
            textBox1.Text = Settings.Default.NestTemplatePath;
            checkBox1.Checked = Settings.Default.CreateNewNestOnOpen;
            numericUpDown1.Value = (decimal)Settings.Default.AutoSizePlateFactor;
            numericUpDown2.Value = (decimal)Settings.Default.ImportSplinePrecision;
        }

        private void SaveSettings()
        {
            Settings.Default.NestTemplatePath = textBox1.Text;
            Settings.Default.CreateNewNestOnOpen = checkBox1.Checked;
            Settings.Default.AutoSizePlateFactor = (double)numericUpDown1.Value;
            Settings.Default.ImportSplinePrecision = (int)numericUpDown2.Value;
            Settings.Default.Save();
        }

        private void SaveSettings_Click(object sender, System.EventArgs e)
        {
            SaveSettings();
        }

        private void BrowseNestTemplatePath_Click(object sender, System.EventArgs e)
        {
            var dlg = new OpenFileDialog();
            dlg.Filter = "Template File|*.nstdot";

            if (dlg.ShowDialog() == DialogResult.OK)
                textBox1.Text = dlg.FileName;
        }
    }
}
