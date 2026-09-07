// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// What the 3D scene is told to draw its ground from, and in what order the two possible answers
/// are consulted.
/// </summary>
/// <remarks>
/// <para>
/// Two factories, deliberately, because the thing under test is a precedence and a precedence
/// cannot be observed from one side of it. One installation names a pyramid in its own
/// configuration, the way every installation that has terrain today does; the other names none and
/// has a build somebody chose. Both share the database, so the same chosen build is present in both
/// and the difference in what they answer is the rule itself.
/// </para>
/// <para>
/// The height correction is asserted rather than assumed. It is a single number the client applies
/// to every surveyed altitude before drawing it, and getting it wrong puts every cave about forty
/// metres above or below its own hillside with nothing on screen saying so — so what is checked is
/// that a build's declared datum reaches the scene, not merely that some number does.
/// </para>
/// </remarks>
public sealed class TerrainMapConfigTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string MountedUrl = "/terrain/";
    private const string MountedCredit = "Baked by hand from data somebody already had";

    private readonly string publishRoot = Path.Combine(
        Path.GetTempPath(), "silexgis-mapcfg-" + Guid.NewGuid().ToString("N")[..8]);

    private readonly SilexGisApiFactory builder;   // names no pyramid: builds decide
    private readonly SilexGisApiFactory mounted;   // names one, the way installations do today

    private HttpClient reader = null!;         // an ordinary account, which is all this needs
    private HttpClient mountedReader = null!;
    private HttpClient chooser = null!;        // holds the right that chooses the terrain
    private HttpClient anonymous = null!;

    public TerrainMapConfigTests(PostgresFixture postgres)
    {
        builder = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Terrain:PublishRoot"] = publishRoot,
                ["Terrain:BuildRoot"] = Path.Combine(publishRoot, "builds"),
            },
            JobWorkers.RemoveFrom);

        mounted = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Terrain:PublishRoot"] = publishRoot,
                ["Terrain:BuildRoot"] = Path.Combine(publishRoot, "builds"),
                // No trailing slash on purpose: what is published must carry one, or the manifest
                // is looked for beside the pyramid instead of inside it and the scene draws
                // nothing while every request answers 200.
                ["Terrain:Url"] = "/terrain",
                ["Terrain:Attribution"] = MountedCredit,
                ["Terrain:HeightDatum"] = nameof(TerrainHeightDatum.Ellipsoidal),
                ["Terrain:GeoidHeightM"] = "36",
            },
            JobWorkers.RemoveFrom);
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        await AuthHelper.CreateUserAsync(builder, GlobalRoles.Viewer, $"tm-read-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(builder, $"tm-read-{suffix}@t.local");
        mountedReader = await AuthHelper.BearerClientAsync(mounted, $"tm-read-{suffix}@t.local");

        var chooserId = await AuthHelper.CreateUserAsync(builder, GlobalRoles.Viewer, $"tm-pick-{suffix}@t.local");
        chooser = await AuthHelper.BearerClientAsync(builder, $"tm-pick-{suffix}@t.local");
        await GrantAsync(chooserId, AccessAction.Execute);

        anonymous = builder.CreateClient();
    }

    /// <summary>
    /// An installation with nothing chosen draws bare ground; the same installation with a build
    /// chosen draws that build.
    /// </summary>
    /// <remarks>
    /// The two halves are one test because the absent case is only worth anything beside the
    /// present one: an endpoint that had stopped resolving terrain altogether would pass the first
    /// assertion on its own, every time.
    /// </remarks>
    [Fact]
    public async Task Nothing_is_drawn_until_a_build_is_chosen()
    {
        await ClearBuildsAsync();

        (await TerrainAsync(reader)).ValueKind.ShouldBe(JsonValueKind.Null);

        var build = await SeedPublishedBuildAsync();
        var chosen = await chooser.PostAsync($"/api/v1/terrain/builds/{build}/active", null);
        chosen.StatusCode.ShouldBe(HttpStatusCode.OK, await chosen.Content.ReadAsStringAsync());

        var terrain = await TerrainAsync(reader);
        terrain.ValueKind.ShouldBe(JsonValueKind.Object);
        terrain.GetProperty("url").GetString().ShouldBe($"/terrain/builds/{build:N}/");
    }

    /// <summary>
    /// The chosen build supplies where its tiles are, what they were made from, and what their
    /// heights are measured from — all three together.
    /// </summary>
    [Fact]
    public async Task A_chosen_build_supplies_its_address_its_credit_and_its_own_datum()
    {
        await ClearBuildsAsync();

        var build = await SeedPublishedBuildAsync(
            datum: TerrainHeightDatum.Ellipsoidal,
            geoidHeightM: 41.5,
            // Two rasters from one programme and one from elsewhere: a credit is one line naming
            // each source once, not one line per file that went into the pyramid.
            credits: ["Copernicus DEM GLO-30", "Copernicus DEM GLO-30", "Apuseni LiDAR 2019"]);
        await ChooseAsync(build);

        var terrain = await TerrainAsync(reader);
        terrain.GetProperty("url").GetString().ShouldBe($"/terrain/builds/{build:N}/");
        terrain.GetProperty("attribution").GetString()
            .ShouldBe("Copernicus DEM GLO-30; Apuseni LiDAR 2019");
        terrain.GetProperty("surveyHeightOffsetM").GetDouble().ShouldBe(41.5);

        // The same answer the one place that owns this rule gives, rather than a number that
        // happens to match: heights measured from the ellipsoid are the only ones a survey has to
        // be moved to meet, and the sign of that move lives in exactly one function.
        terrain.GetProperty("surveyHeightOffsetM").GetDouble()
            .ShouldBe(GeoidOffset.SurveyToSceneOffsetM(TerrainHeightDatum.Ellipsoidal, 41.5));
    }

    /// <summary>
    /// A build whose heights need no correction asks for none, and says so with a zero rather than
    /// by omitting the answer.
    /// </summary>
    [Fact]
    public async Task A_build_of_sea_level_heights_asks_the_scene_for_no_correction()
    {
        await ClearBuildsAsync();

        var build = await SeedPublishedBuildAsync(
            datum: TerrainHeightDatum.Orthometric,
            // Recorded and irrelevant: the correction is a property of what the heights are
            // measured from, so an undulation noted against sea-level heights is not applied.
            geoidHeightM: 41.5,
            credits: []);
        await ChooseAsync(build);

        var terrain = await TerrainAsync(reader);
        terrain.GetProperty("surveyHeightOffsetM").GetDouble().ShouldBe(0);
        // Nothing this build was made from declared a credit, so the scene is given none rather
        // than an empty line to render.
        terrain.GetProperty("attribution").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    /// <summary>
    /// An installation that names a pyramid of its own keeps drawing it, whatever anybody chooses.
    /// </summary>
    /// <remarks>
    /// This is the only path an installation has had until now — a pyramid baked with a
    /// command-line tool, put where the web server can read it, and named in configuration — and it
    /// must survive this application learning to bake its own. Both halves are asserted from one
    /// chosen build, so what is proved is the precedence rather than two unrelated installations.
    /// </remarks>
    [Fact]
    public async Task A_pyramid_named_in_the_configuration_wins_over_the_chosen_build()
    {
        await ClearBuildsAsync();

        var build = await SeedPublishedBuildAsync(
            datum: TerrainHeightDatum.Orthometric,
            geoidHeightM: 0,
            credits: ["Copernicus DEM GLO-30"]);
        await ChooseAsync(build);

        var configured = await TerrainAsync(mountedReader);
        configured.GetProperty("url").GetString().ShouldBe(MountedUrl);
        configured.GetProperty("attribution").GetString().ShouldBe(MountedCredit);
        configured.GetProperty("surveyHeightOffsetM").GetDouble().ShouldBe(36);

        // The same instant, the same database, the same chosen build — and an installation that
        // names nothing draws it.
        (await TerrainAsync(reader)).GetProperty("url").GetString()
            .ShouldBe($"/terrain/builds/{build:N}/");
    }

    /// <summary>
    /// A build carrying the mark but no stamped version is not drawn.
    /// </summary>
    /// <remarks>
    /// The stamp is put on when the tiles are read back and found whole, and it is the only mark
    /// saying anything was ever checked. A pyramid with holes in it draws as plausible ground at
    /// the wrong resolution and reports nothing, so the row is not taken at its word: choosing such
    /// a build is refused where it is chosen, and it is refused again here, because a mark written
    /// straight into the table by anything else would otherwise reach the scene.
    /// </remarks>
    [Fact]
    public async Task A_build_nothing_ever_checked_is_not_drawn_even_holding_the_mark()
    {
        await ClearBuildsAsync();

        var build = await SeedPublishedBuildAsync(
            datum: TerrainHeightDatum.Orthometric, geoidHeightM: 0, credits: [], version: null);
        await using (var scope = builder.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.TerrainBuilds.Where(x => x.Id == build)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, true));
        }

        (await TerrainAsync(reader)).ValueKind.ShouldBe(JsonValueKind.Null);

        // The same row, once its tiles have been read back and found whole.
        await using (var scope = builder.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.TerrainBuilds.Where(x => x.Id == build)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.PyramidVersion, "1.1.0-abcdef123456"));
        }

        (await TerrainAsync(reader)).GetProperty("url").GetString()
            .ShouldBe($"/terrain/builds/{build:N}/");
    }

    /// <summary>
    /// The terrain a scene draws is told to accounts, not to the internet.
    /// </summary>
    [Fact]
    public async Task What_the_scene_draws_is_not_told_to_a_visitor_without_an_account()
    {
        await ClearBuildsAsync();
        var build = await SeedPublishedBuildAsync();
        await ChooseAsync(build);

        (await anonymous.GetAsync("/api/v1/map/config")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);

        // An ordinary account with no terrain right at all still gets it: it is what the scene
        // needs in order to draw, not a fact about administering builds.
        (await TerrainAsync(reader)).GetProperty("url").GetString()
            .ShouldBe($"/terrain/builds/{build:N}/");
    }

    private async Task ChooseAsync(Guid build)
    {
        var chosen = await chooser.PostAsync($"/api/v1/terrain/builds/{build}/active", null);
        chosen.StatusCode.ShouldBe(HttpStatusCode.OK, await chosen.Content.ReadAsStringAsync());
    }

    private static async Task<JsonElement> TerrainAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/map/config");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        using var body = JsonDocument.Parse(payload);
        return body.RootElement.GetProperty("terrain").Clone();
    }

    private async Task ClearBuildsAsync()
    {
        await using var scope = builder.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.TerrainBuilds.ExecuteDeleteAsync();
    }

    private Task<Guid> SeedPublishedBuildAsync() =>
        SeedPublishedBuildAsync(TerrainHeightDatum.Orthometric, 0, []);

    private async Task<Guid> SeedPublishedBuildAsync(
        TerrainHeightDatum datum,
        double geoidHeightM,
        IReadOnlyList<string> credits,
        string? version = "1.1.0-abcdef123456")
    {
        Guid id;
        await using (var scope = builder.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var build = new TerrainBuild
            {
                Extent = NtsGeometryServices.Instance.CreateGeometryFactory(4326).CreatePolygon(
                [
                    new Coordinate(22.5, 46.5),
                    new Coordinate(22.6, 46.5),
                    new Coordinate(22.6, 46.6),
                    new Coordinate(22.5, 46.6),
                    new Coordinate(22.5, 46.5),
                ]),
                RequestedMaxDepth = 13,
                Status = TerrainBuildStatus.Succeeded,
                Phase = TerrainBuildPhase.Publish,
                PyramidVersion = version,
                HeightDatum = datum,
                GeoidHeightM = geoidHeightM,
                SizeBytes = 4096,
            };
            db.TerrainBuilds.Add(build);
            await db.SaveChangesAsync();
            id = build.Id;

            foreach (var credit in credits)
            {
                db.TerrainBuildSources.Add(new TerrainBuildSource
                {
                    TerrainBuildId = id,
                    Kind = TerrainBuildSourceKind.Fetched,
                    Reference = "cell",
                    Attribution = credit,
                });
            }

            await db.SaveChangesAsync();
        }

        // The pyramid where terrain is served from, because choosing a build asks the disk whether
        // there is anything at the address rather than believing the row.
        var directory = Path.Combine(publishRoot, TerrainPyramid.PublishedName(id));
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            Path.Combine(directory, TerrainPyramid.ManifestFileName),
            """{"format":"quantized-mesh-1.0","version":"1.1.0-abcdef123456"}""");

        return id;
    }

    /// <summary>
    /// A rule naming one person directly, written straight into storage: the authoring surface
    /// refuses rules handing out more than their author holds.
    /// </summary>
    private async Task GrantAsync(Guid userId, AccessAction actions)
    {
        await using var scope = builder.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Domain = AccessDomain.Terrain,
            Actions = actions,
            Effect = AccessEffect.Allow,
            ScopeKind = AccessScopeKind.All,
        });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        reader?.Dispose();
        mountedReader?.Dispose();
        chooser?.Dispose();
        anonymous?.Dispose();
        builder.Dispose();
        mounted.Dispose();

        try
        {
            if (Directory.Exists(publishRoot))
            {
                Directory.Delete(publishRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // A temporary directory something else still has open. Not worth failing a run for.
        }
    }
}
