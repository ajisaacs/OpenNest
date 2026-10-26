using System.Windows.Forms;
using OpenNest.Actions;
using OpenNest.Controls;
using OpenNest.Engine;
using OpenNest.Engine.Strategies;
using OpenNest.Forms;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests.Forms;

// Each desktop edit records one labelled undo entry and Undo puts the nest back. Reads
// Application.OpenForms through the busy guards, so it must not overlap fill-lifetime tests.
[Collection("Fill operation lifetime")]
public class UndoRecordingTests
{
    // Placement

    [Fact]
    public void AFillRecordsOneEntryAtItsCommit() => UndoTestEnv.Run(env =>
    {
        env.Editor.PlateManager.LoadAt(2);
        var square = env.Nest.Drawings.Single();
        using var fill = UseFill(context => [new Part(context.Item.Drawing, context.WorkArea.Location)]);

        env.AssertOneUndoableEdit("Fill", () =>
        {
            env.View.FillWithProgress([new Part(square)], env.View.Plate.WorkArea());
            WaitForFill(env);
        });
    });

    [Fact]
    public void AFillThatPlacesNothingRecordsNothing() => UndoTestEnv.Run(env =>
    {
        env.Editor.PlateManager.LoadAt(2);
        using var fill = UseFill(_ => []);

        env.View.FillWithProgress([new Part(env.Nest.Drawings.Single())], env.View.Plate.WorkArea());
        WaitForFill(env);

        Assert.Equal(0, env.Editor.History.UndoCount);
    });

    [Fact]
    public void FillAreaRecordsOneEntry() => UndoTestEnv.Run(env =>
    {
        env.Editor.PlateManager.LoadAt(2);
        using var fill = UseFill(context => [new Part(context.Item.Drawing, context.WorkArea.Location)]);
        env.View.SetAction(typeof(ActionFillArea), env.Nest.Drawings.Single());
        var action = (ActionFillArea)env.View.Actions.CurrentAction;
        UndoTestEnv.SetBackingField(action, "SelectedArea", new Box(0, 0, 50, 50));

        env.AssertOneUndoableEdit("Fill Area", () => UndoTestEnv.Invoke(action, "FillArea"));
    });

    [Theory]
    [InlineData(PatternTileTarget.CurrentPlate)]
    [InlineData(PatternTileTarget.NewPlate)]
    public void PatternTileRecordsOneEntry(PatternTileTarget target) => UndoTestEnv.Run(env =>
    {
        env.Editor.PlateManager.LoadAt(0);
        var square = env.Nest.Drawings.Single();
        var result = new PatternTileResult
        {
            Target = target,
            PlateSize = new Geometry.Size(60, 60),
            Parts = [new Part(square, new Vector(1, 1)), new Part(square, new Vector(12, 1)), new Part(square, new Vector(23, 1))],
        };

        env.AssertOneUndoableEdit("Pattern Tile", () => MainForm.ApplyPatternTile(env.Editor, result));
    });

    [Fact]
    public void ClearNestRecordsOneEntry() => UndoTestEnv.Run(env =>
        env.AssertOneUndoableEdit("Clear Nest", env.Editor.ClearNest));

    [Fact]
    public void RemoveEmptyPlatesRecordsOneEntry() => UndoTestEnv.Run(env =>
        env.AssertOneUndoableEdit("Remove Empty Plates", env.Editor.RemoveEmptyPlates));

    [Fact]
    public void CenterPartsRecordsOneEntry() => UndoTestEnv.Run(env =>
    {
        env.Editor.PlateManager.LoadAt(0);
        env.AssertOneUndoableEdit("Center Parts", () => env.Click("CenterParts_Click"));
    });

    // Plate

    [Theory]
    [InlineData(nameof(EditNestForm.RotateCw))]
    [InlineData(nameof(EditNestForm.RotateCcw))]
    [InlineData(nameof(EditNestForm.Rotate180))]
    public void RotatingAPlateRecordsOneEntry(string command) => UndoTestEnv.Run(env =>
    {
        env.Editor.PlateManager.LoadAt(0);
        env.AssertOneUndoableEdit("Rotate Plate", () => UndoTestEnv.Invoke(env.Editor, command));
    }, () =>
    {
        var nest = UndoTestEnv.BuildNest();
        nest.Plates[0].Size = new Geometry.Size(60, 100);
        return nest;
    });

