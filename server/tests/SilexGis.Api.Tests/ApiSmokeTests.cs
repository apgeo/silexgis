// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using SilexGis.Api.Tests.Support;

namespace SilexGis.Api.Tests;

public sealed class ApiSmokeTests : IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;

    public ApiSmokeTests(PostgresFixture postgres) => factory = new SilexGisApiFactory(postgres.ConnectionString);

    [Fact]
    public async Task Health_live_returns_ok()
    {
        var response = await factory.CreateClient().GetAsync("/health/live");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Health_ready_returns_ok_with_database_check()
    {
        var response = await factory.CreateClient().GetAsync("/health/ready");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("Healthy");
    }

    [Fact]
    public async Task About_reports_name_version_license_and_source()
    {
        var about = await factory.CreateClient().GetFromJsonAsync<AboutResponse>("/api/v1/about");

        about.ShouldNotBeNull();
        about.Name.ShouldBe("SilexGIS");
        about.License.ShouldBe("AGPL-3.0-or-later");
        about.Version.ShouldNotBeNullOrWhiteSpace();
        about.SourceUrl.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task OpenApi_document_is_served_and_publishes_the_feature_surface()
    {
        var response = await factory.CreateClient().GetAsync("/openapi/v1.json");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // The generated TS client is built from this document, so the routes the client
        // navigates by must actually be in it.
        var document = await response.Content.ReadAsStringAsync();
        document.ShouldContain("/api/v1/caves/{id}/summary");
        document.ShouldContain("/api/v1/caves/{caveId}/entrances");
        document.ShouldContain("/api/v1/features/{id}");
        document.ShouldContain("/api/v1/features/{id}/parents");
        document.ShouldContain("/api/v1/features/{id}/links");
        document.ShouldContain("/api/v1/centerlines/{id}");
        document.ShouldContain("/api/v1/trip-logs/{id}/state");
        document.ShouldContain("/api/v1/stats/cavers/{id}");
        document.ShouldContain("/api/v1/stats/caves/{id}");
        document.ShouldContain("/api/v1/stats/caving-groups/{id}");
        document.ShouldContain("/api/v1/stats/cavers/{id}/export");
        document.ShouldContain("/api/v1/stats/caves/{id}/export");
        document.ShouldContain("/api/v1/stats/caving-groups/{id}/export");
        document.ShouldContain("/api/v1/stats/expeditions/{id}");
        document.ShouldContain("/api/v1/stats/expeditions/{id}/export");
        document.ShouldContain("/api/v1/export/features");
        document.ShouldContain("/api/v1/export/caves/karstlink");
        document.ShouldContain("/api/v1/shared/features/{token}");

        // Routes the feature supertype replaced must be gone, not merely unused.
        document.ShouldNotContain("surface-features");
        document.ShouldNotContain("/api/v1/cave-centerlines/");

        // A trip's lifecycle is one route naming the state it moves to, so the two verbs it
        // replaced must be gone rather than left beside it: two roads into the same table are how
        // the two come to disagree.
        document.ShouldNotContain("/api/v1/trip-logs/{id}/publish");
        document.ShouldNotContain("/api/v1/trip-logs/{id}/unpublish");
    }

    /// <summary>
    /// The served description says the API is behind a bearer token, and names the operations a
    /// separate application is generated from.
    /// </summary>
    /// <remarks>
    /// Both are recent and both are invisible in the browser client — it addresses routes by path
    /// and sends its token from code — so nothing else here would notice either going away. The
    /// application on the other side of the mobile protocol is generated from this document alone:
    /// without the scheme it would emit calls carrying no credential, and without the names its
    /// method names would be invented from the paths and would move whenever a path is tidied.
    /// </remarks>
    [Fact]
    public async Task OpenApi_document_declares_the_bearer_scheme_and_names_the_sync_operations()
    {
        var response = await factory.CreateClient().GetAsync("/openapi/v1.json");
        var document = await response.Content.ReadFromJsonAsync<JsonElement>();

        var scheme = document.GetProperty("components").GetProperty("securitySchemes")
            .GetProperty("bearerAuth");
        scheme.GetProperty("type").GetString().ShouldBe("http");
        scheme.GetProperty("scheme").GetString().ShouldBe("bearer");

        // Document-wide, not per operation: everything under the versioned prefix needs a token.
        document.GetProperty("security").EnumerateArray()
            .Select(entry => entry.EnumerateObject().Select(p => p.Name))
            .SelectMany(names => names)
            .ShouldContain("bearerAuth");

        var named = new List<(string Path, string Id)>();
        foreach (var path in document.GetProperty("paths").EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                if (operation.Value.ValueKind is JsonValueKind.Object
                    && operation.Value.TryGetProperty("operationId", out var id))
                {
                    named.Add((path.Name, id.GetString()!));
                }
            }
        }

        // The sync operations keep the names they were given by hand, which the application on the
        // other side of that protocol is generated from: every other operation is named after its
        // handler, and that derivation must never reach these.
        named.Where(x => x.Path.StartsWith("/api/v1/sync", StringComparison.Ordinal)).Select(x => x.Id).ShouldBe(
            [
                "syncCapabilities", "syncListSets", "syncCreateSet", "syncGetSet",
                "syncReplaceSet", "syncDeleteSet", "syncDownload", "syncUpload",
            ],
            ignoreOrder: true);
    }

    /// <summary>
    /// Every operation in the served description has a name, no two share one, and each is a name
    /// a generator can turn into a method as it stands.
    /// </summary>
    [Fact]
    public async Task OpenApi_document_names_every_operation_once()
    {
        var operations = await OperationsAsync();

        var unnamed = operations.Where(o => o.Id is null).Select(o => $"{o.Verb} {o.Path}").ToList();
        unnamed.ShouldBeEmpty(
            "every operation needs a name; a handler that cannot give one (a lambda) is named with WithName");

        var shared = operations.GroupBy(o => o.Id).Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(o => $"{o.Verb} {o.Path}"))}").ToList();
        shared.ShouldBeEmpty("an operation name may be used once");

        operations.Select(o => o.Id!).Where(id => !System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-z][A-Za-z0-9]*$"))
            .ShouldBeEmpty("an operation name is lower camel case, letters and digits only");
    }

    /// <summary>
    /// The refusals every operation of a kind can send are in the description: a 401 wherever a
    /// token is needed, a 400 wherever a body is validated, and the problem shape they share
    /// carries the code a client branches on.
    /// </summary>
    [Fact]
    public async Task OpenApi_document_declares_the_refusals_every_guarded_and_validated_operation_can_send()
    {
        var document = await DocumentAsync();
        var operations = await OperationsAsync(document);

        // A guarded operation inherits the document's requirement; an open one says it has none.
        // Only the versioned API: the sign-in protocol's own routes beside it answer as that
        // protocol says, not as this API does.
        var guardedWithout401 = operations
            .Where(o => o.Path.StartsWith("/api/v1/", StringComparison.Ordinal))
            .Where(o => !o.Value.TryGetProperty("security", out var security) || security.GetArrayLength() > 0)
            .Where(o => !Responses(o.Value).Contains("401"))
            .Select(o => o.Id).ToList();
        guardedWithout401.ShouldBeEmpty("a route that needs a token can answer 401");

        // The cave write is validated, so it declares the validator's 400.
        var create = operations.Single(o => o.Id == "caveCreate");
        Responses(create.Value).ShouldContain("400");

        var problem = document.GetProperty("components").GetProperty("schemas").GetProperty("ProblemDetails");
        problem.GetProperty("properties").TryGetProperty("code", out _).ShouldBeTrue(
            "the problem shape must publish the code; it is the one member a client branches on");
    }

    /// <summary>
    /// The cave record's own operations declare each refusal their handlers send — the first slice
    /// declared in full, pinned so that one quietly dropped is noticed.
    /// </summary>
    [Fact]
    public async Task OpenApi_document_declares_the_cave_records_own_refusals()
    {
        var operations = (await OperationsAsync()).ToDictionary(o => o.Id!);
        var expected = new Dictionary<string, string[]>
        {
            ["caveList"] = ["400", "401", "404"],
            ["caveGet"] = ["401", "404"],
            ["caveGetSummary"] = ["401", "404"],
            ["caveCreate"] = ["400", "401", "403"],
            ["caveUpdate"] = ["400", "401", "403", "404", "412", "428"],
            ["caveDelete"] = ["401", "403", "404", "412"],
            ["entranceList"] = ["401", "404"],
            ["entranceCreate"] = ["400", "401", "403", "404"],
            ["entranceUpdate"] = ["400", "401", "403", "404", "412"],
            ["entranceDelete"] = ["401", "403", "404", "412"],
            ["caveExternalIdList"] = ["401", "404"],
            ["caveExternalIdWrite"] = ["400", "401", "403", "404"],
            ["caveExternalIdLookup"] = ["400", "401", "403", "404"],
            ["caveDepthPlaceList"] = ["401", "404"],
            ["caveDepthPlaceWrite"] = ["400", "401", "404", "409"],
            ["caveDepthPlaceDelete"] = ["401", "404"],
        };

        foreach (var (id, statuses) in expected)
        {
            operations.ShouldContainKey(id);
            Responses(operations[id].Value).Where(code => code[0] is '4' or '5')
                .ShouldBe(statuses, ignoreOrder: true, customMessage: id);
        }
    }

    /// <summary>
    /// The feature list and the two vector exports read a filter document or a saved filter, so each
    /// can answer a filter it cannot read (400) or a saved filter it cannot find (404) — pinned so
    /// that one quietly dropped is noticed. The filter query itself answers a filter it cannot read
    /// or run, a place to measure from among them, with 400.
    /// </summary>
    [Fact]
    public async Task OpenApi_document_declares_the_filtered_lists_and_exports_own_refusals()
    {
        var operations = (await OperationsAsync()).ToDictionary(o => o.Id!);
        var expected = new Dictionary<string, string[]>
        {
            ["featureList"] = ["400", "401", "404"],
            ["filterQuery"] = ["400", "401", "403"],
            ["exportExportFeatures"] = ["400", "401", "404"],
            ["exportExportCaves"] = ["400", "401", "404"],
        };

        foreach (var (id, statuses) in expected)
        {
            operations.ShouldContainKey(id);
            Responses(operations[id].Value).Where(code => code[0] is '4' or '5')
                .ShouldBe(statuses, ignoreOrder: true, customMessage: id);
        }
    }

    /// <summary>
    /// The two layers every map draws, the survey lines and walls a filtered map draws beside
    /// them, the saved views that keep the map's filter, and a shared view's link with the data it
    /// draws, declare each refusal their handlers send — pinned so that one quietly dropped is
    /// noticed. A saved view's write refuses a filter holding a place of its own (400). The link's
    /// routes need no token, so they carry no 401, and sit behind a rate limit, so they carry 429.
    /// </summary>
    [Fact]
    public async Task OpenApi_document_declares_the_map_layers_and_shared_links_own_refusals()
    {
        var operations = (await OperationsAsync()).ToDictionary(o => o.Id!);
        var expected = new Dictionary<string, string[]>
        {
            ["mapCaveEntrances"] = ["400", "401", "404"],
            ["mapFeatures"] = ["400", "401", "404"],
            ["mapCaveCenterlines"] = ["400", "401", "404"],
            ["caveMeshMapCaveMeshes"] = ["400", "401", "404"],
            ["mapViewShared"] = ["404", "429"],
            ["mapViewCreate"] = ["400", "401", "403"],
            ["mapViewUpdate"] = ["400", "401", "403", "404"],
            ["sharedViewMapEntrances"] = ["400", "404", "429"],
            ["sharedViewMapFeatures"] = ["400", "404", "429"],
        };

        foreach (var (id, statuses) in expected)
        {
            operations.ShouldContainKey(id);
            Responses(operations[id].Value).Where(code => code[0] is '4' or '5')
                .ShouldBe(statuses, ignoreOrder: true, customMessage: id);
        }
    }

    /// <summary>
    /// The feature list and the two vector exports declare the 400 they send for a window that is
    /// not one or a filter that cannot be run (and the exports for an unknown format), and the 404
    /// of a saved filter that is not there for this caller: each reads its list's filter.
    /// </summary>
    [Fact]
    public async Task OpenApi_document_declares_the_feature_list_and_vector_exports_own_refusals()
    {
        var operations = (await OperationsAsync()).ToDictionary(o => o.Id!);
        var expected = new Dictionary<string, string[]>
        {
            ["featureList"] = ["400", "401", "404"],
            ["exportExportCaves"] = ["400", "401", "404"],
            ["exportExportFeatures"] = ["400", "401", "404"],
        };

        foreach (var (id, statuses) in expected)
        {
            operations.ShouldContainKey(id);
            Responses(operations[id].Value).Where(code => code[0] is '4' or '5')
                .ShouldBe(statuses, ignoreOrder: true, customMessage: id);
        }
    }

    /// <summary>
    /// The gallery reads a filter, so it declares the refusals a filter brings: an unreadable,
    /// invalid, ambiguous or off-world filter (400) and a saved filter that is not there for this
    /// caller (404) — beside the 400 a malformed map extent always sent.
    /// </summary>
    [Fact]
    public async Task OpenApi_document_declares_the_gallerys_filter_refusals()
    {
        var operations = (await OperationsAsync()).ToDictionary(o => o.Id!);

        operations.ShouldContainKey("photoList");
        Responses(operations["photoList"].Value).Where(code => code[0] is '4' or '5')
            .ShouldBe(["400", "401", "404"], ignoreOrder: true);
    }

    /// <summary>
    /// The other lists that read a filter declare the same refusals the gallery does; the tag
    /// catalogue, which a stored filter's tag condition is read back through, declares the 403 of
    /// a catalogue an installation has closed; and the link-target feed declares the 400 it sends
    /// for an unknown kind and for a kind that is now found through the filter query instead.
    /// </summary>
    [Fact]
    public async Task OpenApi_document_declares_the_filtered_lists_and_their_lookups_own_refusals()
    {
        var operations = (await OperationsAsync()).ToDictionary(o => o.Id!);
        var expected = new Dictionary<string, string[]>
        {
            ["geofileList"] = ["400", "401", "404"],
            ["georeferencedMapList"] = ["400", "401", "404"],
            ["tagListTags"] = ["401", "403"],
            ["resLinkSearchTargets"] = ["400", "401"],
        };

        foreach (var (id, statuses) in expected)
        {
            operations.ShouldContainKey(id);
            Responses(operations[id].Value).Where(code => code[0] is '4' or '5')
                .ShouldBe(statuses, ignoreOrder: true, customMessage: id);
        }
    }

    /// <summary>
    /// Every question that may be scoped by a study area declares the 404 the shared gate sends
    /// for an area the caller may not use — the same refusal for one that is not there — and the
    /// karst-index layer declares the 400 of a window it cannot read.
    /// </summary>
    [Fact]
    public async Task OpenApi_document_declares_the_study_area_questions_and_karst_layers_own_refusals()
    {
        var operations = (await OperationsAsync()).ToDictionary(o => o.Id!);
        var expected = new Dictionary<string, string[]>
        {
            ["mapKarstIndexKarstIndex"] = ["400", "401"],
            ["mapDensityDensity"] = ["400", "401", "404"],
            ["mapPointPatternPointPattern"] = ["400", "401", "404"],
            ["areaKarstStatisticsAreaStatistics"] = ["401", "404"],
            ["areaKarstStatisticsAreaStatisticsCsv"] = ["401", "404"],
            ["registryStatisticsDistribution"] = ["400", "401", "404"],
            ["registryStatisticsExportDistribution"] = ["400", "401", "404"],
            ["registryStatisticsCorrelation"] = ["400", "401", "404"],
            ["registryStatisticsExportCorrelation"] = ["400", "401", "404"],
            ["registryStatisticsClustering"] = ["400", "401", "404"],
            ["registryStatisticsCategories"] = ["400", "401", "404"],
            ["registryStatisticsRegions"] = ["400", "401", "404"],
            ["registryStatisticsExportRegions"] = ["400", "401", "404"],
        };

        foreach (var (id, statuses) in expected)
        {
            operations.ShouldContainKey(id);
            Responses(operations[id].Value).Where(code => code[0] is '4' or '5')
                .ShouldBe(statuses, ignoreOrder: true, customMessage: id);
        }
    }

    /// <summary>
    /// An enumeration's own values never include null; a property that may be null says so itself.
    /// </summary>
    [Fact]
    public async Task OpenApi_document_keeps_null_out_of_an_enumerations_values_and_on_the_property_that_takes_it()
    {
        var schemas = (await DocumentAsync()).GetProperty("components").GetProperty("schemas");

        var kinds = schemas.GetProperty("EventKind").GetProperty("enum").EnumerateArray().ToList();
        kinds.ShouldNotBeEmpty();
        kinds.ShouldAllBe(value => value.ValueKind == JsonValueKind.String);

        // The positive twin: where the kind may be absent, the property still says so.
        var calendarKind = schemas.GetProperty("CalendarEntryDto").GetProperty("properties").GetProperty("kind");
        calendarKind.GetRawText().ShouldContain("\"null\"");
        calendarKind.GetRawText().ShouldContain("#/components/schemas/EventKind");
    }

    /// <summary>
    /// What a request's validator always requires is published, so a generated client's types
    /// refuse what the server would refuse; what it requires only conditionally is not.
    /// </summary>
    [Fact]
    public async Task OpenApi_document_publishes_what_a_requests_validator_always_requires()
    {
        var schemas = (await DocumentAsync()).GetProperty("components").GetProperty("schemas");

        var kind = schemas.GetProperty("FeatureTypeRequest");
        var properties = kind.GetProperty("properties");
        properties.GetProperty("code").GetProperty("minLength").GetInt32().ShouldBe(1);
        properties.GetProperty("acceptedGeometryClasses").GetProperty("minItems").GetInt32().ShouldBe(1);

        // A member typed as optional that the validator refuses when absent loses its "or null".
        var bands = schemas.GetProperty("SaveCaveLevelBandsRequest").GetProperty("properties").GetProperty("bands");
        bands.GetRawText().ShouldNotContain("\"null\"");
        bands.GetProperty("minItems").GetInt32().ShouldBe(1);

        // The positive twins: a member with no presence rule keeps its "or null", and so does one
        // whose rule applies only when it is given.
        properties.GetProperty("description").GetRawText().ShouldContain("\"null\"");
        properties.GetProperty("symbolFile").GetRawText().ShouldContain("\"null\"");
        properties.GetProperty("symbolFile").TryGetProperty("minLength", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task A_request_without_a_token_is_refused_with_a_code()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/caves");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        problem.GetProperty("code").GetString().ShouldBe("auth.unauthenticated");
    }

    private async Task<JsonElement> DocumentAsync()
    {
        var response = await factory.CreateClient().GetAsync("/openapi/v1.json");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<List<(string Path, string Verb, string? Id, JsonElement Value)>> OperationsAsync(
        JsonElement? document = null)
    {
        var paths = (document ?? await DocumentAsync()).GetProperty("paths");
        var operations = new List<(string, string, string?, JsonElement)>();
        foreach (var path in paths.EnumerateObject())
        {
            foreach (var verb in path.Value.EnumerateObject().Where(v => v.Value.ValueKind == JsonValueKind.Object
                && v.Name is "get" or "post" or "put" or "patch" or "delete"))
            {
                var id = verb.Value.TryGetProperty("operationId", out var name) ? name.GetString() : null;
                operations.Add((path.Name, verb.Name, id, verb.Value));
            }
        }

        return operations;
    }

    private static List<string> Responses(JsonElement operation) =>
        operation.GetProperty("responses").EnumerateObject().Select(p => p.Name).ToList();

    /// <summary>
    /// The document-wide requirement is taken back off the routes that are deliberately open, and
    /// the sign-in calls are the reason it matters: a client has to issue them with no credential
    /// in hand, because obtaining one is what they are for.
    /// </summary>
    /// <remarks>
    /// Asserted on a route that is guarded as well, so this cannot pass by the exemption being
    /// applied to everything — which would put the description back to describing the whole server
    /// as open, the exact state the bearer declaration was added to end.
    /// </remarks>
    [Fact]
    public async Task OpenApi_document_exempts_the_deliberately_open_routes_from_the_bearer_requirement()
    {
        var response = await factory.CreateClient().GetAsync("/openapi/v1.json");
        var document = await response.Content.ReadFromJsonAsync<JsonElement>();
        var paths = document.GetProperty("paths");

        // "Present and empty" is how OpenAPI says "no requirement on this operation", and it is
        // what overrides the document-level entry. An absent property would inherit it instead.
        var login = paths.GetProperty("/api/v1/auth/login").GetProperty("post");
        login.TryGetProperty("security", out var loginSecurity).ShouldBeTrue(
            "The sign-in route must state that it needs no token, or a generated client will "
            + "demand one before it can obtain one.");
        loginSecurity.EnumerateArray().ShouldBeEmpty();

        // A guarded route says nothing of its own and inherits the document's requirement. Both
        // halves of the mobile contract are named, the write half included: a generated client
        // that issued an upload with no credential would be pushing a caver's survey at whatever
        // the server does with an unauthenticated write.
        foreach (var route in new[] { "download", "upload" })
        {
            var operation = paths.GetProperty($"/api/v1/sync/sets/{{id}}/{route}");
            var verb = operation.TryGetProperty("get", out var get) ? get : operation.GetProperty("post");
            verb.TryGetProperty("security", out _).ShouldBeFalse();
        }
    }

    /// <summary>
    /// The failures a device has to act on are in the served description, not only in the code
    /// that produces them.
    /// </summary>
    /// <remarks>
    /// The application on the other side of this protocol branches on these: a stale cursor means
    /// start again, a contract mismatch means stop and tell the caver to update, a batch that is
    /// too large means send fewer rows. A status a generated client has never been told about is
    /// a status it handles as "something went wrong", which for the first of those loses a
    /// device's place in the stream and for the last is an upload that never succeeds however
    /// many times it is retried.
    /// </remarks>
    [Fact]
    public async Task OpenApi_document_declares_the_statuses_a_device_has_to_branch_on()
    {
        var response = await factory.CreateClient().GetAsync("/openapi/v1.json");
        var document = await response.Content.ReadFromJsonAsync<JsonElement>();
        var paths = document.GetProperty("paths");

        var download = paths.GetProperty("/api/v1/sync/sets/{id}/download").GetProperty("get")
            .GetProperty("responses").EnumerateObject().Select(p => p.Name).ToList();
        download.ShouldContain("400");
        download.ShouldContain("404");
        download.ShouldContain("409");

        var upload = paths.GetProperty("/api/v1/sync/sets/{id}/upload").GetProperty("post")
            .GetProperty("responses").EnumerateObject().Select(p => p.Name).ToList();
        upload.ShouldContain("400");
        upload.ShouldContain("404");
        upload.ShouldContain("409");

        // A row that lost its arbitration is not one of these. It rides a 200 beside the rows
        // that were written, because a batch of forty is answered row by row — and a device told
        // about a conflict by a status code would have to throw the other thirty-nine away.
        upload.ShouldContain("200");
    }

    public void Dispose() => factory.Dispose();

    private sealed record AboutResponse(string Name, string Version, string License, string SourceUrl);
}
