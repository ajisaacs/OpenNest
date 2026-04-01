using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Geometry;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace OpenNest.Controls
{
    public partial class ProgramEditorControl : UserControl
    {
        private List<ContourInfo> contours = new();
        private bool isDirty;
        private bool isLoaded;

        public ProgramEditorControl()
        {
            InitializeComponent();
        }

        public Program Program { get; private set; }
        public bool IsDirty => isDirty;
        public bool IsLoaded => isLoaded;

        public event EventHandler ProgramChanged;

        public void LoadEntities(List<Entity> entities)
        {
            // Will be implemented in Task 3
        }

        public void Clear()
        {
            contours.Clear();
            contourList.Items.Clear();
            preview.Entities.Clear();
            preview.Invalidate();
            gcodeEditor.Clear();
            Program = null;
            isDirty = false;
            isLoaded = false;
        }
    }
}
