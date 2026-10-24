namespace OpenNest.Data;

/// <summary>What adding library drawings to a set did.</summary>
public sealed class DrawingSetAddResult
{
    internal DrawingSetAddResult(IReadOnlyList<DrawingLibraryRecord> added, IReadOnlyList<DrawingLibraryRecord> alreadyMembers)
    {
        Added = added;
        AlreadyMembers = alreadyMembers;
    }

    public IReadOnlyList<DrawingLibraryRecord> Added { get; }

    /// <summary>Drawings left out because the set already lists them (S6: one member per drawing).</summary>
    public IReadOnlyList<DrawingLibraryRecord> AlreadyMembers { get; }
}

/// <summary>
/// Edits one drawing set (new or read from the server): its name, customer and notes and its
/// ordered members, each one library drawing with a quantity. A save of an existing set is
/// guarded by the revision it was read at, so a stale save raises
/// <see cref="DrawingSetSaveConflictException"/> and changes nothing.
/// <para>
/// A new set's id is allocated here, before the first save. When that save fails without a
/// definite answer it stays pending: the next save first reads the id, adopts the stored set
/// if it is there (then saves any later change as its next revision), and otherwise creates it
/// again under the same id. A new set is therefore never stored twice.
/// </para>
/// </summary>
public sealed class DrawingSetEditSession
{
    private readonly IDrawingSetStore _sets;
    private readonly List<DrawingSetMember> _members = new();
    private bool _pendingCreate;

    /// <param name="current">A set read with <see cref="IDrawingSetStore.GetAsync"/>, or null for a new set.</param>
    public DrawingSetEditSession(IDrawingSetStore sets, DrawingSetRecord? current, string savedBy)
    {
        _sets = sets ?? throw new ArgumentNullException(nameof(sets));
        SavedBy = savedBy ?? "";
        if (current == null)
        {
            Id = Guid.NewGuid();
            return;
        }

        if (current.Id == Guid.Empty || current.Revision < 1)
            throw new ArgumentException("Choose a set read from the server to edit.", nameof(current));
        if (current.Archived)
            throw new ArgumentException("An archived set cannot be edited. Restore it first.", nameof(current));
        if (current.Members is null || current.Members.Any(m => m?.Drawing is null))
            throw new ArgumentException("The set's members are missing their library drawings. Reload the set.", nameof(current));
        Id = current.Id;
        Load(current);
    }

    public Guid Id { get; }

    /// <summary>The store the set is read from and saved to; also reads its history.</summary>
    public IDrawingSetStore Store => _sets;

    public string SavedBy { get; }

    /// <summary>The set as last read or saved; null until a new set is first saved.</summary>
    public DrawingSetRecord? Saved { get; private set; }

    public bool IsNew => Saved == null;

    public string Name { get; set; } = "";

    public string Customer { get; set; } = "";

    public string Notes { get; set; } = "";

    /// <summary>The members in order. Each carries the library record it was added or read with.</summary>
    public IReadOnlyList<DrawingSetMember> Members => _members;

    /// <summary>An earlier save failed without a definite answer; the next save checks it first.</summary>
    public bool HasPendingSave => _pendingCreate;

    /// <summary>
    /// Appends each drawing not already in the set, in order, with <paramref name="quantity"/>.
    /// Drawings the set already lists (or listed twice in <paramref name="drawings"/>) are skipped.
    /// </summary>
    public DrawingSetAddResult AddDrawings(IEnumerable<DrawingLibraryRecord> drawings, int quantity = 1)
    {
        ArgumentNullException.ThrowIfNull(drawings);
        if (quantity < 1 || quantity > DrawingSetRules.MaxQuantity)
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, $"Quantity must be 1 to {DrawingSetRules.MaxQuantity}.");

        var added = new List<DrawingLibraryRecord>();
        var skipped = new List<DrawingLibraryRecord>();
        foreach (var drawing in drawings)
        {
            if (drawing is null || drawing.Id == Guid.Empty)
                throw new ArgumentException("Only library drawings can be added to a set.", nameof(drawings));
            if (_members.Any(m => m.DrawingId == drawing.Id))
            {
                skipped.Add(drawing);
                continue;
            }

            _members.Add(new DrawingSetMember { DrawingId = drawing.Id, Quantity = quantity, Drawing = drawing });
            added.Add(drawing);
        }

