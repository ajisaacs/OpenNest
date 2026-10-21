using System.Text.Json;
using System.Text.Json.Serialization;
using OpenNest.Data;

namespace OpenNest.Server;

/// <summary>
/// <c>/api/materials</c>: the company material list. Adding is idempotent by
/// <see cref="SharedListNames.Key"/>: 201 when a material or grade was added, 200 when both
/// were already listed. Either way the reply is the material as listed.
/// </summary>
internal static class MaterialRoutes
{
    private static readonly JsonSerializerOptions AdditionOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static void MapMaterials(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/materials", (NestDatabase db) => Results.Ok(db.ListMaterials()));

        app.MapPost("/api/materials", async (HttpRequest request, NestDatabase db) =>
        {
            var (json, error) = await DrawingSetRoutes.ReadJson(request, "material");
            if (error is not null) return error;

            MaterialListAddition? addition;
            try { addition = JsonSerializer.Deserialize<MaterialListAddition>(json!, AdditionOptions); }
            catch (JsonException) { return Results.BadRequest("Invalid material JSON."); }
            if (MaterialListRules.GetError(addition) is { } invalid)
                return Results.BadRequest(invalid);

            var (entry, added) = db.AddMaterial(addition!);
            return added ? Results.Created("/api/materials", entry) : Results.Ok(entry);
        });
    }
}
