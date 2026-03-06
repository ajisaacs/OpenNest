using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Actions;
using OpenNest.Collections;
using OpenNest.Controls;
using OpenNest.IO;
using OpenNest.Math;
using OpenNest.Properties;
using Timer = System.Timers.Timer;

namespace OpenNest.Forms
{
    public partial class EditNestForm : Form
    {
        public event EventHandler PlateChanged;

        public readonly Nest Nest;
        public readonly PlateView PlateView;

        private readonly Timer updateDrawingListTimer;

        /// <summary>
        /// Used to distinguish between single/double click on drawing within drawinglistbox.
        /// If double click, this is set to false so the single click action won't be triggered.
        /// </summary>
        private bool addPart;

        private EditNestForm()
        {
            PlateView = new PlateView();
            PlateView.Enter += PlateView_Enter;
            PlateView.PartAdded += PlateView_PartAdded;
            PlateView.PartRemoved += PlateView_PartRemoved;
            PlateView.Dock = DockStyle.Fill;

            InitializeComponent();

            splitContainer.Panel2.Controls.Add(PlateView);

            var renderer = new ToolStripRenderer(ToolbarTheme.Toolbar);
            toolStrip1.Renderer = renderer;
            toolStrip2.Renderer = renderer;

            platesListView.SelectedIndexChanged += (sender, e) =>
            {
                if (platesListView.SelectedIndices.Count == 0)
                    return;

                CurrentPlateIndex = platesListView.SelectedIndices[0];
                PlateView.Plate = Nest.Plates[CurrentPlateIndex];
                PlateView.ZoomToFit();
                FirePlateChanged(false);
            };
        }

        public EditNestForm(Nest nest)
            : this()
        {
            updateDrawingListTimer = new Timer()
            {
                AutoReset = false,
                Enabled = true,
                Interval = 50
            };
            updateDrawingListTimer.Elapsed += drawingListUpdateTimer_Elapsed;

            Nest = nest;
            Nest.Plates.ItemAdded += Plates_PlateAdded;
            Nest.Plates.ItemRemoved += Plates_PlateRemoved;

            if (Nest.Plates.Count == 0)
                Nest.CreatePlate();

            UpdatePlateList();
            UpdateDrawingList();

            LoadFirstPlate();

            Text = Nest.Name;
            drawingListBox1.Units = Nest.Units;
        }

        public string LastSavePath { get; private set; }

        public DateTime LastSaveDate { get; private set; }

        public int CurrentPlateIndex { get; private set; }

        public int PlateCount
        {
            get { return Nest.Plates.Count; }
        }

        public void LoadFirstPlate()
        {
            if (Nest.Plates.Count > 0)
            {
                CurrentPlateIndex = 0;
                PlateView.Plate = Nest.Plates[CurrentPlateIndex];
                PlateView.ZoomToFit();
                FirePlateChanged();
            }
        }

        public void LoadLastPlate()
        {
            if (Nest.Plates.Count > 0)
            {
                CurrentPlateIndex = Nest.Plates.Count - 1;
                PlateView.Plate = Nest.Plates[CurrentPlateIndex];
                PlateView.ZoomToFit();
                FirePlateChanged();
            }
        }

        public bool LoadNextPlate()
        {
            if (CurrentPlateIndex + 1 >= Nest.Plates.Count)
                return false;

            CurrentPlateIndex++;
            PlateView.Plate = Nest.Plates[CurrentPlateIndex];
            PlateView.ZoomToFit();
            FirePlateChanged();

            return true;
        }

        public bool LoadPreviousPlate()
        {
            if (Nest.Plates.Count == 0 || CurrentPlateIndex - 1 < 0)
                return false;

            CurrentPlateIndex--;
            PlateView.Plate = Nest.Plates[CurrentPlateIndex];
            PlateView.ZoomToFit();
            FirePlateChanged();

            return true;
        }

        public bool IsFirstPlate()
        {
            return (Nest.Plates.Count == 0 || (CurrentPlateIndex - 1) < 0);
        }

        public bool IsLastPlate()
        {
            return CurrentPlateIndex + 1 >= Nest.Plates.Count;
        }

        public void UpdatePlateList()
        {
            platesListView.Items.Clear();

            var items = new ListViewItem[Nest.Plates.Count];

            for (int i = 0; i < items.Length; ++i)
            {
                var plate = Nest.Plates[i];
                var item = GetListViewItem(plate, i + 1);
                items[i] = item;
            }

            platesListView.Items.AddRange(items);
        }

