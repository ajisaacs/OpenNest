using System.Runtime.CompilerServices;

using OpenNest.Mcp;
using OpenNest.Mcp.Tools;

namespace OpenNest.FrontEnd.Tests;

[Collection("FrontEndRegistry")]
public class McpRepeatReservationLifecycleTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "opennest-mcp-reservations-" + Guid.NewGuid());

    public McpRepeatReservationLifecycleTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);

    private static NestSession Session(int plateCount = 2)
    {
        var session = new NestSession { Nest = new Nest("reservations") };
        var input = new InputTools(session);
        input.CreateDrawing("A", "rectangle", 2, 2);
        input.CreateDrawing("B", "rectangle", 2, 2);
        var setup = new SetupTools(session);
        for (var i = 0; i < plateCount; i++)
            setup.CreatePlate(3, 3, partSpacing: 0.25);
        return session;
    }

    private static string Repeat(NestSession session, int index = 0, string name = "A", int quantity = 2) =>
        new NestingTools(session).AutoNestJob(index, name, quantity.ToString(), "Rectangles", no_new_plates: true);

    private static int Index(NestSession session, Plate plate) =>
        session.AllPlates().FindIndex(p => ReferenceEquals(p, plate));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClearingOwnerAllowsCompleteRenestWithoutCreatingOrDeletingSheets(bool noNew)
    {
        var session = Session();
        var original = session.AllPlates().ToArray();
        var tools = new NestingTools(session);
        var first = tools.AutoNestJob(0, "A", "2", "Rectangles", no_new_plates: noNew,
            sheets: noNew ? null : [new SheetStockInput { Width = 3, Length = 3, Quantity = 2 }]);
        Assert.Contains("2 sheet(s) placed in 1 layout(s)", first);
        Assert.Equal(2, original[0].Quantity);
        Assert.Empty(original[1].Parts);
        Assert.Contains("removed 1 parts", new SetupTools(session).ClearPlate(0));
        Assert.Equal(0, session.GetDrawing("A").Quantity.Nested);

        Assert.Contains("2 sheet(s) placed in 1 layout(s)", Repeat(session));
        Assert.Equal(original, session.AllPlates());
        Assert.Single(original[0].Parts);
        Assert.Equal(2, original[0].Quantity);
        Assert.Empty(original[1].Parts);
        Assert.Equal(2, session.GetDrawing("A").Quantity.Nested);
    }

    [Fact]
    public void ClearingPlaceholderDoesNotReleasePopulatedOwnersPhysicalStock()
    {
        var session = Session(3);
        Assert.Contains("2 sheet(s) placed in 1 layout(s)", Repeat(session));
        var owner = session.GetPlate(0);
        var part = Assert.Single(owner.Parts);
        var setup = new SetupTools(session);
        Assert.Contains("removed 0 parts", setup.ClearPlate(1));
        var tools = new NestingTools(session);
        Assert.Contains("consumed", Repeat(session, 1, "B", 1));
        Assert.Contains("consumed", tools.FillPlate(1, "B", 1));
        Assert.Contains("consumed", tools.FillArea(1, "B", 0, 0, 3, 3, 1));
        Assert.Contains("consumed", tools.FillRemnants(1, "B", 1));
        Assert.Contains("consumed", tools.PackPlate(1, "B", "1"));
        Assert.Contains("consumed", tools.AutoNestPlate(1, "B", "1", allow_invalid: true));
        Assert.Contains("Nothing committed", Repeat(session, 2, "B"));
        Assert.Same(part, Assert.Single(owner.Parts));
        Assert.Equal(2, session.GetDrawing("A").Quantity.Nested);
        Assert.Equal(0, session.GetDrawing("B").Quantity.Nested);
        Assert.All(session.AllPlates().Skip(1), plate => Assert.Empty(plate.Parts));
    }

    [Fact]
    public void ClearingOneOwnerDoesNotFreeAnotherPopulatedLayoutsReservation()
    {
        var session = Session(5);
        Assert.Contains("Whole job complete", Repeat(session));
        Assert.Contains("Whole job complete", Repeat(session, 2, "B"));
        var otherOwner = session.GetPlate(2);
        var otherPart = Assert.Single(otherOwner.Parts);
        var otherPlaceholder = session.GetPlate(3);
        new SetupTools(session).ClearPlate(0);

        Assert.Contains("consumed", Repeat(session, 3, "B", 3));
        Assert.Contains("3 sheet(s) placed in 1 layout(s)", Repeat(session, quantity: 3));
        Assert.Same(otherPart, Assert.Single(otherOwner.Parts));
        Assert.Equal(2, otherOwner.Quantity);
        Assert.Empty(otherPlaceholder.Parts);
        Assert.Contains("consumed", new NestingTools(session).FillPlate(Index(session, otherPlaceholder), "A", 1));
        Assert.Equal(5, session.AllPlates().Count);
        Assert.Equal(3, session.GetDrawing("A").Quantity.Nested);
        Assert.Equal(2, session.GetDrawing("B").Quantity.Nested);
    }

    [Fact]
    public void DeletingPlaceholderDoesNotReleaseItsOwnersOtherCopiesOrAnotherOwner()
    {
        var session = Session(5);
        Assert.Contains("Whole job complete", Repeat(session, quantity: 3));
        Assert.Contains("Whole job complete", Repeat(session, 3, "B"));
        var original = session.AllPlates().ToArray();
        var setup = new SetupTools(session);
        Assert.Contains("occupied", setup.DeletePlate(0));
        Assert.Contains("Deleted plate 1", setup.DeletePlate(1));
        Assert.Equal(new[] { original[0], original[2], original[3], original[4] }, session.AllPlates());
        Assert.Contains("consumed", Repeat(session, 1, "A", 4));
        Assert.Contains("consumed", Repeat(session, 3, "B", 3));
        Assert.Equal(3, original[0].Quantity);
        Assert.Equal(2, original[3].Quantity);
        Assert.Single(original[0].Parts);
        Assert.Single(original[3].Parts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MutableCollectionsReleaseOnlyRemovedOwnersReservation(bool loadedOwner)
    {
        var session = Session(4);
        if (loadedOwner)
        {
            session.Nest.Plates.Add(session.Plates[0]);
            session.Plates.RemoveAt(0);
        }
        Assert.Contains("Whole job complete", Repeat(session));
        Assert.Contains("Whole job complete", Repeat(session, 2, "B"));
        var removed = session.GetPlate(0);
        var freed = session.GetPlate(1);
        var otherOwner = session.GetPlate(2);
        var otherPlaceholder = session.GetPlate(3);
        if (loadedOwner)
            session.Nest.Plates.Remove(removed);
        else
            session.Plates.Remove(removed); // List removal leaves the detached owner's parts populated.

        Assert.Contains("consumed", Repeat(session, Index(session, otherPlaceholder), "B", 3));
        Assert.Contains("Parts added: 1", new NestingTools(session).FillPlate(Index(session, freed), "A", 1));
        Assert.Single(freed.Parts);
        Assert.Single(otherOwner.Parts);
        Assert.Equal(2, otherOwner.Quantity);
        Assert.Empty(otherPlaceholder.Parts);
        Assert.Equal(3, session.AllPlates().Count);
    }

    [Fact]
    public void DirectlyClearedThenDeletedOwnerReleasesCopiesWithoutCorruptingOtherOwner()
    {
        var session = Session(4);
        Assert.Contains("Whole job complete", Repeat(session));
        Assert.Contains("Whole job complete", Repeat(session, 2, "B"));
        var freed = session.GetPlate(1);
        var otherPlaceholder = session.GetPlate(3);
        session.GetPlate(0).Parts.Clear();
        Assert.Contains("Deleted plate 0", new SetupTools(session).DeletePlate(0));
        Assert.Contains("consumed", Repeat(session, Index(session, otherPlaceholder), "B", 3));
        Assert.Contains("1 sheet(s) placed in 1 layout(s)", Repeat(session, Index(session, freed), quantity: 1));
        Assert.Single(freed.Parts);
        Assert.Empty(otherPlaceholder.Parts);
    }

    [Fact]
    public void LoadingReplacementReleasesRemovedOwnerButPreservesAppendedOwnersReservation()
    {
        var session = Session(4);
        session.Nest.Plates.Add(session.Plates[0]);
        session.Plates.RemoveAt(0);
        Assert.Contains("Whole job complete", Repeat(session));
        Assert.Contains("Whole job complete", Repeat(session, 2, "B"));
        var freed = session.GetPlate(1);
        var otherOwner = session.GetPlate(2);
        var otherPlaceholder = session.GetPlate(3);
        var path = ReplacementFile();
        Assert.Contains("Loaded nest", new InputTools(session).LoadNest(path));

        Assert.Contains("consumed", Repeat(session, Index(session, otherPlaceholder), "B", 3));
        Assert.Contains("1 sheet(s) placed in 1 layout(s)", Repeat(session, Index(session, freed), quantity: 1));
        Assert.Single(freed.Parts);
        Assert.Single(otherOwner.Parts);
        Assert.Equal(2, otherOwner.Quantity);
        Assert.Empty(otherPlaceholder.Parts);
        Assert.Equal(4, session.AllPlates().Count);
    }

    private string ReplacementFile()
    {
        var replacement = Session(1);
        replacement.Drawings.Clear();
        new InputTools(replacement).CreateDrawing("LoadedOnly", "rectangle", 2, 2);
        Assert.Contains("Whole job complete", Repeat(replacement, name: "LoadedOnly", quantity: 1));
        var path = Path.Combine(directory, "replacement.nest");
        Assert.Contains("Saved nest", new InputTools(replacement).SaveNest(path));
        return path;
    }

    [Fact]
    public void LoadingAwayPlaceholderDoesNotReleaseAppendedOwnersRemainingReservation()
    {
        var session = Session(3);
        Assert.Contains("Whole job complete", Repeat(session, quantity: 3));
        var owner = session.GetPlate(0);
        var remaining = session.GetPlate(2);
        var replaced = ReplaceLoadedPlaceholder(session, ReplacementFile());
        Assert.Contains("consumed", Repeat(session, Index(session, remaining), "B", 1));
        Assert.Single(owner.Parts);
        Assert.Equal(3, owner.Quantity);
        Assert.Empty(remaining.Parts);
        AssertCollected(replaced);
        GC.KeepAlive(session);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ReplaceLoadedPlaceholder(NestSession session, string path)
    {
        var placeholder = session.GetPlate(1);
        session.Plates.Remove(placeholder);
        session.Nest.Plates.Add(placeholder);
        Assert.Contains("Loaded nest", new InputTools(session).LoadNest(path));
        return new WeakReference(placeholder);
    }

    [Fact]
    public void DeletedPlaceholderReferencesAreNotRetainedWhileSiblingReservationStaysBlocked()
    {
        var session = Session(3);
        Assert.Contains("Whole job complete", Repeat(session, quantity: 3));
        var deleted = DeletePlaceholder(session);
        Assert.Contains("consumed", Repeat(session, 1, "B", 1));
        AssertCollected(deleted);
        GC.KeepAlive(session);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference DeletePlaceholder(NestSession session)
    {
        var placeholder = session.GetPlate(1);
        Assert.Contains("Deleted plate 1", new SetupTools(session).DeletePlate(1));
        return new WeakReference(placeholder);
    }

    [Fact]
    public void RepeatedLoadedNestReplacementDoesNotRetainOldOwnersOrPlaceholders()
    {
        var session = new NestSession();
        var path = ReplacementFile();
        var oldPlates = ReplaceLoadedReservations(session, path);
        Assert.All(oldPlates, AssertCollected);
        GC.KeepAlive(session);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static List<WeakReference> ReplaceLoadedReservations(NestSession session, string path)
    {
        var oldPlates = new List<WeakReference>();
        for (var i = 0; i < 8; i++)
        {
            var fixture = Session();
            foreach (var drawing in fixture.AllDrawings())
                fixture.Nest.Drawings.Add(drawing);
            foreach (var plate in fixture.AllPlates())
                fixture.Nest.Plates.Add(plate);
            session.Nest = fixture.Nest;
            Assert.Contains("Whole job complete", Repeat(session));
            oldPlates.AddRange(session.AllPlates().Select(p => new WeakReference(p)));
            Assert.Contains("Loaded nest", new InputTools(session).LoadNest(path));
        }
        return oldPlates;
    }

    private static void AssertCollected(WeakReference reference)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(reference.IsAlive, "Session retained a plate outside its live reservation ownership.");
    }
}
