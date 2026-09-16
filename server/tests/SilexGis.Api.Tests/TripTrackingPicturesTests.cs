// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Photographs hung on the moments of a tracked trip.
/// </summary>
/// <remarks>
/// <para>
/// The load-bearing test here is <see cref="A_correction_deletes_and_re_records_the_report_and_the_picture_survives_it"/>:
/// it is the one that justifies the whole design. The log is append-only and a correction is a
/// deletion followed by a fresh report with a new id, so an attachment hung on the report row would
/// be destroyed by an organiser fixing a typo in a time. That test does exactly that and asserts
/// the membership row is untouched — and, as its negative twin, that nothing anywhere keys on the
/// id of the report that was deleted.
/// </para>
/// <para>
/// The protection tests each assert both halves. A negative that passes because the fixture never
/// worked is not a protection test.
/// </para>
/// </remarks>
public sealed class TripTrackingPicturesTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;
    private HttpClient reader = null!;
    /// <summary>
    /// Somebody else who may upload. Needed because the "unreadable photograph" case has to be a
    /// document the <em>attacher</em> cannot read, and an uploader can always read their own — so it
    /// is a second account with the same rights rather than a narrower one, which could not upload
    /// at all.
    /// </summary>
    private HttpClient stranger = null!;
    private long caveTypeId;

    public TripTrackingPicturesTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-files-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Files:Root"] = filesRoot,
                ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            },
            // Workers off: the graph-extraction job would otherwise pick up the fake survey file
            // below, fail to parse it, and rewrite the very station rows these tests seed.
            JobWorkers.RemoveFrom);
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tpic-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tpic-read-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tpic-str-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"tpic-own-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"tpic-read-{suffix}@t.local");
        stranger = await AuthHelper.BearerClientAsync(factory, $"tpic-str-{suffix}@t.local");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        factory.Dispose();
        try { Directory.Delete(filesRoot, recursive: true); } catch { /* best effort */ }
    }

    // ---- the core loop ---------------------------------------------------------------------

    /// <summary>
    /// A picture lands on a moment and comes back through the general link read, with the two
    /// halves that make it a picture on a moment rather than a picture on a trip: the anchor that
    /// says which instant, and a rendering URL for the photograph. Either one missing draws
    /// nothing, and an empty strip looks the same whichever half failed.
    /// </summary>
    [Fact]
    public async Task A_photograph_hung_on_a_moment_comes_back_anchored_to_that_instant_with_a_renderings_only_URL()
    {
        var context = await TrackedTripAsync("Core");
        var photo = await PictureAsync("core");
        var at = At(14, 5);

        var result = await AttachAsync(owner, context.Trip, new[]
        {
            new { documentId = photo.DocumentId, at, caverId = (Guid?)context.Cavers[0], caption = "first pitch" },
        });
        result.GetProperty("attached").GetArrayLength().ShouldBe(1);
        result.GetProperty("refused").EnumerateObject().ShouldBeEmpty();

        var links = await MomentLinksAsync(owner, context.Trip);
        links.Count.ShouldBe(1);
        var members = MembersOf(links[0]);

        // The anchor half: the trip, at the instant, as the main member.
        var moment = members.Single(m => m.GetProperty("targetType").GetString() == "tripLog");
        moment.GetProperty("targetId").GetGuid().ShouldBe(context.Trip);
        moment.GetProperty("anchorKind").GetString().ShouldBe("tripMoment");
        moment.GetProperty("anchor").GetProperty("at").GetDateTimeOffset().ShouldBe(at);
        moment.GetProperty("isMain").GetBoolean().ShouldBeTrue();

        // Who it is about, so the replay knows whose position to draw it at.
        members.Single(m => m.GetProperty("targetType").GetString() == "caver")
            .GetProperty("targetId").GetGuid().ShouldBe(context.Cavers[0]);

        // The picture half, and what its URL reaches — asserted by spending the token rather than
        // by reading it.
        var picture = members.Single(m => m.GetProperty("targetType").GetString() == "document");
        picture.GetProperty("note").GetString().ShouldBe("first pitch");
        var thumbnail = picture.GetProperty("display").GetProperty("thumbnailUrl").GetString();
        thumbnail.ShouldNotBeNull();
        thumbnail.ShouldStartWith($"/api/v1/files/{photo.FileId}/thumbnail?");
        (await owner.GetAsync(thumbnail)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // …and the stored bytes are refused to the very caller who was handed the URL. That is the
        // whole difference between a renderings-only reach and a full one: a photograph's own bytes
        // carry the fix its camera wrote, and this surface never resolved the right to that.
        (await owner.GetAsync($"/api/v1/files/{photo.FileId}/content?token={TokenIn(thumbnail)}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Taking it off takes the link with it: a link holding only the trip and a caver is an
        // association that says nothing.
        var memberId = result.GetProperty("attached")[0].GetProperty("memberId").GetGuid();
        (await owner.DeleteAsync($"/api/v1/trip-logs/{context.Trip}/tracking/pictures/{memberId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await MomentLinksAsync(owner, context.Trip)).ShouldBeEmpty();
    }

    /// <summary>
    /// <b>The test the design exists for.</b> Corrections in this log are delete-and-re-record —
    /// there is no update route at all — so a picture keyed to a report row would be destroyed by
    /// somebody fixing a typo in a time. Here the report at 14:05 is deleted and re-entered at
    /// 14:20, and the membership is asserted byte-for-byte unmoved.
    /// </summary>
    [Fact]
    public async Task A_correction_deletes_and_re_records_the_report_and_the_picture_survives_it()
    {
        var context = await TrackedTripAsync("Correction");
        var photo = await PictureAsync("correction");
        var at = At(14, 5);

        var reported = await ReportAsync(context.Trip, new
        {
            caverIds = new[] { context.Cavers[0] },
            kind = "atStation",
            stationName = "cave.upper.2",
        }, at);
        var eventId = reported[0].GetProperty("id").GetGuid();

        var attached = await AttachAsync(owner, context.Trip, new[]
        {
            new { documentId = photo.DocumentId, at, caverId = (Guid?)context.Cavers[0], caption = (string?)null },
        });
        var memberId = attached.GetProperty("attached")[0].GetProperty("memberId").GetGuid();

        var before = await MemberRowAsync(memberId);

        // The correction, exactly as the application performs one.
        (await owner.DeleteAsync($"/api/v1/trip-logs/{context.Trip}/tracking/events/{eventId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var again = await ReportAsync(context.Trip, new
        {
            caverIds = new[] { context.Cavers[0] },
            kind = "atStation",
            stationName = "cave.upper.2",
        }, At(14, 20));
        again[0].GetProperty("id").GetGuid().ShouldNotBe(eventId, "a re-record is a new row, never an edit");

        // The membership did not move, and neither did its anchor.
        var after = await MemberRowAsync(memberId);
        after.ShouldNotBeNull();
        after!.ResLinkId.ShouldBe(before!.ResLinkId);
        after.Anchor.ShouldBe(before.Anchor);
        after.EntityId.ShouldBe(photo.DocumentId);
        after.UpdatedAt.ShouldBe(before.UpdatedAt, "nothing wrote to the row, so nothing restamped it");

        // …and the picture is still reachable through the read the surface actually uses.
        var members = MembersOf((await MomentLinksAsync(owner, context.Trip)).Single());
        members.Single(m => m.GetProperty("targetType").GetString() == "document")
            .GetProperty("display").GetProperty("thumbnailUrl").GetString().ShouldNotBeNull();

        // The negative twin, and the reason this design was chosen over the obvious one: nothing
        // anywhere keys on the report that was deleted. Had the picture hung on the event row, the
        // row above would be gone with it.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.ResLinkMembers.AsNoTracking().AnyAsync(m => m.EntityId == eventId)).ShouldBeFalse();
        (await db.Attachments.AsNoTracking().AnyAsync(a => a.EntityId == eventId)).ShouldBeFalse();
        (await db.TripPositionEvents.AsNoTracking().AnyAsync(e => e.Id == eventId)).ShouldBeFalse();
    }

    // ---- protection ------------------------------------------------------------------------

    /// <summary>
    /// A picture is minted on the branch that decided the caller may read the document and on no
    /// other. The photograph here is born private to its uploader, so the reader is handed the
    /// membership with no display at all — and the same photograph, widened, reaches the same
    /// reader with a URL, which is what makes the refusal a decision about rights rather than a
    /// fixture that never worked.
    /// </summary>
    [Fact]
    public async Task A_caller_who_may_not_read_the_photograph_is_handed_no_URL_for_it()
    {
        var context = await TrackedTripAsync("Private picture");
        var photo = await PictureAsync("private", makeReadable: false);

        await AttachAsync(owner, context.Trip, new[]
        {
            new { documentId = photo.DocumentId, at = At(11, 0), caverId = (Guid?)null, caption = (string?)null },
        });

        // Negative half. The membership is still there — a member with an unreadable target renders
        // without display data rather than disappearing — and nothing on it is a URL.
        var withheld = MembersOf((await MomentLinksAsync(reader, context.Trip)).Single())
            .Single(m => m.GetProperty("targetType").GetString() == "document");
        withheld.GetProperty("display").ValueKind.ShouldBe(JsonValueKind.Null);
        JsonSerializer.Serialize(withheld).ShouldNotContain("token=");

        // Positive half, same reader, same picture, once they may read it.
        await MakeDocumentReadableAsync(photo.DocumentId);
        var shown = MembersOf((await MomentLinksAsync(reader, context.Trip)).Single())
            .Single(m => m.GetProperty("targetType").GetString() == "document");
        var thumbnail = shown.GetProperty("display").GetProperty("thumbnailUrl").GetString();
        thumbnail.ShouldNotBeNull();
        (await reader.GetAsync(thumbnail)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // And the reach it opens is renderings only, for this caller too.
        (await reader.GetAsync($"/api/v1/files/{photo.FileId}/content?token={TokenIn(thumbnail)}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// A reader who may not place the cave is refused every position on the watch, and hanging a
    /// picture on a moment does not hand one back. The picture and its instant travel — they are
    /// the same class of thing as the note beside them, which this reader already gets — and no
    /// station name, depth or model id appears anywhere in the same breath.
    /// </summary>
    [Fact]
    public async Task A_withheld_position_does_not_acquire_a_picture_that_reveals_it()
    {
        var context = await TrackedTripAsync("Guarded", locationProtected: true);
        var photo = await PictureAsync("guarded");
        var at = At(13, 30);

        await ReportAsync(context.Trip, new
        {
            caverIds = new[] { context.Cavers[0] },
            kind = "atStation",
            stationName = "cave.upper.2",
            note = "rigging the pitch",
        }, at);
        await AttachAsync(owner, context.Trip, new[]
        {
            new { documentId = photo.DocumentId, at, caverId = (Guid?)context.Cavers[0], caption = (string?)null },
        });

        // Positive half: the placer gets the station, and the picture beside it.
        var placer = await StateAsync(owner, context.Trip);
        Participant(placer, context.Cavers[0])
            .GetProperty("stationName").GetString().ShouldBe("cave.upper.2");
        MembersOf((await MomentLinksAsync(owner, context.Trip)).Single())
            .Single(m => m.GetProperty("targetType").GetString() == "document")
            .GetProperty("display").GetProperty("thumbnailUrl").GetString().ShouldNotBeNull();

        // Negative half: the same trip, read by somebody who may not place the cave.
        var theirs = await StateAsync(reader, context.Trip);
        theirs.GetProperty("positionsWithheld").GetBoolean().ShouldBeTrue();
        Participant(theirs, context.Cavers[0])
            .GetProperty("stationName").ValueKind.ShouldBe(JsonValueKind.Null);

        var log = await BodyAsync(await reader.GetAsync($"/api/v1/trip-logs/{context.Trip}/tracking/events"));
        var entry = log.GetProperty("items").EnumerateArray().Single();
        entry.GetProperty("stationName").ValueKind.ShouldBe(JsonValueKind.Null);
        entry.GetProperty("depthEnteredM").ValueKind.ShouldBe(JsonValueKind.Null);
        // The words survive the withholding today; the picture is the same class of statement and
        // is neither stricter nor looser than the note standing beside it.
        entry.GetProperty("note").GetString().ShouldBe("rigging the pitch");

        // The picture and its moment reach them — and the whole link payload names no station, no
        // depth and no survey model, so there is nothing for a marker to be hung on.
        var link = (await MomentLinksAsync(reader, context.Trip)).Single();
        var text = JsonSerializer.Serialize(link);
        text.ShouldNotContain("cave.upper.2");
        text.ShouldNotContain("surveyModel");
        MembersOf(link).Single(m => m.GetProperty("targetType").GetString() == "tripLog")
            .GetProperty("anchor").GetProperty("at").GetDateTimeOffset().ShouldBe(at);
    }

    /// <summary>
    /// What the trip's timeline says. A membership is rooted at what it targets, so the moment
    /// member lands on the trip's timeline and the photograph's member lands on the photograph's —
    /// which means the trip's timeline records that a moment was documented without naming the
    /// document. And a correction, which touches no membership, adds no membership row to it.
    /// </summary>
    [Fact]
    public async Task The_trips_timeline_records_the_membership_without_naming_the_photograph()
    {
        var context = await TrackedTripAsync("Timeline");
        var photo = await PictureAsync("timeline");
        var at = At(9, 45);

        var before = await MembershipEventsAsync(context.Trip);
        before.ShouldBeEmpty("nothing has been attached yet");

        await AttachAsync(owner, context.Trip, new[]
        {
            new { documentId = photo.DocumentId, at, caverId = (Guid?)null, caption = (string?)null },
        });

        // The positive twin of the assertion below: attaching does produce a row.
        var after = await MembershipEventsAsync(context.Trip);
        after.Count.ShouldBe(1);
        // …and it does not name the photograph. The document member is rooted at the document,
        // where whoever may read that document's history reads it.
        JsonSerializer.Serialize(after[0]).ShouldNotContain(photo.DocumentId.ToString());

        var reported = await ReportAsync(context.Trip, new
        {
            caverIds = new[] { context.Cavers[0] },
            kind = "atStation",
            stationName = "cave.upper.2",
        }, at);
        (await owner.DeleteAsync(
            $"/api/v1/trip-logs/{context.Trip}/tracking/events/{reported[0].GetProperty("id").GetGuid()}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await ReportAsync(context.Trip, new
        {
            caverIds = new[] { context.Cavers[0] },
            kind = "atStation",
            stationName = "cave.upper.2",
        }, At(10, 0));

        (await MembershipEventsAsync(context.Trip)).Count
            .ShouldBe(1, "a correction touches no membership, so it writes no membership history");
    }

    // ---- the write's own refusals ------------------------------------------------------------

    [Fact]
    public async Task The_write_refuses_what_it_should_and_names_only_what_it_may()
    {
        var context = await TrackedTripAsync("Refusals");
        var photo = await PictureAsync("refusals");

        // A reader who may read the trip and not write it is forbidden; somebody who was never
        // shown the trip is answered as if it does not exist — the same masking every trip write
        // route uses.
        (await AttachRawAsync(reader, context.Trip, new[]
        {
            new { documentId = photo.DocumentId, at = At(12, 0), caverId = (Guid?)null, caption = (string?)null },
        })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await AttachRawAsync(reader, Guid.NewGuid(), new[]
        {
            new { documentId = photo.DocumentId, at = At(12, 0), caverId = (Guid?)null, caption = (string?)null },
        })).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // A trip nobody ever watched has no moments to hang anything on.
        var untracked = await CreateTripAsync("Never watched", guests: 1);
        var never = await AttachRawAsync(owner, untracked.Trip, new[]
        {
            new { documentId = photo.DocumentId, at = At(12, 0), caverId = (Guid?)null, caption = (string?)null },
        });
        never.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await never.Content.ReadAsStringAsync()).ShouldContain("tracking.not_tracked");

        var future = await PictureAsync("future");
        var stranger = await PictureAsync("stranger");
        var prose = await TextDocumentAsync("prose");
        var unreadable = await PictureAsync("unreadable", makeReadable: false, ownedByOwner: false);

        var result = await AttachAsync(owner, context.Trip, new object[]
        {
            new { documentId = future.DocumentId, at = DateTimeOffset.UtcNow.AddHours(3), caverId = (Guid?)null, caption = (string?)null },
            new { documentId = stranger.DocumentId, at = At(12, 0), caverId = (Guid?)Guid.NewGuid(), caption = (string?)null },
            new { documentId = prose.DocumentId, at = At(12, 0), caverId = (Guid?)null, caption = (string?)null },
            new { documentId = unreadable.DocumentId, at = At(12, 0), caverId = (Guid?)null, caption = (string?)null },
            new { documentId = photo.DocumentId, at = At(12, 0), caverId = (Guid?)null, caption = (string?)null },
        });

        var refused = result.GetProperty("refused");
        refused.GetProperty(future.DocumentId.ToString()).GetString().ShouldBe("tracking.picture_in_future");
        refused.GetProperty(stranger.DocumentId.ToString()).GetString().ShouldBe("tracking.caver_not_participant");
        refused.GetProperty(prose.DocumentId.ToString()).GetString().ShouldBe("tracking.picture_not_image");

        // A photograph this caller may not read is in neither list: naming it as refused would
        // confirm it exists. Its positive twin is the row below it — one they may read lands.
        refused.EnumerateObject().ShouldNotContain(p => p.Name == unreadable.DocumentId.ToString());
        var attached = result.GetProperty("attached").EnumerateArray().ToList();
        attached.Count.ShouldBe(1);
        attached[0].GetProperty("documentId").GetGuid().ShouldBe(photo.DocumentId);

        // The same photograph on the same moment twice is one statement, said once.
        var repeat = await AttachAsync(owner, context.Trip, new[]
        {
            new { documentId = photo.DocumentId, at = At(12, 0), caverId = (Guid?)null, caption = (string?)null },
        });
        repeat.GetProperty("attached").GetArrayLength().ShouldBe(0);
        repeat.GetProperty("refused").GetProperty(photo.DocumentId.ToString()).GetString()
            .ShouldBe("tracking.picture_already_attached");
        MembersOf((await MomentLinksAsync(owner, context.Trip)).Single())
            .Count(m => m.GetProperty("targetType").GetString() == "document").ShouldBe(1);

        // A photograph named with no moment at all is refused outright, and that is not the same
        // rule as the wrong-clock one above. The field is not nullable, so an item leaving it out
        // deserialises to the first instant of year one — which would be stored as a real reading
        // and would sit two thousand years outside anything that draws a trip. Its positive twin
        // is the attach a few lines up: the same photograph, with a moment, lands.
        var noMoment = await AttachRawAsync(owner, context.Trip, new object[]
        {
            new { documentId = photo.DocumentId, caverId = (Guid?)null, caption = (string?)null },
        });
        noMoment.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Detaching something that is not a picture on a moment of this trip answers one shape,
        // whether it is missing or belongs elsewhere.
        (await owner.DeleteAsync($"/api/v1/trip-logs/{context.Trip}/tracking/pictures/{Guid.NewGuid()}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// A memory card: many pictures, many instants, one request — which is the act this write
    /// exists for, since nobody uploads from underground. Two moments and a subject apart means
    /// two links, so the derivation that places them has something to tell them apart by.
    /// </summary>
    [Fact]
    public async Task A_memory_card_lands_in_one_request_and_one_moment_is_one_link()
    {
        var context = await TrackedTripAsync("Card");
        var first = await PictureAsync("card-1");
        var second = await PictureAsync("card-2");
        var third = await PictureAsync("card-3");

        var result = await AttachAsync(owner, context.Trip, new[]
        {
            new { documentId = first.DocumentId, at = At(14, 5), caverId = (Guid?)context.Cavers[0], caption = (string?)null },
            new { documentId = second.DocumentId, at = At(14, 5), caverId = (Guid?)context.Cavers[0], caption = (string?)null },
            new { documentId = third.DocumentId, at = At(15, 40), caverId = (Guid?)null, caption = (string?)null },
        });
        result.GetProperty("attached").GetArrayLength().ShouldBe(3);

        var links = await MomentLinksAsync(owner, context.Trip);
        links.Count.ShouldBe(2, "two moments, two links — the same instant and subject share one");
        links.Sum(l => MembersOf(l).Count(m => m.GetProperty("targetType").GetString() == "document"))
            .ShouldBe(3);
    }

    /// <summary>
    /// Deleting the trip takes the moments with it, and therefore the links hung on them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What is left behind if it does not is worse than an orphan.</b> The trip's own membership
    /// goes with the trip; what is left of such a link is the caver the moment was about and the
    /// photograph — two things related to each other by nothing at all, under a directed relation
    /// with its distinguished member gone. That renders on the caver's own links panel as a
    /// photograph documenting a person, an association nobody ever made and one no later edit
    /// would be accepted for, since the link rules refuse a directed link with no main member.
    /// </para>
    /// <para>
    /// The relation these are written under is not one of the trip roles, so the sweep that takes
    /// role links whole does not reach them — which is exactly how this slipped through, and why
    /// the test names the caver and the photograph rather than only the link.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Deleting_the_trip_takes_its_moment_pictures_with_it_and_relates_nobody_to_anything()
    {
        var context = await TrackedTripAsync("Deleted");
        var photo = await PictureAsync("deleted");

        var attached = await AttachAsync(owner, context.Trip, new[]
        {
            new
            {
                documentId = photo.DocumentId,
                at = At(14, 5),
                caverId = (Guid?)context.Cavers[0],
                caption = (string?)null,
            },
        });
        var memberId = attached.GetProperty("attached")[0].GetProperty("memberId").GetGuid();
        var linkId = (await MemberRowAsync(memberId))!.ResLinkId;

        // The positive twin: before the delete this is a real link of three members, so the
        // absences below are a delete having worked rather than an attach having failed.
        await using (var before = factory.Services.CreateAsyncScope())
        {
            var db = before.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.ResLinkMembers.AsNoTracking().CountAsync(m => m.ResLinkId == linkId)).ShouldBe(3);
        }

        (await owner.DeleteAsync($"/api/v1/trip-logs/{context.Trip}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var scope = factory.Services.CreateAsyncScope();
        var after = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await after.ResLinks.AsNoTracking().AnyAsync(l => l.Id == linkId)).ShouldBeFalse();
        (await after.ResLinkMembers.AsNoTracking().AnyAsync(m => m.ResLinkId == linkId)).ShouldBeFalse();

        // Said as the sentence a reader would have been shown, rather than only as a row count:
        // nothing anywhere relates this caver to this photograph any more.
        (await after.ResLinkMembers.AsNoTracking()
            .AnyAsync(m => m.EntityType == AttachedEntityType.Caver && m.EntityId == context.Cavers[0]))
            .ShouldBeFalse();
        (await after.ResLinkMembers.AsNoTracking()
            .AnyAsync(m => m.EntityType == AttachedEntityType.Document && m.EntityId == photo.DocumentId))
            .ShouldBeFalse();

        // And the photograph itself is untouched: what was deleted is an association, never a file
        // somebody uploaded.
        (await after.Documents.AsNoTracking().AnyAsync(d => d.Id == photo.DocumentId)).ShouldBeTrue();
    }

    /// <summary>
    /// A link that merely mentions a moment of this trip is not this route's to delete.
    /// </summary>
    /// <remarks>
    /// Curation of a link belongs to whoever may write its <b>main</b> member, and this route's
    /// only gate is write on the trip. The general link route lets any caller who may read two
    /// things relate them and choose which of them is main — so a document's writer can author a
    /// documenting link with the <em>document</em> as its main member and a moment of this trip
    /// beside it, and that link answers to the document's writers. Recognised by its moment member
    /// alone, it could be deleted by anybody holding trip-write, taking every sibling with it. So
    /// what this route demands is the exact shape its own write produces.
    /// </remarks>
    [Fact]
    public async Task A_link_whose_subject_is_not_this_trips_moment_is_not_detachable_here()
    {
        var context = await TrackedTripAsync("Foreign");
        var photo = await PictureAsync("foreign");
        var at = At(16, 20);

        long relationId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            relationId = await db.ResLinkRelationTypes.AsNoTracking()
                .Where(r => r.Code == "documents").Select(r => r.Id).FirstAsync();
        }

        // Authored the way anybody may author one: the photograph as the main member, the trip's
        // moment beside it. Nothing about this is illegitimate — it is simply not curated by
        // whoever may write the trip.
        var created = await owner.PostAsJsonAsync("/api/v1/reslinks/", new
        {
            relationTypeId = relationId,
            description = (string?)null,
            members = new object[]
            {
                new
                {
                    targetType = "document",
                    targetId = photo.DocumentId,
                    isMain = true,
                    sortOrder = 0,
                    note = (string?)null,
                    anchorKind = "whole",
                    anchor = (object?)null,
                    anchorFileId = (Guid?)null,
                },
                new
                {
                    targetType = "tripLog",
                    targetId = context.Trip,
                    isMain = false,
                    sortOrder = 1,
                    note = (string?)null,
                    anchorKind = "tripMoment",
                    anchor = new { at = at.ToString("O") },
                    anchorFileId = (Guid?)null,
                },
            },
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var link = await BodyAsync(created);
        var linkId = link.GetProperty("id").GetGuid();
        var foreignMember = MembersOf(link)
            .Single(m => m.GetProperty("targetType").GetString() == "document")
            .GetProperty("id").GetGuid();

        // Negative half: trip-write does not reach it, and the answer is the one every other thing
        // this route does not own gets.
        (await owner.DeleteAsync(
            $"/api/v1/trip-logs/{context.Trip}/tracking/pictures/{foreignMember}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            // Whole: not the named member, and not the siblings a detach would have swept with it.
            (await db.ResLinkMembers.AsNoTracking().CountAsync(m => m.ResLinkId == linkId)).ShouldBe(2);
        }

        // Positive half, so the refusal is about the link's shape and not about this route being
        // unable to delete anything: the same photograph, hung on the same moment by the write
        // this route owns, comes off it.
        var attached = await AttachAsync(owner, context.Trip, new[]
        {
            new { documentId = photo.DocumentId, at, caverId = (Guid?)null, caption = (string?)null },
        });
        var ours = attached.GetProperty("attached")[0].GetProperty("memberId").GetGuid();
        (await owner.DeleteAsync($"/api/v1/trip-logs/{context.Trip}/tracking/pictures/{ours}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // …and the one this route does not own is still standing after it.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.ResLinks.AsNoTracking().AnyAsync(l => l.Id == linkId)).ShouldBeTrue();
        }
    }

    // ---- plumbing --------------------------------------------------------------------------

    private sealed record TrackedTrip(Guid Trip, List<Guid> Cavers, Guid Cave, Guid Model);

    private sealed record Picture(Guid DocumentId, Guid FileId);

    /// <summary>The trip's own day — reports and moments are stated, never left to the clock.</summary>
    private static DateTimeOffset At(int hour, int minute) =>
        new(2026, 9, 12, hour, minute, 0, TimeSpan.Zero);

    private async Task<TrackedTrip> TrackedTripAsync(string title, bool locationProtected = false)
    {
        var (trip, cavers) = await CreateTripAsync(title, guests: 2);
        var cave = await CreateCaveAsync(locationProtected);
        var model = await SeedModelWithStationsAsync(cave);
        (await PutConfigAsync(owner, trip, new { state = "armed", surveyModelId = model }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        return new TrackedTrip(trip, cavers, cave, model);
    }

    private async Task<(Guid Trip, List<Guid> Cavers)> CreateTripAsync(string title, int guests)
    {
        var participants = Enumerable.Range(1, guests)
            .Select(i => new { newCaverName = $"Guest {i} {Guid.NewGuid():N}"[..24] })
            .ToArray();
        var response = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"{title} {Guid.NewGuid():N}",
            tripDate = "2026-09-12",
            participants,
            visibility = "authenticated",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var trip = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var cavers = await db.TripLogParticipants.Where(p => p.TripLogId == trip)
            .Select(p => p.CaverId).Distinct().OrderBy(c => c).ToListAsync();
        return (trip, cavers);
    }

    private async Task<Guid> CreateCaveAsync(bool locationProtected)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Pic Cave {Guid.NewGuid():N}"[..28],
            caveTypeId,
            visibility = "authenticated",
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> SeedModelWithStationsAsync(Guid caveId)
    {
        using var form = new MultipartFormDataContent();
        var bytes = new ByteArrayContent([1, 2, 3, 4]);
        bytes.Headers.ContentType = new("application/octet-stream");
        form.Add(bytes, "file", "pictures.3d");
        var created = await owner.PostAsync($"/api/v1/caves/{caveId}/survey-models", form);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var modelId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var model = await db.SurveyModels.SingleAsync(m => m.Id == modelId);
        model.Status = SurveyModelStatus.Ready;
        db.SurveyStations.AddRange(
            Station(modelId, "cave.ent.0", "cave.ent", 350, SurveyStationFlags.Entrance),
            Station(modelId, "cave.upper.2", "cave.upper", 300, SurveyStationFlags.Underground));
        await db.SaveChangesAsync();
        return modelId;
    }

    private static SurveyStation Station(
        Guid modelId, string name, string survey, double z, SurveyStationFlags flags) =>
        new()
        {
            SurveyModelId = modelId,
            Name = name,
            SurveyName = survey,
            Position = new Point(new CoordinateZ(25.5, 45.5, z)) { SRID = 4326 },
            Flags = flags,
        };

    /// <summary>
    /// A photograph in the store. Born private to whoever uploaded it, and widened here unless a
    /// test wants the unreadable case — which is what makes "no URL" a decision about rights.
    /// </summary>
    private async Task<Picture> PictureAsync(
        string name, bool makeReadable = true, bool ownedByOwner = true)
    {
        var client = ownedByOwner ? owner : stranger;
        var fileId = await UploadAsync(client, $"{name}-{Guid.NewGuid():N}"[..20] + ".jpg", PlainJpeg(), "image/jpeg");
        var documentId = await DocumentIdOfAsync(fileId);
        if (makeReadable)
        {
            await MakeDocumentReadableAsync(documentId);
        }

        return new Picture(documentId, fileId);
    }

    /// <summary>A readable document that is not a picture — the one refusal a person can act on.</summary>
    private async Task<Picture> TextDocumentAsync(string name)
    {
        var fileId = await UploadAsync(
            owner, $"{name}-{Guid.NewGuid():N}"[..20] + ".txt",
            System.Text.Encoding.UTF8.GetBytes($"notes for {name}"), "text/plain");
        var documentId = await DocumentIdOfAsync(fileId);
        await MakeDocumentReadableAsync(documentId);
        return new Picture(documentId, fileId);
    }

    private static async Task<Guid> UploadAsync(
        HttpClient client, string fileName, byte[] bytes, string contentType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new(contentType);
        using var form = new MultipartFormDataContent { { content, "file", fileName } };
        var response = await client.PostAsync("/api/v1/files/?allowDuplicate=true", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> DocumentIdOfAsync(Guid fileId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var file = await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == fileId);
        var version = await db.DocumentVersions.AsNoTracking().FirstAsync(v => v.Id == file.DocumentVersionId);
        return version.DocumentId;
    }

    private async Task MakeDocumentReadableAsync(Guid documentId)
    {
        var response = await owner.GetAsync($"/api/v1/documents/{documentId}");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        var current = JsonDocument.Parse(body).RootElement;

        var updated = await owner.PutAsJsonAsync($"/api/v1/documents/{documentId}", new
        {
            title = current.GetProperty("title").GetString(),
            documentTypeId = current.GetProperty("documentTypeId").ValueKind == JsonValueKind.Number
                ? current.GetProperty("documentTypeId").GetInt64()
                : (long?)null,
            metadata = (object?)null,
            visibility = "authenticated",
            cavingGroupId = (Guid?)null,
        });
        updated.StatusCode.ShouldBe(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
    }

    private static byte[] PlainJpeg()
    {
        using var image = new MagickImage(MagickColors.SlateGray, 64, 64);
        return image.ToByteArray(MagickFormat.Jpeg);
    }

    /// <summary>
    /// The address the generated client actually calls — spelled without a trailing slash, which
    /// is how the contract document names it. A test that reached the same handler by a spelling
    /// the client never uses would be testing a route nothing calls.
    /// </summary>
    private static Task<HttpResponseMessage> AttachRawAsync(HttpClient client, Guid trip, object items) =>
        client.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/pictures", new { items });

    private static async Task<JsonElement> AttachAsync(HttpClient client, Guid trip, object items)
    {
        var response = await AttachRawAsync(client, trip, items);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await BodyAsync(response);
    }

    /// <summary>Config writes ride the trip's version: fetch the ETag, then PUT with If-Match.</summary>
    private static async Task<HttpResponseMessage> PutConfigAsync(HttpClient client, Guid trip, object body)
    {
        var current = await client.GetAsync($"/api/v1/trip-logs/{trip}/tracking");
        current.StatusCode.ShouldBe(HttpStatusCode.OK, await current.Content.ReadAsStringAsync());
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/trip-logs/{trip}/tracking")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.TryAddWithoutValidation("If-Match", current.Headers.ETag!.ToString());
        return await client.SendAsync(request);
    }

    /// <summary>One report, stamped with the hour the reporter gave.</summary>
    private async Task<List<JsonElement>> ReportAsync(Guid trip, object body, DateTimeOffset recordedAt)
    {
        var json = JsonSerializer.SerializeToNode(body)!.AsObject();
        json["recordedAt"] = recordedAt.ToString("O");
        var response = await owner.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/events", json);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await BodyAsync(response)).EnumerateArray().ToList();
    }

    /// <summary>One named participant of the watch — these trips carry more than one.</summary>
    private static JsonElement Participant(JsonElement state, Guid caverId) =>
        state.GetProperty("participants").EnumerateArray()
            .Single(p => p.GetProperty("caverId").GetGuid() == caverId);

    private static async Task<JsonElement> StateAsync(HttpClient client, Guid trip)
    {
        var response = await client.GetAsync($"/api/v1/trip-logs/{trip}/tracking");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await BodyAsync(response);
    }

    /// <summary>
    /// The read the surface itself uses: the trip's links under the documenting relation, narrowed
    /// to the ones anchored to a moment. No read endpoint of its own exists for these, deliberately
    /// — one protection surface rather than two.
    /// </summary>
    private static async Task<List<JsonElement>> MomentLinksAsync(HttpClient client, Guid trip)
    {
        var response = await client.GetAsync(
            $"/api/v1/reslinks/for-target?type=tripLog&id={trip}&relation=documents&pageSize=200");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return [.. JsonDocument.Parse(payload).RootElement.GetProperty("items").EnumerateArray()
            .Where(l => MembersOf(l).Any(m => m.GetProperty("anchorKind").GetString() == "tripMoment"))
            .Select(l => l.Clone())];
    }

    private static List<JsonElement> MembersOf(JsonElement link) =>
        [.. link.GetProperty("members").EnumerateArray()];

    /// <summary>The stored membership row, so a test can assert it did not move.</summary>
    private async Task<ResLinkMember?> MemberRowAsync(Guid memberId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.ResLinkMembers.AsNoTracking().FirstOrDefaultAsync(m => m.Id == memberId);
    }

    /// <summary>Membership rows on the trip's own timeline.</summary>
    private async Task<List<JsonElement>> MembershipEventsAsync(Guid trip)
    {
        var response = await owner.GetAsync($"/api/v1/history?entityType=tripLog&entityId={trip}&pageSize=200");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return [.. JsonDocument.Parse(payload).RootElement.GetProperty("items").EnumerateArray()
            .Where(e => e.GetProperty("entityType").GetString() == nameof(ResLinkMember))
            .Select(e => e.Clone())];
    }

    private static string TokenIn(string url) => url[(url.IndexOf("token=", StringComparison.Ordinal) + 6)..];

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
}
