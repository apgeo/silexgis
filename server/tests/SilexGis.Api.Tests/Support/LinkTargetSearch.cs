// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shouldly;
using SilexGis.Api.Features.Filters;
using SilexGis.Domain.Filters;

namespace SilexGis.Api.Tests.Support;

/// <summary>
/// What the link dialog's target picker asks when somebody types part of a name, asked the way
/// the picker asks it.
/// </summary>
/// <remarks>
/// Most kinds are filter worlds, and for them the picker sends one "name contains" condition to
/// the filter query; the rest — people, clubs and camps — keep the link surface's own search
/// feed. A test that says "the picker finds only what the caller may read" therefore has to put
/// its question to whichever of the two now answers it, or it proves something about a route
/// nobody calls. The field each world matches typed text against is the one the client's selector
/// names for that world; a kind missing from this table is one the feed still serves.
/// </remarks>
public static class LinkTargetSearch
{
    private static readonly Dictionary<string, (string World, string Field)> Worlds = new()
    {
        ["feature"] = ("feature", FeatureFilterFields.Name),
        ["document"] = ("document", DocumentFilterFields.Title),
        ["tripLog"] = ("tripLog", TripLogFilterFields.Title),
        ["mapView"] = ("mapView", CommonFilterFields.Name),
        ["cabinet"] = ("cabinet", CommonFilterFields.Name),
        ["surveyModel"] = ("surveyModel", CommonFilterFields.Name),
        ["geofile"] = ("geofile", CommonFilterFields.Name),
    };

    /// <summary>The wire's own settings: enum members travel as camel-cased names.</summary>
    private static readonly JsonSerializerOptions Wire = Build();

    private static JsonSerializerOptions Build()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    /// <summary>Whether this link target kind is found through the filter query.</summary>
    public static bool IsWorld(string type) => Worlds.ContainsKey(type);

    /// <summary>The ids the picker would offer this caller for what was typed.</summary>
    public static async Task<List<Guid>> IdsAsync(HttpClient client, string type, string query)
    {
        if (!Worlds.TryGetValue(type, out var world))
        {
            var hits = await client.GetFromJsonAsync<JsonElement>(
                $"/api/v1/reslinks/targets/search?type={type}&q={Uri.EscapeDataString(query)}");
            return [.. hits.EnumerateArray().Select(h => h.GetProperty("id").GetGuid())];
        }

        var document = new FilterDocument
        {
            Scope =
            [
                new WorldScope(
                    world.World,
                    new ConditionNode(world.Field, FilterOp.Contains, [new TextValue(query)])),
            ],
        };
        var response = await client.PostAsJsonAsync(
            "/api/v1/filters/query", new FilterQueryRequest(document, PageSize: 50, Count: false), Wire);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var answer = await response.Content.ReadFromJsonAsync<FilterQueryResponse>(Wire);
        return [.. answer!.Worlds.Single(w => w.World == world.World).Hits.Select(h => h.Id)];
    }
}
