// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Phone recordings made into trip reports with nobody choosing anything: the job an upload queues,
/// what it makes of a recording that settles and of one that does not, the installation's switch,
/// and the flag a person clears.
///
/// The survey has an entrance at 350 m and stations 20, 50 and 120 m below it. A place measured at
/// 50 m settles on one station; a place at 35 m is exactly as far from the 20 m station as from the
/// 50 m one, which is the tie a reviewer exists for. One phone user is mapped to the uploader's
/// caver; another is mapped to nobody. Everything is invented. Job workers are removed, so the job
/// runs only where a test runs it.
/// </summary>
public sealed class TripRecordingConversionTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset Start = new(2026, 9, 12, 8, 0, 0, TimeSpan.Zero);

    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string marker = Guid.NewGuid().ToString("N")[..8];
    private readonly Guid mappedPhoneUser = Guid.CreateVersion7();
    private readonly Guid unmappedPhoneUser = Guid.CreateVersion7();

    private HttpClient uploader = null!; // Editor, club member
    private HttpClient colleague = null!; // Editor, club member: creates nothing here, places no guarded cave
    private HttpClient member = null!;   // Viewer, club member: reads the club's trips, changes none
    private HttpClient stranger = null!; // Viewer outside the club: reads none of the club's trips
    private HttpClient admin = null!;
    private HttpClient anonymous = null!;
    private Guid uploaderId;
    private Guid adminId;
    private Guid uploaderCaverId;
    private Guid clubId;
    private long caveTypeId;
    private long placeTypeId;

    public TripRecordingConversionTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-tripconvert-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Files:Root"] = filesRoot,
                ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            },
            JobWorkers.RemoveFrom);
    }

    public async ValueTask InitializeAsync()
    {
        uploaderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tc-up-{marker}@t.local");
        var colleagueId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tc-col-{marker}@t.local");
        var memberId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tc-mem-{marker}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tc-str-{marker}@t.local");
        adminId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"tc-adm-{marker}@t.local");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var club = new CavingGroup { Name = $"Club {marker}", Slug = $"club-{marker}" };
            db.CavingGroups.Add(club);
            await db.SaveChangesAsync();
            clubId = club.Id;
            uploaderCaverId = (await RosterHelper.AddMemberAsync(db, clubId, uploaderId)).Id;
            await RosterHelper.AddMemberAsync(db, clubId, memberId);
            await RosterHelper.AddMemberAsync(db, clubId, colleagueId);
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
            placeTypeId = await db.FeatureTypes.Where(t => t.Code == "cave_place").Select(t => t.Id).FirstAsync();

            var now = DateTimeOffset.UtcNow;
            db.DevicePersons.Add(new DevicePerson
            {
                DeviceUserId = mappedPhoneUser,
                Username = $"phone-{marker}",
                CaverId = uploaderCaverId,
                LinkSource = DevicePersonLinkSource.Administrator,
                FirstSeenAt = now,
                LastSeenAt = now,
            });
            await db.SaveChangesAsync();
        }

        uploader = await AuthHelper.BearerClientAsync(factory, $"tc-up-{marker}@t.local");
        colleague = await AuthHelper.BearerClientAsync(factory, $"tc-col-{marker}@t.local");
        member = await AuthHelper.BearerClientAsync(factory, $"tc-mem-{marker}@t.local");
        stranger = await AuthHelper.BearerClientAsync(factory, $"tc-str-{marker}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"tc-adm-{marker}@t.local");
        anonymous = factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        await factory.DisposeAsync();
        Dispose();
    }

    public void Dispose()
    {
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }

    [Fact]
    public async Task An_unambiguous_recording_becomes_a_finished_trip_with_one_position_per_scan_and_its_people()
    {
        var (cave, model) = await CaveWithSurveyAsync();
        var atFifty = await CreatePlaceAsync(cave, depth: -50);
        var atDeep = await CreatePlaceAsync(cave, depth: 120);
        var set = await CreateSetAsync(cave);

        var recording = Guid.CreateVersion7();
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        var answer = await UploadAsync(
            set,
            [Recording(recording, cave, mappedPhoneUser, title: $"Clean {marker}")],
            [Point(first, recording, atFifty, 30), Point(second, recording, atDeep, 90, notes: "  turned back  ")]);
        var batch = answer.GetProperty("importBatchId").GetGuid();

        // Nothing is made by the upload itself: the job is queued with it, once.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.TripRecordings.AsNoTracking().SingleAsync(r => r.Id == recording)).ConvertedTripLogId.ShouldBeNull();
            (await JobsOfAsync(db, batch)).Count.ShouldBe(1);
        }

        await RunConversionAsync(batch);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var tripId = (await db.TripRecordings.AsNoTracking().SingleAsync(r => r.Id == recording)).ConvertedTripLogId;
            tripId.ShouldNotBeNull();

            var trip = await db.TripLogs.AsNoTracking().SingleAsync(t => t.Id == tripId);
            trip.State.ShouldBe(ActivityState.Done);
            trip.OwnerUserId.ShouldBe(uploaderId);
            trip.CavingGroupId.ShouldBe(clubId);
            trip.Visibility.ShouldBe(Visibility.CavingGroup);
            trip.TripDate.ShouldBe(new DateOnly(2026, 9, 12));
            trip.Title.ShouldBe($"Clean {marker}");

            (await db.TripLogParticipants.AsNoTracking().Where(p => p.TripLogId == tripId).Select(p => p.CaverId).ToListAsync())
                .ShouldBe([uploaderCaverId]);
            var tracking = await db.TripTrackings.AsNoTracking().SingleAsync(t => t.TripLogId == tripId);
            tracking.State.ShouldBe(TripTrackingState.Closed);
            tracking.SurveyModelId.ShouldBe(model);

            var events = await db.TripPositionEvents.AsNoTracking()
                .Where(e => e.TripLogId == tripId).OrderBy(e => e.RecordedAt).ToListAsync();
            events.Select(e => e.ViewerStationName).ShouldBe(["cave.upper.2", "cave.deep.3"]);
            events.ShouldAllBe(e => e.Source == TripPositionEventSource.SpeleolocSync
                && e.Kind == TripPositionEventKind.AtStation
                && e.CaverId == uploaderCaverId
                && e.CaveFeatureId == cave
                && e.SurveyModelId == model);
            events[0].RecordedAt.ShouldBe(Start.AddMinutes(30));
            events[1].Note.ShouldBe("turned back");

            (await db.TripAttentions.AsNoTracking().AnyAsync(a => a.TripLogId == tripId)).ShouldBeFalse();
            (await db.Notifications.AsNoTracking().AnyAsync(n => n.Category == NotificationCategory.TripNeedsAttention
                && n.TargetId == tripId)).ShouldBeFalse();

            // The history says the trip came out of the conversion of that upload.
            var created = await db.AuditEntries.AsNoTracking()
                .SingleAsync(a => a.EntityType == nameof(TripLog) && a.EntityId == tripId.ToString() && a.Action == AuditActions.Created);
            created.Source.ShouldBe(ChangeSource.TripConversion);
            created.ImportBatchId.ShouldBe(batch);
        }

        // A second run of the same upload's job — a restart re-delivering it — makes nothing more.
        await RunConversionAsync(batch);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.TripLogs.AsNoTracking().CountAsync(t => t.Title == $"Clean {marker}")).ShouldBe(1);
            var tripId = (await db.TripRecordings.AsNoTracking().SingleAsync(r => r.Id == recording)).ConvertedTripLogId;
            (await db.TripPositionEvents.AsNoTracking().CountAsync(e => e.TripLogId == tripId)).ShouldBe(2);
        }
    }

    [Fact]
    public async Task An_ambiguous_recording_with_the_switch_on_becomes_a_trip_of_what_settled_and_a_flag_naming_the_rest()
    {
        var (cave, _) = await CaveWithSurveyAsync();
        var atFifty = await CreatePlaceAsync(cave, depth: -50);
        var atTie = await CreatePlaceAsync(cave, depth: 35);
        var set = await CreateSetAsync(cave);

        var recording = Guid.CreateVersion7();
        var settles = Guid.CreateVersion7();
        var tied = Guid.CreateVersion7();
        var nowhere = Guid.CreateVersion7();
        var nobody = Guid.CreateVersion7();
        var answer = await UploadAsync(
            set,
            [Recording(recording, cave, mappedPhoneUser)],
            [
                Point(settles, recording, atFifty, 10),
                Point(tied, recording, atTie, 20),
                Point(nowhere, recording, placeId: null, 30),
                Point(nobody, recording, atFifty, 40, by: unmappedPhoneUser),
            ]);
        var batch = answer.GetProperty("importBatchId").GetGuid();

        await RunConversionAsync(batch);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var tripId = (await db.TripRecordings.AsNoTracking().SingleAsync(r => r.Id == recording)).ConvertedTripLogId;
        tripId.ShouldNotBeNull();

        // Only the scan that settled is a position.
        (await db.TripPositionEvents.AsNoTracking().Where(e => e.TripLogId == tripId).Select(e => e.ViewerStationName).ToListAsync())
            .ShouldBe(["cave.upper.2"]);

        var attention = await db.TripAttentions.AsNoTracking().SingleAsync(a => a.TripLogId == tripId);
        attention.ClearedAt.ShouldBeNull();
        var reasons = TripRecordingConversionRules.Parse(attention.Reasons);
        reasons.Select(r => r.Code).ShouldBe(
            [TripAttentionReasons.PlaceUnknown, TripAttentionReasons.UnresolvedStation, TripAttentionReasons.UnmappedPerson]);
        reasons.Single(r => r.Code == TripAttentionReasons.PlaceUnknown).PointIds.ShouldBe([nowhere.ToString()]);
        reasons.Single(r => r.Code == TripAttentionReasons.UnresolvedStation).PointIds.ShouldBe([tied.ToString()]);
        reasons.Single(r => r.Code == TripAttentionReasons.UnmappedPerson).PointIds.ShouldBe([nobody.ToString()]);
        // A reason names scans and how many, and never a station or a place.
        attention.Reasons.ShouldNotContain("cave.upper");
        attention.Reasons.ShouldNotContain(atTie.ToString());

        // The uploader and the installation's administrators are told, about the trip.
        var told = await db.Notifications.AsNoTracking()
            .Where(n => n.Category == NotificationCategory.TripNeedsAttention && n.TargetId == tripId)
            .ToListAsync();
        await ShouldBeTheUploaderAndTheAdministratorsAsync(db, told);
        told.ShouldAllBe(n => n.TargetKind == NotificationTargetKind.TripLog);
        told.ShouldAllBe(n => n.TemplateKey == Domain.Messaging.MessageTemplateCatalog.NotifyTripNeedsAttention);
    }

    [Fact]
    public async Task With_the_switch_off_an_ambiguous_recording_stays_a_recording_and_its_uploader_is_told()
    {
        var (cave, _) = await CaveWithSurveyAsync();
        var atFifty = await CreatePlaceAsync(cave, depth: -50);
        var set = await CreateSetAsync(cave);
        await SwitchAsync(convertTripsWithProblems: false);
        try
        {
            var ambiguous = Guid.CreateVersion7();
            var clean = Guid.CreateVersion7();
            var answer = await UploadAsync(
                set,
                [Recording(ambiguous, cave, mappedPhoneUser, title: $"Waiting {marker}"), Recording(clean, cave, mappedPhoneUser)],
                [
                    Point(Guid.CreateVersion7(), ambiguous, atFifty, 10, by: unmappedPhoneUser),
                    Point(Guid.CreateVersion7(), clean, atFifty, 10),
                ]);

            await RunConversionAsync(answer.GetProperty("importBatchId").GetGuid());

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.TripRecordings.AsNoTracking().SingleAsync(r => r.Id == ambiguous)).ConvertedTripLogId.ShouldBeNull();
            // The switch is about recordings that do not settle; one that does is still made a trip.
            (await db.TripRecordings.AsNoTracking().SingleAsync(r => r.Id == clean)).ConvertedTripLogId.ShouldNotBeNull();
            (await db.TripLogs.AsNoTracking().CountAsync(t => t.Title == $"Waiting {marker}")).ShouldBe(0);

            var told = await db.Notifications.AsNoTracking()
                .Where(n => n.Category == NotificationCategory.TripNeedsAttention && n.TargetId == cave)
                .ToListAsync();
            await ShouldBeTheUploaderAndTheAdministratorsAsync(db, told);
            told.ShouldAllBe(n => n.TemplateKey == Domain.Messaging.MessageTemplateCatalog.NotifyTripRecordingWaiting
                && n.TargetKind == NotificationTargetKind.Feature);
        }
        finally
        {
            await SwitchAsync(convertTripsWithProblems: true);
        }
    }

    [Fact]
    public async Task A_flag_is_cleared_by_somebody_who_may_change_the_trip_and_by_nobody_else()
    {
        var (cave, _) = await CaveWithSurveyAsync();
        var set = await CreateSetAsync(cave);
        var recording = Guid.CreateVersion7();
        var answer = await UploadAsync(
            set, [Recording(recording, cave, mappedPhoneUser)], [Point(Guid.CreateVersion7(), recording, null, 10)]);
        await RunConversionAsync(answer.GetProperty("importBatchId").GetGuid());
        var tripId = await TripOfAsync(recording);
        var url = $"/api/v1/trip-logs/{tripId}/attention/clear";

        // Fixture proof: the member of the club reads the trip, the stranger does not.
        (await member.GetAsync($"/api/v1/trip-logs/{tripId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await stranger.GetAsync($"/api/v1/trip-logs/{tripId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await anonymous.PostAsync(url, null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var hidden = await stranger.PostAsync(url, null);
        hidden.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeOf(hidden)).ShouldBe("trip_log.not_found");
        (await member.PostAsync(url, null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await AttentionOfAsync(tripId)).ClearedAt.ShouldBeNull();

        (await uploader.PostAsync(url, null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var cleared = await AttentionOfAsync(tripId);
        cleared.ClearedAt.ShouldNotBeNull();
        cleared.ClearedByUserId.ShouldBe(uploaderId);
        TripRecordingConversionRules.Parse(cleared.Reasons).ShouldNotBeEmpty();

        // A second clearance is not a new one.
        (await admin.PostAsync(url, null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var again = await AttentionOfAsync(tripId);
        again.ClearedAt.ShouldBe(cleared.ClearedAt);
        again.ClearedByUserId.ShouldBe(uploaderId);

        // A trip with no flag says so.
        var plain = await uploader.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"Plain {marker}",
            tripDate = "2026-09-10",
            participants = Array.Empty<object>(),
        });
        plain.StatusCode.ShouldBe(HttpStatusCode.Created, await plain.Content.ReadAsStringAsync());
        var plainId = (await plain.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var none = await uploader.PostAsync($"/api/v1/trip-logs/{plainId}/attention/clear", null);
        none.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeOf(none)).ShouldBe("trip_attention.not_found");
    }

    [Fact]
    public async Task An_open_recording_waits_and_a_change_after_conversion_flags_the_trip_instead_of_rewriting_it()
    {
        var (cave, _) = await CaveWithSurveyAsync();
        var atFifty = await CreatePlaceAsync(cave, depth: -50);
        var set = await CreateSetAsync(cave);
        var recording = Guid.CreateVersion7();

        // Still under way on the phone: nothing is made.
        var open = await UploadAsync(
            set, [Recording(recording, cave, mappedPhoneUser, ended: false)], [Point(Guid.CreateVersion7(), recording, atFifty, 10)]);
        await RunConversionAsync(open.GetProperty("importBatchId").GetGuid());
        (await RecordingAsync(recording)).ConvertedTripLogId.ShouldBeNull();

        // The upload that closes it makes the trip.
        var revision = (await RecordingAsync(recording)).UpdatedAt;
        var closing = await UploadAsync(set, [Recording(recording, cave, mappedPhoneUser, baseRevision: revision)], []);
        await RunConversionAsync(closing.GetProperty("importBatchId").GetGuid());
        var tripId = await TripOfAsync(recording);
        (await EventCountAsync(tripId)).ShouldBe(1);

        // A scan arriving afterwards does not rewrite the trip; it flags it.
        var late = Guid.CreateVersion7();
        var later = await UploadAsync(set, [], [Point(late, recording, atFifty, 50)]);
        var laterBatch = later.GetProperty("importBatchId").GetGuid();
        await RunConversionAsync(laterBatch);
        (await EventCountAsync(tripId)).ShouldBe(1);
        var reasons = TripRecordingConversionRules.Parse((await AttentionOfAsync(tripId)).Reasons);
        var changed = reasons.Single(r => r.Code == TripAttentionReasons.RecordingChangedAfterConversion);
        changed.Count.ShouldBe(1);
        changed.PointIds.ShouldBe([late.ToString()]);

        // Re-delivered, the same upload's job does not count the same change twice.
        await RunConversionAsync(laterBatch);
        TripRecordingConversionRules.Parse((await AttentionOfAsync(tripId)).Reasons)
            .Single(r => r.Code == TripAttentionReasons.RecordingChangedAfterConversion).Count.ShouldBe(1);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.Notifications.AsNoTracking()
                .CountAsync(n => n.Category == NotificationCategory.TripNeedsAttention && n.TargetId == tripId && n.RecipientUserId == uploaderId))
            .ShouldBe(1);

        // The phone deleting the recording is a change after conversion too: the trip is flagged
        // once more and its people told, and the trip itself stays.
        var current = (await db.TripRecordings.AsNoTracking().SingleAsync(r => r.Id == recording)).UpdatedAt;
        var removal = await UploadAsync(set, [Recording(recording, cave, mappedPhoneUser, baseRevision: current, deleted: true)], []);
        await RunConversionAsync(removal.GetProperty("importBatchId").GetGuid());
        TripRecordingConversionRules.Parse((await AttentionOfAsync(tripId)).Reasons)
            .Single(r => r.Code == TripAttentionReasons.RecordingChangedAfterConversion).Count.ShouldBe(2);
        (await db.TripLogs.AsNoTracking().CountAsync(t => t.Id == tripId)).ShouldBe(1);
        (await db.Notifications.AsNoTracking()
                .CountAsync(n => n.Category == NotificationCategory.TripNeedsAttention && n.TargetId == tripId && n.RecipientUserId == uploaderId))
            .ShouldBe(2);
    }

    /// <summary>Everybody told is the uploader or a full administrator, and both are told.</summary>
    /// <summary>
    /// Undoing a phone upload takes back the recording it brought, and the trip report made from it
    /// when nobody has touched that report since; a report somebody worked on is left, and named.
    /// </summary>
    [Fact]
    public async Task An_undo_takes_back_a_recording_with_its_untouched_trip_and_leaves_a_trip_somebody_changed()
    {
        var (cave, _) = await CaveWithSurveyAsync();
        var atFifty = await CreatePlaceAsync(cave, depth: -50);
        var set = await CreateSetAsync(cave);

        var untouched = Guid.CreateVersion7();
        var untouchedScan = Guid.CreateVersion7();
        var first = (await UploadAsync(
                set,
                [Recording(untouched, cave, mappedPhoneUser, title: $"Untouched {marker}")],
                [Point(untouchedScan, untouched, atFifty, 30)]))
            .GetProperty("importBatchId").GetGuid();
        await RunConversionAsync(first);
        var untouchedTrip = await TripOfAsync(untouched);

        var edited = Guid.CreateVersion7();
        var second = (await UploadAsync(
                set,
                [Recording(edited, cave, mappedPhoneUser, title: $"Edited {marker}")],
                [Point(Guid.CreateVersion7(), edited, atFifty, 40)]))
            .GetProperty("importBatchId").GetGuid();
        await RunConversionAsync(second);
        var editedTrip = await TripOfAsync(edited);

        // Somebody writes the second report up by hand.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var trip = await db.TripLogs.SingleAsync(t => t.Id == editedTrip);
            trip.Results = "Written up by hand";
            await db.SaveChangesAsync();
        }

        var firstUndo = await RevertAsync(first);
        var recordingLine = firstUndo.Single(l => l.TryGetProperty("tripRecordingId", out var r)
            && r.ValueKind == JsonValueKind.String && r.GetGuid() == untouched);
        recordingLine.GetProperty("outcome").GetString().ShouldBe("undone");
        recordingLine.GetProperty("tripLogId").GetGuid().ShouldBe(untouchedTrip);
        recordingLine.GetProperty("tripOutcome").GetString().ShouldBe("undone");
        firstUndo.Single(l => l.TryGetProperty("tripRecordingPointId", out var p)
                && p.ValueKind == JsonValueKind.String && p.GetGuid() == untouchedScan)
            .GetProperty("outcome").GetString().ShouldBe("undone");

        var secondUndo = await RevertAsync(second);
        var editedLine = secondUndo.Single(l => l.TryGetProperty("tripRecordingId", out var r)
            && r.ValueKind == JsonValueKind.String && r.GetGuid() == edited);
        editedLine.GetProperty("outcome").GetString().ShouldBe("undone");
        editedLine.GetProperty("tripLogId").GetGuid().ShouldBe(editedTrip);
        editedLine.GetProperty("tripOutcome").GetString().ShouldBe("not_undone");
        editedLine.GetProperty("tripReason").GetString().ShouldBe("trip_changed");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            // Both recordings are deleted — not removed, so the phones carrying them are told.
            (await db.TripRecordings.CountAsync(r => r.Id == untouched || r.Id == edited)).ShouldBe(0);
            (await db.TripRecordings.IgnoreQueryFilters().CountAsync(r => (r.Id == untouched || r.Id == edited) && r.DeletedAt != null))
                .ShouldBe(2);
            (await db.TripRecordingPoints.IgnoreQueryFilters().SingleAsync(p => p.Id == untouchedScan)).DeletedAt.ShouldNotBeNull();

            // The untouched report went with its recording, deleted the way a person deletes one.
            (await db.TripLogs.CountAsync(t => t.Id == untouchedTrip)).ShouldBe(0);
            (await db.TripLogs.IgnoreQueryFilters().SingleAsync(t => t.Id == untouchedTrip)).DeletedAt.ShouldNotBeNull();
            // The one somebody wrote up is still theirs.
            (await db.TripLogs.AsNoTracking().SingleAsync(t => t.Id == editedTrip)).Results.ShouldBe("Written up by hand");

            // Every line of both uploads is closed, so both read as undone.
            (await db.ImportBatches.AsNoTracking().Where(b => b.Id == first || b.Id == second).CountAsync(b => b.RevertedAt != null))
                .ShouldBe(2);
        }
    }

    /// <summary>A generated picture hung on a recording or a scan; returns its file.</summary>
    private async Task<Guid> HangPictureAsync(AttachedEntityType target, Guid targetId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var document = new Document { Title = $"Picture {marker}", OwnerUserId = uploaderId, Visibility = Visibility.Authenticated };
        db.Documents.Add(document);
        var version = new DocumentVersion { DocumentId = document.Id, VersionNumber = 1, IsCurrent = true, UploadedBy = uploaderId };
        db.DocumentVersions.Add(version);
        var file = new StoredFile
        {
            DocumentVersionId = version.Id,
            StoragePath = Guid.NewGuid().ToString("N"),
            OriginalName = "picture.jpg",
            MimeType = "image/jpeg",
            SizeBytes = 1,
            Sha256 = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            Kind = FileKind.Image,
        };
        db.StoredFiles.Add(file);
        db.Attachments.Add(new Attachment { FileId = file.Id, EntityType = target, EntityId = targetId });
        await db.SaveChangesAsync();
        return file.Id;
    }

    /// <summary>
    /// A recording the conversion left waiting becomes a trip by hand: the archive import's review,
    /// reading the recording instead of an archive, with the same answers and the same refusals —
    /// and once it is a report, neither the review nor the automatic conversion makes another.
    /// </summary>
    [Fact]
    public async Task A_recording_left_waiting_is_made_a_trip_by_hand_through_the_review_of_the_recording()
    {
        var (cave, model) = await CaveWithSurveyAsync();
        var atFifty = await CreatePlaceAsync(cave, depth: -50);
        var set = await CreateSetAsync(cave);
        var waiting = Guid.CreateVersion7();
        var scan = Guid.CreateVersion7();
        await SwitchAsync(convertTripsWithProblems: false);
        try
        {
            var answer = await UploadAsync(
                set,
                [Recording(waiting, cave, mappedPhoneUser, title: $"By hand {marker}")],
                [Point(scan, waiting, atFifty, 10, by: unmappedPhoneUser)]);
            await RunConversionAsync(answer.GetProperty("importBatchId").GetGuid());
            (await RecordingAsync(waiting)).ConvertedTripLogId.ShouldBeNull();
        }
        finally
        {
            await SwitchAsync(convertTripsWithProblems: true);
        }

        var options = new
        {
            createTrip = true,
            surveyModelId = model,
            cavers = new Dictionary<string, Guid> { [unmappedPhoneUser.ToString()] = uploaderCaverId },
            cavingGroupId = clubId,
            visibility = "cavingGroup",
            candidateCount = 5,
        };
        var preview = $"/api/v1/speleoloc-imports/recordings/{waiting}/preview";
        var commit = $"/api/v1/speleoloc-imports/recordings/{waiting}/commit";

        (await anonymous.PostAsJsonAsync(preview, new { options })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync(commit, new { options, pointIds = new[] { scan.ToString() } }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        // A Viewer reads the recording and may not make trips at all.
        var viewer = await member.PostAsJsonAsync(preview, new { options });
        viewer.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeOf(viewer)).ShouldBe("access.create_forbidden");
        var nowhere = await uploader.PostAsJsonAsync(
            $"/api/v1/speleoloc-imports/recordings/{Guid.CreateVersion7()}/preview", new { options });
        nowhere.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeOf(nowhere)).ShouldBe("trip_recording.not_found");

        var dryRun = await uploader.PostAsJsonAsync(preview, new { options, page = 1, pageSize = 50 });
        var dryBody = await dryRun.Content.ReadAsStringAsync();
        dryRun.StatusCode.ShouldBe(HttpStatusCode.OK, dryBody);
        var read = JsonDocument.Parse(dryBody).RootElement;
        read.GetProperty("recordingTitle").GetString().ShouldBe($"By hand {marker}");
        read.GetProperty("selectablePointIds").EnumerateArray().Select(e => e.GetString()).ShouldBe([scan.ToString()]);
        read.GetProperty("points")[0].GetProperty("candidates")[0].GetProperty("stationName").GetString().ShouldBe("cave.upper.2");

        // A picture on the recording and one on its scan, hung before the review.
        var pictures = new[]
        {
            await HangPictureAsync(AttachedEntityType.TripRecording, waiting),
            await HangPictureAsync(AttachedEntityType.TripRecordingPoint, scan),
        };

        var made = await uploader.PostAsJsonAsync(commit, new { options, pointIds = new[] { scan.ToString() } });
        var madeBody = await made.Content.ReadAsStringAsync();
        made.StatusCode.ShouldBe(HttpStatusCode.OK, madeBody);
        var result = JsonDocument.Parse(madeBody).RootElement;
        result.GetProperty("createdTrip").GetBoolean().ShouldBeTrue();
        result.GetProperty("createdEventCount").GetInt32().ShouldBe(1);
        var tripId = result.GetProperty("tripLogId").GetGuid();
        (await TripOfAsync(waiting)).ShouldBe(tripId);

        // The report made by hand carries the photographs the automatic conversion would give it.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.Attachments.AsNoTracking()
                    .Where(a => a.EntityType == AttachedEntityType.TripLog && a.EntityId == tripId)
                    .Select(a => a.FileId).ToListAsync())
                .ShouldBe(pictures, ignoreOrder: true);
        }

        (await EventCountAsync(tripId)).ShouldBe(1);

        // Made once: the review refuses it, naming the trip, and a later upload's change flags it.
        var again = await uploader.PostAsJsonAsync(preview, new { options });
        var againBody = await again.Content.ReadAsStringAsync();
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict, againBody);
        JsonDocument.Parse(againBody).RootElement.GetProperty("code").GetString().ShouldBe("import.recording_already_converted");
        JsonDocument.Parse(againBody).RootElement.GetProperty("tripLogId").GetGuid().ShouldBe(tripId);
        var twice = await uploader.PostAsJsonAsync(commit, new { options, pointIds = new[] { scan.ToString() } });
        twice.StatusCode.ShouldBe(HttpStatusCode.Conflict, await twice.Content.ReadAsStringAsync());
        (await EventCountAsync(tripId)).ShouldBe(1);
    }

    /// <summary>
    /// What an upload changed or deleted in a recording is put back by its undo, and the batch
    /// reads as undone only once it is.
    /// </summary>
    [Fact]
    public async Task An_undo_puts_back_a_recording_an_upload_deleted_and_the_fields_it_changed()
    {
        var (cave, _) = await CaveWithSurveyAsync();
        var set = await CreateSetAsync(cave);
        var recording = Guid.CreateVersion7();
        var created = await UploadAsync(
            set, [Recording(recording, cave, mappedPhoneUser, title: $"Before {marker}", ended: false)], []);
        await RunConversionAsync(created.GetProperty("importBatchId").GetGuid());

        var renamed = (await UploadAsync(
                set,
                [Recording(recording, cave, mappedPhoneUser, title: $"After {marker}", ended: false,
                    baseRevision: (await RecordingAsync(recording)).UpdatedAt)],
                []))
            .GetProperty("importBatchId").GetGuid();
        var removed = (await UploadAsync(
                set,
                [Recording(recording, cave, mappedPhoneUser, title: $"After {marker}", ended: false,
                    baseRevision: (await RecordingAsync(recording)).UpdatedAt, deleted: true)],
                []))
            .GetProperty("importBatchId").GetGuid();

        var removedLine = (await RevertAsync(removed)).Single();
        removedLine.GetProperty("change").GetString().ShouldBe("deleted");
        removedLine.GetProperty("outcome").GetString().ShouldBe("undone");
        (await RecordingAsync(recording)).Title.ShouldBe($"After {marker}");

        var renamedLine = (await RevertAsync(renamed)).Single();
        renamedLine.GetProperty("outcome").GetString().ShouldBe("undone");
        renamedLine.GetProperty("applied").EnumerateArray().Select(a => a.GetString()).ShouldBe(["Title"]);

        var back = await RecordingAsync(recording);
        back.DeletedAt.ShouldBeNull();
        back.Title.ShouldBe($"Before {marker}");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.ImportBatches.AsNoTracking().Where(b => b.Id == renamed || b.Id == removed).CountAsync(b => b.RevertedAt != null))
            .ShouldBe(2);
    }

    /// <summary>
    /// A recording sent while the trip was under way and closed by a later upload: undoing either
    /// upload takes back the report the closing upload's conversion made, while nobody has touched it.
    /// </summary>
    [Fact]
    public async Task An_undo_of_either_upload_takes_back_the_report_made_when_a_later_upload_closed_the_recording()
    {
        var (cave, _) = await CaveWithSurveyAsync();
        var atFifty = await CreatePlaceAsync(cave, depth: -50);
        var set = await CreateSetAsync(cave);

        async Task<(Guid Opening, Guid Closing, Guid Trip)> OpenThenCloseAsync(Guid recording)
        {
            var opening = (await UploadAsync(
                    set, [Recording(recording, cave, mappedPhoneUser, ended: false)], [Point(Guid.CreateVersion7(), recording, atFifty, 10)]))
                .GetProperty("importBatchId").GetGuid();
            await RunConversionAsync(opening);
            var closing = (await UploadAsync(
                    set, [Recording(recording, cave, mappedPhoneUser, baseRevision: (await RecordingAsync(recording)).UpdatedAt)], []))
                .GetProperty("importBatchId").GetGuid();
            await RunConversionAsync(closing);
            return (opening, closing, await TripOfAsync(recording));
        }

        var first = Guid.CreateVersion7();
        var (firstOpening, _, firstTrip) = await OpenThenCloseAsync(first);
        var openingLine = (await RevertAsync(firstOpening)).Single(l => l.TryGetProperty("tripRecordingId", out var r)
            && r.ValueKind == JsonValueKind.String && r.GetGuid() == first);
        openingLine.GetProperty("tripLogId").GetGuid().ShouldBe(firstTrip);
        openingLine.GetProperty("tripOutcome").GetString().ShouldBe("undone");

        var second = Guid.CreateVersion7();
        var (_, secondClosing, secondTrip) = await OpenThenCloseAsync(second);
        var closingLine = (await RevertAsync(secondClosing)).Single();
        closingLine.GetProperty("outcome").GetString().ShouldBe("undone");
        closingLine.GetProperty("applied").EnumerateArray().Select(a => a.GetString()).ShouldContain("EndedAt");
        closingLine.GetProperty("tripLogId").GetGuid().ShouldBe(secondTrip);
        closingLine.GetProperty("tripOutcome").GetString().ShouldBe("undone");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.TripLogs.CountAsync(t => t.Id == firstTrip || t.Id == secondTrip)).ShouldBe(0);
        (await db.TripRecordings.IgnoreQueryFilters().AsNoTracking().SingleAsync(r => r.Id == first)).DeletedAt.ShouldNotBeNull();
        // Open again, and unmarked, so the upload that closes it next makes the report again.
        var reopened = await db.TripRecordings.AsNoTracking().SingleAsync(r => r.Id == second);
        reopened.EndedAt.ShouldBeNull();
        reopened.ConvertedTripLogId.ShouldBeNull();
    }

    /// <summary>
    /// The trip made from a recording names its cave only to a reader who may read the cave and
    /// place it — a club member reads the trip and is told neither a private cave of somebody else's
    /// nor a guarded one, while the uploader is told both; and the guarded cave's positions are
    /// withheld from that member.
    /// </summary>
    [Fact]
    public async Task A_trip_made_from_a_recording_names_its_cave_only_to_a_reader_who_may_read_and_place_it()
    {
        foreach (var (visibility, guarded) in new[] { ("private", false), ("authenticated", true) })
        {
            var (cave, _) = await CaveWithSurveyAsync(visibility, guarded);
            var atFifty = await CreatePlaceAsync(cave, depth: -50);
            var set = await CreateSetAsync(cave);
            var recording = Guid.CreateVersion7();
            var answer = await UploadAsync(set, [Recording(recording, cave, mappedPhoneUser)], [Point(Guid.CreateVersion7(), recording, atFifty, 10)]);
            await RunConversionAsync(answer.GetProperty("importBatchId").GetGuid());
            var tripId = await TripOfAsync(recording);
            (await EventCountAsync(tripId)).ShouldBe(1);

            // Fixture proof: the member reads the private cave not at all, the guarded one without
            // its survey; the uploader reads both with theirs.
            (await member.GetAsync($"/api/v1/caves/{cave}")).StatusCode.ShouldBe(guarded ? HttpStatusCode.OK : HttpStatusCode.NotFound);
            if (guarded)
            {
                (await member.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{cave}/survey-models")).GetArrayLength().ShouldBe(0);
            }

            var mine = await uploader.GetFromJsonAsync<JsonElement>($"/api/v1/trip-logs/{tripId}");
            mine.GetProperty("caveIds").EnumerateArray().Select(c => c.GetGuid()).ShouldBe([cave]);
            var theirs = await member.GetFromJsonAsync<JsonElement>($"/api/v1/trip-logs/{tripId}");
            theirs.GetProperty("caveIds").GetArrayLength().ShouldBe(0, visibility);

            var myEvents = (await uploader.GetFromJsonAsync<JsonElement>($"/api/v1/trip-logs/{tripId}/tracking/events"))
                .GetProperty("items").EnumerateArray().ToList();
            myEvents.ShouldAllBe(e => e.GetProperty("stationName").ValueKind == JsonValueKind.String);
            if (guarded)
            {
                var theirEvents = (await member.GetFromJsonAsync<JsonElement>($"/api/v1/trip-logs/{tripId}/tracking/events"))
                    .GetProperty("items").EnumerateArray().ToList();
                theirEvents.Count.ShouldBe(1);
                theirEvents.ShouldAllBe(e => e.GetProperty("stationName").ValueKind == JsonValueKind.Null
                    && e.GetProperty("surveyModelId").ValueKind == JsonValueKind.Null);
            }
        }
    }

    /// <summary>
    /// The conversion places scans on a survey only for an uploader who may see that survey: the
    /// same recording in a guarded cave becomes positions for the cave's owner, and for a colleague
    /// who may make the trip but not place the cave becomes a trip with none, flagged.
    /// </summary>
    [Fact]
    public async Task A_conversion_gives_positions_only_to_an_uploader_who_may_place_the_cave()
    {
        var (cave, _) = await CaveWithSurveyAsync(locationProtected: true);
        var atFifty = await CreatePlaceAsync(cave, depth: -50);

        // Fixture proof: the colleague reads the cave and not its survey; the owner reads both.
        (await colleague.GetAsync($"/api/v1/caves/{cave}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await colleague.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{cave}/survey-models")).GetArrayLength().ShouldBe(0);
        (await uploader.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{cave}/survey-models")).GetArrayLength().ShouldBe(1);

        async Task<Guid> ConvertAsAsync(HttpClient client)
        {
            var set = await CreateSetAsync(client, cave);
            var recording = Guid.CreateVersion7();
            var answer = await UploadAsync(
                client, set, [Recording(recording, cave, mappedPhoneUser)], [Point(Guid.CreateVersion7(), recording, atFifty, 10)]);
            await RunConversionAsync(answer.GetProperty("importBatchId").GetGuid());
            return await TripOfAsync(recording);
        }

        var owners = await ConvertAsAsync(uploader);
        (await EventCountAsync(owners)).ShouldBe(1);

        var colleagues = await ConvertAsAsync(colleague);
        (await EventCountAsync(colleagues)).ShouldBe(0);
        TripRecordingConversionRules.Parse((await AttentionOfAsync(colleagues)).Reasons)
            .Select(r => r.Code).ShouldContain(TripAttentionReasons.UnresolvedStation);
    }

    /// <summary>
    /// The flag is written against the version it was read at: a clearance prepared before an
    /// upload raised the flag again fails rather than clearing a reason nobody saw, and the raise
    /// prepared before a clearance is read again rather than landing on a cleared row.
    /// </summary>
    [Fact]
    public async Task A_flag_written_against_a_stale_reading_fails_instead_of_landing()
    {
        var (cave, _) = await CaveWithSurveyAsync();
        var set = await CreateSetAsync(cave);
        var recording = Guid.CreateVersion7();
        var answer = await UploadAsync(
            set, [Recording(recording, cave, mappedPhoneUser)], [Point(Guid.CreateVersion7(), recording, null, 10)]);
        await RunConversionAsync(answer.GetProperty("importBatchId").GetGuid());
        var tripId = await TripOfAsync(recording);

        await using var stale = factory.Services.CreateAsyncScope();
        var staleDb = stale.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var read = await staleDb.TripAttentions.SingleAsync(a => a.TripLogId == tripId);

        // Raised again in between, the way a later upload's job does.
        var late = await UploadAsync(set, [], [Point(Guid.CreateVersion7(), recording, null, 50)]);
        await RunConversionAsync(late.GetProperty("importBatchId").GetGuid());

        read.ClearedAt = DateTimeOffset.UtcNow;
        read.ClearedByUserId = uploaderId;
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => staleDb.SaveChangesAsync());

        var current = await AttentionOfAsync(tripId);
        current.ClearedAt.ShouldBeNull();
        TripRecordingConversionRules.Parse(current.Reasons).Select(r => r.Code)
            .ShouldContain(TripAttentionReasons.RecordingChangedAfterConversion);
    }

    private async Task<List<JsonElement>> RevertAsync(Guid importBatchId)
    {
        var response = await uploader.PostAsync($"/api/v1/import-batches/{importBatchId}/revert", null);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        return [.. JsonDocument.Parse(body).RootElement.GetProperty("items").EnumerateArray().Select(e => e.Clone())];
    }

    private async Task ShouldBeTheUploaderAndTheAdministratorsAsync(SilexGisDbContext db, IReadOnlyList<Notification> told)
    {
        var administrators = await Infrastructure.Permissions.FullAdministrators.LiveMemberIdsAsync(db);
        administrators.ShouldContain(adminId);
        var recipients = told.Select(n => n.RecipientUserId).ToList();
        recipients.ShouldBeUnique();
        recipients.ShouldContain(uploaderId);
        recipients.ShouldContain(adminId);
        recipients.ShouldAllBe(r => r == uploaderId || administrators.Contains(r));
    }

    private async Task RunConversionAsync(Guid importBatchId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var job = (await JobsOfAsync(db, importBatchId)).Single();
        var handler = scope.ServiceProvider.GetServices<IProcessingJobHandler>()
            .Single(h => h.Kind == ProcessingJobKinds.TripRecordingConvert);
        await handler.ExecuteAsync(job, CancellationToken.None);
    }

    private static async Task<List<ProcessingJob>> JobsOfAsync(SilexGisDbContext db, Guid importBatchId)
    {
        var jobs = await db.ProcessingJobs.AsNoTracking()
            .Where(j => j.Kind == ProcessingJobKinds.TripRecordingConvert)
            .ToListAsync();
        return [.. jobs.Where(j => JsonSerializer.Deserialize<TripRecordingConvertPayload>(
            j.Payload, JsonSerializerOptions.Web)!.ImportBatchId == importBatchId)];
    }

    private async Task<TripRecording> RecordingAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.TripRecordings.AsNoTracking().SingleAsync(r => r.Id == id);
    }

    private async Task<Guid> TripOfAsync(Guid recording) =>
        (await RecordingAsync(recording)).ConvertedTripLogId ?? throw new ShouldAssertException("no trip was made");

    private async Task<TripAttention> AttentionOfAsync(Guid tripId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.TripAttentions.AsNoTracking().SingleAsync(a => a.TripLogId == tripId);
    }

    private async Task<int> EventCountAsync(Guid tripId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.TripPositionEvents.AsNoTracking().CountAsync(e => e.TripLogId == tripId);
    }

    private async Task SwitchAsync(bool convertTripsWithProblems)
    {
        var saved = await admin.PutAsJsonAsync("/api/v1/admin/settings/sync", new
        {
            peopleDirectoryEnabled = true,
            peopleDirectoryScope = "myGroups",
            convertTripsWithProblems,
            stagedUploadRetentionDays = 7,
            rejectedHeldBytesRetentionDays = 30,
        });
        saved.IsSuccessStatusCode.ShouldBeTrue(await saved.Content.ReadAsStringAsync());
    }

    private object Recording(
        Guid id,
        Guid caveId,
        Guid startedBy,
        string title = "Sump trip",
        bool ended = true,
        DateTimeOffset? baseRevision = null,
        bool deleted = false) => new
    {
        id,
        baseRevision,
        deleted,
        caveId,
        title,
        description = "Invented for the test",
        log = "entered; turned back at the sump",
        startedAt = Start,
        endedAt = ended ? Start.AddHours(8) : (DateTimeOffset?)null,
        createdByDeviceUserId = startedBy,
    };

    private static object Point(
        Guid id, Guid recordingId, Guid? placeId, int minutes, Guid? by = null, string? notes = null) => new
    {
        id,
        recordingId,
        placeId,
        scannedAt = Start.AddMinutes(minutes),
        notes = notes ?? $"scan {minutes}",
        createdByDeviceUserId = by,
    };

    private Task<JsonElement> UploadAsync(Guid set, IReadOnlyList<object> recordings, IReadOnlyList<object> points) =>
        UploadAsync(uploader, set, recordings, points);

    private static async Task<JsonElement> UploadAsync(
        HttpClient client, Guid set, IReadOnlyList<object> recordings, IReadOnlyList<object> points)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/sync/sets/{set}/upload", new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            tripRecordings = recordings,
            tripPoints = points,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        var answer = JsonDocument.Parse(payload).RootElement;
        foreach (var section in new[] { "tripRecordings", "tripPoints" })
        {
            if (answer.TryGetProperty(section, out var results) && results.ValueKind == JsonValueKind.Array)
            {
                results.EnumerateArray().Select(r => r.GetProperty("status").GetString())
                    .ShouldAllBe(s => s == "created" || s == "updated" || s == "deleted", payload);
            }
        }

        return answer;
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString();

    private Task<Guid> CreateSetAsync(Guid cave) => CreateSetAsync(uploader, cave);

    private async Task<Guid> CreateSetAsync(HttpClient client, Guid cave)
    {
        var response = await client.PostAsJsonAsync("/api/v1/sync/sets/", new
        {
            name = $"Phone {marker}",
            cavingGroupId = clubId,
            uploadVisibility = "private",
            rootFeatureIds = new[] { cave },
            settings = new { },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>A cave of the uploader's, open unless said otherwise, with a finished line-plot survey of four stations.</summary>
    private async Task<(Guid Cave, Guid Model)> CaveWithSurveyAsync(string visibility = "authenticated", bool locationProtected = false)
    {
        var response = await uploader.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Convert {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility,
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var cave = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        using var form = new MultipartFormDataContent();
        var bytes = new ByteArrayContent([1, 2, 3, 4]);
        bytes.Headers.ContentType = new("application/octet-stream");
        form.Add(bytes, "file", "convert.3d");
        var created = await uploader.PostAsync($"/api/v1/caves/{cave}/survey-models", form);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var model = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var row = await db.SurveyModels.SingleAsync(m => m.Id == model);
        row.Status = SurveyModelStatus.Ready;
        db.SurveyStations.AddRange(
            Station(model, "cave.ent.0", "cave.ent", 350, SurveyStationFlags.Entrance),
            Station(model, "cave.upper.1", "cave.upper", 330, SurveyStationFlags.Underground),
            Station(model, "cave.upper.2", "cave.upper", 300, SurveyStationFlags.Underground),
            Station(model, "cave.deep.3", "cave.deep", 230, SurveyStationFlags.Underground));
        await db.SaveChangesAsync();
        return (cave, model);
    }

    private static SurveyStation Station(Guid modelId, string name, string survey, double z, SurveyStationFlags flags) => new()
    {
        SurveyModelId = modelId,
        Name = name,
        SurveyName = survey,
        Position = new Point(new CoordinateZ(25.5, 45.5, z)) { SRID = 4326 },
        Flags = flags,
    };

    /// <summary>A marked place inside the cave, measured at <paramref name="depth"/>, owned by the uploader.</summary>
    private async Task<Guid> CreatePlaceAsync(Guid caveId, double depth)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var writer = scope.ServiceProvider.GetRequiredService<FeatureWriteService>();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var feature = new Feature
        {
            Id = Guid.CreateVersion7(),
            Name = $"Marcaj {Guid.NewGuid():N}"[..14],
            FeatureTypeId = placeTypeId,
            OwnerUserId = uploaderId,
            Visibility = Visibility.Authenticated,
            Properties = $$"""{"speleolocSchemaVersion":1,"speleolocDepthInCave":{{depth.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}""",
        };
        await writer.CreateGenericAsync(feature, [new ParentSpec(caveId, IsPrimary: true)]);
        await db.SaveChangesAsync();
        return feature.Id;
    }
}
