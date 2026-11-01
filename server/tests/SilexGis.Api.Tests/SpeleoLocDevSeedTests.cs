// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using SilexGis.Api.Auth;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The development dataset that makes location protection observable, and the proof that it
/// does.
///
/// A stock demonstration installation cannot show what protection does at all: every row is
/// owned by the bootstrap administrator, who is also a full administrator, so the caller is
/// exempt twice over and a protected cave hands out its exact position. That is not a cosmetic
/// gap — it means no test, no reviewer and nobody building a client can see the rule work. So
/// the check below is not "the seeder ran": it signs in as the plain account the seeder makes
/// and asserts the protected cave arrives without its exact position, alongside the same read
/// by the administrator, which arrives with it. One without the other proves nothing — an
/// account that could see no cave at all would pass the negative on its own.
///
/// The dataset is also offered as a command against an installation that already has it, so it
/// is laid down twice here and every table in the schema is counted either side of the second
/// run. A guard that stops guarding is otherwise invisible.
///
/// It runs on a database and an application of its own, because it mints a bootstrap
/// administrator and a named account, and the shared test database belongs to everybody.
/// </summary>
public sealed class SpeleoLocDevSeedTests : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private const string AdminEmail = "seed-admin@dev.local";
    private const string AdminPassword = "dev-seed-admin-pass-1";

    /// <summary>
    /// Deliberately not the seeder's own default. The account's password is configurable
    /// precisely so an installation is not stuck with the one printed in the guide, and a test
    /// that used the default would pass whether or not the configured value was ever read.
    /// </summary>
    private const string MemberPassword = "dev-seed-member-pass-2";

    private readonly string adminConnectionString;
    private readonly string databaseName = $"silexgis_speleoloc_dev_{Guid.NewGuid():N}";
    private string connectionString = null!;
    private SilexGisApiFactory factory = null!;

    public SpeleoLocDevSeedTests(PostgresFixture postgres) =>
        adminConnectionString = postgres.ConnectionString;

    public async ValueTask InitializeAsync()
    {
        await using (var admin = new NpgsqlConnection(adminConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        connectionString =
            new NpgsqlConnectionStringBuilder(adminConnectionString) { Database = databaseName }
                .ConnectionString;

        factory = new SilexGisApiFactory(
            connectionString,
            new Dictionary<string, string?>
            {
                ["Admin:Email"] = AdminEmail,
                ["Admin:Password"] = AdminPassword,

                // The dataset mints a login, so it refuses to run unless the host is a
                // development one or somebody said in configuration that this installation
                // wants it. Said here rather than leaned on the harness's environment, so the
                // test proves the permitted path and not the harness's default.
                [SpeleoLocDevSeeder.AllowKey] = "true",
                [SpeleoLocDevSeeder.MemberPasswordKey] = MemberPassword,

                // Two sign-ins and a seeder in one class sit well inside the real limit, but the
                // container is shared and its clock is not this class's to reason about.
                ["Auth:RateLimitPerMinute"] = "200",
            });

        // Boots the application, which migrates and runs the startup seeders — including the one
        // that creates the bootstrap administrator this dataset needs a second party for.
        using var client = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var adminId = await db.Users.Where(u => u.Email == AdminEmail).Select(u => u.Id).SingleAsync();
        await DemoSeeder.SeedAsync(db, adminId);
    }

    [Fact]
    public async Task Seeding_the_development_dataset_twice_writes_it_once()
    {
        using (var scope = factory.Services.CreateScope())
        {
            (await SpeleoLocDevSeeder.SeedAsync(scope.ServiceProvider))
                .ShouldBe(SpeleoLocDevSeedOutcome.Seeded);
        }

        // The survey models are queued for reading and this host keeps its worker, which would
        // otherwise write their stations while the tables are being counted.
        await ReadQueuedSurveysAsync();

        Dictionary<string, long> afterFirst;
        await using (var counting = CreateContext())
        {
            afterFirst = await RowCountsAsync(counting);

            // The run has to have done something, or "nothing changed the second time" would be
            // true of a seeder that does nothing at all.
            afterFirst["caving_groups"].ShouldBeGreaterThan(0);
            afterFirst["caving_group_memberships"].ShouldBeGreaterThan(0);
        }

        using (var scope = factory.Services.CreateScope())
        {
            (await SpeleoLocDevSeeder.SeedAsync(scope.ServiceProvider))
                .ShouldBe(SpeleoLocDevSeedOutcome.Seeded);
        }

        await using var db = CreateContext();
        (await RowCountsAsync(db)).ShouldBe(afterFirst);

        // What the counts cannot say: that the rows are the ones meant, and joined up.
        var club = await db.CavingGroups.SingleAsync(g => g.Slug == SpeleoLocDevSeeder.GroupSlug);

        var memberUserIds = await (
            from membership in db.CavingGroupMemberships
            where membership.CavingGroupId == club.Id
            join caver in db.Cavers on membership.CaverId equals caver.Id
            where caver.UserId != null
            select caver.UserId!.Value).ToListAsync();

        var adminId = await db.Users.Where(u => u.Email == AdminEmail).Select(u => u.Id).SingleAsync();
        var plainId = await db.Users
            .Where(u => u.Email == SpeleoLocDevSeeder.MemberEmail).Select(u => u.Id).SingleAsync();
        memberUserIds.ShouldContain(adminId);
        memberUserIds.ShouldContain(plainId);

        // The account exists to be the party that is not exempt, so it must own nothing at all —
        // ownership is one of the two ways the exact-location rule is short-circuited.
        (await db.Features.AnyAsync(f => f.OwnerUserId == plainId)).ShouldBeFalse();

        // The cave was seeded visible to a caving group while naming none, which no rule can
        // match: the group arm requires a group on the row as well as on the caller.
        var caveId = await db.Caves
            .Where(c => c.IdentificationCode == SpeleoLocDevSeeder.GroupVisibleCaveCode)
            .Select(c => c.Id).SingleAsync();
        var cave = await db.Features.SingleAsync(f => f.Id == caveId);
        cave.Visibility.ShouldBe(Visibility.CavingGroup);
        cave.CavingGroupId.ShouldBe(club.Id);
    }

    /// <summary>
    /// The whole reason this dataset exists: a caller who is neither the owner nor an
    /// administrator reads the protected cave without its exact position, and the administrator
    /// reading the same row gets it. Asserted as a pair on purpose — the negative alone is also
    /// satisfied by an account that cannot read the cave at all, or by a seeder that never made
    /// the account.
    /// </summary>
    [Fact]
    public async Task The_plain_account_reads_the_protected_cave_without_its_exact_position()
    {
        using (var scope = factory.Services.CreateScope())
        {
            (await SpeleoLocDevSeeder.SeedAsync(scope.ServiceProvider))
                .ShouldBe(SpeleoLocDevSeedOutcome.Seeded);
        }

        Guid protectedFeatureId;
        await using (var db = CreateContext())
        {
            protectedFeatureId = await db.Caves
                .Where(c => c.IdentificationCode == "DEMO-0002").Select(c => c.Id).SingleAsync();
        }

        using var member = await AuthHelper.BearerClientAsync(
            factory, SpeleoLocDevSeeder.MemberEmail, MemberPassword);
        using var admin = await AuthHelper.BearerClientAsync(factory, AdminEmail, AdminPassword);

        var asMember = await ReadFeatureAsync(member, protectedFeatureId);
        var asAdmin = await ReadFeatureAsync(admin, protectedFeatureId);

        asMember["locationProtected"]!.GetValue<bool>().ShouldBeTrue();
        (asMember["approximateLocation"]!.GetValue<bool>() || asMember["omittedLocation"]!.GetValue<bool>())
            .ShouldBeTrue("the plain account is neither owner nor administrator");

        asAdmin["approximateLocation"]!.GetValue<bool>().ShouldBeFalse();
        asAdmin["omittedLocation"]!.GetValue<bool>().ShouldBeFalse();
        asAdmin["geometry"].ShouldNotBeNull();

        // Not merely flagged differently: what arrives is different. A flag that said "this was
        // protected" over an unchanged coordinate would satisfy the assertions above.
        asMember["geometry"]?.ToJsonString().ShouldNotBe(asAdmin["geometry"]!.ToJsonString());
    }

    /// <summary>
    /// What a phone reads beyond features, as the plain account and as the administrator, side by
    /// side. The photo hung on the open cave alone reaches both as stored; the one hung on the open
    /// cave and the protected one reaches the plain account without its location and the
    /// administrator as stored. The raster map reaches both, with two pins for the administrator
    /// and one for the plain account — the other is on a place that is its own protection root.
    /// Each negative sits beside its positive, so an account that could read nothing, or a seeder
    /// that wrote nothing, fails here rather than passing.
    /// </summary>
    [Fact]
    public async Task The_plain_account_is_handed_the_seeded_photos_and_map_as_protection_decides()
    {
        using (var scope = factory.Services.CreateScope())
        {
            (await SpeleoLocDevSeeder.SeedAsync(scope.ServiceProvider))
                .ShouldBe(SpeleoLocDevSeedOutcome.Seeded);
        }

        Guid openCaveId;
        Guid openPlaceId;
        Guid protectedPlaceId;
        await using (var db = CreateContext())
        {
            openCaveId = await db.Caves
                .Where(c => c.IdentificationCode == SpeleoLocDevSeeder.OpenCaveCode).Select(c => c.Id).SingleAsync();
            openPlaceId = await db.Features
                .Where(f => f.Name == SpeleoLocDevSeeder.OpenPlaceName).Select(f => f.Id).SingleAsync();
            protectedPlaceId = await db.Features
                .Where(f => f.Name == SpeleoLocDevSeeder.ProtectedPlaceName).Select(f => f.Id).SingleAsync();
        }

        using var member = await AuthHelper.BearerClientAsync(
            factory, SpeleoLocDevSeeder.MemberEmail, MemberPassword);
        using var admin = await AuthHelper.BearerClientAsync(factory, AdminEmail, AdminPassword);
        var membersSet = await CreateSetAsync(member, openCaveId);
        var adminsSet = await CreateSetAsync(admin, openCaveId);

        var membersReach = await PhotoReachAsync(member, membersSet);
        var adminsReach = await PhotoReachAsync(admin, adminsSet);
        membersReach[SpeleoLocDevSeeder.OpenPhotoTitle].ShouldBe("original");
        membersReach[SpeleoLocDevSeeder.SharedPhotoTitle].ShouldBe("locationFree");
        adminsReach[SpeleoLocDevSeeder.OpenPhotoTitle].ShouldBe("original");
        adminsReach[SpeleoLocDevSeeder.SharedPhotoTitle].ShouldBe("original");

        // Named, not counted: a filter keyed on the wrong place would hand the plain account the
        // protected pin instead of the open one and still hand it exactly one.
        (await PinPlacesAsync(member, membersSet)).ShouldBe([openPlaceId]);
        (await PinPlacesAsync(admin, adminsSet)).ShouldBe([openPlaceId, protectedPlaceId], ignoreOrder: true);

        // The recording rides the club's visibility, which the plain account is in.
        var trips = await GetJsonAsync(member, $"/api/v1/sync/sets/{membersSet}/trips");
        trips.GetProperty("recordings").EnumerateArray()
            .Select(r => r.GetProperty("title").GetString())
            .ShouldContain(SpeleoLocDevSeeder.RecordingTitle);

        // The directory lists the person without an account, and hands the plain account back the
        // phone user its phone reported, linked to that person.
        var people = await GetJsonAsync(member, "/api/v1/sync/people");
        var listed = people.GetProperty("people").EnumerateArray()
            .Single(p => p.GetProperty("fullName").GetString() == SpeleoLocDevSeeder.RosterOnlyCaverName);
        listed.GetProperty("hasAccount").GetBoolean().ShouldBeFalse();
        var link = people.GetProperty("links").EnumerateArray().ShouldHaveSingleItem();
        link.GetProperty("deviceUserId").GetGuid().ShouldBe(SpeleoLocDevSeeder.PhoneUserId);
        link.GetProperty("caverId").GetGuid().ShouldBe(listed.GetProperty("id").GetGuid());

        // The second account differs from the plain one by its mark and by nothing else: a Viewer,
        // in the club, owning nothing.
        await using var check = CreateContext();
        var reviewed = await check.Users.SingleAsync(u => u.Email == SpeleoLocDevSeeder.ReviewEmail);
        var plain = await check.Users.SingleAsync(u => u.Email == SpeleoLocDevSeeder.MemberEmail);
        reviewed.UploadsNeedReview.ShouldBeTrue();
        plain.UploadsNeedReview.ShouldBeFalse();
        (await check.Features.AnyAsync(f => f.OwnerUserId == reviewed.Id)).ShouldBeFalse();
        using var underReview = await AuthHelper.BearerClientAsync(
            factory, SpeleoLocDevSeeder.ReviewEmail, MemberPassword);
        var reviewSet = await CreateSetAsync(underReview, openCaveId);
        var refused = await underReview.PostAsJsonAsync($"/api/v1/sync/sets/{reviewSet}/upload", new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            rows = new object[]
            {
                new
                {
                    id = Guid.CreateVersion7(),
                    kind = "cave",
                    baseRevision = (DateTimeOffset?)null,
                    deleted = false,
                    name = "Not written",
                    caveTypeCode = "cave",
                    isMain = false,
                },
            },
        });
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict, await refused.Content.ReadAsStringAsync());
        (await refused.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>()
            .ShouldBe("sync.client_update_required");
        (await check.Features.AnyAsync(f => f.Name == "Not written")).ShouldBeFalse();
    }

    /// <summary>
    /// The survey models as a phone reads them, as the plain account and as the administrator, over
    /// a set rooted at the open cave and the protected one. The plain account is handed both models
    /// of the open cave — the Therion one current, having taken the mark from the demonstration
    /// model when it was read — with sizes and hashes that match the file it is then handed, and
    /// both places at their stations, one with a splay and one without. The protected cave's model
    /// reaches the administrator and not the plain account, whose stream tells it only as gone.
    /// </summary>
    [Fact]
    public async Task The_plain_account_is_handed_the_seeded_survey_models_and_places_at_stations_as_protection_decides()
    {
        using (var scope = factory.Services.CreateScope())
        {
            (await SpeleoLocDevSeeder.SeedAsync(scope.ServiceProvider))
                .ShouldBe(SpeleoLocDevSeedOutcome.Seeded);
        }

        await ReadQueuedSurveysAsync();

        Guid openCaveId;
        Guid protectedCaveId;
        Guid openPlaceId;
        Guid stationPlaceId;
        Guid protectedModelId;
        await using (var db = CreateContext())
        {
            openCaveId = await db.Caves
                .Where(c => c.IdentificationCode == SpeleoLocDevSeeder.OpenCaveCode).Select(c => c.Id).SingleAsync();
            protectedCaveId = await db.Caves
                .Where(c => c.IdentificationCode == SpeleoLocDevSeeder.ProtectedCaveCode).Select(c => c.Id).SingleAsync();
            openPlaceId = await db.Features
                .Where(f => f.Name == SpeleoLocDevSeeder.OpenPlaceName).Select(f => f.Id).SingleAsync();
            stationPlaceId = await db.Features
                .Where(f => f.Name == SpeleoLocDevSeeder.StationPlaceName).Select(f => f.Id).SingleAsync();
            protectedModelId = await db.SurveyModels
                .Where(m => m.Name == SpeleoLocDevSeeder.ProtectedSurveyModelName).Select(m => m.Id).SingleAsync();

            // Read, all of them: a model the reading refused would still be listed, and would show
            // nothing on a phone.
            (await db.SurveyModels.CountAsync(m => m.Status != SurveyModelStatus.Ready)).ShouldBe(0);
        }

        using var member = await AuthHelper.BearerClientAsync(
            factory, SpeleoLocDevSeeder.MemberEmail, MemberPassword);
        using var admin = await AuthHelper.BearerClientAsync(factory, AdminEmail, AdminPassword);
        var membersSet = await CreateSetAsync(member, openCaveId, protectedCaveId);
        var adminsSet = await CreateSetAsync(admin, openCaveId, protectedCaveId);

        var membersPage = await GetJsonAsync(member, $"/api/v1/sync/sets/{membersSet}/survey-models");
        var adminsPage = await GetJsonAsync(admin, $"/api/v1/sync/sets/{adminsSet}/survey-models");
        var membersModels = membersPage.GetProperty("models").EnumerateArray()
            .ToDictionary(m => m.GetProperty("name").GetString()!);
        var adminsModels = adminsPage.GetProperty("models").EnumerateArray()
            .ToDictionary(m => m.GetProperty("name").GetString()!);

        var lox = membersModels[SpeleoLocDevSeeder.SurveyModelName];
        var survex = membersModels[SpeleoLocDevSeeder.SurveyExportModelName];
        lox.GetProperty("format").GetString().ShouldBe("lox");
        lox.GetProperty("isCurrent").GetBoolean().ShouldBeTrue();
        survex.GetProperty("format").GetString().ShouldBe("3d");
        survex.GetProperty("isCurrent").GetBoolean().ShouldBeFalse();
        membersModels.Values.Count(m => m.GetProperty("isCurrent").GetBoolean()).ShouldBe(1);

        // Named and counted the other way round: the administrator is handed the protected model,
        // so its absence for the plain account is the rule and not a seeder that never wrote it.
        adminsModels.ShouldContainKey(SpeleoLocDevSeeder.ProtectedSurveyModelName);
        membersModels.ShouldNotContainKey(SpeleoLocDevSeeder.ProtectedSurveyModelName);
        membersModels.Values.ShouldAllBe(m => m.GetProperty("caveId").GetGuid() == openCaveId);
        membersPage.GetProperty("tombstones").EnumerateArray()
            .Select(t => t.GetProperty("id").GetGuid()).ShouldContain(protectedModelId);

        // What the stream lists is what the file is: fetched through the route a phone uses and
        // hashed here.
        foreach (var model in new[] { lox, survex })
        {
            var links = await member.PostAsJsonAsync("/api/v1/sync/files/links", new
            {
                items = new[] { new { documentId = model.GetProperty("documentId").GetGuid(), rendition = "original" } },
            });
            var linksPayload = await links.Content.ReadAsStringAsync();
            links.StatusCode.ShouldBe(HttpStatusCode.OK, linksPayload);
            var url = JsonDocument.Parse(linksPayload).RootElement.GetProperty("links")
                .EnumerateArray().Single().GetProperty("url").GetString()!;
            var bytes = await member.GetByteArrayAsync(url);
            bytes.LongLength.ShouldBe(model.GetProperty("sizeBytes").GetInt64());
            Convert.ToHexStringLower(SHA256.HashData(bytes)).ShouldBe(model.GetProperty("sha256").GetString());
        }

        var stations = membersPage.GetProperty("placeStations").EnumerateArray().ToList();
        stations.Count.ShouldBe(2);
        stations.ShouldAllBe(s => s.GetProperty("surveyModelId").GetGuid() == lox.GetProperty("id").GetGuid());
        var plain = stations.Single(s => s.GetProperty("placeId").GetGuid() == openPlaceId);
        plain.GetProperty("station").GetString().ShouldBe(SpeleoLocDevSeeder.MappedStation);
        plain.TryGetProperty("splay", out _).ShouldBeFalse();
        var splayed = stations.Single(s => s.GetProperty("placeId").GetGuid() == stationPlaceId);
        splayed.GetProperty("station").GetString().ShouldBe(SpeleoLocDevSeeder.SplayStation);
        splayed.GetProperty("splay").EnumerateArray().Select(v => v.GetDouble()).ShouldBe(SpeleoLocDevSeeder.StationSplay);

        // The stations the mappings name are stations the reading found in the file.
        await using var check = CreateContext();
        var loxId = lox.GetProperty("id").GetGuid();
        (await check.SurveyStations.CountAsync(s => s.SurveyModelId == loxId
            && (s.Name == SpeleoLocDevSeeder.MappedStation || s.Name == SpeleoLocDevSeeder.SplayStation))).ShouldBe(2);
    }

    /// <summary>
    /// Carries every survey reading in the queue through, once each, whether this host's worker or
    /// the call takes it.
    /// </summary>
    private async Task ReadQueuedSurveysAsync()
    {
        long[] jobs;
        await using (var db = CreateContext())
        {
            jobs = await db.ProcessingJobs
                .Where(j => j.Kind == ProcessingJobKinds.SurveyGraph)
                .OrderBy(j => j.Id)
                .Select(j => j.Id)
                .ToArrayAsync();
        }

        foreach (var job in jobs)
        {
            await QueuedJob.RunAsync(factory.Services, job);
        }
    }

    private static async Task<Guid> CreateSetAsync(HttpClient client, params Guid[] rootIds)
    {
        var response = await client.PostAsJsonAsync("/api/v1/sync/sets/", new
        {
            name = $"Seeded {Guid.NewGuid():N}"[..20],
            uploadVisibility = "private",
            rootFeatureIds = rootIds,
            settings = new { },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<Guid>();
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    /// <summary>The reach each seeded photo is announced with, by title.</summary>
    private static async Task<Dictionary<string, string>> PhotoReachAsync(HttpClient client, Guid set)
    {
        var page = await GetJsonAsync(client, $"/api/v1/sync/sets/{set}/documents");
        return page.GetProperty("links").EnumerateArray()
            .Select(l => l.GetProperty("document"))
            .Where(d => d.GetProperty("title").GetString() is SpeleoLocDevSeeder.OpenPhotoTitle
                or SpeleoLocDevSeeder.SharedPhotoTitle)
            .GroupBy(d => d.GetProperty("title").GetString()!)
            .ToDictionary(g => g.Key, g => g.Select(d => d.GetProperty("reach").GetString()!).Distinct().Single());
    }

    private static async Task<List<Guid>> PinPlacesAsync(HttpClient client, Guid set)
    {
        var page = await GetJsonAsync(client, $"/api/v1/sync/sets/{set}/rasters");
        return [.. page.GetProperty("maps").EnumerateArray()
            .Single(m => m.GetProperty("title").GetString() == SpeleoLocDevSeeder.RasterTitle)
            .GetProperty("pins").EnumerateArray()
            .Select(p => p.GetProperty("placeId").GetGuid())];
    }

    /// <summary>
    /// The dataset creates an account somebody can sign in with, and its password is printed in
    /// the installation guide. So on a host that is not a development one, and where nobody said
    /// this installation wants it, the seeder writes nothing at all — the refusal is the guard,
    /// not the paragraph of prose beside the command.
    ///
    /// Asserted as a pair with the permitted run above: a seeder that never wrote anything
    /// anywhere would satisfy this test on its own.
    /// </summary>
    [Fact]
    public async Task The_development_dataset_is_refused_on_a_host_that_did_not_ask_for_it()
    {
        using var refusing = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting(SpeleoLocDevSeeder.AllowKey, "false");
        });

        // Realises the host, which is what reads the environment and the settings above.
        using (var client = refusing.CreateClient())
        {
            using var scope = refusing.Services.CreateScope();
            (await SpeleoLocDevSeeder.SeedAsync(scope.ServiceProvider))
                .ShouldBe(SpeleoLocDevSeedOutcome.NotPermitted);
        }

        // Refused, not partly done: no account, and no group for one to be put in.
        await using var db = CreateContext();
        (await db.Users.AnyAsync(u => u.Email == SpeleoLocDevSeeder.MemberEmail)).ShouldBeFalse();
        (await db.CavingGroups.AnyAsync(g => g.Slug == SpeleoLocDevSeeder.GroupSlug)).ShouldBeFalse();

        // And the same application, permitted, does write it — so what the refusal above proves
        // is the guard and not a broken seeder.
        using (var scope = factory.Services.CreateScope())
        {
            (await SpeleoLocDevSeeder.SeedAsync(scope.ServiceProvider))
                .ShouldBe(SpeleoLocDevSeedOutcome.Seeded);
        }

        (await db.Users.AnyAsync(u => u.Email == SpeleoLocDevSeeder.MemberEmail)).ShouldBeTrue();
    }

    private static async Task<JsonObject> ReadFeatureAsync(HttpClient client, Guid id)
    {
        var response = await client.GetAsync($"/api/v1/features/{id}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        // The route answers a typed envelope; the position and its protection flags live on the
        // kind-independent view inside it.
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!["feature"]!.AsObject();
    }

    /// <summary>
    /// Every table the schema has, by name, with its row count. Read from the catalogue rather
    /// than from a list somebody maintains, so a table added tomorrow is covered without anyone
    /// remembering to add it here.
    /// </summary>
    private static async Task<Dictionary<string, long>> RowCountsAsync(SilexGisDbContext db)
    {
        var names = new List<string>();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            await using (var list = new NpgsqlCommand(
                """
                SELECT table_name FROM information_schema.tables
                WHERE table_schema = 'public' AND table_type = 'BASE TABLE'
                ORDER BY table_name
                """, connection))
            await using (var reader = await list.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    names.Add(reader.GetString(0));
                }
            }

            var counts = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var name in names)
            {
                // The name comes from the catalogue, not from a caller, and is quoted; a count
                // cannot be expressed as a parameter.
                await using var count = new NpgsqlCommand($"SELECT count(*) FROM \"{name}\"", connection);
                counts[name] = (long)(await count.ExecuteScalarAsync())!;
            }

            return counts;
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    private SilexGisDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<SilexGisDbContext>()
            .UseNpgsql(connectionString, o => o.UseNetTopologySuite())
            .UseSnakeCaseNamingConvention()
            .UseOpenIddict()
            .Options);

    public async ValueTask DisposeAsync()
    {
        await factory.DisposeAsync();
        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }
}