    [Fact]
    public void ResizingAPlateToItsPartsRecordsOneEntry() => UndoTestEnv.Run(env =>
    {
        env.Editor.PlateManager.LoadAt(0);
        env.AssertOneUndoableEdit("Resize Plate", env.Editor.ResizePlateToFitParts);
    });

    [Fact]
    public void EditPlateRecordsOneEntryOnOk() => UndoTestEnv.Run(env =>
    {
        env.Editor.PlateManager.LoadAt(0);
        UndoTestEnv.AcceptWhenShown<EditPlateForm>(form => form.Quantity = 3);
        env.AssertOneUndoableEdit("Edit Plate", env.Editor.EditPlate);
        Assert.Equal(3, env.Nest.Plates[0].Quantity);
    });

    [Fact]
    public void CancellingEditPlateRecordsNothing() => UndoTestEnv.Run(env =>
    {
        env.Editor.PlateManager.LoadAt(2);
        UndoTestEnv.WhenShown<EditPlateForm>(form => form.DialogResult = DialogResult.Cancel);
        var before = UndoTestEnv.Describe(env.Nest);

        env.Editor.EditPlate();

        Assert.Equal(0, env.Editor.History.UndoCount);
        Assert.Equal(before, UndoTestEnv.Describe(env.Nest));
    });

    [Fact]
    public void SettingTheNestDefaultPlateRecordsOneEntry() => UndoTestEnv.Run(env =>
    {
        env.Editor.PlateManager.LoadAt(0);
        env.AssertOneUndoableEdit("Set Nest Default Plate", env.Editor.SetCurrentPlateAsNestDefault);
    });

    [Fact]
    public void TheClampsDialogRecordsOneEntry() => UndoTestEnv.Run(env =>
    {
        UndoTestEnv.AcceptWhenShown<ClampsForm>(form =>
            Assert.True(form.Editor.TryAddClamp(new RailClamp(ClampEdge.OriginSide, 20), out _)));
        env.AssertOneUndoableEdit("Clamps", () => env.Click("Clamps_Click"));
    });

    [Fact]
    public void TheSupportGridDialogRecordsOneEntry() => UndoTestEnv.Run(env =>
    {
        UndoTestEnv.AcceptWhenShown<SupportGridForm>(form =>
            AllControls(form).OfType<CheckBox>().First(box => box.Text.StartsWith("This nest's sheets")).Checked = true);
        env.AssertOneUndoableEdit("Support Grid", () => env.Click("SupportGrid_Click"));
    });

    [Fact]
    public void RemovingAndPuttingBackAClampRecordOneEntryEach() => UndoTestEnv.Run(env =>
    {
        env.Editor.PlateManager.LoadAt(0);
        var plate = env.View.Plate;
        var id = plate.ClampZones[0].Id;

        env.AssertOneUndoableEdit("Remove Clamp", () => UndoTestEnv.Invoke(env.View.ClampMenu, "Apply", plate, id, true));
        env.AssertOneUndoableEdit("Put Clamp Back", () => UndoTestEnv.Invoke(env.View.ClampMenu, "Apply", env.View.Plate, id, false));
    }, NestWithClamps);

    [Fact]
    public void EditingClampsOnThePlateRecordsOneEntryPerEdit() => UndoTestEnv.Run(env =>
    {
        env.Editor.PlateManager.LoadAt(0);
        env.View.SetAction(typeof(ActionClampEdit));
        var action = env.View.Actions.CurrentAction;
        var first = new RailClamp(ClampEdge.OriginSide, 20);
        var added = new RailClamp(ClampEdge.OriginSide, 50);

        env.AssertOneUndoableEdit("Add Clamp", () => UndoTestEnv.Invoke(action, "Add", added));
        env.View.SetAction(typeof(ActionClampEdit));
        action = env.View.Actions.CurrentAction;
        env.AssertOneUndoableEdit("Move Clamp", () => UndoTestEnv.Invoke(action, "Move", first, new RailClamp(ClampEdge.OriginSide, 30)));
        env.View.SetAction(typeof(ActionClampEdit));
        action = env.View.Actions.CurrentAction;
        env.AssertOneUndoableEdit("Delete Clamp", () => UndoTestEnv.Invoke(action, "Delete", added));
    }, NestWithClamps);