        public void UpdateDrawingList()
        {
            drawingListBox1.Items.Clear();

            foreach (var dwg in Nest.Drawings.OrderBy(d => d.Name).ToList())
                drawingListBox1.Items.Add(dwg);
        }

        public void Save()
        {
            if (File.Exists(LastSavePath))
                SaveAs(LastSavePath);
            else
                SaveAs();
        }

        public void SaveAs()
        {
            var dlg = new SaveFileDialog();
            dlg.Filter = "Nest Files|*.zip|Template File|*.nstdot";
            dlg.FileName = Nest.Name;

            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                if (dlg.FilterIndex == 2)
                    SaveTemplate(dlg.FileName);
                else
                    SaveAs(dlg.FileName);
            }
        }

        public void SaveAs(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            Nest.Name = name;
            Text = name;
            LastSaveDate = DateTime.Now;
            LastSavePath = path;

            var writer = new NestWriter(Nest);
            writer.Write(path);
        }

        public void SaveTemplate(string path)
        {
            var nst = new Nest();
            nst.Name = Path.GetFileNameWithoutExtension(path);
            nst.PlateDefaults = Nest.PlateDefaults;
            nst.Units = Nest.Units;

            var writer = new NestWriter(nst);
            writer.Write(path);
        }

        public void Import()
        {
            var dlg = new OpenFileDialog();
            dlg.Multiselect = true;
            dlg.Filter = "DXF Files (*.dxf) | *.dxf";

            if (dlg.ShowDialog() != DialogResult.OK)
                return;

            var converter = new CadConverterForm();
            converter.AddFiles(dlg.FileNames);

            var result = converter.ShowDialog();

            if (result != DialogResult.OK)
                return;

            var drawings = converter.GetDrawings();
            drawings.ForEach(d => Nest.Drawings.Add(d));

            UpdateDrawingList();
            tabControl1.SelectedIndex = 1;
        }

        public bool Export()
        {
            var dlg = new SaveFileDialog();
            dlg.Filter = "DXF file (*.dxf)|*.dxf|" +
                "Image as displayed (*.jpg)|*.jpg|" +
                "Locations and rotations (*.txt)|*.txt";

            dlg.FileName = string.Format("{0}-P{1}", Nest.Name, CurrentPlateIndex + 1);
            dlg.AddExtension = true;
            dlg.DefaultExt = ".";

            if (dlg.ShowDialog() == DialogResult.OK)
            {
                if (dlg.FilterIndex == 1)
                {
                    var exporter = new DxfExporter();
                    var success = exporter.ExportPlate(PlateView.Plate, dlg.FileName);
                    return success;
                }
                else if (dlg.FilterIndex == 2)
                {
                    try
                    {
                        var img = new Bitmap(PlateView.Width, PlateView.Height);
                        PlateView.DrawToBitmap(img, new Rectangle(0, 0, PlateView.Width, PlateView.Height));
                        img.Save(dlg.FileName);
                    }
                    catch { }

                    return true;
                }
                else if (dlg.FilterIndex == 3)
                {
                    StreamWriter writer = null;

                    try
                    {
                        writer = new StreamWriter(dlg.FileName);

                        foreach (var part in PlateView.Plate.Parts)
                        {
                            var pt = part.BaseDrawing.Source.Offset.Rotate(part.Rotation);

                            writer.WriteLine("{0}|{1},{2}|{3}",
                                part.BaseDrawing.Source.Path,
                                System.Math.Round(part.Location.X - pt.X, 8),
                                System.Math.Round(part.Location.Y - pt.Y, 8),
                                Angle.ToDegrees(part.Rotation));
                        }
                    }
                    catch { }
                    finally
                    {
                        if (writer != null)
                            writer.Dispose();
                    }
                }
            }

            return false;
        }

        public void ExportAll()
        {
            LoadFirstPlate();

            do
            {
                if (!Export()) return;
            }
            while (LoadNextPlate());
        }

        public void RotateCw()
        {
            PlateView.Plate.Rotate90(RotationType.CW);
            PlateView.ZoomToFit();
        }

        public void RotateCcw()
        {
            PlateView.Plate.Rotate90(RotationType.CCW);
            PlateView.ZoomToFit();
        }

        public void Rotate180()
        {
            PlateView.Plate.Rotate180();
            PlateView.ZoomToFit();
        }

