using OpenNest.Actions;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Collections;
using OpenNest.Controls;
using OpenNest.Engine;
using OpenNest.Engine.Sequencing;
using OpenNest.IO;
using OpenNest.Math;
using OpenNest.Properties;
using OpenNest.Shapes;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Api;
using Timer = System.Timers.Timer;

namespace OpenNest.Forms
{
    public partial class EditNestForm : Form
    {
        public event EventHandler PlateChanged;

        public readonly Document Document;
        public readonly PlateView PlateView;
        public readonly PlateManager PlateManager;

        public Nest Nest => Document.Nest;

        private readonly Timer updateDrawingListTimer;
        private bool updatingPlateList;

        private Panel plateHeaderPanel;
        private Label plateInfoLabel;
        private Button btnFirstPlate;

        private Button btnPreviousPlate;
        private Button btnNextPlate;
        private Button btnLastPlate;

        private SplitContainer viewSplitContainer;
        private Panel sidePanel;

        /// <summary>
        /// Used to distinguish between single/double click on drawing within drawinglistbox.
        /// If double click, this is set to false so the single click action won't be triggered.
        /// </summary>
        private bool addPart;

        private EditNestForm()
        {
            PlateView = new PlateView();
            PlateView.MouseEnter += PlateView_MouseEnter;
            PlateView.Enter += PlateView_Enter;
            PlateView.PartAdded += PlateView_PartAdded;
            PlateView.PartRemoved += PlateView_PartRemoved;
            PlateView.Dock = DockStyle.Fill;

            InitializeComponent();
            CreatePlateHeader();
            CreateSidePanel();

            splitContainer.Panel2.Controls.Add(viewSplitContainer);
            splitContainer.Panel2.Controls.Add(plateHeaderPanel);

            var renderer = new ToolStripRenderer(ToolbarTheme.Toolbar);
            toolStrip1.Renderer = renderer;
            toolStrip2.Renderer = renderer;

            platesListView.SelectedIndexChanged += (sender, e) =>
            {
                if (updatingPlateList || platesListView.SelectedIndices.Count == 0)
                    return;

                PlateManager.LoadAt(platesListView.SelectedIndices[0]);
            };
        }

        private void CreatePlateHeader()
        {
            plateHeaderPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 30,
                BackColor = Color.FromArgb(240, 240, 240),
                Padding = new Padding(4, 0, 4, 0)
            };

            plateInfoLabel = new Label
            {
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                ForeColor = Color.FromArgb(120, 120, 120),
                Dock = DockStyle.Left,
                Padding = new Padding(4, 4, 4, 4)
            };

            var btnSize = new System.Drawing.Size(28, 28);

            btnFirstPlate = CreateNavButton(Resources.move_first);
            btnFirstPlate.Click += (s, e) => PlateManager.LoadFirst();

            btnPreviousPlate = CreateNavButton(Resources.move_previous);
            btnPreviousPlate.Click += (s, e) => PlateManager.LoadPrevious();

            btnNextPlate = CreateNavButton(Resources.move_next);
            btnNextPlate.Click += (s, e) => PlateManager.LoadNext();

            btnLastPlate = CreateNavButton(Resources.move_last);
            btnLastPlate.Click += (s, e) => PlateManager.LoadLast();

            // Panel that holds the nav buttons and centers itself in the header
            var navPanel = new Panel
            {
                Width = btnSize.Width * 4,
                Height = btnSize.Height,
                Anchor = AnchorStyles.None
            };

            btnFirstPlate.Location = new Point(0, 0);
            btnPreviousPlate.Location = new Point(btnSize.Width, 0);
            btnNextPlate.Location = new Point(btnSize.Width * 2, 0);
            btnLastPlate.Location = new Point(btnSize.Width * 3, 0);

            navPanel.Controls.AddRange(new Control[] { btnFirstPlate, btnPreviousPlate, btnNextPlate, btnLastPlate });

            plateHeaderPanel.Controls.Add(navPanel);
            plateHeaderPanel.Controls.Add(plateInfoLabel);

            // Center the nav panel on resize
            CenterNavPanel(navPanel);
            plateHeaderPanel.Resize += (s, e) => CenterNavPanel(navPanel);
        }

        private void CenterNavPanel(Panel navPanel)
        {
            navPanel.Left = (plateHeaderPanel.Width - navPanel.Width) / 2;
            navPanel.Top = (plateHeaderPanel.Height - navPanel.Height) / 2;
        }

