using OpenNest.Geometry;
using OpenNest.Undo;

namespace OpenNest.Desktop.Tests.Undo;

public class NestUndoRecorderTests
{
    private static NestUndoRecorder Open(out Document document)
    {
        document = new Document { Nest = TestNests.OneSquare() };
        var recorder = new NestUndoRecorder(document);
        recorder.Reset();
        return recorder;
    }

    private static Vector FirstPartLocation(Document document) =>
        document.Nest.Plates[0].Parts[0].Location;

    [Fact]
    public void RecordedEdit_UndoesAndRedoesUnderItsLabel()
    {
        var recorder = Open(out var document);
        var start = FirstPartLocation(document);

        recorder.RecordChange("Move", () => document.Nest.Plates[0].Parts[0].Offset(5, 0));
        var moved = FirstPartLocation(document);

        Assert.Equal("Undo Move", recorder.UndoText);
        Assert.Equal("Redo", recorder.RedoText);
        Assert.True(recorder.CanUndo);

        recorder.Undo();

        Assert.Equal(start, FirstPartLocation(document));
        Assert.Equal("Undo", recorder.UndoText);
        Assert.Equal("Redo Move", recorder.RedoText);

        recorder.Redo();

        Assert.Equal(moved, FirstPartLocation(document));
        Assert.Equal("Undo Move", recorder.UndoText);
    }

    [Fact]
    public void Undo_SwapsTheRestoredCopyInThroughTheView_KeepingTheNestsIdentity()
    {
        var recorder = Open(out var document);
        var live = document.Nest;
        Nest swapped = null;
        var restoringDuringSwap = false;
        recorder.SwapIn = restored =>
        {
            restoringDuringSwap = recorder.IsRestoring && recorder.IsChangeInProgress;
            // Rebinding the view is not an edit.
            using (var change = recorder.BeginChange("Rebind"))
            {
                change.MarkChanged();
                change.Complete();
            }
            swapped = restored;
            document.Nest = restored;
        };
        var restoredEvents = 0;
        recorder.Restored += (_, _) => restoredEvents++;
        recorder.RecordChange("Move", () => live.Plates[0].Parts[0].Offset(5, 0));
        live.Name = "renamed";
        live.Status = NestStatus.HasBeenCut;

        recorder.Undo();

        Assert.True(restoringDuringSwap);
        Assert.NotSame(live, document.Nest);
        Assert.Same(swapped, document.Nest);
        Assert.Equal("renamed", document.Nest.Name);
        Assert.Equal(NestStatus.HasBeenCut, document.Nest.Status);
        Assert.Equal(1, restoredEvents);
        Assert.False(recorder.IsRestoring);
        Assert.Equal(0, recorder.History.UndoCount);
        Assert.Equal(1, recorder.History.RedoCount);
    }

    [Fact]
    public void ASwapInThatRefuses_LeavesTheLiveNest()
    {
        var recorder = Open(out var document);
        recorder.RecordChange("Move", () => document.Nest.Plates[0].Parts[0].Offset(5, 0));
        var live = document.Nest;
        recorder.SwapIn = _ => throw new InvalidOperationException("busy");

        Assert.Throws<InvalidOperationException>(recorder.Undo);

        Assert.Same(live, document.Nest);
        Assert.False(recorder.IsChangeInProgress);
    }

    [Fact]
    public void EditOutsideAScope_IsCommittedAsChangeBeforeUndo_SoRedoBringsItBack()
    {
        var recorder = Open(out var document);
        var start = FirstPartLocation(document);

        // An entry point that forgot its scope.
        document.Nest.Plates[0].Parts[0].Offset(3, 0);
        var moved = FirstPartLocation(document);

        Assert.True(recorder.HasUnrecordedChange);
        Assert.False(recorder.History.CanUndo);

        recorder.Undo();

        Assert.Equal(start, FirstPartLocation(document));
        Assert.Equal("Redo Change", recorder.RedoText);
        recorder.Redo();
        Assert.Equal(moved, FirstPartLocation(document));
    }

    [Fact]
    public void BeginChange_FirstCommitsAnEditNoScopeRecorded()
    {
        var recorder = Open(out var document);
        document.Nest.Drawings.First().Quantity.Required = 7;

        recorder.RecordChange("Rotate", () => document.Nest.Plates[0].Parts[0].Rotate(System.Math.PI / 2));

        Assert.Equal(2, recorder.History.UndoCount);
        Assert.Equal("Undo Rotate", recorder.UndoText);
        recorder.Undo();
        Assert.Equal("Undo Change", recorder.UndoText);
        Assert.Equal(7, document.Nest.Drawings.First().Quantity.Required);
    }