        public void ShowNestInfoEditor()
        {
            var form = new EditNestInfoForm();
            form.LoadNestInfo(Nest);

            if (form.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                form.SaveNestInfo(Nest);
                drawingListBox1.Units = Nest.Units;
                drawingListBox1.Invalidate();
            }
        }

        public void ResizePlateToFitParts()
        {
            PlateView.Plate.AutoSize(Settings.Default.AutoSizePlateFactor);
            PlateView.ZoomToPlate();
            PlateView.Refresh();
            UpdatePlateList();
        }

        public void SelectAllParts()
        {
            PlateView.SelectAll();
            PlateView.Invalidate();
        }

        public void ToggleRapid()
        {
            PlateView.DrawRapid = !PlateView.DrawRapid;
            PlateView.Invalidate();
        }

        public void ToggleDrawBounds()
        {
            PlateView.DrawBounds = !PlateView.DrawBounds;
            PlateView.Invalidate();
        }

        public void ToggleFillParts()
        {
            PlateView.FillParts = !PlateView.FillParts;
            PlateView.Invalidate();
        }

        public void SetCurrentPlateAsNestDefault()
        {
            Nest.PlateDefaults.SetFromExisting(PlateView.Plate);
        }

        public void EditPlate()
        {
            var form = new EditPlateForm(PlateView.Plate);
            form.Units = Nest.Units;

            if (form.ShowDialog() == DialogResult.OK)
            {
                PlateView.Invalidate();
                FirePlateChanged(false);
                UpdatePlateList();
            }

            Nest.UpdateDrawingQuantities();
            drawingListBox1.Refresh();

            if (PlateView.Plate.Parts.Count == 0)
                Nest.PlateDefaults.SetFromExisting(PlateView.Plate);
        }

        /// <summary>
        /// Opens the current plate as a DXF file by the default program set in Windows.
        /// </summary>
        public void OpenCurrentPlate()
        {
            var plate = PlateView.Plate;
            var name = string.Format("{0}-P{1}.dxf", Nest.Name, CurrentPlateIndex + 1);
            var path = Path.Combine(Path.GetTempPath(), name);
            var exporter = new DxfExporter();
            exporter.ExportPlate(plate, path);

            Process.Start(path);
        }

        public void RemoveCurrentPlate()
        {
            if (Nest.Plates.Count < 2)
                return;

            Nest.Plates.RemoveAt(CurrentPlateIndex);
        }

        public void AutoSequenceCurrentPlate()
        {
            var seq = new SequenceByNearest();
            var parts = seq.SequenceParts(PlateView.Plate.Parts);

            PlateView.Plate.Parts.Clear();
            PlateView.Plate.Parts.AddRange(parts);
        }

        public void AutoSequenceAllPlates()
        {
            var seq = new SequenceByNearest();

            foreach (var plate in Nest.Plates)
            {
                var parts = seq.SequenceParts(plate.Parts);
                plate.Parts.Clear();
                plate.Parts.AddRange(parts);
            }

            PlateView.Invalidate();
        }

        public void CalculateCurrentPlateCutTime()
        {
            var cutParamsForm = new CutParametersForm();
            cutParamsForm.Units = Nest.Units;

            if (cutParamsForm.ShowDialog() == DialogResult.OK)
            {
                var cutparams = cutParamsForm.GetCutParameters();
                var info = Timing.GetTimingInfo(PlateView.Plate);
                var time = Timing.CalculateTime(info, cutparams);

                var timingForm = new TimingForm();
                timingForm.Units = Nest.Units;
                timingForm.SetCutDistance(info.CutDistance);
                timingForm.SetCutTime(time);
                timingForm.SetIntersectionCount(info.IntersectionCount);
                timingForm.SetPierceCount(info.PierceCount);
                timingForm.SetRapidDistance(info.TravelDistance);
                timingForm.SetCutParameters(cutparams);

                timingForm.ShowDialog();
            }
        }

        public void CalculateNestCutTime()
        {
            var cutParamsForm = new CutParametersForm();
            cutParamsForm.Units = Nest.Units;

            if (cutParamsForm.ShowDialog() == DialogResult.OK)
            {
                var cutparams = cutParamsForm.GetCutParameters();
                var info = Timing.GetTimingInfo(Nest);
                var time = Timing.CalculateTime(info, cutparams);

                var timingForm = new TimingForm();
                timingForm.Units = Nest.Units;
                timingForm.SetCutDistance(info.CutDistance);
                timingForm.SetCutTime(time);
                timingForm.SetIntersectionCount(info.IntersectionCount);
                timingForm.SetPierceCount(info.PierceCount);
                timingForm.SetRapidDistance(info.TravelDistance);
                timingForm.SetCutParameters(cutparams);

                timingForm.ShowDialog();
            }
        }