        private void CreateSidePanel()
        {
            sidePanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.White
            };

            viewSplitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                FixedPanel = FixedPanel.Panel2,
                Panel2MinSize = 0
            };

            viewSplitContainer.Panel1.Controls.Add(PlateView);
            viewSplitContainer.Panel2.Controls.Add(sidePanel);
            viewSplitContainer.Panel2Collapsed = true;
        }

        public void ShowSidePanel(Control content, int width = 390)
        {
            sidePanel.Controls.Clear();
            content.Dock = DockStyle.Fill;
            sidePanel.Controls.Add(content);
            viewSplitContainer.SplitterDistance = viewSplitContainer.Width - width;
            viewSplitContainer.Panel2Collapsed = false;
        }

        public void HideSidePanel()
        {
            viewSplitContainer.Panel2Collapsed = true;
            sidePanel.Controls.Clear();
        }

        private static Button CreateNavButton(System.Drawing.Image image)
        {
            return new Button
            {
                Image = image,
                Size = new System.Drawing.Size(28, 28),
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 0 },
                Cursor = Cursors.Hand
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

            Document = new Document { Nest = nest };

            PlateManager = new PlateManager(nest);
            PlateManager.CurrentPlateChanged += PlateManager_CurrentPlateChanged;
            PlateManager.PlateListChanged += PlateManager_PlateListChanged;

            PlateManager.EnsureSentinel();

            UpdatePlateList();
            UpdateDrawingList();
            UpdateRemovePlateButton();

            PlateManager.LoadFirst();

            Text = Nest.Name;
            drawingListBox1.Units = Nest.Units;
            drawingListBox1.DeleteRequested += drawingListBox1_DeleteRequested;
        }


        public void UpdatePlateList()
        {
            updatingPlateList = true;
            var focused = ContainsFocus ? GetFocusedControl() : null;

            platesListView.BeginUpdate();
            platesListView.Items.Clear();

            var items = new ListViewItem[Nest.Plates.Count];

            for (int i = 0; i < items.Length; ++i)
            {
                var plate = Nest.Plates[i];
                var item = GetListViewItem(plate, i + 1);
                items[i] = item;
            }

            platesListView.Items.AddRange(items);

            if (PlateManager.CurrentIndex < platesListView.Items.Count)
                platesListView.Items[PlateManager.CurrentIndex].Selected = true;

            platesListView.EndUpdate();
            updatingPlateList = false;

            if (focused != null && focused != platesListView)
                focused.Focus();
        }

        private Control GetFocusedControl()
        {
            var ctrl = this;
            while (ctrl is ContainerControl container && container.ActiveControl != null)
                return container.ActiveControl;
            return ctrl;
        }

        public void UpdateDrawingList()
        {
            var topIndex = drawingListBox1.TopIndex;
            var selected = drawingListBox1.SelectedItem;

            drawingListBox1.BeginUpdate();

            drawingListBox1.Items.Clear();

            foreach (var dwg in Nest.Drawings.OrderBy(d => d.Name).ToList())
            {
                if (hideNestedButton.Checked && dwg.Quantity.Required > 0 && dwg.Quantity.Remaining == 0)
                    continue;

                drawingListBox1.Items.Add(dwg);
            }

            if (selected != null && drawingListBox1.Items.Contains(selected))
                drawingListBox1.SelectedItem = selected;

            if (topIndex < drawingListBox1.Items.Count)
                drawingListBox1.TopIndex = topIndex;

            drawingListBox1.EndUpdate();
        }

        public void Save()
        {
            if (Document.HasSavePath)
                SaveAs(Document.LastSavePath);
            else
                SaveAs();
        }

        public void SaveAs()
        {
            var dlg = new SaveFileDialog();
            dlg.Filter = $"{NestFormat.FileFilter}|Template File|*.nstdot";
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
            Document.SaveAs(path);
            Text = Document.Name;
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
        }

        public bool Export()
        {
            var dlg = new SaveFileDialog();
            dlg.Filter = "DXF file (*.dxf)|*.dxf|" +
                "Image as displayed (*.jpg)|*.jpg|" +
                "Locations and rotations (*.txt)|*.txt";

            dlg.FileName = string.Format("{0}-P{1}", Nest.Name, PlateManager.CurrentIndex + 1);
            dlg.AddExtension = true;
            dlg.DefaultExt = ".";

            if (dlg.ShowDialog() == DialogResult.OK)
            {
                if (dlg.FilterIndex == 1)
                {
                    Dxf.ExportPlate(PlateView.Plate, dlg.FileName);
                    return true;
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
            PlateManager.LoadFirst();

            do
            {
                if (!Export()) return;
            }
            while (PlateManager.LoadNext());
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
            var options = new PlateSizeOptions
            {
                SnapIncrement = Settings.Default.AutoSizePlateFactor,
            };
            PlateView.Plate.SnapToStandardSize(options);
            PlateView.ZoomToPlate();
            PlateView.Refresh();
            UpdatePlateList();
            UpdatePlateHeader();
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

        public void TogglePiercePoints()
        {
            PlateView.DrawPiercePoints = !PlateView.DrawPiercePoints;
            PlateView.Invalidate();
        }

        public void ToggleDrawBounds()
        {
            PlateView.DrawBounds = !PlateView.DrawBounds;
            PlateView.Invalidate();
        }

        public void ToggleBendLines()
        {
            PlateView.ShowBendLines = !PlateView.ShowBendLines;
            PlateView.Invalidate();
        }

        public void ToggleDrawOffset()
        {
            PlateView.DrawOffset = !PlateView.DrawOffset;
            PlateView.Invalidate();
        }

        public void ToggleCutDirection()
        {
            PlateView.DrawCutDirection = !PlateView.DrawCutDirection;
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
            var name = string.Format("{0}-P{1}.dxf", Nest.Name, PlateManager.CurrentIndex + 1);
            var path = Path.Combine(Path.GetTempPath(), name);
            Dxf.ExportPlate(plate, path);

            Process.Start(path);
        }

        public void RemoveCurrentPlate()
        {
            PlateManager.RemoveCurrent();
        }

        public void AutoSequenceCurrentPlate()
        {
            SequencePlate(PlateView.Plate);
            PlateView.Invalidate();
        }

        public void AutoSequenceAllPlates()
        {
            foreach (var plate in Nest.Plates)
                SequencePlate(plate);

            PlateView.Invalidate();
        }

        private static void SequencePlate(Plate plate)
        {
            var parameters = new SequenceParameters { Method = SequenceMethod.LeastCode };
            var sequencer = PartSequencerFactory.Create(parameters);
            var ordered = sequencer.Sequence(plate.Parts.ToList(), plate);

            plate.Parts.Clear();
            for (var i = ordered.Count - 1; i >= 0; i--)
                plate.Parts.Add(ordered[i].Part);
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
            if (updateListView && PlateManager.CurrentIndex < platesListView.Items.Count)
                platesListView.Items[PlateManager.CurrentIndex].Selected = true;

            UpdatePlateHeader();
            PlateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void UpdatePlateHeader()
        {
            var plate = PlateManager.CurrentPlate;

            if (plate != null)
            {
                plateInfoLabel.Text = string.Format("Plate {0} of {1}  |  {2}",
                    PlateManager.CurrentIndex + 1, PlateManager.Count, plate.Size);
            }
            else
            {
                plateInfoLabel.Text = "No plates";
            }

            btnFirstPlate.Enabled = !PlateManager.IsFirst;
            btnPreviousPlate.Enabled = !PlateManager.IsFirst;
            btnNextPlate.Enabled = !PlateManager.IsLast;
            btnLastPlate.Enabled = !PlateManager.IsLast;
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

            PlateManager.Dispose();

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

        public void AssignLeadIns_Click(object sender, EventArgs e)
        {
            if (PlateView?.Plate == null)
                return;

            var plate = PlateView.Plate;

            var parameters = LoadOrDefaultParameters(plate.CuttingParameters);

            using var dlg = new CuttingParametersDialog();
            dlg.LoadParameters(parameters);

            if (dlg.ShowDialog() != DialogResult.OK)
                return;

            parameters = dlg.GetParameters();
            plate.CuttingParameters = parameters;
            SaveCuttingParameters(parameters);

            var assigner = new LeadInAssigner
            {
                Sequencer = new LeftSideSequencer()
            };
            assigner.Assign(plate);

            foreach (var lp in PlateView.Parts)
                lp.IsDirty = true;

            PlateView.Invalidate();
        }

        public void RemoveLeadIns_Click(object sender, EventArgs e)
        {
            if (PlateView?.Plate == null)
                return;

            var plate = PlateView.Plate;
            var count = 0;

            foreach (var part in plate.Parts)
            {
                if (part.HasManualLeadIns)
                {
                    part.RemoveLeadIns();
                    count++;
                }
            }

            if (count == 0)
                return;

            plate.CuttingParameters = null;

            // Rebuild all layout part graphics
            foreach (var lp in PlateView.Parts)
            {
                lp.IsDirty = true;
                lp.Update();
            }

            PlateView.Invalidate();
        }

        public void AssignLeadInsAllPlates()
        {
            if (Nest == null)
                return;

            var parameters = LoadOrDefaultParameters(PlateView?.Plate?.CuttingParameters);

            using var dlg = new CuttingParametersDialog();
            dlg.LoadParameters(parameters);

            if (dlg.ShowDialog() != DialogResult.OK)
                return;

            parameters = dlg.GetParameters();
            SaveCuttingParameters(parameters);

            var assigner = new LeadInAssigner
            {
                Sequencer = new LeftSideSequencer()
            };

            foreach (var plate in Nest.Plates)
            {
                plate.CuttingParameters = parameters;
                assigner.Assign(plate);
            }

            PlateView.Invalidate();
        }

        public void RemoveLeadInsAllPlates()
        {
            if (Nest == null)
                return;

            foreach (var plate in Nest.Plates)
            {
                foreach (var part in plate.Parts)
                {
                    if (part.HasManualLeadIns)
                        part.RemoveLeadIns();
                }

                plate.CuttingParameters = null;
            }

            foreach (var lp in PlateView.Parts)
            {
                lp.IsDirty = true;
                lp.Update();
            }

            PlateView.Invalidate();
        }

        public void PlaceLeadIn_Click(object sender, EventArgs e)
        {
            if (PlateView?.Plate == null)
                return;

            var plate = PlateView.Plate;

            if (plate.CuttingParameters == null)
                plate.CuttingParameters = LoadOrDefaultParameters(null);

            PlateView.SetAction(typeof(Actions.ActionLeadIn));
        }

        private static CuttingParameters LoadOrDefaultParameters(CuttingParameters existing)
        {
            if (existing != null)
                return existing;

            var json = Properties.Settings.Default.CuttingParametersJson;
            if (!string.IsNullOrEmpty(json))
            {
                try { return CuttingParametersSerializer.Deserialize(json); }
                catch { /* fall through */ }
            }

            return new CuttingParameters();
        }

        private static void SaveCuttingParameters(CuttingParameters parameters)
        {
            var json = CuttingParametersSerializer.Serialize(parameters);
            Properties.Settings.Default.CuttingParametersJson = json;
            Properties.Settings.Default.Save();
        }

        private void ImportDrawings_Click(object sender, EventArgs e)
        {
            Import();
        }

        private void ShapeLibrary_Click(object sender, EventArgs e)
        {
            var form = new ShapeLibraryForm(Nest.Drawings.Select(d => d.Name));
            form.ShowDialog();

            var drawings = form.GetDrawings();
            if (drawings.Count == 0) return;

            drawings.ForEach(d => Nest.Drawings.Add(d));
            UpdateDrawingList();
        }

        private void EditDrawingsInConverter_Click(object sender, EventArgs e)
        {
            if (Nest.Drawings.Count == 0)
                return;

            var converter = new CadConverterForm();
            converter.LoadDrawings(Nest.Drawings);

            if (converter.ShowDialog() != DialogResult.OK)
                return;

            var newDrawings = converter.GetDrawings();
            var newByName = newDrawings.ToDictionary(d => d.Name);

            // Update existing drawings in-place so parts keep their BaseDrawing references
            foreach (var existing in Nest.Drawings.ToList())
            {
                if (newByName.TryGetValue(existing.Name, out var updated))
                {
                    existing.Program = updated.Program;
                    existing.SourceEntities = updated.SourceEntities;
                    existing.SuppressedEntityIds = updated.SuppressedEntityIds;
                    existing.Source = updated.Source;
                    existing.Customer = updated.Customer;
                    existing.Quantity.Required = updated.Quantity.Required;
                    existing.Bends.Clear();
                    existing.Bends.AddRange(updated.Bends);
                    newByName.Remove(existing.Name);
                }
                else
                {
                    Nest.Drawings.Remove(existing);
                }
            }

            // Add any new drawings that weren't in the original set
            foreach (var d in newByName.Values)
                Nest.Drawings.Add(d);

            // Refresh all parts to use the updated programs
            foreach (var plate in Nest.Plates)
                foreach (var part in plate.Parts)
                    if (!part.BaseDrawing.IsCutOff)
                        part.Update();

            UpdateDrawingList();
            PlateView.Invalidate();
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

        private void HideNestedButton_CheckedChanged(object sender, EventArgs e)
        {
            UpdateDrawingList();
        }

        #endregion

        #region PlateManager Events

        private void PlateManager_CurrentPlateChanged(object sender, PlateChangedEventArgs e)
        {
            PlateView.Plate = PlateManager.CurrentPlate;
            PlateView.ZoomToFit();
            UpdatePlateHeader();
            UpdateRemovePlateButton();
            PlateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void PlateManager_PlateListChanged(object sender, EventArgs e)
        {
            UpdatePlateList();
            UpdatePlateHeader();
            UpdateRemovePlateButton();
        }

        private void UpdateRemovePlateButton()
        {
            toolStripLabel2.Enabled = PlateManager.CanRemoveCurrent;
        }

        #endregion

        private static ListViewItem GetListViewItem(Plate plate, int id)
        {
            var item = new ListViewItem();
            item.Text = id.ToString();
            item.SubItems.Add(plate.Size.ToString());
            item.SubItems.Add(plate.Quantity.ToString());

            var partCount = plate.Parts.Count(p => !p.BaseDrawing.IsCutOff);
            item.SubItems.Add(partCount.ToString());

            var util = plate.Utilization();
            item.SubItems.Add(partCount > 0 ? $"{util:P0}" : "");

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
            if (!drawingListBox1.IsHandleCreated)
                return;

            drawingListBox1.Invoke(new MethodInvoker(() =>
            {
                if (hideNestedButton.Checked)
                {
                    drawingListBox1.BeginUpdate();

                    for (var i = drawingListBox1.Items.Count - 1; i >= 0; i--)
                    {
                        var dwg = (Drawing)drawingListBox1.Items[i];
                        if (dwg.Quantity.Required > 0 && dwg.Quantity.Remaining == 0)
                            drawingListBox1.Items.RemoveAt(i);
                    }

                    foreach (var dwg in Nest.Drawings.OrderBy(d => d.Name))
                    {
                        if (dwg.Quantity.Required > 0 && dwg.Quantity.Remaining == 0)
                            continue;

                        if (!drawingListBox1.Items.Contains(dwg))
                            drawingListBox1.Items.Add(dwg);
                    }

                    drawingListBox1.EndUpdate();
                }

                drawingListBox1.Invalidate();
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

        private void drawingListBox1_DeleteRequested(object sender, Drawing drawing)
        {
            var result = MessageBox.Show(
                $"Delete drawing '{drawing.Name}' and all its parts from every plate?",
                "Delete Drawing",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (result != DialogResult.Yes)
                return;

            foreach (var plate in Nest.Plates)
            {
                for (var i = plate.Parts.Count - 1; i >= 0; i--)
                {
                    if (plate.Parts[i].BaseDrawing == drawing)
                        plate.Parts.RemoveAt(i);
                }
            }

            Nest.Drawings.Remove(drawing);
            UpdateDrawingList();
            PlateView.Invalidate();
        }

        private void drawingListBox1_Click(object sender, EventArgs e)
        {
            addPart = true;
        }

        private void PlateView_MouseEnter(object sender, EventArgs e)
        {
            if (!PlateView.Focused)
                PlateView.Focus();
        }

        private void PlateView_Enter(object sender, EventArgs e)
        {
            if (!addPart)
                return;

            var drawing = drawingListBox1.SelectedItem as Drawing;

            if (drawing == null)
                return;

            PlateView.SetAction(typeof(ActionClone), drawing);

            addPart = false;
        }

        private void toolStripLabel2_Click(object sender, EventArgs e)
        {
            RemoveSelectedPlate_Click(sender, e);
        }

        private void toolStripLabel1_Click(object sender, EventArgs e)
        {
            AddPlate_Click(sender, e);
        }
    }
}
