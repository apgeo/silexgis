// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Common;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Infrastructure.Identity;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// A value that has to be one of a closed list of names, arriving as something else: a word the
/// enum does not have, a null where the shape has no room for one, the camelCase spelling the
/// responses use on a query string, a number, a comma list. Every shape is refused as validation
/// — 400, a stable code, the member named — and never as a server fault, on every route alike,
/// because the refusal is produced once for the whole application rather than per route.
/// </summary>
/// <remarks>
/// The framework reads and binds a request before any filter or validator runs, so these cases
/// used to answer 500 on every enum-bearing route: the exception-handling middleware met the
/// binding failure and knew nothing better to say. The first tests here drive one route per shape
/// and assert the answer; the last walks the endpoint table and asserts that no handler binds an
/// enum from a route or query string directly, which is the only way the query-string half can
/// come back.
/// </remarks>
public sealed class EnumBindingTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient owner = null!;
    private long caveTypeId;

    public EnumBindingTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"enum-own-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"enum-own-{suffix}@t.local");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        owner.Dispose();
        factory.Dispose();
    }

    [Fact]
    public async Task A_word_the_enum_does_not_have_in_a_body_is_refused_as_validation_naming_the_member()
    {
        var tripId = await CreateTripAsync();

        var nonsense = await owner.PostWithIfMatchAsync($"/api/v1/trip-logs/{tripId}/state", new { state = "abandoned" });
        var body = await nonsense.Content.ReadAsStringAsync();
        nonsense.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body);
        nonsense.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        (await CodeAsync(nonsense)).ShouldBe(ApiProblems.InvalidEnumCode);

        // The member is named the way the validators name it, and the refusal lists what it
        // takes — in the spelling a response would print — without repeating what was sent.
        var errors = await ErrorsAsync(nonsense);
        errors.Keys.ShouldBe(["State"]);
        errors["State"].ShouldHaveSingleItem().ShouldContain("draft");
        body.ShouldNotContain("abandoned");

        // And nothing moved.
        (await ReadTripAsync(tripId)).GetProperty("state").GetString().ShouldBe("draft");

        // The same answer from a route whose body is a whole trip: the member is the one named.
        var badVisibility = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"Bad visibility {suffix}",
            tripDate = "2026-07-01",
            caveIds = Array.Empty<Guid>(),
            participants = Array.Empty<object>(),
            visibility = "nonsense",
        });
        badVisibility.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await badVisibility.Content.ReadAsStringAsync());
        (await CodeAsync(badVisibility)).ShouldBe(ApiProblems.InvalidEnumCode);
        (await ErrorsAsync(badVisibility)).Keys.ShouldBe(["Visibility"]);
    }

    /// <summary>
    /// The cave's exploration status has no nullable form on the write request, so a null is not
    /// a value the shape can hold — the same refusal as a word it does not have, because to the
    /// caller it is the same mistake: a closed list, and something not on it.
    /// </summary>
    [Fact]
    public async Task A_null_where_the_shape_has_no_room_for_one_is_refused_the_same_way()
    {
        var refused = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Null status {suffix}",
            caveTypeId,
            visibility = "authenticated",
            locationProtected = false,
            explorationStatus = (string?)null,
            isShowCave = false,
        });
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync());
        (await CodeAsync(refused)).ShouldBe(ApiProblems.InvalidEnumCode);
        (await ErrorsAsync(refused)).Keys.ShouldBe(["ExplorationStatus"]);
    }

    /// <summary>
    /// A member two lists deep is named by its whole path, so a client can point at the cell that
    /// was wrong rather than at the matrix.
    /// </summary>
    [Fact]
    public async Task A_member_inside_a_list_is_named_by_its_path()
    {
        var refused = await owner.PutAsJsonAsync("/api/v1/me/notifications/", new
        {
            categories = new[]
            {
                new
                {
                    category = "cavingGroupMembership",
                    channels = new[] { new { channel = "email", choice = "sometimes" } },
                },
            },
        });
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync());
        (await CodeAsync(refused)).ShouldBe(ApiProblems.InvalidEnumCode);
        (await ErrorsAsync(refused)).Keys.ShouldBe(["Categories[0].Channels[0].Choice"]);
    }

    /// <summary>
    /// A transition body carries one enum and nothing else, and that enum's zero value is a legal
    /// target — the workshop — so a body naming nothing must not read as naming it. The request
    /// declares the state nullable and the validator demands it be present; this pins both halves
    /// on the wire, and the second test below pins the shape for every such body in the
    /// application, so the next slice cannot repeat the trap.
    /// </summary>
    [Fact]
    public async Task An_all_enum_body_cannot_read_absence_as_the_zero_value()
    {
        var tripId = await CreateTripAsync();
        (await Transition(tripId, "proposed")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var empty = await owner.PostWithIfMatchAsync($"/api/v1/trip-logs/{tripId}/state", new { });
        empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await empty.Content.ReadAsStringAsync());
        (await CodeAsync(empty)).ShouldBe("validation.failed");

        var nulled = await owner.PostWithIfMatchAsync($"/api/v1/trip-logs/{tripId}/state", new { state = (string?)null });
        nulled.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await nulled.Content.ReadAsStringAsync());
        (await CodeAsync(nulled)).ShouldBe("validation.failed");

        (await ReadTripAsync(tripId)).GetProperty("state").GetString().ShouldBe("proposed");
    }

    [Fact]
    public void Every_request_body_made_only_of_enums_declares_them_nullable()
    {
        var offenders = typeof(Program).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith("Request", StringComparison.Ordinal))
            .Select(t => (Type: t, Properties: t.GetProperties(BindingFlags.Public | BindingFlags.Instance)))
            .Where(x => x.Properties.Length > 0
                && x.Properties.All(p => (Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType).IsEnum))
            .SelectMany(x => x.Properties
                .Where(p => Nullable.GetUnderlyingType(p.PropertyType) is null)
                .Select(p => $"{x.Type.Name}.{p.Name}"))
            .ToList();

        // Absent and zero are the same bytes to a non-nullable enum, and a request body that is
        // nothing but enums has no other member whose absence a validator could notice.
        offenders.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_query_string_enum_is_read_in_the_spelling_the_responses_use_and_refused_as_validation_otherwise()
    {
        var opened = await owner.PostAsJsonAsync("/api/v1/upload-batches/", new { label = $"Enum probe {suffix}" });
        opened.IsSuccessStatusCode.ShouldBeTrue(await opened.Content.ReadAsStringAsync());
        var batchId = (await opened.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var items = $"/api/v1/upload-batches/{batchId}/items";

        // The spelling a response prints, and the declared one: both are the name.
        (await owner.GetAsync($"{items}?outcome=stored")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await owner.GetAsync($"{items}?outcome=Stored")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // A word that is not a member, the member's number, and a list — none of which is a name.
        foreach (var written in new[] { "nonsense", "1", "stored,pending" })
        {
            var refused = await owner.GetAsync($"{items}?outcome={written}");
            var body = await refused.Content.ReadAsStringAsync();
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, $"{written}: {body}");
            (await CodeAsync(refused)).ShouldBe(ApiProblems.InvalidEnumCode, written);
            var errors = await ErrorsAsync(refused);
            errors.Keys.ShouldBe(["outcome"]);
            errors["outcome"].ShouldHaveSingleItem().ShouldContain("stored");
            // Only the sentences are checked for an echo: the problem's own trace identifier is
            // hexadecimal and may contain any digit.
            (await DetailAsync(refused)).ShouldNotContain(written);
            errors["outcome"].Single().ShouldNotContain(written);
        }
    }

    [Fact]
    public async Task Malformed_JSON_is_refused_as_a_bad_request_not_answered_as_a_fault()
    {
        var tripId = await CreateTripAsync();

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/trip-logs/{tripId}/state")
        {
            Content = new StringContent("{\"state\": ", Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("If-Match", "*");
        var broken = await owner.SendAsync(request);

        broken.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await broken.Content.ReadAsStringAsync());
        (await CodeAsync(broken)).ShouldBe(BindingProblemHandler.BodyInvalidCode);
    }

    [Fact]
    public async Task A_query_value_that_is_not_what_the_handler_declares_is_refused_naming_the_parameter()
    {
        var opened = await owner.PostAsJsonAsync("/api/v1/upload-batches/", new { label = $"Page probe {suffix}" });
        var batchId = (await opened.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var refused = await owner.GetAsync($"/api/v1/upload-batches/{batchId}/items?page=abc");
        var body = await refused.Content.ReadAsStringAsync();
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body);
        (await CodeAsync(refused)).ShouldBe(BindingProblemHandler.BindingFailedCode);
        (await ErrorsAsync(refused)).Keys.ShouldBe(["page"]);
        (await DetailAsync(refused)).ShouldNotContain("abc");
    }

    /// <summary>
    /// The audit, kept: a handler parameter of enum type that is not the body is bound by the
    /// framework's own case-sensitive parse, which answers the camelCase spelling every response
    /// uses with a refusal of the wrong kind and accepts numbers and comma lists it should not. A
    /// closed vocabulary reaches a route or query string as text, parsed by the shared parser.
    /// </summary>
    [Fact]
    public void No_handler_binds_an_enum_from_a_route_or_query_string()
    {
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints;
        var offenders = new List<string>();

        foreach (var endpoint in endpoints.OfType<RouteEndpoint>())
        {
            var method = endpoint.Metadata.GetMetadata<MethodInfo>();
            if (method is null)
            {
                continue;
            }

            var bodyType = endpoint.Metadata.GetMetadata<IAcceptsMetadata>()?.RequestType;
            foreach (var parameter in method.GetParameters())
            {
                if (parameter.GetCustomAttribute<FromBodyAttribute>() is not null
                    || parameter.GetCustomAttribute<FromServicesAttribute>() is not null
                    || parameter.ParameterType == bodyType)
                {
                    continue;
                }

                if (IsEnumLike(parameter.ParameterType))
                {
                    offenders.Add($"{endpoint.RoutePattern.RawText}: {method.Name}({parameter.ParameterType.Name} {parameter.Name})");
                }

                // A record bound member-wise from the query is the same thing, one level down.
                if (parameter.GetCustomAttribute<AsParametersAttribute>() is not null)
                {
                    offenders.AddRange(parameter.ParameterType
                        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .Where(p => IsEnumLike(p.PropertyType))
                        .Select(p => $"{endpoint.RoutePattern.RawText}: {method.Name}({parameter.ParameterType.Name}.{p.Name})"));
                }
            }
        }

        offenders.ShouldBeEmpty();
    }

    private static bool IsEnumLike(Type type) => (Nullable.GetUnderlyingType(type) ?? type).IsEnum;

    private async Task<Guid> CreateTripAsync()
    {
        var created = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"Enum trip {Guid.NewGuid():N}",
            tripDate = "2026-07-01",
            caveIds = Array.Empty<Guid>(),
            participants = Array.Empty<object>(),
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> Transition(Guid tripId, string state) =>
        owner.PostWithIfMatchAsync($"/api/v1/trip-logs/{tripId}/state", new { state });

    private async Task<JsonElement> ReadTripAsync(Guid tripId) =>
        await owner.GetFromJsonAsync<JsonElement>($"/api/v1/trip-logs/{tripId}");

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static async Task<string> DetailAsync(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.GetProperty("detail").GetString() ?? string.Empty;
    }

    private static async Task<Dictionary<string, string[]>> ErrorsAsync(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.GetProperty("errors").Deserialize<Dictionary<string, string[]>>()!;
    }
}
