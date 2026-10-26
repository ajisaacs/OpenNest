using OpenNest.Data;

namespace OpenNest.Tests.Data;

public class UndoHistoryTests
{
    [Fact]
    public void UndoAndRedo_WalkTheCommittedStates_WithTheirLabels()
    {
        var history = Started(out var s0);
        var s1 = State("1");
        var s2 = State("2");
        history.Commit("Move", s1);
        history.Commit("Rotate", s2);

        Assert.Equal("Rotate", history.UndoLabel);
        Assert.Null(history.RedoLabel);
        Assert.Same(s1, history.Undo());
        Assert.Equal("Move", history.UndoLabel);
        Assert.Equal("Rotate", history.RedoLabel);
        Assert.Same(s0, history.Undo());
        Assert.False(history.CanUndo);
        Assert.Null(history.UndoLabel);
        Assert.Throws<InvalidOperationException>(() => history.Undo());

        Assert.Same(s1, history.Redo());
        Assert.Same(s2, history.Redo());
        Assert.False(history.CanRedo);
        Assert.Throws<InvalidOperationException>(() => history.Redo());
        Assert.Same(s2, history.Current);
    }

    [Fact]
    public void Commit_AfterUndo_DropsTheRedoTail()
    {
        var history = Started(out _);
        history.Commit("A", State("a", 10));
        history.Commit("B", State("b", 20));
        history.Undo();
        var c = State("c", 5);

        history.Commit("C", c);

        Assert.False(history.CanRedo);
        Assert.Equal(2, history.UndoCount);
        Assert.Same(c, history.Current);
        Assert.Equal(1 + 10 + 5, history.TotalBytes);
    }

    [Fact]
    public void Commit_RequiresReset()
    {
        var history = new UndoHistory<TestState>();

        Assert.Null(history.Current);
        Assert.False(history.CanUndo);
        Assert.Throws<InvalidOperationException>(() => history.Commit("A", State("a")));
    }

    [Fact]
    public void Depth_EvictsTheOldestStates()
    {
        var history = new UndoHistory<TestState>(maxDepth: 3);
        history.Reset(State("0"));
        for (var i = 1; i <= 5; i++)
            history.Commit($"Step {i}", State(i.ToString()));

        Assert.Equal(3, history.UndoCount);
        var undone = new List<string>();
        while (history.CanUndo)
            undone.Add(history.Undo().Name);
        Assert.Equal(new[] { "4", "3", "2" }, undone);
    }

    [Fact]
    public void Bytes_EvictTheOldestStates_ButNeverTheCurrentOne()
    {
        var history = new UndoHistory<TestState>(maxBytes: 100);
        history.Reset(State("0", 40));
        history.Commit("A", State("a", 40));
        history.Commit("B", State("b", 40));

        Assert.Equal(1, history.UndoCount);
        Assert.Equal(80, history.TotalBytes);

        var huge = State("huge", 500);
        history.Commit("Huge", huge);

        Assert.False(history.CanUndo);
        Assert.Same(huge, history.Current);
        Assert.Equal(500, history.TotalBytes);
    }

    [Fact]
    public void SavePoint_FollowsTheCursor_AndIsLostWhenDroppedOrEvicted()
    {
        var history = Started(out _);
        Assert.True(history.IsAtSavePoint);
        history.Commit("A", State("a"));
        Assert.False(history.IsAtSavePoint);
        history.MarkSaved();
        Assert.True(history.IsAtSavePoint);
        history.Undo();
        Assert.False(history.IsAtSavePoint);
        history.Redo();
        Assert.True(history.IsAtSavePoint);

        history.Undo();
        history.Commit("B", State("b")); // Drops the saved "A": no reachable state is saved.
        Assert.False(history.IsAtSavePoint);
        history.Undo();
        Assert.False(history.IsAtSavePoint);
        history.Redo();
        Assert.False(history.IsAtSavePoint);

        var shallow = new UndoHistory<TestState>(maxDepth: 1);
        shallow.Reset(State("0"));
        shallow.Commit("A", State("a"));
        shallow.Commit("B", State("b"));
        shallow.Undo();
        Assert.False(shallow.IsAtSavePoint); // The saved reset state was evicted.
    }

    [Fact]
    public void Coalescing_MergesMatchingCommitsWithinTheWindow()
    {
        var clock = new ManualClock();
        var history = new UndoHistory<TestState>(clock: clock);
        var s0 = State("0");
        history.Reset(s0);
        history.Commit("Nudge", State("n1", 3), coalesceKey: "selection");
        clock.Advance(TimeSpan.FromMilliseconds(600));
        history.Commit("Nudge", State("n2", 3), coalesceKey: "selection");
        clock.Advance(TimeSpan.FromMilliseconds(600)); // Sliding: within a second of the last nudge.
        var n3 = State("n3", 3);
        history.Commit("Nudge", n3, coalesceKey: "selection");

        Assert.Equal(1, history.UndoCount);
        Assert.Same(n3, history.Current);
        Assert.Equal(1 + 3, history.TotalBytes);

        clock.Advance(TimeSpan.FromSeconds(2));
        history.Commit("Nudge", State("late"), coalesceKey: "selection");
        history.Commit("Nudge", State("other"), coalesceKey: "other selection");
        history.Commit("Delete", State("plain"));
        history.Commit("Delete", State("plain again"));

        Assert.Equal(5, history.UndoCount);
        history.Undo();
        history.Undo();
        history.Undo();
        history.Undo();
        Assert.Same(n3, history.Current);
        Assert.Same(s0, history.Undo());
    }

    [Fact]
    public void Coalescing_StopsAtUndoRedoAndSave()
    {
        var history = new UndoHistory<TestState>(clock: new ManualClock());
        history.Reset(State("0"));
        history.Commit("Nudge", State("1"), "k");
        history.MarkSaved();
        history.Commit("Nudge", State("2"), "k");
        Assert.Equal(2, history.UndoCount); // The saved state is kept.

        history.Undo();
        history.Redo();
        history.Commit("Nudge", State("3"), "k");
        Assert.Equal(3, history.UndoCount);

        history.Undo();
        history.Commit("Nudge", State("4"), "k");
        Assert.Equal(3, history.UndoCount);
        Assert.Equal("4", history.Current!.Name);
    }

    [Fact]
    public void Changed_IsRaisedForEveryChange()
    {
        var history = new UndoHistory<TestState>();
        var raised = 0;
        history.Changed += (_, _) => raised++;

        history.Reset(State("0"));
        history.Commit("A", State("a"));
        history.Undo();
        history.Redo();
        history.MarkSaved();

        Assert.Equal(5, raised);
    }

    private static UndoHistory<TestState> Started(out TestState initial)
    {
        var history = new UndoHistory<TestState>();
        initial = State("0");
        history.Reset(initial);
        return history;
    }

    private static TestState State(string name, long size = 1) => new(name, size);

    private sealed record TestState(string Name, long SizeInBytes) : IUndoState;

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 26, 8, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now += by;
    }
}
