using System.IO.Compression;
using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.IO;
using static OpenNest.IO.NestFormat;

namespace OpenNest.Tests.IO;

/// <summary>
/// Per-entity role overrides from the CAD converter: an entity can be cut, etched or kept for
/// display only regardless of its DXF layer, and the choice survives a save.
/// </summary>
public class EntityRoleTests
{
    private readonly Layer zero = new("0") { IsVisible = true };
    private readonly Layer etchLayer = new("ETCH") { IsVisible = true };
    private readonly List<Entity> outline;
    private readonly Circle hole;
    private readonly Line markOnEtchLayer;
    private readonly Line reference;

    public EntityRoleTests()
    {
        outline = new List<Entity>
        {
            new Line(10, 5, 30, 5) { Layer = zero },
            new Line(30, 5, 30, 25) { Layer = zero },
            new Line(30, 25, 10, 25) { Layer = zero },
            new Line(10, 25, 10, 5) { Layer = zero },
        };
        hole = new Circle(20, 15, 2) { Layer = zero };
        markOnEtchLayer = new Line(12, 22, 18, 22) { Layer = etchLayer };
        reference = new Line(12, 8, 18, 8) { Layer = zero };
    }

    private List<Entity> All => outline.Concat(new Entity[] { hole, markOnEtchLayer, reference }).ToList();

    private Dictionary<Guid, EntityRole> Overrides => new()
    {
        [markOnEtchLayer.Id] = EntityRole.Cut,
        [hole.Id] = EntityRole.Etch,
        [reference.Id] = EntityRole.Display,
    };

    [Fact]
    public void WithoutOverridesLayersDecideTheRole()
    {
        var drawing = Build(null);

        Assert.Equal(LayerType.Scribe, LayerAt(drawing, new Vector(15, 22)));
        Assert.Equal(LayerType.Cut, LayerAt(drawing, new Vector(22, 15)));
        Assert.Equal(LayerType.Cut, LayerAt(drawing, new Vector(15, 8)));
        Assert.Empty(drawing.EntityRoles);
        Assert.Empty(EntityRoles.DisplayGeometry(drawing));
    }

    [Fact]
    public void OverridesDecideTheProgramLayerAndDisplayOnlyStaysOut()
    {
        var drawing = Build(Overrides);

        Assert.Equal(LayerType.Cut, LayerAt(drawing, new Vector(15, 22)));
        Assert.Equal(LayerType.Scribe, LayerAt(drawing, new Vector(22, 15)));
        Assert.Null(LayerAt(drawing, new Vector(15, 8)));
        Assert.Equal(3, drawing.EntityRoles.Count);

        // The source entities are untouched: the overrides live beside them.
        Assert.Same(etchLayer, markOnEtchLayer.Layer);
        Assert.Same(zero, hole.Layer);
    }

    [Fact]
    public void DisplayGeometryIsInTheProgramFrame()
    {
        var drawing = Build(Overrides);

        var display = Assert.IsType<Line>(Assert.Single(EntityRoles.DisplayGeometry(drawing)));
        var offset = drawing.Source.Offset;

        Assert.Equal(reference.Id, display.Id);
        Assert.True(display.StartPoint.DistanceTo(reference.StartPoint - offset) < 1e-9);
        Assert.True(display.EndPoint.DistanceTo(reference.EndPoint - offset) < 1e-9);
    }

    [Fact]
    public void ExcludedDisplayGeometryIsNotShown()
    {
        reference.IsVisible = false;

        var drawing = Build(Overrides);

        Assert.Contains(reference.Id, drawing.SuppressedEntityIds);
        Assert.Empty(EntityRoles.DisplayGeometry(drawing));
    }

    [Fact]
    public void OverridesSurviveASaveAndAreNotUndoneByMarkRepair()
    {
        var drawing = Build(Overrides);
        var text = NestWriter.GetProgramText(drawing.Program);

        var loaded = Read(Save(drawing)).Drawings.Single();

        Assert.Equal(Overrides.OrderBy(r => r.Key), loaded.EntityRoles.OrderBy(r => r.Key));
        Assert.Equal(text, NestWriter.GetProgramText(loaded.Program));
        Assert.Equal(LayerType.Cut, LayerAt(loaded, new Vector(15, 22)));
        Assert.Equal(reference.Id, Assert.Single(EntityRoles.DisplayGeometry(loaded)).Id);
    }

    [Fact]
    public void EntitySetWithoutOverridesHasNoRolesKey()
    {
        var bytes = Save(Build(null));

        using var archive = new ZipArchive(new MemoryStream(bytes));
        using var reader = new StreamReader(archive.GetEntry("entities/entities-1")!.Open());
        var json = reader.ReadToEnd();

        Assert.DoesNotContain("\"roles\"", json);
    }

    [Fact]
    public void UnknownRolesAndIdsAreSkipped()
    {
        var id = Guid.NewGuid();
        var dto = new EntitySetDto
        {
            Roles = new Dictionary<string, string>
            {
                [id.ToString()] = "Display",
                [Guid.NewGuid().ToString()] = "bevel",
                ["not-a-guid"] = "etch",
            },
        };

        var roles = EntitySerializer.RolesFromDto(dto);

        Assert.Equal(EntityRole.Display, Assert.Single(roles).Value);
        Assert.Equal(id, roles.Single().Key);
    }

    private Drawing Build(Dictionary<Guid, EntityRole>? roles)
    {
        var entities = All;
        var result = new CadImportResult
        {
            Entities = entities,
            Bounds = entities.GetBoundingBox(),
            Name = "roles",
        };
        return CadImporter.BuildDrawing(
            result,
            entities.Where(e => e.IsVisible).ToList(),
            result.Bends,
            1,
            null,
            editedProgram: null,
            entityRoles: roles);
    }

    // The program layer of the move through a source-frame point, or null when there is none.
    private static LayerType? LayerAt(Drawing drawing, Vector sourcePoint)
    {
        var point = sourcePoint - drawing.Source.Offset;
        var geometry = ConvertProgram.ToGeometry(drawing.Program)
            .Where(e => e.Layer != SpecialLayers.Rapid)
            .FirstOrDefault(e => e.ClosestPointTo(point).DistanceTo(point) < 1e-6);
        if (geometry == null)
            return null;
        if (geometry.Layer == SpecialLayers.Scribe)
            return LayerType.Scribe;
        return geometry.Layer == SpecialLayers.Cut ? LayerType.Cut : (LayerType?)LayerType.Display;
    }

    private static byte[] Save(Drawing drawing)
    {
        var nest = new Nest { Name = "roles" };
        nest.Drawings.Add(drawing);
        using var stream = new MemoryStream();
        Assert.True(new NestWriter(nest).Write(stream));
        return stream.ToArray();
    }

    private static Nest Read(byte[] bytes) => new NestReader(new MemoryStream(bytes)).Read();
}
