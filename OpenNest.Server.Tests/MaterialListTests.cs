using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using OpenNest.Data;
using static OpenNest.Server.Tests.DrawingLibraryFixtures;
using static OpenNest.Server.Tests.DrawingSetFixtures;

namespace OpenNest.Server.Tests;

public sealed class MaterialListTests
{
    [Fact]
    public void V9Upgrade_StartsEmptyAndLeavesStoredMaterialsAlone()
    {
        using var factory = new ServerFactory();
        var nest = Guid.NewGuid();
        var drawing = Record();
        using (var database = new NestDatabase(factory.DatabasePath, pooling: false))
        {
            database.Insert(nest, Nest("A", " Stainless ", "304"), [1]);
            database.InsertDrawing(drawing.Id, drawing, Package());
        }
        Execute(factory.DatabasePath, "DROP TABLE material_grades; DROP TABLE materials; PRAGMA user_version=9;");

        using var upgraded = new NestDatabase(factory.DatabasePath, pooling: false);

        Assert.Equal(10L, Scalar(factory.DatabasePath, "PRAGMA user_version"));
        Assert.Empty(upgraded.ListMaterials());
        Assert.Equal((" Stainless ", "304"), (upgraded.Get(nest)!.Material, upgraded.Get(nest)!.MaterialGrade));
        Assert.Equal(drawing.MaterialName, upgraded.GetDrawing(drawing.Id)!.MaterialName);
    }

    [Fact]
    public void Add_IsIdempotentByKeyAndKeepsTheFirstSpelling()
    {
        using var factory = new ServerFactory();
        using var database = new NestDatabase(factory.DatabasePath, pooling: false);

        var (created, createdAdded) = database.AddMaterial(new MaterialListAddition { Name = "  Aluminum  5000 " });
        var (again, againAdded) = database.AddMaterial(new MaterialListAddition { Name = "ALUMINUM 5000" });
        var (graded, gradeAdded) = database.AddMaterial(new MaterialListAddition { Name = "aluminum 5000", Grade = "5052" });
        var (_, sameGradeAdded) = database.AddMaterial(new MaterialListAddition { Name = "Aluminum 5000", Grade = " 5052" });
        var (both, bothAdded) = database.AddMaterial(new MaterialListAddition { Name = "Copper", Grade = "C110" });

        Assert.Equal((true, false, true, false, true), (createdAdded, againAdded, gradeAdded, sameGradeAdded, bothAdded));
        Assert.Equal("Aluminum 5000", created.Name);
        Assert.Equal(("Aluminum 5000", 0), (again.Name, again.Grades.Count));
        Assert.Equal(("Aluminum 5000", "5052"), (graded.Name, Assert.Single(graded.Grades)));
        Assert.Equal(("Copper", "C110"), (both.Name, Assert.Single(both.Grades)));
        Assert.Equal(new[] { "Aluminum 5000", "Copper" }, database.ListMaterials().Select(m => m.Name));
    }

    [Fact]
    public void Add_SameGradeUnderTwoMaterials_ListsItUnderEach()
    {
        using var factory = new ServerFactory();
        using var database = new NestDatabase(factory.DatabasePath, pooling: false);

        database.AddMaterial(new MaterialListAddition { Name = "Stainless", Grade = "Grade A" });
        var (_, added) = database.AddMaterial(new MaterialListAddition { Name = "Mild Steel", Grade = "Grade A" });

        Assert.True(added);
        Assert.All(database.ListMaterials(), m => Assert.Equal("Grade A", Assert.Single(m.Grades)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Add_BlankName_IsRefusedWithNoChange(string? name)
    {
        using var factory = new ServerFactory();
        using var database = new NestDatabase(factory.DatabasePath, pooling: false);

        Assert.Throws<ArgumentException>(() => database.AddMaterial(new MaterialListAddition { Name = name, Grade = "304" }));
        Assert.Empty(database.ListMaterials());
    }

    [Fact]
    public async Task Api_PostReturnsCreatedThenOkAndGetListsTheMaterial()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();

        using var first = await client.PostAsync("/api/materials", Json("""{"name":"Stainless","grade":"304"}"""));
        using var repeat = await client.PostAsync("/api/materials", Json("""{"name":"stainless","grade":"304"}"""));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        var entry = (await repeat.Content.ReadFromJsonAsync<MaterialListEntry>(Options))!;
        Assert.Equal(("Stainless", "304"), (entry.Name, Assert.Single(entry.Grades)));
        var listed = Assert.Single((await client.GetFromJsonAsync<List<MaterialListEntry>>("/api/materials", Options))!);
        Assert.Equal(("Stainless", "304"), (listed.Name, Assert.Single(listed.Grades)));
    }

    [Theory]
    [InlineData("""{"grade":"304"}""")]
    [InlineData("""{"name":" "}""")]
    [InlineData("""{"name":"Stainless","density":0.29}""")]
    [InlineData("""{"name":"Stainless","grade":7}""")]
    [InlineData("""not json""")]
    [InlineData("""null""")]
    public async Task Api_RejectedAddition_IsBadRequestWithNoChange(string body)
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/materials", Json(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<MaterialListEntry>>("/api/materials", Options))!);
    }

    [Fact]
    public async Task Api_OverlongName_IsBadRequest()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var name = new string('x', DrawingLibraryRules.MaxTextLength + 1);

        using var response = await client.PostAsync("/api/materials", Json($$"""{"name":"{{name}}"}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static NestRecord Nest(string name, string material, string grade) => new()
    {
        Name = name,
        Material = material,
        MaterialGrade = grade,
        Thickness = 0.25,
        DateCreated = new DateTime(2026, 1, 1),
        DateModified = new DateTime(2026, 1, 2),
    };

    private static void Execute(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static object? Scalar(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }
}