    [Fact]
    public void HasUnsavedChanges_FollowsTheSavePointAndUnrecordedEdits()
    {
        var recorder = Open(out var document);
        Assert.False(recorder.HasUnsavedChanges);

        document.Nest.Plates[0].Parts[0].Offset(1, 0);
        Assert.True(recorder.HasUnsavedChanges);

        recorder.Undo();
        Assert.False(recorder.HasUnsavedChanges);

        recorder.Redo();
        Assert.True(recorder.HasUnsavedChanges);

        recorder.MarkSaved();
        Assert.False(recorder.HasUnsavedChanges);
    }

    [Fact]
    public void MarkSaved_RecordsAnUnrecordedEditFirst_SoTheSavePointCoversIt()
    {
        var recorder = Open(out var document);
        document.Nest.Plates[0].Parts[0].Offset(1, 0);

        recorder.MarkSaved();

        Assert.False(recorder.HasUnsavedChanges);
        Assert.Equal("Undo Change", recorder.UndoText);
        Assert.True(recorder.History.IsAtSavePoint);
    }

    [Fact]
    public void AScopeLeftIncomplete_RollsTheNestBack()
    {
        var recorder = Open(out var document);
        var start = FirstPartLocation(document);

        using (var change = recorder.BeginChange("Push"))
        {
            change.MarkChanged();
            document.Nest.Plates[0].Parts[0].Offset(9, 0);
            // An exception or a cancellation: never completed.
        }

        Assert.Equal(start, FirstPartLocation(document));
        Assert.False(recorder.History.CanUndo);
        Assert.False(recorder.HasUnsavedChanges);
    }

    [Fact]
    public void AScopeThatChangedNothing_RecordsNothing()
    {
        var recorder = Open(out _);
        var changed = 0;
        recorder.Changed += (_, _) => changed++;

        using (var change = recorder.BeginChange("Fill"))
            change.Complete();

        Assert.False(recorder.History.CanUndo);
        Assert.Equal(0, changed);
    }

    [Fact]
    public void NestedScopes_UndoAsOneEntryUnderTheOuterLabel()
    {
        var recorder = Open(out var document);

        using (var outer = recorder.BeginChange("Auto Nest"))
        {
            Assert.True(recorder.IsChangeInProgress);
            Assert.False(recorder.CanUndo);
            recorder.RecordChange("Add Plate", () => document.Nest.CreatePlate());
            outer.MarkChanged();
            document.Nest.Plates[0].Parts[0].Offset(2, 0);
            outer.Complete();
        }

        Assert.Equal(1, recorder.History.UndoCount);
        Assert.Equal("Undo Auto Nest", recorder.UndoText);
    }

    [Fact]
    public void UndoAndRedo_DoNothingWhileAScopeIsOpen()
    {
        var recorder = Open(out var document);
        recorder.RecordChange("Move", () => document.Nest.Plates[0].Parts[0].Offset(5, 0));
        var live = document.Nest;

        using (var change = recorder.BeginChange("Rotate"))
        {
            recorder.Undo();
            Assert.Same(live, document.Nest);
            change.Complete();
        }

        Assert.Equal(1, recorder.History.UndoCount);
    }

    [Fact]
    public void TakeUnrecordedChangeWarning_ReportsEachUnrecordedEditOnce()
    {
        var recorder = Open(out var document);
        Assert.False(recorder.TakeUnrecordedChangeWarning());

        document.Nest.Plates[0].Parts[0].Offset(1, 0);

        Assert.True(recorder.TakeUnrecordedChangeWarning());
        Assert.False(recorder.TakeUnrecordedChangeWarning());
        document.Nest.Plates[0].Parts[0].Offset(1, 0);
        Assert.True(recorder.TakeUnrecordedChangeWarning());
    }

    [Fact]
    public void MenuText_NamesTheNextStep()
    {
        var recorder = Open(out var document);
        Assert.Equal("Undo", recorder.UndoText);
        Assert.Equal("Redo", recorder.RedoText);
        Assert.Equal("Undo", NestUndoRecorder.UndoMenuText<NestUndoState>(null));

        recorder.RecordChange("Fill", () => document.Nest.Plates[0].Parts[0].Offset(1, 0));

        Assert.Equal("Undo Fill", recorder.UndoText);
    }
}
