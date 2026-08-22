// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain.Entities;
using SilexGis.Domain;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The account holder's own settings end to end: profile and per-field visibility, addresses
/// with an optional map point, the avatar, interface and notification preferences, the account
/// data export, and the credential changes.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AccountSettingsTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient me = null!;
    private HttpClient other = null!;
    private Guid myId;

    public AccountSettingsTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-account-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async Task InitializeAsync()
    {
        myId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, MyEmail);
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, OtherEmail);

        me = await AuthHelper.BearerClientAsync(factory, MyEmail);
        other = await AuthHelper.BearerClientAsync(factory, OtherEmail);
    }

    private string MyEmail => $"acct-me-{suffix}@t.local";

    private string OtherEmail => $"acct-other-{suffix}@t.local";

    [Fact]
    public async Task Profile_requires_authentication()
    {
        using var anonymous = factory.CreateClient();

        (await anonymous.GetAsync("/api/v1/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync("/api/v1/members")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_fresh_account_keeps_every_field_private()
    {
        var profile = await GetMeAsync(me);

        var visibility = profile.GetProperty("visibility");
        foreach (var field in new[] { "realName", "bio", "email", "phone", "cavingClub", "address", "addressPoint" })
        {
            visibility.GetProperty(field).GetString().ShouldBe("private", $"{field} should start private");
        }

        profile.GetProperty("addresses").GetArrayLength().ShouldBe(0);
        profile.GetProperty("avatarUrl").ValueKind.ShouldBe(JsonValueKind.Null);
        profile.GetProperty("email").GetString().ShouldBe(MyEmail);
    }

    [Fact]
    public async Task Profile_round_trips_every_field_and_visibility_choice()
    {
        var response = await me.PutAsJsonAsync("/api/v1/me", ProfileBody(
            firstName: "Ana", lastName: "Pop", displayName: "Ana P", bio: "Caver since 2010.",
            realName: "cavingGroup", email: "authenticated"));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var profile = await GetMeAsync(me);
        profile.GetProperty("firstName").GetString().ShouldBe("Ana");
        profile.GetProperty("lastName").GetString().ShouldBe("Pop");
        profile.GetProperty("displayName").GetString().ShouldBe("Ana P");

        profile.GetProperty("visibility").GetProperty("realName").GetString().ShouldBe("cavingGroup");
        profile.GetProperty("visibility").GetProperty("email").GetString().ShouldBe("authenticated");
    }

    [Fact]
    public async Task The_profile_save_cannot_move_the_language()
    {
        // The language is switched from the application shell, far from any open profile form,
        // and this save is a full-DTO replace. If it carried the language, a form opened before
        // the switch would put the old answer back — and with it, the language every message this
        // account is sent is written in.
        (await me.PutAsJsonAsync("/api/v1/me/locale", new { language = "ro", timeZone = (string?)null }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await me.PutAsJsonAsync("/api/v1/me", ProfileBody(firstName: "Ana"));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        (await GetMeAsync(me)).GetProperty("locale").GetString().ShouldBe("ro");
    }

    [Fact]
    public async Task Profile_rejects_an_overlong_name()
    {
        var response = await me.PutAsJsonAsync("/api/v1/me", ProfileBody(
            firstName: new string('x', 300)));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        problem.GetProperty("code").GetString().ShouldBe("validation.failed");
        problem.GetProperty("errors").EnumerateObject().ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Profile_cannot_change_the_email_or_the_user_name()
    {
        // Both are credentials with their own endpoints; the save DTO simply has no field for them.
        await me.PutAsJsonAsync("/api/v1/me", ProfileBody(displayName: "Renamed"));

        var profile = await GetMeAsync(me);
        profile.GetProperty("email").GetString().ShouldBe(MyEmail);
        profile.GetProperty("userName").GetString().ShouldBe(MyEmail);
    }

    [Fact]
    public async Task The_language_choice_is_stored_on_its_own_without_the_profile_form()
    {
        // The application shell switches language far from any profile form, so the choice has a
        // route of its own rather than riding the full-DTO profile save, which would let a stale
        // form overwrite it.
        var saved = await me.PutAsJsonAsync(
            "/api/v1/me/locale", new { language = "ro", timeZone = "Europe/Bucharest" });

        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        JsonDocument.Parse(await saved.Content.ReadAsStringAsync()).RootElement
            .GetProperty("language").GetString().ShouldBe("ro");

        // Visible on both the resource of its own and the profile the rest of the app reads.
        var read = JsonDocument.Parse(await (await me.GetAsync("/api/v1/me/locale/")).Content.ReadAsStringAsync())
            .RootElement;
        read.GetProperty("language").GetString().ShouldBe("ro");
        (await GetMeAsync(me)).GetProperty("locale").GetString().ShouldBe("ro");

        // The zone is in the contract and validated, but there is no column for it yet, so it
        // reads back as nothing. When the column lands this assertion is what changes.
        read.GetProperty("timeZone").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task The_language_route_refuses_a_language_that_is_not_a_tag_and_a_zone_that_is_not_a_zone()
    {
        foreach (var body in new object[]
        {
            new { language = "english", timeZone = (string?)null },
            new { language = "", timeZone = (string?)null },
            new { language = "ro", timeZone = "Not A Zone" },
        })
        {
            var response = await me.PutAsJsonAsync("/api/v1/me/locale", body);

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body.ToString());
            JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
                .GetProperty("code").GetString().ShouldBe("validation.failed");
        }

        // The positive half: a language with no zone at all is accepted, because a browser that
        // cannot name its zone must still be able to say what it reads.
        (await me.PutAsJsonAsync("/api/v1/me/locale", new { language = "en", timeZone = (string?)null }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_language_route_requires_authentication()
    {
        using var anonymous = factory.CreateClient();

        (await anonymous.GetAsync("/api/v1/me/locale/")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PutAsJsonAsync("/api/v1/me/locale", new { language = "ro", timeZone = (string?)null }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Addresses_round_trip_including_the_optional_point()
    {
        var created = await me.PostAsJsonAsync("/api/v1/me/addresses/", new
        {
            label = "Home",
            country = "Romania",
            city = "Braşov",
            addressText = "Str. Lungă 1",
            geom = new { type = "Point", coordinates = new[] { 25.59122, 45.65412 } },
            sortOrder = 0,
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        created.Headers.Location.ShouldNotBeNull();
        var addressId = JsonDocument.Parse(await created.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetGuid();

        // Coordinates are compared numerically — a serialized GeoJSON string would be brittle.
        var stored = (await GetMeAsync(me)).GetProperty("addresses")[0];
        var coordinates = stored.GetProperty("geom").GetProperty("coordinates");
        coordinates[0].GetDouble().ShouldBe(25.59122, 1e-9);
        coordinates[1].GetDouble().ShouldBe(45.65412, 1e-9);

        var updated = await me.PutAsJsonAsync($"/api/v1/me/addresses/{addressId}", new
        {
            label = "Cabin",
            country = "Romania",
            city = "Zărneşti",
            addressText = (string?)null,
            geom = (object?)null,
            sortOrder = 1,
        });
        updated.StatusCode.ShouldBe(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());

        var afterEdit = (await GetMeAsync(me)).GetProperty("addresses")[0];
        afterEdit.GetProperty("label").GetString().ShouldBe("Cabin");
        afterEdit.GetProperty("geom").ValueKind.ShouldBe(JsonValueKind.Null);

        (await me.DeleteAsync($"/api/v1/me/addresses/{addressId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await GetMeAsync(me)).GetProperty("addresses").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Another_users_address_is_not_found_rather_than_forbidden()
    {
        var created = await me.PostAsJsonAsync("/api/v1/me/addresses/", NewAddress("Home"));
        var addressId = JsonDocument.Parse(await created.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetGuid();

        // A distinguishable refusal would confirm the id exists.
        var read = await other.PutAsJsonAsync($"/api/v1/me/addresses/{addressId}", NewAddress("Stolen"));
        read.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ProblemCodeAsync(read)).ShouldBe("me.address_not_found");

        (await other.DeleteAsync($"/api/v1/me/addresses/{addressId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Addresses_are_capped()
    {
        for (var i = 0; i < 10; i++)
        {
            (await me.PostAsJsonAsync("/api/v1/me/addresses/", NewAddress($"Place {i}")))
                .StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        var eleventh = await me.PostAsJsonAsync("/api/v1/me/addresses/", NewAddress("One too many"));
        eleventh.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(eleventh)).ShouldBe("me.address_limit");
    }

    [Fact]
    public async Task Avatar_upload_replaces_a_preset_and_is_delivered_over_the_signed_url()
    {
        using var form = BuildForm("face.png", MakePng(64, 64), "image/png");
        var upload = await me.PostAsync("/api/v1/me/avatar", form);
        upload.StatusCode.ShouldBe(HttpStatusCode.OK, await upload.Content.ReadAsStringAsync());

        var url = JsonDocument.Parse(await upload.Content.ReadAsStringAsync())
            .RootElement.GetProperty("avatarUrl").GetString();
        url.ShouldNotBeNull();
        (await me.GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var preset = await me.PutAsJsonAsync("/api/v1/me/avatar", new { preset = "bat" });
        preset.StatusCode.ShouldBe(HttpStatusCode.OK, await preset.Content.ReadAsStringAsync());
        var afterPreset = JsonDocument.Parse(await preset.Content.ReadAsStringAsync()).RootElement;
        afterPreset.GetProperty("avatarPreset").GetString().ShouldBe("bat");
        afterPreset.GetProperty("avatarUrl").ValueKind.ShouldBe(JsonValueKind.Null);

        (await me.DeleteAsync("/api/v1/me/avatar")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var cleared = await GetMeAsync(me);
        cleared.GetProperty("avatarPreset").ValueKind.ShouldBe(JsonValueKind.Null);
        cleared.GetProperty("avatarUrl").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Avatar_never_stores_the_photos_capture_point()
    {
        // For a selfie that point is the user's home, and stored photo points are published on
        // the map — so the avatar path must not run the geotag reader at all.
        using var form = BuildForm("selfie.jpg", MakeGeotaggedJpeg(45.8, 25.8), "image/jpeg");
        var upload = await me.PostAsync("/api/v1/me/avatar", form);
        upload.StatusCode.ShouldBe(HttpStatusCode.OK, await upload.Content.ReadAsStringAsync());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var avatarFileId = await db.Users.Where(u => u.Id == myId).Select(u => u.AvatarFileId).FirstAsync();
        avatarFileId.ShouldNotBeNull();
        var file = await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == avatarFileId!.Value);
        file.Geom.ShouldBeNull();
    }

    [Fact]
    public async Task Avatar_rejects_a_non_image_and_an_unknown_preset()
    {
        using var form = BuildForm("notes.txt", "not an image"u8.ToArray(), "text/plain");
        var upload = await me.PostAsync("/api/v1/me/avatar", form);
        upload.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(upload)).ShouldBe("me.avatar_invalid");

        var unknown = await me.PutAsJsonAsync("/api/v1/me/avatar", new { preset = "dragon" });
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(unknown)).ShouldBe("validation.failed");
    }

    [Fact]
    public async Task Avatar_presets_are_published_for_the_client()
    {
        var response = await me.GetAsync("/api/v1/avatar-presets");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var presets = JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("presets");
        presets.GetArrayLength().ShouldBeGreaterThan(0);
        presets.EnumerateArray().Select(p => p.GetString()).ShouldContain("bat");
    }

    [Fact]
    public async Task Interface_preferences_round_trip_and_reject_anything_but_an_object()
    {
        var saved = await me.PutAsJsonAsync("/api/v1/me/preferences/", new
        {
            preferences = new { theme = "dark", density = "compact", reduceMotion = true },
        });
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());

        var read = JsonDocument.Parse(await (await me.GetAsync("/api/v1/me/preferences/")).Content.ReadAsStringAsync())
            .RootElement.GetProperty("preferences");
        read.GetProperty("theme").GetString().ShouldBe("dark");
        read.GetProperty("reduceMotion").GetBoolean().ShouldBeTrue();

        var array = await me.PutAsJsonAsync("/api/v1/me/preferences/", new { preferences = new[] { 1, 2 } });
        array.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var huge = await me.PutAsJsonAsync("/api/v1/me/preferences/", new
        {
            preferences = new { blob = new string('x', 9000) },
        });
        huge.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Notification_settings_list_every_category_and_lock_security_alerts()
    {
        // Nothing is delivered on an installation with no mail server, and the page must say so —
        // so this reads the settings with the channel reporting itself as absent.
        factory.Messages.MailConfigured = false;
        var read = JsonDocument.Parse(
            await (await me.GetAsync("/api/v1/me/notifications/")).Content.ReadAsStringAsync()).RootElement;
        factory.Messages.MailConfigured = true;

        var categories = read.GetProperty("categories").EnumerateArray().ToList();
        categories.Count.ShouldBe(NotificationCategories.All.Count);
        categories.Single(c => c.GetProperty("category").GetString() == "securityAlerts")
            .GetProperty("locked").GetBoolean().ShouldBeTrue();
        read.GetProperty("deliveryConfigured").GetBoolean().ShouldBeFalse();

        var saved = await me.PutAsJsonAsync("/api/v1/me/notifications/", new
        {
            emailEnabled = false,
            digest = "daily",
            categories = new[] { new { category = "cavingGroupMembership", enabled = false } },
        });
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());

        var after = JsonDocument.Parse(await saved.Content.ReadAsStringAsync()).RootElement;
        after.GetProperty("emailEnabled").GetBoolean().ShouldBeFalse();
        after.GetProperty("digest").GetString().ShouldBe("daily");
        after.GetProperty("categories").EnumerateArray()
            .Single(c => c.GetProperty("category").GetString() == "cavingGroupMembership")
            .GetProperty("enabled").GetBoolean().ShouldBeFalse();

        // One save writes the whole set, so there is no half-stored state to reason about later.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var stored = await db.UserNotificationPreferences.CountAsync(p => p.UserId == myId);
        stored.ShouldBe(NotificationCategories.All.Count);
    }

    [Fact]
    public async Task Security_alerts_cannot_be_switched_off()
    {
        var response = await me.PutAsJsonAsync("/api/v1/me/notifications/", new
        {
            emailEnabled = true,
            digest = "immediate",
            categories = new[] { new { category = "securityAlerts", enabled = false } },
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(response)).ShouldBe("me.notification_locked");
    }

    [Fact]
    public async Task Data_export_queues_one_job_at_a_time_and_stays_private()
    {
        var requested = await me.PostAsync("/api/v1/me/data-export/", null);
        requested.StatusCode.ShouldBe(HttpStatusCode.Accepted, await requested.Content.ReadAsStringAsync());
        var exportId = JsonDocument.Parse(await requested.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var job = await db.ProcessingJobs.AsNoTracking()
                .Where(j => j.Kind == ProcessingJobKinds.AccountDataExport)
                .OrderByDescending(j => j.Id)
                .FirstAsync();
            // The job carries only the export row id — there is no user field in it to tamper with.
            job.Payload.ShouldContain(exportId.ToString());
            job.RequestedBy.ShouldBe(myId);
        }

        // The worker may already have picked the job up, so only a still-pending request conflicts.
        var second = await me.PostAsync("/api/v1/me/data-export/", null);
        second.StatusCode.ShouldBeOneOf(HttpStatusCode.Conflict, HttpStatusCode.Accepted);

        // Someone else's export is invisible, and their own history is empty.
        var theirs = await other.GetAsync("/api/v1/me/data-export/");
        theirs.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.GetAsync($"/api/v1/me/data-export/{exportId}/download"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Data_export_builds_an_archive_of_personal_data_only()
    {
        await me.PutAsJsonAsync("/api/v1/me", ProfileBody(firstName: "Ana"));
        await me.PostAsJsonAsync("/api/v1/me/addresses/", NewAddress("Home"));

        // Membership hangs off the person, so the export must carry the roster entry and
        // the caving groups reached through it — creating one makes that observable.
        var clubName = $"Export Club {suffix}";
        (await me.PostAsJsonAsync("/api/v1/caving-groups/", new
        {
            name = clubName,
            type = "cavingClub",
            description = (string?)null,
            website = (string?)null,
        })).StatusCode.ShouldBe(HttpStatusCode.Created);

        var requested = await me.PostAsync("/api/v1/me/data-export/", null);
        var exportId = JsonDocument.Parse(await requested.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetGuid();

        // Run the handler directly rather than waiting on the polling worker.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            // Selected by requester rather than by payload contents: the payload is jsonb, which
            // has no text-pattern operator.
            var job = await db.ProcessingJobs
                .Where(j => j.Kind == ProcessingJobKinds.AccountDataExport && j.RequestedBy == myId)
                .OrderByDescending(j => j.Id)
                .FirstAsync();
            var handler = scope.ServiceProvider.GetServices<IProcessingJobHandler>()
                .First(h => h.Kind == ProcessingJobKinds.AccountDataExport);
            await handler.ExecuteAsync(job, CancellationToken.None);
        }

        var download = await me.GetAsync($"/api/v1/me/data-export/{exportId}/download");
        download.StatusCode.ShouldBe(HttpStatusCode.OK, await download.Content.ReadAsStringAsync());

        using var archive = new System.IO.Compression.ZipArchive(await download.Content.ReadAsStreamAsync());
        var names = archive.Entries.Select(e => e.FullName).ToList();
        names.ShouldContain("account.json");
        names.ShouldContain("addresses.json");
        names.ShouldContain("visibility.json");
        names.ShouldContain("caver.json");
        names.ShouldContain("caving-groups.json");

        var account = await ReadEntryAsync(archive, "account.json");
        account.ShouldContain("Ana");
        account.ShouldContain(MyEmail);

        // The person behind the account, and the memberships that flow through them.
        var caver = await ReadEntryAsync(archive, "caver.json");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var roster = await db.Cavers.AsNoTracking().SingleAsync(c => c.UserId == myId);
            caver.ShouldContain(roster.Id.ToString());
        }

        (await ReadEntryAsync(archive, "caving-groups.json")).ShouldContain(clubName);
    }

    [Fact]
    public async Task Username_change_takes_effect_and_refuses_a_name_in_use()
    {
        var taken = await me.PutAsJsonAsync("/api/v1/me/username", new { username = OtherEmail });
        taken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(taken)).ShouldBe("me.username_taken");

        var invalid = await me.PutAsJsonAsync("/api/v1/me/username", new { username = "bad name!" });
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(invalid)).ShouldBe("validation.failed");

        var chosen = $"ana-{suffix}";
        var changed = await me.PutAsJsonAsync("/api/v1/me/username", new { username = chosen });
        changed.StatusCode.ShouldBe(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync());
        JsonDocument.Parse(await changed.Content.ReadAsStringAsync())
            .RootElement.GetProperty("userName").GetString().ShouldBe(chosen);

        // Signing in still resolves by address, so a chosen handle never breaks login.
        using var reauthenticated = await AuthHelper.BearerClientAsync(factory, MyEmail);
        (await reauthenticated.GetAsync("/api/v1/me")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Password_change_verifies_the_current_one_and_then_replaces_it()
    {
        var wrong = await me.PutAsJsonAsync("/api/v1/me/password", new
        {
            currentPassword = "not-the-password",
            newPassword = "a-new-long-password-1",
        });
        wrong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(wrong)).ShouldBe("me.password_incorrect");

        var tooShort = await me.PutAsJsonAsync("/api/v1/me/password", new
        {
            currentPassword = AuthHelper.Password,
            newPassword = "short",
        });
        tooShort.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var changed = await me.PutAsJsonAsync("/api/v1/me/password", new
        {
            currentPassword = AuthHelper.Password,
            newPassword = "a-new-long-password-1",
        });
        changed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await changed.Content.ReadAsStringAsync());

        // The old password stops working; asserted through a fresh login attempt.
        using var client = factory.CreateClient();
        var relogin = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new { email = MyEmail, password = AuthHelper.Password });
        relogin.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static object ProfileBody(
        string? firstName = null,
        string? lastName = null,
        string? displayName = null,
        string? bio = null,
        Guid? cavingClubId = null,
        string realName = "private",
        string email = "private") => new
        {
            firstName,
            lastName,
            displayName,
            bio,
            cavingClubId,
            visibility = new
            {
                realName,
                bio = "private",
                email,
                phone = "private",
                cavingClub = "private",
                address = "private",
                addressPoint = "private",
            },
        };

    private static object NewAddress(string label) => new
    {
        label,
        country = "Romania",
        city = "Braşov",
        addressText = (string?)null,
        geom = (object?)null,
        sortOrder = 0,
    };

    private static async Task<JsonElement> GetMeAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/me");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement;
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static async Task<string> ReadEntryAsync(System.IO.Compression.ZipArchive archive, string name)
    {
        using var reader = new StreamReader(archive.GetEntry(name)!.Open());
        return await reader.ReadToEndAsync();
    }

    private static MultipartFormDataContent BuildForm(string fileName, byte[] bytes, string contentType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new(contentType);
        return new MultipartFormDataContent { { content, "file", fileName } };
    }

    private static byte[] MakePng(uint width, uint height)
    {
        using var image = new MagickImage(MagickColors.DarkSlateBlue, width, height);
        return image.ToByteArray(MagickFormat.Png);
    }

    private static byte[] MakeGeotaggedJpeg(double lat, double lon)
    {
        using var image = new MagickImage(MagickColors.ForestGreen, 64, 64);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.GPSLatitudeRef, lat >= 0 ? "N" : "S");
        exif.SetValue(ExifTag.GPSLatitude, ToDms(Math.Abs(lat)));
        exif.SetValue(ExifTag.GPSLongitudeRef, lon >= 0 ? "E" : "W");
        exif.SetValue(ExifTag.GPSLongitude, ToDms(Math.Abs(lon)));
        image.SetProfile(exif);
        return image.ToByteArray(MagickFormat.Jpeg);
    }

    private static Rational[] ToDms(double value)
    {
        var degrees = Math.Floor(value);
        var minutes = Math.Floor((value - degrees) * 60);
        var seconds = (value - degrees - (minutes / 60)) * 3600;
        return [new Rational(degrees), new Rational(minutes), new Rational(seconds)];
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        factory.Dispose();
        try
        {
            if (Directory.Exists(filesRoot))
            {
                Directory.Delete(filesRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // Temp files; best effort.
        }
    }
}
