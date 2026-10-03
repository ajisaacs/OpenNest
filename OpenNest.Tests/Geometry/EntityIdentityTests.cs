using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.Tests.Geometry;

public class EntityIdentityTests
{
    [Fact]
    public void ConstructionAndClone_UseDistinctNonemptyIdsAcrossEntityTypes()
    {
        var entities = CreateEntities();
        var clones = entities.Select(entity => entity.Clone()).ToArray();
        var ids = entities.Concat(clones).Select(entity => entity.Id).ToArray();

        Assert.DoesNotContain(Guid.Empty, ids);
        Assert.Equal(ids.Length, ids.Distinct().Count());
        for (var i = 0; i < entities.Length; i++)
        {
            Assert.Equal(entities[i].Type, clones[i].Type);
            Assert.Equal((entities[i].Left, entities[i].Right, entities[i].Bottom, entities[i].Top),
                (clones[i].Left, clones[i].Right, clones[i].Bottom, clones[i].Top));
        }
    }

    [Fact]
    public void ConcurrentConstruction_IdsAreUniqueAcrossSharedProcessSequence()
    {
        var ids = new Guid[20_000];
        Parallel.For(0, ids.Length, i => ids[i] = CreateEntities()[i % 5].Id);

        Assert.DoesNotContain(Guid.Empty, ids);
        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    [Fact]
    public void GeneratedIds_ShareProcessSaltButNotCounterBytes()
    {
        var ids = CreateEntities().Select(entity => entity.Id.ToByteArray()).ToArray();

        foreach (var id in ids.Skip(1))
            Assert.Equal(ids[0][..10], id[..10]);
        Assert.Equal(ids.Length, ids.Select(id => Convert.ToHexString(id[10..])).Distinct().Count());
    }

    [Fact]
    public void ExplicitIdsAndSuppression_SurviveEntitySerialization()
    {
        var entities = CreateEntities().Take(3).ToList();
        entities[0].Id = Guid.Parse("11111111-2222-3333-8444-555555555555");
        entities[1].Id = Guid.Parse("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee");
        var originalIds = entities.Select(entity => entity.Id).ToArray();
        var suppressed = new HashSet<Guid> { entities[1].Id, entities[2].Id };

        var dto = EntitySerializer.ToDto(entities, suppressed);
        var (restored, restoredSuppressed) = EntitySerializer.FromDto(dto);

        Assert.Equal(originalIds, restored.Select(entity => entity.Id));
        Assert.True(suppressed.SetEquals(restoredSuppressed));
        Assert.Equal(entities.Select(entity => entity.Type), restored.Select(entity => entity.Type));
        Assert.NotEqual(restored[0].Id, restored[0].Clone().Id);
    }

    [Fact]
    public void ExplicitIdsAndSuppression_SurviveNestFileRoundTrip()
    {
        var drawing = TestHelpers.MakeSquareDrawing();
        drawing.SourceEntities = CreateEntities().Take(3).ToList();
        drawing.SourceEntities[0].Id = Guid.Parse("11111111-2222-3333-8444-555555555555");
        var ids = drawing.SourceEntities.Select(entity => entity.Id).ToArray();
        drawing.SuppressedEntityIds.Add(ids[1]);
        var nest = new Nest();
        nest.Drawings.Add(drawing);
        using var stream = new MemoryStream();

        new NestWriter(nest).Write(stream);
        stream.Position = 0;
        var restored = Assert.Single(new NestReader(stream).Read().Drawings);

        Assert.Equal(ids, restored.SourceEntities.Select(entity => entity.Id));
        Assert.Equal(ids[1], Assert.Single(restored.SuppressedEntityIds));
    }

    private static Entity[] CreateEntities() =>
    [
        new Line(0, 0, 2, 3),
        new Arc(0, 0, 2, 0, System.Math.PI),
        new Circle(new Vector(1, 2), 3),
        new Polygon(),
        new Shape(),
    ];
}