    // Cutting

    [Fact]
    public void PlacingACutOffRecordsOneEntry() => UndoTestEnv.Run(env =>
    {
        env.Editor.PlateManager.LoadAt(0);
        env.View.ZoomToFit();
        env.View.SetAction(typeof(ActionCutOff));
        var at = env.View.PointWorldToControl(new Vector(70, 50));
        UndoTestEnv.Mouse(env.View, "OnMouseMove", MouseButtons.None, at);

        env.AssertOneUndoableEdit("Add Cut-Off", () => UndoTestEnv.Mouse(env.View, "OnMouseDown", MouseButtons.Left, at));
    });

    [Fact]
    public void DraggingACutOffRecordsOneEntry() => UndoTestEnv.Run(env =>
    {
        env.Editor.PlateManager.LoadAt(0);
        env.View.ZoomToFit();
        var grab = env.View.PointWorldToControl(new Vector(70, 60));

        env.AssertOneUndoableEdit("Move Cut-Off", () =>
        {
            UndoTestEnv.Mouse(env.View, "OnMouseMove", MouseButtons.None, grab);
            UndoTestEnv.Mouse(env.View, "OnMouseDown", MouseButtons.Left, grab);
            for (var step = 1; step <= 4; step++)
                UndoTestEnv.Mouse(env.View, "OnMouseMove", MouseButtons.Left, new System.Drawing.Point(grab.X + step * 5, grab.Y));
            UndoTestEnv.Mouse(env.View, "OnMouseUp", MouseButtons.Left, new System.Drawing.Point(grab.X + 20, grab.Y));
        });
        Assert.Equal(1, env.Editor.History.UndoCount);
    }, NestWithCutOff);

    [Fact]
    public void DeletingASelectedCutOffRecordsOneEntry() => UndoTestEnv.Run(env =>
    {
        env.Editor.PlateManager.LoadAt(0);
        env.View.Selection.SelectedCutOffs.Add(env.View.Plate.CutOffs[0]);
        env.AssertOneUndoableEdit("Delete", env.View.Selection.DeleteSelected);
    }, NestWithCutOff);

    [Fact]
    public void AutomaticCutOffsRecordOneEntry() => UndoTestEnv.Run(env =>
    {
        env.Editor.PlateManager.LoadAt(0);
        using var form = new AutomaticCutOffForm(env.View, env.Nest.Units);
        env.AssertOneUndoableEdit("Automatic Cut-Offs", () => UndoTestEnv.Invoke(form, "ApplyButton_Click", null, EventArgs.Empty));
    });

    [Fact]
    public void AutoSequencingRecordsOneEntry() => UndoTestEnv.Run(env =>
    {
        env.Editor.PlateManager.LoadAt(0);
        env.AssertOneUndoableEdit("Auto Sequence", env.Editor.AutoSequenceCurrentPlate);
        env.AssertOneUndoableEdit("Auto Sequence All Plates", env.Editor.AutoSequenceAllPlates);
    }, NestOutOfOrder);

    [Fact]
    public void SetSequenceRecordsOneEntryPerClick() => UndoTestEnv.Run(env =>
    {
        env.Editor.PlateManager.LoadAt(0);
        env.View.ZoomToFit();
        env.View.SetAction(typeof(ActionSetSequence));
        var action = env.View.Actions.CurrentAction;
        var second = env.View.PointWorldToControl(env.View.Plate.Parts[1].BoundingBox.Center);

        env.AssertOneUndoableEdit("Set Sequence", () => UndoTestEnv.Invoke(action, "plateView_MouseClick", env.View,
            new MouseEventArgs(MouseButtons.Left, 1, second.X, second.Y, 0)));
        env.View.EndAction();
    });

