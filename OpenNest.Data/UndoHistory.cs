namespace OpenNest.Data;

/// <summary>A saved document state the history can hold; its size counts against the byte budget.</summary>
public interface IUndoState
{
    long SizeInBytes { get; }
}

/// <summary>
/// A document's undo history as a list of whole states and a cursor at the live one.
/// Committing records the state after an operation and drops anything that could be
/// redone; Undo and Redo move the cursor and return the state to restore. The oldest
/// states are evicted past <see cref="MaxDepth"/> undo steps or <see cref="MaxBytes"/>,
/// never the current one.
/// </summary>
public sealed class UndoHistory<TState> where TState : class, IUndoState
{
    public const int DefaultMaxDepth = 50;
    public const long DefaultMaxBytes = 128L * 1024 * 1024;
    public static readonly TimeSpan DefaultCoalesceWindow = TimeSpan.FromSeconds(1);

    private readonly List<Entry> entries = new();
    private readonly TimeProvider clock;
    private int cursor;
    private int savePoint = -1;
    private long totalBytes;
    // Only an entry committed last, with nothing undone, redone or saved since, can absorb the next.
    private bool canCoalesce;

    public UndoHistory(int maxDepth = DefaultMaxDepth, long maxBytes = DefaultMaxBytes,
        TimeSpan? coalesceWindow = null, TimeProvider? clock = null)
    {
        if (maxDepth < 1) throw new ArgumentOutOfRangeException(nameof(maxDepth));
        if (maxBytes < 1) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        MaxDepth = maxDepth;
        MaxBytes = maxBytes;
        CoalesceWindow = coalesceWindow ?? DefaultCoalesceWindow;
        this.clock = clock ?? TimeProvider.System;
    }

    /// <summary>Raised after every change to the states, the cursor or the save point.</summary>
    public event EventHandler? Changed;

    public int MaxDepth { get; }
    public long MaxBytes { get; }
    public TimeSpan CoalesceWindow { get; }

    /// <summary>The state the live document should match; null before <see cref="Reset"/>.</summary>
    public TState? Current => entries.Count == 0 ? null : entries[cursor].State;

    public bool CanUndo => cursor > 0;
    public bool CanRedo => cursor + 1 < entries.Count;
    public int UndoCount => cursor;
    public int RedoCount => entries.Count == 0 ? 0 : entries.Count - cursor - 1;

    /// <summary>The label of the operation Undo would reverse, or null.</summary>
    public string? UndoLabel => CanUndo ? entries[cursor].Label : null;

    /// <summary>The label of the operation Redo would repeat, or null.</summary>
    public string? RedoLabel => CanRedo ? entries[cursor + 1].Label : null;

    public long TotalBytes => totalBytes;

    /// <summary>True while the current state is the one last marked saved.</summary>
    public bool IsAtSavePoint => entries.Count > 0 && savePoint == cursor;

    /// <summary>Starts over from <paramref name="initial"/>, which is treated as saved.</summary>
    public void Reset(TState initial)
    {
        ArgumentNullException.ThrowIfNull(initial);
        entries.Clear();
        entries.Add(new Entry(null, initial, null, clock.GetUtcNow()));
        totalBytes = initial.SizeInBytes;
        cursor = 0;
        savePoint = 0;
        canCoalesce = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Records <paramref name="state"/> as the result of <paramref name="label"/>. With a
    /// <paramref name="coalesceKey"/> matching the last commit's within
    /// <see cref="CoalesceWindow"/>, replaces that entry instead, so repeated small edits
    /// (nudges of one selection) undo as one.
    /// </summary>
    public void Commit(string label, TState state, object? coalesceKey = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(label);
        ArgumentNullException.ThrowIfNull(state);
        if (entries.Count == 0)
            throw new InvalidOperationException("Reset the history before committing.");

        DropRedo();
        var now = clock.GetUtcNow();
        var top = entries[cursor];
        if (canCoalesce && coalesceKey != null && cursor > 0 && savePoint != cursor
            && Equals(top.CoalesceKey, coalesceKey) && now - top.CommittedAt <= CoalesceWindow)
        {
            totalBytes += state.SizeInBytes - top.State.SizeInBytes;
            entries[cursor] = top with { State = state, CommittedAt = now };
        }
        else
        {
            entries.Add(new Entry(label, state, coalesceKey, now));
            totalBytes += state.SizeInBytes;
            cursor++;
        }

        canCoalesce = true;
        Evict();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Steps back one operation and returns the state to restore.</summary>
    public TState Undo()
    {
        if (!CanUndo) throw new InvalidOperationException("Nothing to undo.");
        cursor--;
        canCoalesce = false;
        Changed?.Invoke(this, EventArgs.Empty);
        return entries[cursor].State;
    }

    /// <summary>Repeats the next undone operation and returns the state to restore.</summary>
    public TState Redo()
    {
        if (!CanRedo) throw new InvalidOperationException("Nothing to redo.");
        cursor++;
        canCoalesce = false;
        Changed?.Invoke(this, EventArgs.Empty);
        return entries[cursor].State;
    }

    /// <summary>Marks the current state as the one on disk or on the server.</summary>
    public void MarkSaved()
    {
        if (entries.Count == 0)
            throw new InvalidOperationException("Reset the history before marking it saved.");
        savePoint = cursor;
        canCoalesce = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void DropRedo()
    {
        var first = cursor + 1;
        if (first >= entries.Count)
            return;
        for (var i = first; i < entries.Count; i++)
            totalBytes -= entries[i].State.SizeInBytes;
        entries.RemoveRange(first, entries.Count - first);
        // A saved state that was undone past is no longer reachable.
        if (savePoint >= first)
            savePoint = -1;
    }

    private void Evict()
    {
        while (cursor > 0 && (cursor > MaxDepth || totalBytes > MaxBytes))
        {
            totalBytes -= entries[0].State.SizeInBytes;
            entries.RemoveAt(0);
            cursor--;
            if (savePoint >= 0)
                savePoint--;
        }
    }

    private sealed record Entry(string? Label, TState State, object? CoalesceKey, DateTimeOffset CommittedAt);
}