        return new DrawingSetAddResult(added, skipped);
    }

    public void RemoveAt(int index)
    {
        CheckIndex(index);
        _members.RemoveAt(index);
    }

    /// <summary>Moves a member by <paramref name="offset"/> places; false when it is already at that end.</summary>
    public bool Move(int index, int offset)
    {
        CheckIndex(index);
        var target = index + offset;
        if (target < 0 || target >= _members.Count || offset == 0)
            return false;
        var member = _members[index];
        _members.RemoveAt(index);
        _members.Insert(target, member);
        return true;
    }

    public void SetQuantity(int index, int quantity)
    {
        CheckIndex(index);
        if (quantity < 1 || quantity > DrawingSetRules.MaxQuantity)
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, $"Quantity must be 1 to {DrawingSetRules.MaxQuantity}.");
        _members[index].Quantity = quantity;
    }

    /// <summary>
    /// Shows <paramref name="drawing"/>, a newer revision of a member's library drawing (for
    /// example one just edited), on that member. Members are saved by id, so the set itself is
    /// not changed; false when no member lists the drawing or it already shows that revision.
    /// </summary>
    public bool UpdateDrawing(DrawingLibraryRecord drawing)
    {
        ArgumentNullException.ThrowIfNull(drawing);
        var member = _members.FirstOrDefault(m => m.DrawingId == drawing.Id);
        if (member == null || (member.Drawing != null && member.Drawing.Revision >= drawing.Revision))
            return false;
        member.Drawing = drawing;
        return true;
    }

    /// <summary>The record a save sends.</summary>
    public DrawingSetRecord Build() => new()
    {
        Id = Id,
        Name = Name ?? "",
        Customer = Customer ?? "",
        Notes = Notes ?? "",
        SavedBy = SavedBy,
        Members = _members.Select(m => new DrawingSetMember { DrawingId = m.DrawingId, Quantity = m.Quantity, Drawing = m.Drawing }).ToList(),
    };

    /// <summary>The first rule the set breaks, or null when it may be saved.</summary>
    public string? GetError() => DrawingSetRules.GetError(Build());

    /// <summary>True when saving would store something the server does not already hold.</summary>
    public bool IsChanged => Saved == null || !SameContent(Saved, Build());

    /// <summary>
    /// Saves the set: revision 1 for a new set, otherwise the next revision when the one it was
    /// read at is still current. On success the session holds the stored record and its members.
    /// </summary>
    public async Task<DrawingSetRecord> SaveAsync(CancellationToken cancellationToken = default)
    {
        var record = Build();
        var error = DrawingSetRules.GetError(record);
        if (error != null)
            throw new ArgumentException(error);

        if (_pendingCreate && await _sets.GetAsync(Id, cancellationToken) is { } stored)
            Adopt(stored);

        DrawingSetRecord saved;
        if (Saved == null)
        {
            _pendingCreate = true;
            try
            {
                saved = await _sets.CreateAsync(record, cancellationToken);
            }
            catch (DrawingSetSaveConflictException conflict)
            {
                // The id is this session's own: an earlier save whose reply was lost was stored.
                Adopt(conflict.Current);
                if (!IsChanged)
                {
                    Load(conflict.Current);
                    return conflict.Current;
                }
                saved = await _sets.UpdateAsync(Id, record, conflict.Current.Revision, cancellationToken);
            }
        }
        else
        {
            if (!IsChanged)
                return Saved;
            saved = await _sets.UpdateAsync(Id, record, Saved.Revision, cancellationToken);
        }

        _pendingCreate = false;
        Load(saved);
        return saved;
    }

    private void Adopt(DrawingSetRecord stored)
    {
        _pendingCreate = false;
        Saved = stored;
    }

    private void Load(DrawingSetRecord record)
    {
        Saved = record;
        Name = record.Name;
        Customer = record.Customer;
        Notes = record.Notes;
        _members.Clear();
        _members.AddRange(record.Members.Select(m => new DrawingSetMember { DrawingId = m.DrawingId, Quantity = m.Quantity, Drawing = m.Drawing }));
    }

    private void CheckIndex(int index)
    {
        if (index < 0 || index >= _members.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
    }

    // The server's identical-save rule: name, customer, notes and members in order.
    private static bool SameContent(DrawingSetRecord a, DrawingSetRecord b) =>
        a.Name == b.Name && a.Customer == b.Customer && a.Notes == b.Notes
        && a.Members.Select(m => (m.DrawingId, m.Quantity)).SequenceEqual(b.Members.Select(m => (m.DrawingId, m.Quantity)));
}