    [Fact]
    public void SequenceByPathRecordsTheWholeSessionAsOneEntry() => UndoTestEnv.Run(env =>
    {
        env.Editor.PlateManager.LoadAt(0);
        env.View.ZoomToFit();
        var parts = env.View.Plate.Parts;
        var first = env.View.PointWorldToControl(parts[1].BoundingBox.Center);
        var second = env.View.PointWorldToControl(parts[0].BoundingBox.Center);

        env.AssertOneUndoableEdit("Sequence by Path", () =>
        {
            env.View.SetAction(typeof(ActionSequenceByPath));
            var action = env.View.Actions.CurrentAction!;
            Click(env.View, first);
            Click(env.View, second);
            // A line taken back inside the tool is the tool's own step, not the nest's.
            Assert.True(action.TryUndoStep());
            Click(env.View, second);
            Assert.Equal(0, env.Editor.History.UndoCount);
            env.View.EndAction();
        });
    });

    [Fact]
    public void PlacingALeadInRecordsOneEntry() => UndoTestEnv.Run(env =>
    {
        env.Editor.PlateManager.LoadAt(3);
        env.View.ZoomToFit();
        var part = env.View.Plate.Parts[0];
        env.Editor.PlaceLeadIn_Click(null!, EventArgs.Empty);
        Assert.Equal(0, env.Editor.History.UndoCount); // Opening the tool changes nothing.
        env.AssertOneUndoableEdit("Place Lead-In", () =>
        {
            PlaceLeadIn(env);
            Assert.True(part.HasManualLeadIns);
        });
        env.View.EndAction();
    });