        private void FirePlateChanged(bool updateListView = true)
        {
            if (updateListView)
                platesListView.Items[CurrentPlateIndex].Selected = true;

            if (PlateChanged != null)
                PlateChanged.Invoke(this, EventArgs.Empty);
        }

        #region Overrides

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            PlateView.Invalidate();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Icon = Icon.Clone() as Icon;
            PlateView.DrawBounds = Settings.Default.PlateViewDrawBounds;
            PlateView.DrawRapid = Settings.Default.PlateViewDrawRapid;
            splitContainer.SplitterDistance = Settings.Default.SplitterDistance;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            Settings.Default.PlateViewDrawBounds = PlateView.DrawBounds;
            Settings.Default.PlateViewDrawRapid = PlateView.DrawRapid;
            Settings.Default.SplitterDistance = splitContainer.SplitterDistance;
            Settings.Default.Save();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            PlateView.Invalidate();
        }

        #endregion

        #region Plates/Drawings Panel Events

        private void AddPlate_Click(object sender, EventArgs e)
        {
            Nest.CreatePlate();
        }

        private void EditSelectedPlate_Click(object sender, EventArgs e)
        {
            if (platesListView.SelectedIndices.Count != 0)
                EditPlate();
        }

        private void RemoveSelectedPlate_Click(object sender, EventArgs e)
        {
            RemoveCurrentPlate();
        }

        private void CalculateSelectedPlateCutTime_Click(object sender, EventArgs e)
        {
            CalculateCurrentPlateCutTime();
        }

        private void ImportDrawings_Click(object sender, EventArgs e)
        {
            Import();
        }

        private void CleanUnusedDrawings_Click(object sender, EventArgs e)
        {
            var result = MessageBox.Show(
                "Remove unused drawings?",
                "Clean Drawings",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1);

            if (result == DialogResult.Yes)
            {
                Nest.Drawings.RemoveWhere(d => d.Quantity.Nested == 0);
                UpdateDrawingList();
            }
        }

        #endregion

        #region Plate Collection Events

        private void Plates_PlateRemoved(object sender, ItemRemovedEventArgs<Plate> e)
        {
            if (Nest.Plates.Count <= CurrentPlateIndex)
                LoadLastPlate();
            else
                PlateView.Plate = Nest.Plates[CurrentPlateIndex];

            UpdatePlateList();
            PlateView.ZoomToFit();
        }

        private void Plates_PlateAdded(object sender, ItemAddedEventArgs<Plate> e)
        {
            tabControl1.SelectedIndex = 0;
            UpdatePlateList();
            LoadLastPlate();
            PlateView.ZoomToFit();
        }

        #endregion

        private static ListViewItem GetListViewItem(Plate plate, int id)
        {
            var item = new ListViewItem();
            item.Text = id.ToString();
            item.SubItems.Add(plate.Size.ToString());
            item.SubItems.Add(plate.Quantity.ToString());
            return item;
        }

        private void PlateView_PartRemoved(object sender, ItemRemovedEventArgs<Part> e)
        {
            updateDrawingListTimer.Stop();
            updateDrawingListTimer.Start();
        }

        private void PlateView_PartAdded(object sender, ItemAddedEventArgs<Part> e)
        {
            updateDrawingListTimer.Stop();
            updateDrawingListTimer.Start();
        }

        private void drawingListUpdateTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            drawingListBox1.Invoke(new MethodInvoker(() =>
            {
                drawingListBox1.Refresh();
            }));
        }

        private void drawingListBox1_DoubleClick(object sender, EventArgs e)
        {
            addPart = false;

            var drawing = drawingListBox1.SelectedItem as Drawing;

            if (drawing == null)
                return;

            var form = new EditDrawingForm();
            form.LoadDrawing(drawing);

            if (form.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                form.SaveDrawing(drawing);

                foreach (var part in PlateView.Parts)
                    part.Update();

                PlateView.Invalidate();
            }
        }

        private void drawingListBox1_Click(object sender, EventArgs e)
        {
            addPart = true;
        }

        private void PlateView_Enter(object sender, EventArgs e)
        {
            if (!addPart)
                return;

            var drawing = drawingListBox1.SelectedItem as Drawing;

            if (drawing == null)
                return;

            PlateView.SetAction(typeof(ActionAddPart), drawing);

            addPart = false;
        }
    }
}
