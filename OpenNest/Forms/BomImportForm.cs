using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.IO;
using OpenNest.IO.Bom;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace OpenNest.Forms
{
    public partial class BomImportForm : Form
    {
        private BomAnalysis _analysis;

        public Form MdiParentForm { get; set; }

        public BomImportForm()
        {
            InitializeComponent();
        }

        private void BrowseBom_Click(object sender, EventArgs e)
        {
        }

        private void BrowseDxf_Click(object sender, EventArgs e)
        {
        }

        private void Analyze_Click(object sender, EventArgs e)
        {
        }

        private void CreateNests_Click(object sender, EventArgs e)
        {
        }

        private void BtnClose_Click(object sender, EventArgs e)
        {
            Close();
        }
    }
}