    [Fact]
    public void TheLeadInToolRecordsItsMenuRemovalAndAParameterChange() => UndoTestEnv.Run(env =>
    {
        env.Editor.PlateManager.LoadAt(3);
        env.View.ZoomToFit();
        env.Editor.PlaceLeadIn_Click(null!, EventArgs.Empty);
        var action = env.View.Actions.CurrentAction!;
        var center = PlaceLeadIn(env);
        Assert.Equal("Place Lead-In", env.Editor.History.UndoLabel);
        Assert.True(action.IsBusy(), "The square is no longer picked.");

        env.AssertOneUndoableEdit("Remove Lead-Ins", () =>
        {
            UndoTestEnv.Invoke(action, "ShowContextMenu", center);
            var menu = (ContextMenuStrip)action.GetType().GetField("contextMenu", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(action)!;
            menu.Items[0].PerformClick();
            menu.Close();
        });

        // Undo ended the tool; a changed panel is recorded when the tool closes.
        env.Editor.PlaceLeadIn_Click(null!, EventArgs.Empty);
        action = env.View.Actions.CurrentAction!;
        var panel = (CuttingPanel)action.GetType().GetField("cuttingPanel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(action)!;
        panel.LoadFromParameters(new CNC.CuttingStrategy.CuttingParameters
        {
            ExternalLeadIn = new CNC.CuttingStrategy.LineLeadIn { Length = 0.75, ApproachAngle = 30 },
        });
        env.AssertOneUndoableEdit("Cutting Parameters", env.View.EndAction);
    });

    [Fact]
    public void ClosingTheLeadInToolUnchangedRecordsNothing() => UndoTestEnv.Run(env =>
    {
        env.Editor.PlateManager.LoadAt(0);
        env.Editor.PlaceLeadIn_Click(null!, EventArgs.Empty);
        env.View.EndAction();
        Assert.Equal(0, env.Editor.History.UndoCount);
    }, NestWithLeadIns);

    [Fact]
    public void RemovingLeadInsRecordsOneEntry() => UndoTestEnv.Run(env =>
    {
        env.Editor.PlateManager.LoadAt(0);
        env.AssertOneUndoableEdit("Remove Lead-Ins", () => env.Editor.RemoveLeadIns_Click(null!, EventArgs.Empty));
        env.AssertOneUndoableEdit("Remove Lead-Ins", env.Editor.RemoveLeadInsAllPlates);
    }, NestWithLeadIns);

    [Fact]
    public void ApplyingACuttingPlanRecordsOneEntry() => UndoTestEnv.Run(env =>
    {
        env.Editor.PlateManager.LoadAt(0);
        env.AssertOneUndoableEdit("Plan Cutting", () =>
        {
            env.Editor.OpenCuttingInspection();
            var panel = env.Editor.CuttingInspection!;
            PumpUntil(() => panel.Proposal != null && !panel.IsPlanning);
            UndoTestEnv.Invoke(panel, "Apply");
            PumpUntil(() => env.Editor.CuttingInspection == null);
        });
    });

    [Fact]
    public void ClosingACuttingPlanWithoutApplyingRecordsNothing() => UndoTestEnv.Run(env =>
    {
        env.Editor.PlateManager.LoadAt(0);
        var before = UndoTestEnv.Describe(env.Nest);
        env.Editor.OpenCuttingInspection();
        var panel = env.Editor.CuttingInspection!;
        PumpUntil(() => panel.Proposal != null && !panel.IsPlanning);
        panel.RequestClose();
        PumpUntil(() => env.Editor.CuttingInspection == null);

        Assert.Equal(0, env.Editor.History.UndoCount);
        Assert.Equal(before, UndoTestEnv.Describe(env.Nest));
    });

    // Picks the current plate's first part, locks its left edge and places a lead-in there;
    // returns the pick point.
    private static System.Drawing.Point PlaceLeadIn(UndoTestEnv env)
    {
        var box = env.View.Plate.Parts[0].BoundingBox;
        var center = env.View.PointWorldToControl(box.Center);
        var edge = env.View.PointWorldToControl(new Vector(box.Left, box.Center.Y));
        UndoTestEnv.Mouse(env.View, "OnMouseMove", MouseButtons.None, center);
        UndoTestEnv.Mouse(env.View, "OnMouseDown", MouseButtons.Left, center);
        UndoTestEnv.Mouse(env.View, "OnMouseMove", MouseButtons.None, edge);
        UndoTestEnv.Mouse(env.View, "OnMouseDown", MouseButtons.Left, edge);
        UndoTestEnv.Mouse(env.View, "OnMouseMove", MouseButtons.None, edge);
        UndoTestEnv.Mouse(env.View, "OnMouseDown", MouseButtons.Left, edge);
        return center;
    }

    // Drawings

    [Fact]
    public void ImportingDrawingsRecordsOneEntry() => UndoTestEnv.Run(env =>
    {
        var directory = Directory.CreateTempSubdirectory("opennest-undo-");
        try
        {
            var path = Path.Combine(directory.FullName, "plate.dxf");
            var document = new ACadSharp.CadDocument();
            var corners = new[] { (0.0, 0.0), (8.0, 0.0), (8.0, 4.0), (0.0, 4.0) };
            for (var i = 0; i < corners.Length; i++)
            {
                var (x1, y1) = corners[i];
                var (x2, y2) = corners[(i + 1) % corners.Length];
                document.Entities.Add(new ACadSharp.Entities.Line(new CSMath.XYZ(x1, y1, 0), new CSMath.XYZ(x2, y2, 0)));
            }
            ACadSharp.IO.DxfWriter.Write(path, document, false);
            UndoTestEnv.AcceptWhenShown<CadConverterForm>(ready: form => form.GetDrawings().Count > 0);

            env.AssertOneUndoableEdit("Import Drawings", () => env.Editor.ImportFiles([path]));
            Assert.Equal(2, env.Nest.Drawings.Count);
        }
        finally
        {
            directory.Delete(true);
        }
    });

    [Fact]
    public void AddingFromTheShapeLibraryRecordsOneEntry() => UndoTestEnv.Run(env =>
    {
        UndoTestEnv.WhenShown<ShapeLibraryForm>(form => UndoTestEnv.Invoke(form, "AddButton_Click", null, EventArgs.Empty));
        env.AssertOneUndoableEdit("Shape Library", () => env.Click("ShapeLibrary_Click"));
    });

    [Fact]
    public void EditingDrawingsInTheConverterRecordsOneEntry() => UndoTestEnv.Run(env =>
    {
        UndoTestEnv.AcceptWhenShown<CadConverterForm>(form =>
            ((System.Windows.Forms.NumericUpDown)AllControls(form).Single(c => c.Name == "numQuantity")).Value = 7);
        env.AssertOneUndoableEdit("Edit Drawings", () => UndoTestEnv.Invoke(env.Editor, "EditDrawingsInConverter", env.Nest.Drawings.ToList()));
        Assert.Equal(7, env.Nest.Drawings.Single().Quantity.Required);
    }, NestWithRequiredQuantity);

    [Fact]
    public void CancellingTheConverterRecordsNothing() => UndoTestEnv.Run(env =>
    {
        var before = UndoTestEnv.Describe(env.Nest);
        UndoTestEnv.WhenShown<CadConverterForm>(form => form.DialogResult = DialogResult.Cancel);

        UndoTestEnv.Invoke(env.Editor, "EditDrawingsInConverter", env.Nest.Drawings.ToList());

        Assert.Equal(0, env.Editor.History.UndoCount);
        Assert.Equal(before, UndoTestEnv.Describe(env.Nest));
    }, NestWithRequiredQuantity);

    // The converter's quantity box starts at 1, so it cannot load a drawing that requires none.
    private static Nest NestWithRequiredQuantity()
    {
        var nest = UndoTestEnv.BuildNest();
        nest.Drawings.Single().Quantity.Required = 5;
        return nest;
    }

    [Fact]
    public void DeletingADrawingRecordsOneEntry() => UndoTestEnv.Run(env =>
        env.AssertOneUndoableEdit("Delete Drawing", () => env.Editor.DeleteDrawing(env.Nest.Drawings.Single())));

    [Fact]
    public void CleaningUnusedDrawingsRecordsOneEntry() => UndoTestEnv.Run(env =>
        env.AssertOneUndoableEdit("Clean Unused Drawings", env.Editor.CleanUnusedDrawings), () =>
    {
        var nest = UndoTestEnv.BuildNest();
        nest.Drawings.Add(new Drawing("unused", UndoTestEnv.Square(4)));
        return nest;
    });

    [Fact]
    public void EditingDrawingPropertiesRecordsOneEntry() => UndoTestEnv.Run(env =>
    {
        var list = (DrawingListBox)AllControls(env.Editor).Single(c => c is DrawingListBox);
        list.SelectedItem = env.Nest.Drawings.Single();
        UndoTestEnv.AcceptWhenShown<EditDrawingForm>(form =>
            typeof(EditDrawingForm).GetProperty("Quantity", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(form, 9));

        env.AssertOneUndoableEdit("Drawing Properties", () => UndoTestEnv.Invoke(env.Editor, "drawingListBox1_DoubleClick", list, EventArgs.Empty));
        Assert.Equal(9, env.Nest.Drawings.Single().Quantity.Required);
    });

    [Fact]
    public void DroppingADrawingOnThePlateRecordsOneEntry() => UndoTestEnv.Run(env =>
    {
        env.Editor.PlateManager.LoadAt(2);
        env.View.ZoomToFit();
        var at = env.View.PointToScreen(env.View.PointWorldToControl(new Vector(50, 50)));
        var drop = new DragEventArgs(new DataObject(env.Nest.Drawings.Single()), 0, at.X, at.Y, DragDropEffects.Copy, DragDropEffects.Copy);

        env.AssertOneUndoableEdit("Add Part", () => UndoTestEnv.Invoke(env.View, "OnDragDrop", drop));
    });

    // Nest

    [Fact]
    public void AssigningAMachineRecordsOneEntry() => UndoTestEnv.Run(env =>
    {
        var machine = new Data.MachineConfig { Name = "Undo test laser" };
        env.AssertOneUndoableEdit("Machine", () => UndoTestEnv.Invoke(env.Host, "AssignMachine", machine));
        Assert.Equal(machine.Id, env.Nest.Machine!.Id);
    });

    [Fact]
    public void EditingNestInfoRecordsOneEntry() => UndoTestEnv.Run(env =>
    {
        UndoTestEnv.AcceptWhenShown<EditNestInfoForm>(form =>
        {
            form.Notes = "edited";
            form.Customer = "someone";
        });
        env.AssertOneUndoableEdit("Nest Info", () => Assert.True(env.Editor.ShowNestInfoEditorAsync().IsCompleted));
        Assert.Equal("edited", env.Nest.Notes);
    });

    [Fact]
    public void ANestInfoEditOfOnlyItsIdentityRecordsNothing() => UndoTestEnv.Run(env =>
    {
        UndoTestEnv.AcceptWhenShown<EditNestInfoForm>(form =>
        {
            form.Status = NestStatus.HasBeenCut;
            form.MadeBy = "someone else";
        });

        Assert.True(env.Editor.ShowNestInfoEditorAsync().IsCompleted);

        Assert.Equal(NestStatus.HasBeenCut, env.Nest.Status);
        Assert.Equal(0, env.Editor.History.UndoCount);
    });

    private static void Click(PlateView view, System.Drawing.Point at)
    {
        UndoTestEnv.Mouse(view, "OnMouseMove", MouseButtons.None, at);
        UndoTestEnv.Mouse(view, "OnMouseDown", MouseButtons.Left, at);
        UndoTestEnv.Mouse(view, "OnMouseUp", MouseButtons.Left, at);
    }

    private static void PumpUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out.");
            Application.DoEvents();
            Thread.Sleep(5);
        }
    }

    private static Nest NestWithCutOff()
    {
        var nest = UndoTestEnv.BuildNest();
        nest.Plates[0].CutOffs.Add(new CutOff(new Vector(70, 0), CutOffAxis.Vertical));
        nest.Plates[0].RegenerateCutOffs(new CutOffSettings());
        return nest;
    }

    // Three squares in a row, cut middle, far, near: any sequencer reorders them.
    private static Nest NestOutOfOrder()
    {
        var nest = UndoTestEnv.BuildNest();
        var square = nest.Drawings.Single();
        foreach (var plate in nest.Plates.Take(2))
        {
            plate.Parts.Clear();
            foreach (var x in new[] { 35.0, 65.0, 5.0 })
                plate.Parts.Add(new Part(square, new Vector(x, 5)));
        }
        return nest;
    }

    private static Nest NestWithLeadIns()
    {
        var nest = UndoTestEnv.BuildNest();
        var parameters = new CNC.CuttingStrategy.CuttingParameters();
        foreach (var plate in nest.Plates.Where(p => p.Parts.Count > 0))
        {
            plate.CuttingParameters = parameters;
            foreach (var part in plate.Parts)
                part.ApplyLeadIns(parameters, new Vector(0, 0));
        }
        return nest;
    }

    private static Nest NestWithClamps()
    {
        var nest = UndoTestEnv.BuildNest();
        nest.SetClampRail(new ClampRail(1.5, 0.625, new[] { 20.0, 60.0 }));
        return nest;
    }

    private static IEnumerable<Control> AllControls(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(child => AllControls(child).Prepend(child));

    private static IDisposable UseFill(Func<FillContext, List<Part>> fill)
    {
        var prior = EngineSelection.EngineName;
        EngineSelection.EngineName = EngineSelection.DefaultEngineName;
        var strategies = FillStrategyRegistry.UseForTesting([new TestFill(fill)]);
        return new Restore(() =>
        {
            strategies.Dispose();
            EngineSelection.EngineName = prior;
        });
    }

    private static void WaitForFill(UndoTestEnv env)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (env.View.IsFillInProgress)
        {
            Assert.True(DateTime.UtcNow < deadline, "The fill did not finish.");
            Application.DoEvents();
            Thread.Sleep(5);
        }
    }

    private sealed class TestFill(Func<FillContext, List<Part>> fill) : IFillStrategy
    {
        public string Name => "Undo recording test fill";
        public NestPhase Phase => NestPhase.Linear;
        public int Order => 0;
        public List<Part> Fill(FillContext context) => fill(context);
    }

    private sealed class Restore(System.Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}
