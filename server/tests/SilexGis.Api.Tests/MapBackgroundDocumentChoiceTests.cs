// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Map;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Which map backgrounds a document may copy, as an administrator decides it: who may decide, what
/// the published catalogue then says, and what a restart does to the decision.
/// </summary>
/// <remarks>
/// <para>
/// The catalogue file is rewritten into the database on every start, and for the entries it names
/// the file wins. An administrator's decision has to be the one thing about an entry that it does
/// not win over — so the restart here is a real one: the application is stopped and started again
/// on the same database, and reads the catalogue it ships with.
/// </para>
/// <para>
/// The sources named are the shipped ones, by name: one that ships marked, one that ships
/// unmarked, and one filed under restricted terms. Each test puts back what it changed, because
/// the tests of this class share a database and each starts from "nobody has decided anything".
/// </para>
/// </remarks>
public sealed class MapBackgroundDocumentChoiceTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string Marked = "OpenStreetMap";
    private const string Unmarked = "Esri World Imagery";

    private readonly string connectionString;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];
    private SilexGisApiFactory factory;

    private HttpClient admin = null!;
    private HttpClient editor = null!;
    private HttpClient reader = null!;
    private Guid adminId;

    public MapBackgroundDocumentChoiceTests(PostgresFixture postgres)
    {
        connectionString = postgres.ConnectionString;
        factory = new SilexGisApiFactory(connectionString);
    }

    public async Task InitializeAsync()
    {
        adminId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"mbd-adm-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"mbd-edt-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"mbd-rdr-{suffix}@t.local");
        await SignInAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        admin.Dispose();
        editor.Dispose();
        reader.Dispose();
        factory.Dispose();
    }

    [Fact]
    public async Task Only_an_administrator_reads_or_decides_which_backgrounds_a_document_may_copy()
    {
        var osm = await IdOfAsync(Marked);

        // An editor writes every kind of content and is not the installation's voice on what a
        // provider's terms allow.
        (await editor.GetAsync("/api/v1/admin/map-backgrounds/")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ChooseAsync(editor, osm, "off")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.GetAsync("/api/v1/admin/map-backgrounds/")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ChooseAsync(reader, osm, "off")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/admin/map-backgrounds/")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ChooseAsync(anonymous, osm, "off")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Nothing a refused caller sent was taken.
        (await PublishedAsync())[Marked].ShouldBeTrue();

        // The administrator: every published background, each with the catalogue's answer and
        // no decision of this installation's yet.
        var listed = await BackgroundsAsync();
        listed[Marked].ShouldBe(new Background(osm, true, true, "default", true));
        listed[Unmarked].InDocuments.ShouldBeFalse();
        listed[Unmarked].Choice.ShouldBe("default");
        listed.Keys.ShouldContain(name => name.StartsWith("Google ", StringComparison.Ordinal));
        listed.Where(pair => pair.Key.StartsWith("Google ", StringComparison.Ordinal))
            .ShouldAllBe(pair => !pair.Value.InDocuments && !pair.Value.CatalogueDefault);

        // The list is the published backgrounds and nothing else: a source withheld for want of
        // its key is drawn by nothing, and so is not something to decide about.
        var published = (await PublishedAsync()).Keys.ToHashSet();
        listed.Keys.ShouldAllBe(name => published.Contains(name));
    }

    [Fact]
    public async Task A_decision_is_what_the_catalogue_publishes_at_once_and_is_taken_back_to_the_catalogues_answer()
    {
        var osm = await IdOfAsync(Marked);
        var esri = await IdOfAsync(Unmarked);
        var auditBefore = await AuditCountAsync(esri);

        try
        {
            // Switched on where the catalogue says no.
            var on = await ChooseAsync(admin, esri, "on");
            var body = await on.Content.ReadAsStringAsync();
            on.StatusCode.ShouldBe(HttpStatusCode.OK, body);
            var answered = JsonDocument.Parse(body).RootElement;
            answered.GetProperty("inDocuments").GetBoolean().ShouldBeTrue();
            answered.GetProperty("catalogueDefault").GetBoolean().ShouldBeFalse();
            answered.GetProperty("choice").GetString().ShouldBe("on");

            // Switched off where the catalogue says yes.
            (await ChooseAsync(admin, osm, "off")).StatusCode.ShouldBe(HttpStatusCode.OK);

            // What every signed-in reader's write-up page goes by says so at once.
            var published = await PublishedAsync();
            published[Unmarked].ShouldBeTrue();
            published[Marked].ShouldBeFalse();

            // With every marked background switched off, none is left for a document to copy:
            // the picture is then drawn on a plain ground, as when none ships marked.
            foreach (var (name, background) in await BackgroundsAsync())
            {
                if (background.InDocuments)
                {
                    (await ChooseAsync(admin, background.Id, "off")).StatusCode
                        .ShouldBe(HttpStatusCode.OK, name);
                }
            }

            (await PublishedAsync()).Values.ShouldAllBe(inDocuments => !inDocuments);

            // Each decision is in the audit trail under whoever made it; saying the same thing
            // twice is not a second decision.
            (await ChooseAsync(admin, esri, "off")).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await AuditCountAsync(esri)).ShouldBe(auditBefore + 2);
            (await LastAuditUserAsync(esri)).ShouldBe(adminId);
        }
        finally
        {
            await ForgetEveryDecisionAsync();
        }

        // Taken back: the catalogue's own answers again.
        var restored = await PublishedAsync();
        restored[Marked].ShouldBeTrue();
        restored[Unmarked].ShouldBeFalse();
        (await BackgroundsAsync())[Unmarked].Choice.ShouldBe("default");
    }

    [Fact]
    public async Task A_restart_keeps_a_decision_and_the_catalogue_goes_on_correcting_what_nobody_decided()
    {
        var osm = await IdOfAsync(Marked);
        var esri = await IdOfAsync(Unmarked);

        try
        {
            (await ChooseAsync(admin, esri, "on")).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await ChooseAsync(admin, osm, "off")).StatusCode.ShouldBe(HttpStatusCode.OK);

            // Stopped and started again on the same database: the start reads the shipped
            // catalogue, which names both sources and says the opposite of each decision.
            await RestartAsync();

            var afterRestart = await BackgroundsAsync();
            afterRestart[Unmarked].ShouldBe(new Background(esri, true, false, "on", true));
            afterRestart[Marked].ShouldBe(new Background(osm, false, true, "off", true));
            var published = await PublishedAsync();
            published[Unmarked].ShouldBeTrue();
            published[Marked].ShouldBeFalse();

            // A later catalogue that changes its mind about both: its answer is recorded for
            // each, and neither decision moves.
            await SeedAsync(
                """
                <silexgis-map-layers version="1">
                  <layer name="OpenStreetMap" url="https://tile.example/{z}/{x}/{y}.png" attribution="© Somebody" default="true" />
                  <layer name="Esri World Imagery" url="https://imagery.example/{z}/{y}/{x}" attribution="© Somebody else" inDocuments="true" />
                </silexgis-map-layers>
                """);
            var afterCorrection = await BackgroundsAsync();
            afterCorrection[Marked].ShouldBe(new Background(osm, false, false, "off", true));
            afterCorrection[Unmarked].ShouldBe(new Background(esri, true, true, "on", true));

            // Taking a decision back returns the source to what the catalogue says now, not to
            // what it said when the decision was made.
            (await ChooseAsync(admin, osm, "default")).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await BackgroundsAsync())[Marked].ShouldBe(new Background(osm, false, false, "default", true));
        }
        finally
        {
            await ForgetEveryDecisionAsync();
            // And the shipped catalogue's own answers, for the tests that follow.
            await RestartAsync();
        }

        (await PublishedAsync())[Marked].ShouldBeTrue();
    }

    [Fact]
    public async Task A_layer_that_is_not_there_an_overlay_and_a_source_with_no_credit_are_each_refused_in_their_own_words()
    {
        // No such layer.
        await ProblemAsync(await ChooseAsync(admin, long.MaxValue, "on"), HttpStatusCode.NotFound, "map_layer.not_found");

        // Not an answer.
        (await admin.PutAsJsonAsync(
            $"/api/v1/admin/map-backgrounds/{await IdOfAsync(Marked)}/in-documents", new { choice = "sometimes" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // No answer at all is not "take the decision back": a decided source stays decided.
        var decided = await IdOfAsync(Marked);
        (await ChooseAsync(admin, decided, "off")).StatusCode.ShouldBe(HttpStatusCode.OK);
        try
        {
            await ProblemAsync(
                await admin.PutAsJsonAsync($"/api/v1/admin/map-backgrounds/{decided}/in-documents", new { }),
                HttpStatusCode.BadRequest,
                "validation.failed");
            await ProblemAsync(
                await admin.PutAsJsonAsync(
                    $"/api/v1/admin/map-backgrounds/{decided}/in-documents", new { choice = (string?)null }),
                HttpStatusCode.BadRequest,
                "validation.failed");
            (await PublishedAsync())[Marked].ShouldBeFalse();
        }
        finally
        {
            (await ChooseAsync(admin, decided, "default")).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        long overlay, uncredited, unpublished;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var rows = new[]
            {
                Layer($"Overlay {suffix}", isBase: false, attribution: "© Somebody", enabled: true),
                Layer($"Uncredited {suffix}", isBase: true, attribution: null, enabled: true),
                Layer($"Unpublished {suffix}", isBase: true, attribution: "© Somebody", enabled: false),
            };
            db.MapLayers.AddRange(rows);
            await db.SaveChangesAsync();
            (overlay, uncredited, unpublished) = (rows[0].Id, rows[1].Id, rows[2].Id);
        }

        // An overlay is not something a document's picture is drawn over, whichever way it is put.
        await ProblemAsync(
            await ChooseAsync(admin, overlay, "on"), HttpStatusCode.Conflict, MapLayerDocumentRules.NotABackgroundCode);
        await ProblemAsync(
            await ChooseAsync(admin, overlay, "off"), HttpStatusCode.Conflict, MapLayerDocumentRules.NotABackgroundCode);

        // A source with no credit cannot be switched on — there would be nothing to write under
        // the picture — and can always be switched off or left to the catalogue.
        await ProblemAsync(
            await ChooseAsync(admin, uncredited, "on"),
            HttpStatusCode.Conflict,
            MapLayerDocumentRules.AttributionRequiredCode);
        (await ChooseAsync(admin, uncredited, "off")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ChooseAsync(admin, uncredited, "default")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var listed = await BackgroundsAsync();
        listed[$"Uncredited {suffix}"].CanBeCopied.ShouldBeFalse();
        listed.Keys.ShouldNotContain($"Overlay {suffix}");

        // A background this installation does not publish is on no list, and answers as absent.
        listed.Keys.ShouldNotContain($"Unpublished {suffix}");
        await ProblemAsync(await ChooseAsync(admin, unpublished, "on"), HttpStatusCode.NotFound, "map_layer.not_found");
    }

    // ---- fixtures

    private sealed record Background(long Id, bool InDocuments, bool CatalogueDefault, string Choice, bool CanBeCopied);

    private static MapLayer Layer(string name, bool isBase, string? attribution, bool enabled) => new()
    {
        Name = name,
        UrlTemplate = "https://tiles.example/{z}/{x}/{y}.png",
        Attribution = attribution,
        IsBase = isBase,
        Enabled = enabled,
        SortOrder = 9000,
    };

    private async Task SignInAsync()
    {
        admin = await AuthHelper.BearerClientAsync(factory, $"mbd-adm-{suffix}@t.local");
        editor = await AuthHelper.BearerClientAsync(factory, $"mbd-edt-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"mbd-rdr-{suffix}@t.local");
    }

    /// <summary>
    /// Stops the application and starts it again on the same database, which is what a restart
    /// is: the start reads the catalogue file the application ships with and applies it.
    /// </summary>
    private async Task RestartAsync()
    {
        admin.Dispose();
        editor.Dispose();
        reader.Dispose();
        await factory.DisposeAsync();
        factory = new SilexGisApiFactory(connectionString);
        await SignInAsync();
    }

    /// <summary>Applies a catalogue the way a start does, without the start.</summary>
    private async Task SeedAsync(string catalogXml)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var report = await MapLayerSeeder.SeedAsync(catalogXml, new Dictionary<string, string>(), db);
        report.Problems.ShouldBeEmpty();
    }

    private async Task ForgetEveryDecisionAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.MapLayers.Where(l => l.InDocumentsChoice != null)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.InDocumentsChoice, (bool?)null));
    }

    private static Task<HttpResponseMessage> ChooseAsync(HttpClient client, long id, string choice) =>
        client.PutAsJsonAsync($"/api/v1/admin/map-backgrounds/{id}/in-documents", new { choice });

    /// <summary>The administrator's list, by name.</summary>
    private async Task<Dictionary<string, Background>> BackgroundsAsync()
    {
        var response = await admin.GetAsync("/api/v1/admin/map-backgrounds/");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.EnumerateArray().ToDictionary(
            row => row.GetProperty("name").GetString()!,
            row => new Background(
                row.GetProperty("id").GetInt64(),
                row.GetProperty("inDocuments").GetBoolean(),
                row.GetProperty("catalogueDefault").GetBoolean(),
                row.GetProperty("choice").GetString()!,
                row.GetProperty("canBeCopied").GetBoolean()));
    }

    /// <summary>What any signed-in reader is told: each published layer, and whether a document may copy it.</summary>
    private async Task<Dictionary<string, bool>> PublishedAsync()
    {
        var layers = await reader.GetFromJsonAsync<JsonElement>("/api/v1/map-layers");
        return layers.EnumerateArray().ToDictionary(
            layer => layer.GetProperty("name").GetString()!,
            layer => layer.GetProperty("inDocuments").GetBoolean());
    }

    private async Task<long> IdOfAsync(string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.MapLayers.Where(l => l.Name == name).Select(l => l.Id).SingleAsync();
    }

    private async Task<int> AuditCountAsync(long layerId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var id = layerId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return await db.Set<AuditEntry>().CountAsync(a => a.EntityType == "MapLayer" && a.EntityId == id);
    }

    private async Task<Guid?> LastAuditUserAsync(long layerId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var id = layerId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return await db.Set<AuditEntry>()
            .Where(a => a.EntityType == "MapLayer" && a.EntityId == id)
            .OrderByDescending(a => a.Id)
            .Select(a => a.UserId)
            .FirstAsync();
    }

    private static async Task ProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(status, body);
        JsonDocument.Parse(body).RootElement.GetProperty("code").GetString().ShouldBe(code);
    }
}
