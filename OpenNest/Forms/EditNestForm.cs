using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Actions;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Collections;
using OpenNest.Controls;
using OpenNest.Data;
using OpenNest.Diagnostics;
using OpenNest.Engine;
using OpenNest.Engine.Sequencing;
using OpenNest.IO;
using OpenNest.Math;
using OpenNest.Properties;
using OpenNest.Reporting;
using OpenNest.Shapes;
using Timer = System.Timers.Timer;

namespace OpenNest.Forms
{
    public partial class EditNestForm : Form
    {
        public event EventHandler PlateChanged;

        public readonly Document Document;
        public readonly PlateView PlateView;

        public System.Threading.Tasks.Task CheckOverlapsAsync() => PlateView.CheckOverlapsAsync(Nest.Units);
        public void CancelOverlapCheck() => PlateView.CancelOverlapCheck();
        public OverlapDisplayMode OverlapDisplay
        {
            get => PlateView.OverlapDisplay;
            set => PlateView.OverlapDisplay = value;
        }

        public readonly PlateManager PlateManager;

        public Nest Nest => Document.Nest;

        private readonly Timer updateDrawingListTimer;
        private bool updatingPlateList;
        private bool addingToSet;
        private CutSimulatorForm cutSimulatorForm;

        private Panel plateHeaderPanel;
        private Label plateInfoLabel;
        private Button btnFirstPlate;

        private Button btnPreviousPlate;
        private Button btnNextPlate;
        private Button btnLastPlate;

        private SplitContainer viewSplitContainer;
        private Panel sidePanel;
        private Label sidePanelTitle;

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
            PlateView.PartsReordered += PlateView_PartsReordered;
            PlateView.Dock = DockStyle.Fill;
            PlateView.ClampNest = () => Nest;

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
                Padding = new Padding(4, 0, 4, 0),
            };

            plateInfoLabel = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                ForeColor = Color.FromArgb(120, 120, 120),
                Padding = new Padding(4, 4, 4, 4),
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

            var zoomOutButton = CreateNavButton(Resources.zoom_out);
            zoomOutButton.Name = "btnZoomOut";
            zoomOutButton.AccessibleName = "Zoom Out";
            zoomOutButton.Click += (s, e) => ZoomPlateView(1.0f / 1.5f);

            var zoomInButton = CreateNavButton(Resources.zoom_in);
            zoomInButton.Name = "btnZoomIn";
            zoomInButton.AccessibleName = "Zoom In";
            zoomInButton.Click += (s, e) => ZoomPlateView(1.5f);

            var zoomToFitButton = CreateNavButton(Resources.zoom_all);
            zoomToFitButton.Name = "btnZoomToFit";
            zoomToFitButton.AccessibleName = "Zoom To Fit";
            zoomToFitButton.Click += (s, e) => PlateView.ZoomToFit();

            var toolTip = new ToolTip(components);
            toolTip.SetToolTip(zoomOutButton, "Zoom Out (Ctrl+Minus)");
            toolTip.SetToolTip(zoomInButton, "Zoom In (Ctrl+Plus)");
            toolTip.SetToolTip(zoomToFitButton, "Zoom To Fit (F)");

            // Keep navigation and zoom controls together in the header.
            var navPanel = new Panel
            {
                Width = btnSize.Width * 7 + 8,
                Height = btnSize.Height,
                Anchor = AnchorStyles.None,
            };

            btnFirstPlate.Location = new Point(0, 0);
            btnPreviousPlate.Location = new Point(btnSize.Width, 0);
            btnNextPlate.Location = new Point(btnSize.Width * 2, 0);
            btnLastPlate.Location = new Point(btnSize.Width * 3, 0);

            // Inspection locks plate navigation as one container but leaves zoom live.
            var plateButtons = new Panel { Size = new System.Drawing.Size(btnSize.Width * 4, btnSize.Height) };
            plateButtons.Controls.AddRange(
                new Control[] { btnFirstPlate, btnPreviousPlate, btnNextPlate, btnLastPlate });
            ZoomControls = [zoomOutButton, zoomInButton, zoomToFitButton];

            zoomOutButton.Location = new Point(btnSize.Width * 4 + 8, 0);
            zoomInButton.Location = new Point(btnSize.Width * 5 + 8, 0);
            zoomToFitButton.Location = new Point(btnSize.Width * 6 + 8, 0);

            var separator = new Panel
            {
                Location = new Point(btnSize.Width * 4 + 3, 4),
                Size = new System.Drawing.Size(1, btnSize.Height - 8),
                BackColor = SystemColors.ControlDark,
                TabStop = false,
            };

            navPanel.Controls.AddRange(
                new Control[]
                {
                    plateButtons, separator, zoomOutButton, zoomInButton, zoomToFitButton
                }
            );

            plateHeaderPanel.Controls.Add(plateInfoLabel);
            plateHeaderPanel.Controls.Add(navPanel);
            plateHeaderPanel.Layout += (s, e) => CenterPlateHeaderControls(navPanel);
            CenterPlateHeaderControls(navPanel);
        }

        private void CenterPlateHeaderControls(Panel navPanel)
        {
            navPanel.Left = (plateHeaderPanel.ClientSize.Width - navPanel.Width) / 2;
            navPanel.Top = (plateHeaderPanel.ClientSize.Height - navPanel.Height) / 2;

            // Reserve the space before the centered buttons for the plate label.
            var labelLeft = plateHeaderPanel.Padding.Left;
            plateInfoLabel.SetBounds(labelLeft, 0,
                System.Math.Max(0, navPanel.Left - labelLeft), plateHeaderPanel.ClientSize.Height);
        }

        private void ZoomPlateView(float factor)
        {
            var point = new Point(PlateView.Width / 2, PlateView.Height / 2);
            PlateView.ZoomToControlPoint(point, factor);
        }

        private void CreateSidePanel()
        {
            sidePanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.White,
            };

            // The header sits outside the scrolling content so Close stays visible.
            sidePanelTitle = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            };

            var closeButton = new Button
            {
                Name = "sidePanelCloseButton",
                Text = "\u00D7",
                AccessibleName = "Close panel",
                Dock = DockStyle.Right,
                Width = 30,
                Font = new Font("Segoe UI", 12f),
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 0 },
                Cursor = Cursors.Hand,
            };
            closeButton.Click += (s, e) => CloseSidePanel();
            new ToolTip(components).SetToolTip(closeButton, "Close (Esc)");

            var sidePanelHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 30,
                BackColor = Color.FromArgb(240, 240, 240),
                Padding = new Padding(8, 0, 0, 0),
            };
            sidePanelHeader.Controls.Add(sidePanelTitle);
            sidePanelHeader.Controls.Add(closeButton);
            SidePanelHeader = sidePanelHeader;

            viewSplitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                FixedPanel = FixedPanel.Panel2,
                Panel2MinSize = 0,
            };

            viewSplitContainer.Panel1.Controls.Add(PlateView);
            viewSplitContainer.Panel2.Controls.Add(sidePanel);
            viewSplitContainer.Panel2.Controls.Add(sidePanelHeader);
            viewSplitContainer.Panel2Collapsed = true;
        }

        public bool IsSidePanelVisible => !viewSplitContainer.Panel2Collapsed;

        internal Control SidePanelHeader { get; private set; }
        internal Control[] ZoomControls { get; private set; }
        internal CuttingInspectionPanel CuttingInspection { get; private set; }
        private bool disposeAfterCuttingInspection;
        private bool closeAfterCuttingInspection;
        internal bool DeferCuttingDisposal()
        {
            if (CuttingInspection == null)
                return false;
            CuttingInspection.RequestClose();
            if (CuttingInspection == null)
                return false;
            disposeAfterCuttingInspection = true;
            return true;
        }
        internal System.Action<System.Threading.CancellationToken> CuttingBeforePlan { get; set; }

        internal void OpenValidationInspection()
        {
            if (CuttingInspection != null)
            {
                CuttingInspection.SwitchMode(CuttingInspectionMode.Validate);
                return;
            }
            OpenInspection(true, CuttingInspectionMode.Validate);
        }

        internal void OpenCuttingInspection(bool allPlates = false)
        {
            if (CuttingInspection != null)
            {
                CuttingInspection.SwitchMode(CuttingInspectionMode.Plan);
                return;
            }
            OpenInspection(allPlates, CuttingInspectionMode.Plan);
        }

        private void OpenInspection(bool allPlates, CuttingInspectionMode mode)
        {
            if (CuttingInspection != null)
                return;
            PlateView.EndAction();
            var panel = new CuttingInspectionPanel(this, LoadOrDefaultParameters(PlateView.Plate.CuttingParameters), allPlates, mode)
            {
                BeforePlan = CuttingBeforePlan
            };
            CuttingInspection = panel;
            ShowSidePanel(panel, "Cutting Inspection — Plan / Validate", 480);
            panel.Open();
        }

        internal void CompleteCuttingApply(CuttingParameters parameters, OpenNest.CNC.CuttingPlanning.CuttingCommitResult commit)
        {
            if (commit.Status != OpenNest.CNC.CuttingPlanning.CuttingCommitStatus.Applied)
                return;
            SaveCuttingParameters(parameters);
            UpdatePlateList();
            if (commit.RefreshErrors.Count > 0)
                MessageBox.Show(this, "The cutting plan was applied, but part of the display did not refresh: "
                    + commit.RefreshErrors[0].Message, "Plan Cutting", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        internal void FinishCuttingInspection(CuttingInspectionPanel panel)
        {
            if (!ReferenceEquals(CuttingInspection, panel))
                return;
            CuttingInspection = null;
            var main = MdiParent as MainForm;
            HideSidePanel();
            if (disposeAfterCuttingInspection)
            {
                disposeAfterCuttingInspection = false;
                Dispose();
            }
            else if (closeAfterCuttingInspection)
            {
                closeAfterCuttingInspection = false;
                Close();
            }
            else
                PlateView.Focus();
            main?.ResumeDeferredCuttingDisposal();
        }

        public void ShowSidePanel(Control content, string title, int width = 390)
        {
            if (CuttingInspection != null && !ReferenceEquals(content, CuttingInspection))
            {
                CuttingInspection.RequestClose();
                return;
            }
            sidePanel.Controls.Clear();
            content.Dock = DockStyle.Fill;
            sidePanel.Controls.Add(content);
            sidePanelTitle.Text = title;
            var widest = System.Math.Max(viewSplitContainer.Panel1MinSize,
                viewSplitContainer.Width - viewSplitContainer.Panel2MinSize - viewSplitContainer.SplitterWidth);
            viewSplitContainer.SplitterDistance = System.Math.Clamp(viewSplitContainer.Width - width,
                viewSplitContainer.Panel1MinSize, widest);
            viewSplitContainer.Panel2Collapsed = false;
        }

        public void HideSidePanel()
        {
            if (CuttingInspection != null)
            {
                CuttingInspection.RequestClose();
                return;
            }
            viewSplitContainer.Panel2Collapsed = true;
            sidePanel.Controls.Clear();
            sidePanelTitle.Text = string.Empty;
        }

        private void CloseSidePanel()
        {
            if (CuttingInspection != null)
            {
                CuttingInspection.RequestClose();
                return;
            }
            // Side panels belong to the plate action that opened them; ending the
            // action hides its panel. Hide directly as well so Close always closes.
            PlateView.EndAction();
            if (IsSidePanelVisible)
                HideSidePanel();
            PlateView.Focus();
        }

        private static Button CreateNavButton(System.Drawing.Image image)
        {
            return new Button
            {
                Image = image,
                Size = new System.Drawing.Size(28, 28),
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 0 },
                Cursor = Cursors.Hand,
            };
        }

        public EditNestForm(Nest nest)
            : this()
        {
            updateDrawingListTimer = new Timer()
            {
                AutoReset = false,
                Enabled = true,
                Interval = 50,
            };
            updateDrawingListTimer.Elapsed += drawingListUpdateTimer_Elapsed;

            Document = new Document { Nest = nest };
            // Units are read when each check starts, matching the manual command.
            PlateView.SetOverlapAutoCheck(() => Nest.Units);

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
            drawingListBox1.SaveToLibraryRequested += drawingListBox1_SaveToLibraryRequested;
            drawingListBox1.AddToSetRequested += drawingListBox1_AddToSetRequested;
            drawingListBox1.OpenInConverterRequested += drawingListBox1_OpenInConverterRequested;
            drawingListBox1.DrawingPickedUp += drawingListBox1_DrawingPickedUp;
            drawingListBox1.DrawingDropped += drawingListBox1_DrawingDropped;
            Activated += (_, _) => UpdateDrawingLibraryCommand();
            UpdateDrawingLibraryCommand();
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
            Control ctrl = this;
            while (ctrl is ContainerControl container && container.ActiveControl != null)
                ctrl = container.ActiveControl;
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
                if (
                    hideNestedButton.Checked
                    && dwg.Quantity.Required > 0
                    && dwg.Quantity.Remaining == 0
                )
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
            dlg.Filter = NestFormat.FileFilter;
            dlg.FileName = Nest.Name;

            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                SaveAs(dlg.FileName);
        }

        public void SaveAs(string path)
        {
            Document.SaveAs(path);
            Text = Document.Name;
        }

        public void Import()
        {
            var dlg = new OpenFileDialog();
            dlg.Multiselect = true;
            dlg.Filter =
                "CAD Files (*.dxf;*.dwg)|*.dxf;*.dwg|DXF Files (*.dxf)|*.dxf|DWG Files (*.dwg)|*.dwg";

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
            var suggestedFileName = string.Format("{0}-P{1}", Nest.Name, PlateManager.CurrentIndex + 1);
            var target = ShowPlateExportDialog(suggestedFileName);
            if (target == null)
                return false;

            return TryExportPlate(PlateView.Plate, target.Value.Destination, target.Value.FilterIndex);
        }

        internal virtual (string Destination, int FilterIndex)? ShowPlateExportDialog(string suggestedFileName)
        {
            using var dlg = new SaveFileDialog
            {
                Filter = "DXF file (*.dxf)|*.dxf|"
                    + "Image as displayed (*.jpg)|*.jpg|"
                    + "Locations and rotations (*.txt)|*.txt",
                FileName = suggestedFileName,
                AddExtension = true,
                DefaultExt = ".",
            };
            return dlg.ShowDialog(this) == DialogResult.OK ? (dlg.FileName, dlg.FilterIndex) : null;
        }

        internal bool TryExportPlate(Plate plate, string destination, int filterIndex)
        {
            try
            {
                WritePlateExport(plate, destination, filterIndex);
                return true;
            }
            catch (Exception ex)
            {
                ReportPlateExportFailure(destination, ex);
                return false;
            }
        }

        internal virtual void WritePlateExport(Plate plate, string destination, int filterIndex)
        {
            switch (filterIndex)
            {
                case 1:
                    Dxf.ExportPlate(plate, destination);
                    break;
                case 2:
                    using (var img = new Bitmap(PlateView.Width, PlateView.Height))
                    {
                        PlateView.DrawToBitmap(
                            img,
                            new Rectangle(0, 0, PlateView.Width, PlateView.Height)
                        );
                        img.Save(destination, System.Drawing.Imaging.ImageFormat.Jpeg);
                    }
                    break;
                case 3:
                    using (var writer = new StreamWriter(destination))
                    {
                        foreach (var part in plate.Parts)
                        {
                            var pt = part.BaseDrawing.Source.Offset.Rotate(part.Rotation);

                            writer.WriteLine(
                                "{0}|{1},{2}|{3}",
                                part.BaseDrawing.Source.Path,
                                System.Math.Round(part.Location.X - pt.X, 8),
                                System.Math.Round(part.Location.Y - pt.Y, 8),
                                Angle.ToDegrees(part.Rotation)
                            );
                        }
                    }
                    break;
                default:
                    throw new ArgumentException("Unsupported plate export format.", nameof(filterIndex));
            }
        }

        internal virtual void ReportPlateExportFailure(string destination, Exception error) =>
            MessageBox.Show(this, $"Could not export plate to '{destination}': {error.Message}",
                "Export Plate", MessageBoxButtons.OK, MessageBoxIcon.Error);

        public void ExportAll()
        {
            PlateManager.LoadFirst();

            do
            {
                if (!Export())
                    return;
            } while (PlateManager.LoadNext());
        }

        /// <summary>The report target fixed before the save dialog opens.</summary>
        internal sealed record NestReportTargets(Nest Nest, string SuggestedFileName);

        /// <summary>
        /// Capture the report target before the owned save dialog opens. The exact nest
        /// reference is fixed here; later failures are reported against it, not a newer
        /// active document. Whole-job nesting, open progress windows (via <paramref name="isJobBusy"/>),
        /// interactive fill and busy plate actions on every view sharing this nest are rejected.
        /// </summary>
        internal NestReportTargets CaptureReportTargets(Func<bool> isJobBusy)
        {
            ArgumentNullException.ThrowIfNull(isJobBusy);
            var nest = Nest;
            if (nest == null)
                throw new InvalidOperationException("No nest is available to report.");

            // The nest name becomes the suggested file name; reject unusable names before the dialog.
            if (
                string.IsNullOrWhiteSpace(nest.Name)
                || nest.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            )
                throw new InvalidOperationException(
                    $"The nest name '{nest.Name}' cannot be used as a report file name. Rename the nest first."
                );

            if (isJobBusy() || IsReportTargetBusy(nest))
                throw new InvalidOperationException(
                    $"Finish or cancel the current nesting or plate action before exporting the report for '{nest.Name}'."
                );

            return new NestReportTargets(nest, $"{nest.Name}.report.pdf");
        }

        /// <summary>
        /// Revalidate the fixed target after the dialog closed, then synchronously capture the
        /// snapshot on the UI thread and write it. The dialog pumps messages, so an async fill
        /// could have committed or started meanwhile; once capture begins nothing yields.
        /// </summary>
        internal void WriteNestReport(
            NestReportTargets targets,
            string destination,
            Func<bool> isJobBusy
        )
        {
            ArgumentNullException.ThrowIfNull(targets);
            ArgumentException.ThrowIfNullOrWhiteSpace(destination);
            ArgumentNullException.ThrowIfNull(isJobBusy);

            if (!ReferenceEquals(targets.Nest, Nest))
                throw new InvalidOperationException(
                    "The active document changed while saving. No report was written."
                );
            if (isJobBusy() || IsReportTargetBusy(targets.Nest))
                throw new InvalidOperationException(
                    "A nesting or plate operation started while saving. No report was written."
                );

            // Reporting must have no accounting side effects: no quantity refresh, no
            // selection or dirty-state change. Only the detached snapshot reaches the writer.
            var snapshot = NestReportBuilder.Capture(
                targets.Nest,
                DateTimeOffset.Now,
                SavedCutParameters.Load(targets.Nest.Units)
            );
            NestPdfWriter.Write(snapshot, destination);
        }

        private bool IsReportTargetBusy(Nest nest) =>
            Application
                .OpenForms.OfType<EditNestForm>()
                .Where(form => ReferenceEquals(form.Nest, nest))
                .Select(form => form.PlateView)
                .Any(view =>
                    !view.IsDisposed
                    && (view.IsFillInProgress || view.Actions.CurrentAction?.IsBusy() == true)
                );

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

        public async Task ShowNestInfoEditorAsync()
        {
            var form = new EditNestInfoForm { AllowRename = NestStorage.Settings.IsDatabaseMode };
            form.LoadNestInfo(Nest);
            _ = ShowReleaseOriginAsync(form);

            while (form.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                // A declined duplicate name reopens the editor with the user's edits intact.
                if (form.NestName != Nest.Name && !await ConfirmRenameAsync(form.NestName))
                    continue;

                PlateManager.ChangePlateDefaults(() => form.SaveNestInfo(Nest));
                Text = Nest.Name;
                drawingListBox1.Units = Nest.Units;
                drawingListBox1.Invalidate();
                return;
            }
        }

        /// <summary>
        /// Shows which drawing set release made this nest, when it is a server nest that came
        /// from one. Best effort and in the background: Nest Info never waits for the server.
        /// </summary>
        private async Task ShowReleaseOriginAsync(EditNestInfoForm form)
        {
            var serverUrl = NestStorage.Settings.ServerUrl;
            if (!NestStorage.Settings.IsDatabaseMode || Document.RemoteId == Guid.Empty || Document.IsFirstDatabaseSave(serverUrl))
                return;

            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var release = await NestStorage.CreateDrawingSetStore().GetReleaseForNestAsync(Document.RemoteId, timeout.Token);
                if (release != null && !form.IsDisposed)
                    form.ReleaseOrigin = SetReleaseText.Origin(release);
            }
            catch (Exception ex) when (ex is IOException or System.Net.Http.HttpRequestException
                or OperationCanceledException or ArgumentException or System.Text.Json.JsonException)
            {
                // An older server, or none reachable: the dialog simply shows no origin.
            }
        }

        /// <summary>Renames go up with the next Save; warn now about a duplicate name.</summary>
        private async Task<bool> ConfirmRenameAsync(string name)
        {
            INestRepository repository;
            try
            {
                repository = NestStorage.CreateRepository();
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException)
            {
                return true; // No usable server address; Save reports that itself.
            }

            var duplicateAccepted = await NestRenameConfirmation.ConfirmAsync(this, repository, name, Document.RemoteId);
            if (duplicateAccepted == true)
                Document.AcceptedDuplicateName = name; // So the first Save does not ask again.
            return duplicateAccepted != false;
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
            PlateManager.ChangePlateDefaults(() => Nest.PlateDefaults.SetFromExisting(PlateView.Plate));
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
                PlateManager.ChangePlateDefaults(() => Nest.PlateDefaults.SetFromExisting(PlateView.Plate));
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

        public void ClearNest()
        {
            PlateManager.ClearAll();
        }

        public void AutoSequenceCurrentPlate()
        {
            SequencePlate(PlateView.Plate);
            PlateView.Invalidate();
        }

        public void AutoSequenceAllPlates()
        {
            var parameters = new SequenceParameters { Method = SequenceMethod.LeastCode };
            PlateSequencing.ApplyAll(Nest.Plates, parameters);
            PlateView.Invalidate();
        }

        private static void SequencePlate(Plate plate)
        {
            var parameters = new SequenceParameters { Method = SequenceMethod.LeastCode };
            PlateSequencing.Apply(plate, parameters);
        }

        public void CalculateCurrentPlateCutTime()
        {
            var cutParamsForm = new CutParametersForm();
            cutParamsForm.Units = Nest.Units;

            if (cutParamsForm.ShowDialog() == DialogResult.OK)
            {
                var cutparams = cutParamsForm.GetCutParameters();
                var info = Timing.GetTimingInfo(PlateView.Plate);
                var time = Timing.CalculateTime(PlateView.Plate, cutparams);

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

        public void SimulateCutting()
        {
            if (cutSimulatorForm is { IsDisposed: false })
            {
                cutSimulatorForm.Activate();
                return;
            }

            cutSimulatorForm = new CutSimulatorForm(this);
            cutSimulatorForm.FormClosed += (_, _) => cutSimulatorForm = null;
            var owner = MdiParent ?? (Form)this;
            var corner = owner.PointToScreen(new Point(owner.ClientSize.Width, owner.ClientSize.Height));
            cutSimulatorForm.Show(owner);
            cutSimulatorForm.Location = new Point(
                System.Math.Max(owner.Left, corner.X - cutSimulatorForm.Width - 24),
                System.Math.Max(owner.Top, corner.Y - cutSimulatorForm.Height - 48));
        }

        public void CalculateNestCutTime()
        {
            var cutParamsForm = new CutParametersForm();
            cutParamsForm.Units = Nest.Units;

            if (cutParamsForm.ShowDialog() == DialogResult.OK)
            {
                var cutparams = cutParamsForm.GetCutParameters();
                var info = Timing.GetTimingInfo(Nest);
                var time = Timing.CalculateTime(Nest, cutparams);

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

            // Display-only numbering: the trailing empty new-plate sentinel is excluded from the
            // total (and labeled as such); navigation and storage indexes are untouched.
            plateInfoLabel.Text = PlateDisplayNumbering.FormatHeader(
                Nest.Plates, PlateManager.CurrentIndex, plate.Size.ToString(), plate.Quantity);

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
            if (CuttingInspection != null)
            {
                CuttingInspection.RequestClose();
                if (CuttingInspection != null)
                {
                    closeAfterCuttingInspection = true;
                    e.Cancel = true;
                    return;
                }
            }
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

            var assigner = new LeadInAssigner { Sequencer = new LeftSideSequencer() };
            var kept = assigner.Assign(plate);

            foreach (var lp in PlateView.Parts)
                lp.IsDirty = true;

            PlateView.Invalidate();
            ShowKeptDrawingLeadIns([(Nest.Plates.IndexOf(plate) + 1, PartNumbers(plate, kept))]);
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

            var assigner = new LeadInAssigner { Sequencer = new LeftSideSequencer() };
            var kept = new List<(int, IReadOnlyList<int>)>();

            for (var index = 0; index < Nest.Plates.Count; index++)
            {
                var plate = Nest.Plates[index];
                plate.CuttingParameters = parameters;
                kept.Add((index + 1, PartNumbers(plate, assigner.Assign(plate))));
            }

            PlateView.Invalidate();
            ShowKeptDrawingLeadIns(kept);
        }

        // Assign Lead-ins never overwrites drawing pins; say which parts it left for Plan Cutting.
        private void ShowKeptDrawingLeadIns(IReadOnlyList<(int PlateNumber, IReadOnlyList<int> PartNumbers)> kept)
        {
            var message = LeadInAssigner.KeptMessage(kept);
            if (message != null)
                MessageBox.Show(this, message, "Assign Lead-ins", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static IReadOnlyList<int> PartNumbers(Plate plate, IReadOnlyList<Part> parts) =>
            parts.Select(part => plate.Parts.IndexOf(part) + 1).Order().ToList();

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
                try
                {
                    return CuttingParametersSerializer.Deserialize(json);
                }
                catch
                { /* fall through */
                }
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
            if (drawings.Count == 0)
                return;

            drawings.ForEach(d => Nest.Drawings.Add(d));
            UpdateDrawingList();
        }

        private void DrawingLibrary_Click(object sender, EventArgs e)
        {
            AddFromDrawingLibrary();
        }

        /// <summary>The library is on the nest server, so it is offered in Database mode only.</summary>
        public void UpdateDrawingLibraryCommand()
        {
            var enabled = NestStorage.Settings.IsDatabaseMode;
            drawingLibraryButton.Enabled = enabled;
            drawingLibraryButton.ToolTipText = enabled ? "Add from Drawing Library" : NestStorage.DatabaseModeOnly;
            drawingListBox1.SaveToLibraryUnavailableReason = enabled ? null : NestStorage.DatabaseModeOnly;
        }

        /// <summary>Opens the drawing library browser and lists whatever it added.</summary>
        public void AddFromDrawingLibrary()
        {
            if (!NestStorage.Settings.IsDatabaseMode)
                return;

            IDrawingLibraryStore library;
            IDrawingSetStore sets;
            try
            {
                library = NestStorage.CreateDrawingLibrary();
                sets = NestStorage.CreateDrawingSetStore();
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show(this, ex.Message, "Drawing Library", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            using var form = new DrawingLibraryForm(library, NestStorage.Settings.ServerUrl, Nest, sets);
            // Released nests open in their own tabs beside this one.
            if (MdiParent is MainForm main)
                form.EnableRelease(NestStorage.CreateRepository(), main.OpenServerNest);
            form.ShowDialog(this);
            if (form.AddedDrawings.Count > 0)
                UpdateDrawingList();
        }

        private void drawingListBox1_SaveToLibraryRequested(object sender, Drawing drawing)
        {
            SaveDrawingToLibrary(drawing);
        }

        /// <summary>
        /// Saves one nest drawing to the drawing library as a new drawing or a revision of a
        /// matching one. On success only the drawing's library link changes.
        /// </summary>
        public void SaveDrawingToLibrary(Drawing drawing)
        {
            if (drawing == null || !NestStorage.Settings.IsDatabaseMode)
                return;

            IDrawingLibraryStore library;
            try
            {
                library = NestStorage.CreateDrawingLibrary();
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show(this, ex.Message, "Drawing Library", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var lastCutType = Enum.TryParse<CutType>(Properties.Settings.Default.DrawingLibraryCutType, out var cutType)
                && Enum.IsDefined(cutType) ? cutType : CutType.Laser;
            using var form = new SaveToDrawingLibraryForm(library, NestStorage.Settings.ServerUrl, Nest, drawing,
                lastCutType, Environment.UserName);
            if (form.ShowDialog(this) != DialogResult.OK || form.Result == null)
                return;

            Properties.Settings.Default.DrawingLibraryCutType = form.Result.Saved.CutType.ToString();
            Properties.Settings.Default.Save();
            UpdateDrawingList();
        }

        private async void drawingListBox1_AddToSetRequested(object sender, EventArgs e)
        {
            await AddLibraryDrawingsToSetAsync();
        }

        /// <summary>
        /// Adds the nest's library drawings, with their required quantities, to a chosen or new
        /// drawing set, which the operator reviews and saves in the set editor.
        /// </summary>
        public async Task AddLibraryDrawingsToSetAsync()
        {
            if (addingToSet || !NestStorage.Settings.IsDatabaseMode)
                return;

            IDrawingLibraryStore library;
            IDrawingSetStore sets;
            try
            {
                library = NestStorage.CreateDrawingLibrary();
                sets = NestStorage.CreateDrawingSetStore();
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show(this, ex.Message, "Drawing Sets", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            addingToSet = true;
            try
            {
                await new NestDrawingsToSet(this, sets, library, NestStorage.Settings.ServerUrl).RunAsync(Nest);
            }
            finally
            {
                addingToSet = false;
            }
        }

        private void EditDrawingsInConverter_Click(object sender, EventArgs e)
        {
            if (Nest.Drawings.Count == 0)
                return;

            EditDrawingsInConverter(Nest.Drawings);
        }

        private void drawingListBox1_OpenInConverterRequested(object sender, Drawing drawing)
        {
            EditDrawingsInConverter(new[] { drawing });
        }

        private void EditDrawingsInConverter(IEnumerable<Drawing> drawings)
        {
            var editedDrawings = drawings.ToList();

            // Capture before loading the editor: its conversion can mutate programs in place.
            var snapshot = DrawingProgramSnapshot.Capture(
                editedDrawings,
                program => NestWriter.GetProgramText(program) + "\0" + NestWriter.GetSubProgramsText(program)
            );
            var converter = new CadConverterForm();
            // LoadDrawings can edit live programs even when the dialog is canceled.
            PlateView.InvalidateOverlapCheck();
            converter.LoadDrawings(editedDrawings);

            if (converter.ShowDialog() != DialogResult.OK)
                return;

            var newDrawings = converter.GetDrawings();
            var newByName = newDrawings.ToDictionary(d => d.Name);

            // Update existing drawings in-place so parts keep their BaseDrawing references
            foreach (var existing in editedDrawings)
            {
                if (newByName.TryGetValue(existing.Name, out var updated))
                {
                    CadImporter.ApplyEdit(existing, updated);
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

            // Leave unchanged parts' lead-ins, tabs, locks and program instances intact.
            var updatedParts = snapshot.UpdateChangedParts(Nest.Plates).ToHashSet();
            foreach (var layoutPart in PlateView.Parts)
            {
                if (!updatedParts.Contains(layoutPart.BasePart))
                    continue;

                layoutPart.IsDirty = true;
                layoutPart.InvalidateOffset();
            }

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
                MessageBoxDefaultButton.Button1
            );

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

        private void PlateView_PartsReordered(object sender, EventArgs e)
        {
            updateDrawingListTimer.Stop();
            updateDrawingListTimer.Start();
        }

        private void drawingListUpdateTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (!drawingListBox1.IsHandleCreated)
                return;

            drawingListBox1.Invoke(
                new MethodInvoker(() =>
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

                    // Parts were added or removed: rebuild the plate rows so the
                    // part-count and utilization columns stay current. The debounce
                    // timer collapses a fill's many per-part events into one refresh.
                    UpdatePlateList();
                })
            );
        }

        private void drawingListBox1_DoubleClick(object sender, EventArgs e)
        {
            addPart = false;

            var drawing = drawingListBox1.SelectedItem as Drawing;

            if (drawing == null)
                return;

            var form = new EditDrawingForm();
            form.LoadDrawing(drawing, Nest);

            if (form.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                form.SaveDrawing(drawing);

                RefreshDrawingColor(drawing);

                UpdateDrawingList();
                PlateView.Invalidate();
            }
        }

        internal void RefreshDrawingColor(Drawing drawing)
        {
            // Metadata edits only refresh color; never rebuild the placed part program.
            foreach (var layoutPart in PlateView.Parts)
                if (ReferenceEquals(layoutPart.BasePart.BaseDrawing, drawing))
                    layoutPart.Update();
        }

        private void drawingListBox1_DeleteRequested(object sender, Drawing drawing)
        {
            var result = MessageBox.Show(
                $"Delete drawing '{drawing.Name}' and all its parts from every plate?",
                "Delete Drawing",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2
            );

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
            UpdatePlateList();
            PlateView.Invalidate();
        }

        private void drawingListBox1_DrawingPickedUp(object sender, Drawing drawing)
        {
            // Armed on the press, so moving the mouse before release still
            // attaches the part when the pointer reaches the plate.
            addPart = true;
        }

        private void drawingListBox1_DrawingDropped(object sender, Drawing drawing)
        {
            // The drop already placed the part; don't attach another copy.
            addPart = false;
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
