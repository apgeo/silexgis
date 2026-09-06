// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Import;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The second half of importing a club's trip spreadsheet: what a confirmation writes, what it
/// refuses to write, and how the whole of it is taken back.
/// </summary>
/// <remarks>
/// <para>
/// Four things are on trial, and none of them is a happy path. A cave the caller may not place is
/// never linked, however plainly the sheet names it. A confirmation that records a hundred
/// finished trips tells nobody — the announcing write path becomes reachable the moment an import
/// creates a trip that is already finished, and nothing but an explicit argument stops it. One row
/// that cannot be recorded costs only itself. And an undo really undoes: no trip is left, and
/// importing the same sheet again is a second batch rather than a quiet merge into the first.
/// </para>
/// <para>
/// Every sheet here is invented — placeholder massifs, placeholder caves and placeholder people,
/// written to exercise the rules rather than to resemble anybody's records.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class TripImportCommitTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient editor = null!;
    private HttpClient viewer = null!;
    private Guid editorId;
    private Guid strangerId;
    private Guid viewerId;
    private string tag = null!;

    /// <summary>
    /// Two rows that can be recorded. The second settles the day/month order for the file on its
    /// own, a seventeenth being no month, so the first row's date is read the same way.
    /// </summary>
    private string Sheet =>
        "Nr crt.,Data inceput,Data sfarsit,Titlu,Tara,Masiv/zona,Subzona,Pesteri,Propus de,Participanti,Detalii,Tip,Erori\r\n"
        + $"1,05/03/2024,,Prima tura,Romania,Masivul Unu {tag},Valea A {tag},Pestera Unu {tag},{Ana},"
        + $"\"{Ana}; {Bogdan}\",Nimic special,explorare {tag},\r\n"
        + $"2,17/04/2024,18/04/2024,A doua tura,Romania,Masivul Doi {tag},Valea B {tag},Pestera Doi {tag},"
        + $"{Bogdan},{Bogdan},Doua zile,cartare {tag},de verificat\r\n";

    /// <summary>
    /// Every name a sheet writes carries the run's own suffix. The database outlives one test
    /// here, so a plain "Ana Popescu" seeded by one test is a second person answering to the name
    /// in the next one, and the ambiguity rule under test would fire for the wrong reason.
    /// </summary>
    private string Ana => $"Ana Popescu {tag}";

    private string Bogdan => $"Bogdan Ionescu {tag}";

    public TripImportCommitTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-files-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
        });
    }

    public async Task InitializeAsync()
    {
        tag = Guid.NewGuid().ToString("N")[..8];
        editorId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tic-editor-{tag}@t.local");
        strangerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tic-owner-{tag}@t.local");
        viewerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tic-viewer-{tag}@t.local");
        editor = await AuthHelper.BearerClientAsync(factory, $"tic-editor-{tag}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"tic-viewer-{tag}@t.local");

        // Adding somebody to the roster is the roster's right, and the shipped editor group does
        // not hold it, so the account almost every test here runs as is given it explicitly.
        // Without it the switch that creates missing people would be refused and the tests below
        // would be measuring the refusal instead of what a confirmation writes.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = editorId,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.Cavers,
            Actions = AccessAction.Create,
            ScopeKind = AccessScopeKind.All,
        });
        await db.SaveChangesAsync();
    }

    // ---------- the protection matrix, at the moment it would be written ----------

    /// <summary>
    /// Three caves, one name each, and only the first ever reaches a trip.
    ///
    /// <para>
    /// The first is readable and placeable and is linked, which is what makes the other two mean
    /// something: an assertion that a cave was not linked passes just as well against an importer
    /// that links nothing at all. The second is perfectly readable and its position is guarded —
    /// a trip carries its own geometry, so saying a trip reached it would place it by proximity.
    /// The third is neither, held back by an explicit refusal rather than by visibility, because
    /// the seeded editors read past visibility at the widest scope and a matrix built on that
    /// would be proving that the query ran.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_cave_the_caller_may_not_place_is_never_linked_to_an_imported_trip()
    {
        var (readable, guarded, hidden) = await SeedCaveMatrixAsync();

        var fileId = await UploadAsync("matrix.csv", CaveSheet());
        var result = await CommitAsync(fileId, [2, 3, 4]);
        result.GetProperty("failures").GetArrayLength().ShouldBe(0);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var trips = await db.TripLogs.AsNoTracking().Where(t => t.OwnerUserId == editorId)
            .OrderBy(t => t.TripDate).ToListAsync();
        trips.Count.ShouldBe(3);

        var linked = await LinkedFeatureIdsAsync(db, [.. trips.Select(t => t.Id)]);
        linked.ShouldContain(readable);
        linked.ShouldNotContain(guarded);
        linked.ShouldNotContain(hidden);

        // Nothing about the two it would not link is explained, and nothing is lost either: the
        // names the sheet wrote are in the trips' own words, exactly as an unmatched name is.
        var withheld = trips.Where(t => t.Title != "Cu pestera vizibila").ToList();
        withheld.ShouldAllBe(t => t.LocationText != null);
        string.Join(" ", withheld.Select(t => t.LocationText)).ShouldContain("Pestera");
    }

    // ---------- the batch is silent ----------

    /// <summary>
    /// A confirmation that records finished trips naming people who hold accounts writes no
    /// notification, no invitation and no callout.
    ///
    /// <para>
    /// The assertion is on the absence of rows rather than on a log line, and it is paired with
    /// the case that does speak, because "nothing was sent" passes just as well when the sending
    /// was never wired up at all. The pairing matters here more than usual: creating a trip
    /// through the ordinary route always produced a draft, so the announcing path was unreachable
    /// over HTTP until an importer began creating trips that are already finished.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_confirmation_that_records_a_club_history_tells_nobody()
    {
        // Somebody who holds an account, because the roster announcement only ever reaches
        // people who do: a sheet of names nobody has an account for would pass this vacuously.
        await using (var seed = factory.Services.CreateAsyncScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var caver = await db.Cavers.FirstOrDefaultAsync(c => c.UserId == strangerId);
            if (caver is null)
            {
                db.Cavers.Add(new Caver { FullName = Ana, UserId = strangerId });
            }
            else
            {
                caver.FullName = Ana;
            }

            await db.SaveChangesAsync();
        }

        var before = await NotificationCountAsync();

        var fileId = await UploadAsync("silent.csv", Sheet);
        var result = await CommitAsync(fileId, [2, 3]);
        result.GetProperty("createdTripCount").GetInt32().ShouldBe(2);

        await using var scope = factory.Services.CreateAsyncScope();
        var db2 = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        // The trips are real, finished, and one of them names the person with the account —
        // which is the shape that would otherwise announce.
        var trips = await db2.TripLogs.AsNoTracking().Where(t => t.OwnerUserId == editorId).ToListAsync();
        trips.Count.ShouldBe(2);
        trips.ShouldAllBe(t => t.State == ActivityState.Done);
        var caverId = await db2.Cavers.AsNoTracking()
            .Where(c => c.UserId == strangerId).Select(c => c.Id).FirstAsync();
        (await db2.TripLogParticipants.AsNoTracking().CountAsync(p => p.CaverId == caverId))
            .ShouldBeGreaterThan(0);

        // And nobody was told: not by a message, not by an invitation, not by a callout.
        (await NotificationCountAsync()).ShouldBe(before);
        var tripIds = trips.Select(t => t.Id).ToList();
        (await db2.TripInvitations.AsNoTracking()
            .CountAsync(i => i.TripLogId != null && tripIds.Contains(i.TripLogId.Value))).ShouldBe(0);
        trips.ShouldAllBe(t => t.CalloutState == TripCalloutState.None);
    }

    // ---------- undo ----------

    /// <summary>
    /// Reverting the batch leaves no trip and no feature it created, and importing the same sheet
    /// again writes a second batch rather than merging quietly into the first.
    /// </summary>
    [Fact]
    public async Task A_reverted_import_leaves_no_trip_and_re_importing_makes_a_second_batch()
    {
        var fileId = await UploadAsync("undo.csv", Sheet);
        var first = await CommitAsync(fileId, [2, 3], createEverything: true);
        var batchId = first.GetProperty("batchId").GetGuid();
        first.GetProperty("createdTripCount").GetInt32().ShouldBe(2);
        // The switches were on, so the sheet's places became features of their own: two massifs,
        // two sub-areas nested inside them, and the caves it named.
        first.GetProperty("createdFeatureCount").GetInt32().ShouldBeGreaterThan(0);

        Guid[] createdFeatures;
        Guid[] createdTrips;
        await using (var before = factory.Services.CreateAsyncScope())
        {
            var db = before.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            createdFeatures = [.. await db.ImportBatchItems.AsNoTracking()
                .Where(i => i.ImportBatchId == batchId && i.FeatureId != null)
                .Select(i => i.FeatureId!.Value).ToListAsync()];
            // Held on to before the revert, because afterwards there is no trip left to name
            // and the links have to be looked for by the identifiers they used to carry.
            createdTrips = [.. await db.ImportBatchItems.AsNoTracking()
                .Where(i => i.ImportBatchId == batchId && i.TripLogId != null)
                .Select(i => i.TripLogId!.Value).ToListAsync()];
            createdTrips.Length.ShouldBe(2);

            // The sub-area went inside the massif rather than beside it — a hierarchy the sheet's
            // two place columns describe and nothing else in the model would record.
            var nested = await db.Features.AsNoTracking()
                .Where(f => createdFeatures.Contains(f.Id) && f.AncestorIds.Length > 1)
                .CountAsync();
            nested.ShouldBeGreaterThan(0);
        }

        var revert = await editor.PostAsync($"/api/v1/import-batches/{batchId}/revert", null);
        revert.StatusCode.ShouldBe(HttpStatusCode.OK, await BodyAsync(revert));

        await using (var after = factory.Services.CreateAsyncScope())
        {
            var db = after.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.TripLogs.AsNoTracking().CountAsync(t => t.OwnerUserId == editorId)).ShouldBe(0);
            // Nor a link naming caves through a trip that is gone. Asked of the trips this
            // batch created rather than of every trip-log link in the table: one database is
            // shared by every test in this suite, and another test's links say nothing about
            // whether this revert cleaned up after itself.
            (await db.ResLinkMembers.AsNoTracking()
                .CountAsync(m => m.EntityType == AttachedEntityType.TripLog
                    && m.EntityId != null && createdTrips.Contains(m.EntityId!.Value)))
                .ShouldBe(0);
            var live = await db.Features.AsNoTracking().IgnoreQueryFilters()
                .Where(f => createdFeatures.Contains(f.Id))
                .ToListAsync();
            live.Count.ShouldBe(createdFeatures.Length);
            live.ShouldAllBe(f => f.DeletedAt != null);
            (await db.ImportBatches.AsNoTracking().Where(b => b.Id == batchId)
                .Select(b => b.RevertedAt).FirstAsync()).ShouldNotBeNull();
        }

        // The batch still says what it created. A reverted trip is removed rather than
        // soft-deleted, so the line's pointer at it is gone — and a list of lines saying nothing
        // is no answer to the one question somebody opens a reverted import to ask.
        var detail = await editor.GetAsync($"/api/v1/import-batches/{batchId}");
        var detailBody = await BodyAsync(detail);
        detail.StatusCode.ShouldBe(HttpStatusCode.OK, detailBody);
        var titles = JsonDocument.Parse(detailBody).RootElement.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("tripTitle").GetString())
            .Where(t => t is not null)
            .ToList();
        titles.ShouldContain("Prima tura");
        titles.ShouldContain("A doua tura");

        // The same file, confirmed again. A second batch, and trips again — the review is spent
        // on confirmation, so there is nothing to merge into and nothing pretends there is.
        var second = await CommitAsync(fileId, [2, 3], createEverything: true);
        second.GetProperty("batchId").GetGuid().ShouldNotBe(batchId);
        second.GetProperty("createdTripCount").GetInt32().ShouldBe(2);

        await using var last = factory.Services.CreateAsyncScope();
        var db3 = last.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db3.ImportBatches.AsNoTracking()
            .CountAsync(b => b.ConfirmedByUserId == editorId && b.Source == ImportSource.TripCsv)).ShouldBe(2);
        (await db3.TripLogs.AsNoTracking().CountAsync(t => t.OwnerUserId == editorId)).ShouldBe(2);
    }

    // ---------- where the trip was ----------

    /// <summary>
    /// The sheet's two place columns reach the trip in every state they can be in: matched to an
    /// area that already exists, created because a switch said so, and left as words because it
    /// did not. Without the first two the columns would be silently dropped — the location note
    /// leaves out anything matched or created, on the understanding that a link says it.
    /// </summary>
    [Fact]
    public async Task The_massif_a_row_names_reaches_the_trip_as_a_link_or_as_words()
    {
        var existing = $"Masivul Unu {tag}";
        Guid existingId;
        await using (var seed = factory.Services.CreateAsyncScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var typeId = await db.FeatureTypes.AsNoTracking()
                .Where(t => t.Code == TripImportKinds.Massif).Select(t => t.Id).FirstAsync();
            var area = new Feature
            {
                Id = Guid.NewGuid(),
                Name = existing,
                Kind = FeatureKind.Generic,
                FeatureTypeId = typeId,
                Category = FeatureCategory.Area,
                OwnerUserId = editorId,
                Visibility = Visibility.Public,
            };
            area.AncestorIds = [area.Id];
            db.Features.Add(area);
            db.FeatureAncestors.Add(new FeatureAncestor { FeatureId = area.Id, AncestorId = area.Id });
            await db.SaveChangesAsync();
            existingId = area.Id;
        }

        // Row one names the area that exists; row two names one that does not.
        var fileId = await UploadAsync("places.csv", Sheet);
        await CommitAsync(fileId, [2, 3]);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var trips = await db.TripLogs.AsNoTracking()
                .Where(t => t.OwnerUserId == editorId).OrderBy(t => t.TripDate).ToListAsync();
            trips.Count.ShouldBe(2);

            // Matched: a relation, because that is what the model has for it.
            (await LinkedFeatureIdsAsync(db, [trips[0].Id])).ShouldContain(existingId);

            // Unmatched with the switch off: no relation, and the name in the trip's own words,
            // so the column is not lost either way.
            (await LinkedFeatureIdsAsync(db, [trips[1].Id])).ShouldBeEmpty();
            trips[1].LocationText.ShouldNotBeNull();
            trips[1].LocationText!.ShouldContain($"Masivul Doi {tag}");
        }

        // The same second row with the switch on: the area is created, and the trip points at it
        // rather than at nothing.
        var again = await UploadAsync("places-again.csv", Sheet);
        var second = await CommitAsync(again, [3], createEverything: true);
        var secondBatch = second.GetProperty("batchId").GetGuid();

        await using var after = factory.Services.CreateAsyncScope();
        var db2 = after.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var created = await db2.Features.AsNoTracking()
            .Where(f => f.Name == $"Masivul Doi {tag}").Select(f => f.Id).ToListAsync();
        created.Count.ShouldBe(1);

        // Asked of the trip that confirmation recorded rather than of whichever row sorts last:
        // three trips now carry titles from the same sheet.
        var latest = await db2.ImportBatchItems.AsNoTracking()
            .Where(i => i.ImportBatchId == secondBatch && i.TripLogId != null)
            .Select(i => i.TripLogId!.Value).SingleAsync();
        (await LinkedFeatureIdsAsync(db2, [latest])).ShouldContain(created[0]);
    }

    // ---------- one right is not another ----------

    /// <summary>
    /// The same rule for the roster. An account may record trips and may not enroll people;
    /// turning on the switch that would enroll them is refused rather than obeyed, because the
    /// roster reconciliation underneath asks nobody's permission and a sheet of a few thousand
    /// rows is the one place where that becomes a bulk act.
    /// </summary>
    [Fact]
    public async Task Recording_trips_is_not_permission_to_add_people_to_the_roster()
    {
        await using (var seed = factory.Services.CreateAsyncScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            db.AccessEntries.Add(new AccessEntry
            {
                SubjectKind = AccessSubjectKind.User,
                SubjectId = editorId,
                Effect = AccessEffect.Deny,
                Domain = AccessDomain.Cavers,
                Actions = AccessAction.Create,
                ScopeKind = AccessScopeKind.All,
            });
            await db.SaveChangesAsync();
        }

        var fileId = await UploadAsync("roster-rights.csv", Sheet);

        var refused = await editor.PostAsync(
            $"/api/v1/trip-imports/{fileId}/commit",
            Body(new { options = Options(createEverything: true), lines = new[] { 2, 3 } }));
        var body = await BodyAsync(refused);
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, body);
        CodeOf(body).ShouldBe(CreateRules.ForbiddenCode);

        // Nothing was written, and the refusal arrived before the sheet was even read.
        await using (var check = factory.Services.CreateAsyncScope())
        {
            var db = check.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.TripLogs.AsNoTracking().CountAsync(t => t.OwnerUserId == editorId)).ShouldBe(0);
            (await db.Cavers.AsNoTracking().CountAsync(c => c.FullName.Contains(tag))).ShouldBe(0);
        }
    }

    /// <summary>
    /// An account may record trips and may not extend the cave register. Turning on the switches
    /// that would extend it is refused rather than quietly obeyed — the feature write core carries
    /// no permission logic of its own, so this endpoint is the only thing standing there.
    /// </summary>
    [Fact]
    public async Task Recording_trips_is_not_permission_to_add_caves_and_areas()
    {
        await using (var seed = factory.Services.CreateAsyncScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            db.AccessEntries.Add(new AccessEntry
            {
                SubjectKind = AccessSubjectKind.User,
                SubjectId = editorId,
                Effect = AccessEffect.Deny,
                Domain = AccessDomain.Features,
                Actions = AccessAction.Create,
                ScopeKind = AccessScopeKind.All,
            });
            await db.SaveChangesAsync();
        }

        var fileId = await UploadAsync("rights.csv", Sheet);

        var refused = await editor.PostAsync(
            $"/api/v1/trip-imports/{fileId}/commit",
            Body(new { options = Options(createEverything: true), lines = new[] { 2, 3 } }));
        var body = await BodyAsync(refused);
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, body);
        CodeOf(body).ShouldBe(CreateRules.ForbiddenCode);

        // The positive half, one thing changed: the same account, the same sheet, the switches
        // off. Matching never needed the right — only creating did — so the trips still land.
        var allowed = await CommitAsync(fileId, [2, 3]);
        allowed.GetProperty("createdTripCount").GetInt32().ShouldBe(2);
        allowed.GetProperty("createdFeatureCount").GetInt32().ShouldBe(0);
    }

    /// <summary>
    /// The same, for the vocabulary. A term added here is permanent — the list is append-only and
    /// an undo deliberately leaves it — and it shows on every trip form, so it is held to the
    /// right the vocabulary screen asks for rather than to the trip log's.
    /// </summary>
    [Fact]
    public async Task Recording_trips_is_not_permission_to_grow_the_trip_type_vocabulary()
    {
        var fileId = await UploadAsync("vocabulary.csv", Sheet);

        // The same account, asked the same question through the ordinary route: refused. Read
        // first, so that what follows is a comparison rather than an assumption about the seeds.
        var throughTheRoute = await editor.PostAsync(
            "/api/v1/trip-types",
            Body(new { code = $"explorare_{tag}", name = $"Explorare {tag}" }));
        throughTheRoute.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await BodyAsync(throughTheRoute));

        var refused = await editor.PostAsync(
            $"/api/v1/trip-imports/{fileId}/commit",
            Body(new { options = Options(createTripTypes: true), lines = new[] { 2, 3 } }));
        var body = await BodyAsync(refused);
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, body);
        CodeOf(body).ShouldBe(CreateRules.ForbiddenCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.TripTypes.AsNoTracking().CountAsync(t => t.Name.Contains(tag))).ShouldBe(0);
        (await db.TripLogs.AsNoTracking().CountAsync(t => t.OwnerUserId == editorId)).ShouldBe(0);
    }

    // ---------- a confirmation that would record nothing ----------

    /// <summary>
    /// Every chosen row set aside is a confirmation that would record nothing, and it is refused
    /// with the same code as one whose rows all failed. What is really being protected is the
    /// review: a session assembled across nine pages must not be spent by pressing the wrong
    /// button on a page where everything was skipped.
    /// </summary>
    [Fact]
    public async Task A_confirmation_in_which_every_row_was_set_aside_keeps_the_review()
    {
        var fileId = await UploadAsync("skipped.csv", Sheet);

        var setAside = new Dictionary<string, object>
        {
            ["2"] = new { action = "skip" },
            ["3"] = new { action = "skip" },
        };
        var saved = await editor.PutAsync(
            $"/api/v1/trip-imports/{fileId}/session",
            Body(new { options = Options(), decisions = setAside }));
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await BodyAsync(saved));

        var response = await editor.PostAsync(
            $"/api/v1/trip-imports/{fileId}/commit",
            Body(new { options = Options(), lines = new[] { 2, 3 } }));
        var body = await BodyAsync(response);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body);
        CodeOf(body).ShouldBe("trip_import.nothing_created");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        // No batch recording nothing, no trips — and, the point of this test, the review is still
        // there with the decisions that were made in it.
        (await db.ImportBatches.AsNoTracking().CountAsync(b => b.ConfirmedByUserId == editorId)).ShouldBe(0);
        (await db.TripLogs.AsNoTracking().CountAsync(t => t.OwnerUserId == editorId)).ShouldBe(0);
        (await db.TripImportSessions.AsNoTracking().CountAsync(s => s.UserId == editorId)).ShouldBe(1);
    }

    // ---------- a guess is worse than a gap ----------

    /// <summary>
    /// A name two people in the roster answer to leaves the trip without either of them, and the
    /// name in the trip's own words. The unambiguous name on the same row is recorded, which is
    /// what stops this passing against an importer that records nobody.
    /// </summary>
    [Fact]
    public async Task A_name_two_people_answer_to_puts_neither_of_them_on_the_trip()
    {
        await using (var seed = factory.Services.CreateAsyncScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            db.Cavers.Add(new Caver { FullName = Ana });
            db.Cavers.Add(new Caver { FullName = Ana.ToLowerInvariant() });
            db.Cavers.Add(new Caver { FullName = Bogdan });
            await db.SaveChangesAsync();
        }

        var fileId = await UploadAsync("ambiguous.csv", Sheet);
        await CommitAsync(fileId, [2]);

        await using var scope = factory.Services.CreateAsyncScope();
        var db2 = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var trip = await db2.TripLogs.AsNoTracking().FirstAsync(t => t.OwnerUserId == editorId);
        var roster = await db2.TripLogParticipants.AsNoTracking()
            .Where(p => p.TripLogId == trip.Id).Select(p => p.CaverId).ToListAsync();
        var lowered = Ana.ToLowerInvariant();
        var ambiguous = await db2.Cavers.AsNoTracking()
            .Where(c => c.FullName == Ana || c.FullName == lowered)
            .Select(c => c.Id).ToListAsync();
        var plain = await db2.Cavers.AsNoTracking()
            .Where(c => c.FullName == Bogdan).Select(c => c.Id).FirstAsync();

        roster.ShouldContain(plain);
        roster.ShouldNotContain(ambiguous[0]);
        roster.ShouldNotContain(ambiguous[1]);
        // Nobody new was invented for the name either — the switch was off, and a guess is not
        // what an off switch turns into.
        (await db2.Cavers.AsNoTracking().CountAsync(c => c.FullName.Contains(tag))).ShouldBe(3);
        trip.Results.ShouldNotBeNull();
        trip.Results!.ShouldContain(Ana);
    }

    // ---------- one bad row ----------

    /// <summary>
    /// A row that invents places and only then finds it cannot record its trip leaves nothing of
    /// them behind — no feature, no containment edge, no closure row — and the rows on either
    /// side of it still land.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The refusal is a real one and it has to arrive late. A row saves the places it invents
    /// part-way through itself, because the trip write checks in the database that the caves it
    /// is asked to name are there; from that save onwards those features are in the database and
    /// tracked as unchanged, and the row's savepoint is the only thing that can take them back.
    /// Every refusal that fires before that save leaves them merely pending and is undone by
    /// detaching them, so a test built on one would pass against a rollback that never learned
    /// the difference. The purpose seeded here demands a field-data section that no spreadsheet
    /// can fill in, which puts the refusal inside the trip write, after the save.
    /// </para>
    /// <para>
    /// Every count is scoped to this run's own names and account: one database is shared by the
    /// whole suite, so a count over the whole table is a failure waiting for a busier run.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_row_that_fails_after_saving_the_places_it_invented_leaves_none_of_them()
    {
        await SeedStrictTripTypeAsync();

        var fileId = await UploadAsync("rollback.csv", RollbackSheet());
        var result = await CommitAsync(fileId, [2, 3, 4], createEverything: true);

        // Two trips, and two places apiece for the rows that landed.
        result.GetProperty("createdTripCount").GetInt32().ShouldBe(2);
        result.GetProperty("createdFeatureCount").GetInt32().ShouldBe(4);

        var failures = result.GetProperty("failures").EnumerateArray().ToList();
        failures.Count.ShouldBe(1);
        failures[0].GetProperty("line").GetInt32().ShouldBe(3);
        failures[0].GetProperty("code").GetString().ShouldBe("trip_log.field_data_invalid");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        // Nothing the failed row invented is anywhere, deleted rows included: a soft delete
        // would be the importer tidying up rather than the row never having happened.
        (await db.Features.AsNoTracking().IgnoreQueryFilters()
            .CountAsync(f => f.Name == $"Masivul Beta {tag}" || f.Name == $"Valea Beta {tag}"))
            .ShouldBe(0);

        // The rows around it did land, which is what makes the emptiness above mean something:
        // it would read exactly the same against an importer that created nothing at all.
        var mine = await db.Features.AsNoTracking().IgnoreQueryFilters()
            .Where(f => f.OwnerUserId == editorId && f.Name != null && f.Name.Contains(tag))
            .Select(f => new { f.Id, f.Name })
            .ToListAsync();
        mine.Select(f => f.Name).OrderBy(n => n, StringComparer.Ordinal).ShouldBe(
        [
            $"Masivul Alfa {tag}", $"Masivul Gama {tag}", $"Valea Alfa {tag}", $"Valea Gama {tag}",
        ]);

        var mineIds = mine.Select(f => f.Id).ToHashSet();

        // Each surviving sub-area sits under its own massif and under nothing else, and the
        // closure says the same thing the edges do: two self rows for the massifs, and a self
        // row plus its massif for each sub-area.
        var edges = await db.FeatureHierarchyEdges.AsNoTracking()
            .Where(e => mineIds.Contains(e.ChildId) || mineIds.Contains(e.ParentId))
            .Select(e => new { e.ParentId, e.ChildId })
            .ToListAsync();
        edges.Count.ShouldBe(2);
        edges.ShouldAllBe(e => mineIds.Contains(e.ParentId) && mineIds.Contains(e.ChildId));

        var closure = await db.FeatureAncestors.AsNoTracking()
            .Where(a => mineIds.Contains(a.FeatureId) || mineIds.Contains(a.AncestorId))
            .Select(a => new { a.FeatureId, a.AncestorId })
            .ToListAsync();
        closure.Count.ShouldBe(6);
        closure.ShouldAllBe(a => mineIds.Contains(a.FeatureId) && mineIds.Contains(a.AncestorId));

        // And the check the whole hierarchy answers to says these features are sound. Filtered
        // to this run's own features because the verifier reads the whole database.
        var problems = await scope.ServiceProvider
            .GetRequiredService<FeatureIntegrityVerifier>().VerifyAsync();
        problems.Where(p => mineIds.Contains(p.FeatureId)).ShouldBeEmpty();

        var trips = await db.TripLogs.AsNoTracking()
            .Where(t => t.OwnerUserId == editorId && t.Title.Contains(tag))
            .Select(t => t.Title)
            .ToListAsync();
        trips.OrderBy(t => t, StringComparer.Ordinal).ShouldBe(
            [$"A treia tura {tag}", $"Prima tura {tag}"]);
    }


    /// <summary>
    /// The invariant underneath the rollback, stated on its own: a confirmation never leaves a
    /// feature of kind cave without the subtype row that makes it one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A cave is two rows — the feature, which carries the name, the access control and the
    /// representative point, and the subtype row that carries everything cave-specific. The
    /// database guards one direction of that pair and only one: the subtype row's foreign key is
    /// composite on identifier and kind, so a subtype row without its feature cannot exist. The
    /// other way round nothing stops, and nothing before this asked. It is worth asking because
    /// the consequence is not local: the listing projects the subtype of every row it returns, so
    /// a single feature missing its half answers the whole page with a server error, for every
    /// reader of the archive rather than for whoever owns the row.
    /// </para>
    /// <para>
    /// The row this drives fails <em>after</em> its cave was created and saved, which is the only
    /// moment at which the two halves could be parted: the places a row invents have to reach the
    /// database before the trip write can be asked whether the caves it names exist, so a cave is
    /// already stored by the time the purpose that cannot be filled in refuses the row. Stated as
    /// an invariant over this run's own features rather than as a count, because it is a property
    /// the importer owes on every path, not a fact about this sheet.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_confirmation_never_leaves_a_cave_feature_without_its_cave_row()
    {
        await SeedStrictTripTypeAsync();

        var fileId = await UploadAsync("cave-rollback.csv", CaveRollbackSheet());
        var result = await CommitAsync(fileId, [2, 3, 4], createEverything: true);

        // Two rows landed, each inventing a massif, a sub-area and a cave.
        result.GetProperty("createdTripCount").GetInt32().ShouldBe(2);
        result.GetProperty("createdFeatureCount").GetInt32().ShouldBe(6);

        var failures = result.GetProperty("failures").EnumerateArray().ToList();
        failures.Count.ShouldBe(1);
        failures[0].GetProperty("line").GetInt32().ShouldBe(3);
        failures[0].GetProperty("code").GetString().ShouldBe("trip_log.field_data_invalid");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        // Nothing the failed row invented survives, its cave included.
        (await db.Features.AsNoTracking().IgnoreQueryFilters()
            .CountAsync(f => f.Name == $"Masivul Beta {tag}"
                || f.Name == $"Valea Beta {tag}"
                || f.Name == $"Pestera Beta {tag}"))
            .ShouldBe(0);

        // Scoped to this run's own account and suffix: one database is shared by the whole
        // suite, so a query over every cave in it is a failure waiting for a busier run.
        var mine = await db.Features.AsNoTracking().IgnoreQueryFilters()
            .Where(f => f.OwnerUserId == editorId && f.Name != null && f.Name.Contains(tag))
            .Select(f => new { f.Id, f.Name, f.Kind })
            .ToListAsync();
        var mineIds = mine.Select(f => f.Id).ToHashSet();

        // The positive half, without which "no cave feature is missing its row" would read the
        // same against a confirmation that created no caves at all.
        var caveFeatureIds = mine.Where(f => f.Kind == FeatureKind.Cave).Select(f => f.Id).ToList();
        caveFeatureIds.Count.ShouldBe(2);

        var subtypeIds = (await db.Caves.AsNoTracking().IgnoreQueryFilters()
            .Where(c => mineIds.Contains(c.Id)).Select(c => c.Id).ToListAsync()).ToHashSet();
        caveFeatureIds.Where(id => !subtypeIds.Contains(id)).ShouldBeEmpty();

        // And nothing else about these features is half-written either — the closure rows, the
        // ancestor arrays and the effective protection flags all agree with the edges. Filtered
        // to this run's own features because the verifier reads the whole database.
        var problems = await scope.ServiceProvider
            .GetRequiredService<FeatureIntegrityVerifier>().VerifyAsync();
        problems.Where(p => mineIds.Contains(p.FeatureId)).ShouldBeEmpty();
    }


    [Fact]
    public async Task A_row_that_cannot_be_recorded_is_a_listed_failure_and_the_rest_still_land()
    {
        var fileId = await UploadAsync("mixed.csv", Sheet);

        // Line 4 is past the end of the sheet: a review left open while the upload moved on. It
        // is the shape of every row-level refusal — reported with its line, its code and a
        // reason, rather than taking the confirmation with it.
        var result = await CommitAsync(fileId, [2, 3, 4]);

        result.GetProperty("createdTripCount").GetInt32().ShouldBe(2);
        var failures = result.GetProperty("failures").EnumerateArray().ToList();
        failures.Count.ShouldBe(1);
        failures[0].GetProperty("line").GetInt32().ShouldBe(4);
        failures[0].GetProperty("code").GetString().ShouldBe("trip_import.row_missing");
        failures[0].GetProperty("reason").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_confirmation_in_which_nothing_lands_is_refused_with_its_own_code()
    {
        var fileId = await UploadAsync("nothing.csv", Sheet);

        var response = await editor.PostAsync(
            $"/api/v1/trip-imports/{fileId}/commit",
            Body(new { options = Options(), lines = new[] { 40, 41 } }));
        var body = await BodyAsync(response);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body);
        CodeOf(body).ShouldBe("trip_import.nothing_created");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        // The batch would have recorded only failures, so there is no batch at all.
        (await db.ImportBatches.AsNoTracking().CountAsync(b => b.ConfirmedByUserId == editorId)).ShouldBe(0);
        (await db.TripLogs.AsNoTracking().CountAsync(t => t.OwnerUserId == editorId)).ShouldBe(0);
    }

    /// <summary>
    /// The other half of the same rollback: what the confirmation still believes about the
    /// database once a row has been taken back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A row's savepoint takes back the rows it wrote; the mark beside it takes back what it had
    /// not written yet. Between them they have to leave the unit of work as if the row had never
    /// been read — and the easy thing to get wrong is the entity the row created <em>and saved</em>
    /// before it failed, because a saved entity is no longer pending and a rollback that unpicks
    /// only pending work walks straight past it. The tracker is then holding a feature whose row
    /// the savepoint deleted, and nothing in it says so: an unchanged entry writes no statement of
    /// its own, so the confirmation still commits cleanly and the damage only shows when something
    /// later reads tracked state over stored state, which the hierarchy recompute does whenever it
    /// works out what the edge set holds or which features are protected.
    /// </para>
    /// <para>
    /// So this drives the confirmation through the service in one scope and then asks the scope's
    /// own unit of work what it thinks exists. Asserting on the database instead would not tell
    /// the two rollbacks apart: today every identifier the recompute reaches is one the row it is
    /// running for has just minted, so a stale entry is never read back and both spellings leave
    /// the same rows behind. That is a property of what this path currently does, not of the
    /// rollback, which is exactly why it is worth pinning here.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_row_taken_back_leaves_nothing_of_itself_in_the_unit_of_work()
    {
        await SeedStrictTripTypeAsync();
        var fileId = await UploadAsync("unit-of-work.csv", RollbackSheet());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<TripImportCommitService>();

        // The caller's real reading of their own rights, resolved the way a request resolves it,
        // so the confirmation is refused and permitted exactly where a request would be.
        var ctx = await AccessContextResolver.ResolveAsync(db, editorId);
        var file = await db.StoredFiles.FirstAsync(f => f.Id == fileId);

        var result = await service.CommitAsync(
            file,
            new TripImportOptions
            {
                CreateMissingAreas = true,
                CreateMissingCaves = true,
                CreateMissingCavers = true,
            },
            [2, 3, 4],
            new Dictionary<int, TripImportDecision>(),
            ctx);

        result.Failures.Count.ShouldBe(1);
        result.Failures[0].Line.ShouldBe(3);
        result.Failures[0].Code.ShouldBe("trip_log.field_data_invalid");
        result.CreatedFeatureCount.ShouldBe(4);

        var trackedFeatures = db.ChangeTracker.Entries<Feature>()
            .Select(e => e.Entity.Id).ToList();
        var trackedClosure = db.ChangeTracker.Entries<FeatureAncestor>()
            .Select(e => (e.Entity.FeatureId, e.Entity.AncestorId)).ToList();
        var trackedEdges = db.ChangeTracker.Entries<FeatureHierarchyEdge>()
            .Select(e => (e.Entity.ParentId, e.Entity.ChildId)).ToList();

        // Read back through a unit of work of its own, because asking the same one whether a row
        // exists is asking the very tracker under test.
        await using var check = factory.Services.CreateAsyncScope();
        var fresh = check.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        // Narrowed to the features this run minted. One database is shared by the whole suite,
        // so reading either of these tables whole is a query that grows with everything else
        // running beside it.
        var stored = (await fresh.Features.AsNoTracking().IgnoreQueryFilters()
            .Where(f => trackedFeatures.Contains(f.Id)).Select(f => f.Id).ToListAsync()).ToHashSet();
        var storedClosure = (await fresh.FeatureAncestors.AsNoTracking()
            .Where(a => trackedFeatures.Contains(a.FeatureId) || trackedFeatures.Contains(a.AncestorId))
            .ToListAsync())
            .Select(a => (a.FeatureId, a.AncestorId)).ToHashSet();
        var storedEdges = (await fresh.FeatureHierarchyEdges.AsNoTracking()
            .Where(e => trackedFeatures.Contains(e.ParentId) || trackedFeatures.Contains(e.ChildId))
            .ToListAsync())
            .Select(e => (e.ParentId, e.ChildId)).ToHashSet();

        // Counted before they are compared, because "everything tracked also exists" is true of a
        // tracker holding nothing at all — and over-detaching is the other way this seam breaks.
        // The two rows that landed wrote a massif and a sub-area apiece: four features, two
        // containment edges, and six closure rows, a self row per massif and a self row plus its
        // massif per sub-area.
        trackedFeatures.Count.ShouldBe(4);
        trackedClosure.Count.ShouldBe(6);
        trackedEdges.Count.ShouldBe(2);

        trackedFeatures.Where(id => !stored.Contains(id)).ShouldBeEmpty();
        trackedClosure.Where(a => !storedClosure.Contains(a)).ShouldBeEmpty();
        trackedEdges.Where(e => !storedEdges.Contains(e)).ShouldBeEmpty();
    }

    // ---------- who may confirm ----------

    [Fact]
    public async Task Confirming_without_an_account_is_refused()
    {
        var fileId = await UploadAsync("anon.csv", Sheet);

        var response = await factory.CreateClient().PostAsync(
            $"/api/v1/trip-imports/{fileId}/commit", Body(new { options = Options(), lines = new[] { 2 } }));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, await BodyAsync(response));
    }

    [Fact]
    public async Task An_account_that_may_not_record_trips_is_told_so_rather_than_told_about_the_file()
    {
        var fileId = await UploadAsync("forbidden.csv", Sheet);

        var response = await viewer.PostAsync(
            $"/api/v1/trip-imports/{fileId}/commit", Body(new { options = Options(), lines = new[] { 2 } }));
        var body = await BodyAsync(response);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, body);
        CodeOf(body).ShouldBe(CreateRules.ForbiddenCode);

        // And nothing was recorded: the refusal happens before the upload is even fetched.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.TripLogs.AsNoTracking().CountAsync(t => t.OwnerUserId == editorId)).ShouldBe(0);
    }

    [Fact]
    public async Task A_confirmation_with_no_rows_chosen_fails_validation()
    {
        var fileId = await UploadAsync("empty.csv", Sheet);

        var response = await editor.PostAsync(
            $"/api/v1/trip-imports/{fileId}/commit",
            Body(new { options = Options(), lines = Array.Empty<int>() }));
        var body = await BodyAsync(response);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body);
        CodeOf(body).ShouldBe("validation.failed");
    }

    [Fact]
    public async Task A_file_this_account_may_not_read_answers_the_same_as_one_that_is_not_there()
    {
        var response = await editor.PostAsync(
            $"/api/v1/trip-imports/{Guid.NewGuid()}/commit",
            Body(new { options = Options(), lines = new[] { 2 } }));
        var body = await BodyAsync(response);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound, body);
        CodeOf(body).ShouldBe("file.not_found");
    }

    // ---------- helpers ----------

    /// <summary>
    /// Three caves under one name apiece: readable and placeable, readable but guarded, and
    /// neither. The third is refused explicitly rather than left private, because the seeded
    /// editors read past visibility at the widest scope.
    /// </summary>
    private async Task<(Guid Readable, Guid Guarded, Guid Hidden)> SeedCaveMatrixAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        var readable = Cave($"Pestera Vizibila {tag}", editorId, Visibility.Public, guarded: false, caveTypeId);
        var guarded = Cave($"Pestera Ascunsa {tag}", strangerId, Visibility.Public, guarded: true, caveTypeId);
        var hidden = Cave($"Pestera Interzisa {tag}", strangerId, Visibility.Public, guarded: false, caveTypeId);
        foreach (var cave in new[] { readable, guarded, hidden })
        {
            db.Features.Add(cave);

            // The closure row the write service would have written beside the ancestor array.
            // A fixture that writes only the array leaves the installation in a state the
            // integrity verifier is right to call broken, and because that verifier reads the
            // whole database the failure surfaces in whichever unrelated test runs it.
            db.FeatureAncestors.Add(new FeatureAncestor { FeatureId = cave.Id, AncestorId = cave.Id });
        }

        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = editorId,
            Effect = AccessEffect.Deny,
            Domain = AccessDomain.Features,
            Actions = AccessAction.Read | AccessAction.ViewExactLocation,
            ScopeKind = AccessScopeKind.Object,
            // A feature's own reach is anchored by the feature column rather than the general
            // one; the database refuses the other spelling outright.
            ScopeFeatureId = hidden.Id,
        });
        await db.SaveChangesAsync();
        return (readable.Id, guarded.Id, hidden.Id);
    }

    private static Feature Cave(
        string name, Guid ownerId, Visibility visibility, bool guarded, long caveTypeId)
    {
        var id = Guid.NewGuid();
        return new Feature
        {
            Id = id,
            Name = name,
            Kind = FeatureKind.Cave,
            OwnerUserId = ownerId,
            Visibility = visibility,
            LocationProtected = guarded,
            // Stamped here because the write service, which normally derives it, is not what put
            // this row in. A guard nothing reads is a guard that does not hold.
            IsProtectedEffective = guarded,
            AncestorIds = [id],
            // A cave is two rows: the feature and the subtype row that carries its
            // cave-specific attributes. The database only guards the direction that cannot
            // happen anyway — a subtype row without its feature — so a fixture that writes the
            // feature alone leaves behind a state no write path can produce and every read path
            // that projects a cave falls over, for every reader of the listing rather than only
            // for whoever owns the row.
            Cave = new Cave { Id = id, CaveTypeId = caveTypeId },
        };
    }

    /// <summary>
    /// Three rows, each inventing a massif and a sub-area inside it, of which the middle one
    /// names a purpose that cannot be recorded. Nested rather than flat on purpose: a row that
    /// creates one feature leaves one closure row behind, and a row that creates a feature inside
    /// a feature it also created leaves a transitive one — the only case in which a rollback that
    /// takes back the rows but not the derived state can be told from one that works.
    /// </summary>
    private string RollbackSheet() =>
        "Nr crt.,Data inceput,Titlu,Masiv/zona,Subzona,Participanti,Tip\r\n"
        + $"1,05/03/2024,Prima tura {tag},Masivul Alfa {tag},Valea Alfa {tag},{Ana},explorare {tag}\r\n"
        + $"2,17/04/2024,A doua tura {tag},Masivul Beta {tag},Valea Beta {tag},{Ana},{StrictType}\r\n"
        + $"3,18/04/2024,A treia tura {tag},Masivul Gama {tag},Valea Gama {tag},{Ana},explorare {tag}\r\n";

    /// <summary>
    /// The same three rows as the sheet above, each also naming a cave of its own. Separate
    /// rather than folded into that one because both of the tests there count the features a
    /// confirmation creates exactly, and a cave apiece changes every one of those numbers.
    /// </summary>
    private string CaveRollbackSheet() =>
        "Nr crt.,Data inceput,Titlu,Masiv/zona,Subzona,Pesteri,Participanti,Tip\r\n"
        + $"1,05/03/2024,Prima tura {tag},Masivul Alfa {tag},Valea Alfa {tag},Pestera Alfa {tag},{Ana},explorare {tag}\r\n"
        + $"2,17/04/2024,A doua tura {tag},Masivul Beta {tag},Valea Beta {tag},Pestera Beta {tag},{Ana},{StrictType}\r\n"
        + $"3,18/04/2024,A treia tura {tag},Masivul Gama {tag},Valea Gama {tag},Pestera Gama {tag},{Ana},explorare {tag}\r\n";

    private string StrictType => $"Cartare stricta {tag}";

    /// <summary>
    /// A purpose whose field-data section is required to carry a depth. A spreadsheet has no
    /// column that fills a section in, so every row naming this purpose is refused by the trip
    /// write — which is what puts the refusal after the row has already saved its places.
    /// </summary>
    private async Task SeedStrictTripTypeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.TripTypes.Add(new TripType
        {
            Code = $"tic-strict-{tag}",
            Name = StrictType,
            FieldDataSchema =
                """{"type":"object","required":["depth"],"properties":{"depth":{"type":"number"}}}""",
        });
        await db.SaveChangesAsync();
    }

    private string CaveSheet() =>
        "Nr crt.,Data inceput,Titlu,Masiv/zona,Pesteri,Participanti,Tip\r\n"
        + $"1,05/03/2024,Cu pestera vizibila,Masivul Unu,Pestera Vizibila {tag},{Ana},explorare\r\n"
        + $"2,17/04/2024,Cu pestera ascunsa,Masivul Unu,Pestera Ascunsa {tag},{Ana},explorare\r\n"
        + $"3,18/04/2024,Cu pestera interzisa,Masivul Unu,Pestera Interzisa {tag},{Ana},explorare\r\n";

    private static async Task<HashSet<Guid>> LinkedFeatureIdsAsync(SilexGisDbContext db, List<Guid> tripIds)
    {
        var linkIds = await db.ResLinkMembers.AsNoTracking()
            .Where(m => m.EntityType == AttachedEntityType.TripLog
                && m.EntityId != null && tripIds.Contains(m.EntityId!.Value))
            .Select(m => m.ResLinkId).Distinct().ToListAsync();
        return [.. await db.ResLinkMembers.AsNoTracking()
            .Where(m => linkIds.Contains(m.ResLinkId) && m.FeatureId != null)
            .Select(m => m.FeatureId!.Value).ToListAsync()];
    }

    /// <summary>
    /// Notifications addressed to the three accounts this run created, which are the only
    /// people an import of this run's sheet could reach. Counted that way rather than over
    /// the whole table because one database is shared by every test in this suite, so a
    /// notification another test wrote between the two readings would otherwise read as this
    /// import having spoken.
    /// </summary>
    private async Task<int> NotificationCountAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.Notifications.AsNoTracking().CountAsync(n =>
            n.RecipientUserId == editorId
            || n.RecipientUserId == strangerId
            || n.RecipientUserId == viewerId);
    }

    /// <summary>
    /// The choices a confirmation is made under. The places and people switch together because
    /// almost every test here wants either all of them or none; the vocabulary switch is separate
    /// because it is held to a different right — adding a term every trip form in the installation
    /// will show is the taxonomy's business, not the trip log's, and the seeded editor these tests
    /// run as does not hold it.
    /// </summary>
    private static object Options(bool createEverything = false, bool createTripTypes = false) => new
    {
        delimiter = ",",
        multiValueSeparators = ";,",
        slashSeparatedFields = Array.Empty<string>(),
        dateOrder = "dayFirst",
        columns = new Dictionary<string, string>(),
        visibility = "private",
        cavingGroupId = (Guid?)null,
        createMissingCaves = createEverything,
        createMissingAreas = createEverything,
        createMissingCavers = createEverything,
        createMissingTripTypes = createTripTypes,
    };

    private async Task<JsonElement> CommitAsync(Guid fileId, int[] lines, bool createEverything = false)
    {
        var response = await editor.PostAsync(
            $"/api/v1/trip-imports/{fileId}/commit",
            Body(new { options = Options(createEverything), lines }));
        var body = await BodyAsync(response);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private async Task<Guid> UploadAsync(string fileName, string text)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
        content.Headers.ContentType = new("text/csv");
        using var form = new MultipartFormDataContent { { content, "file", fileName } };
        var response = await editor.PostAsync("/api/v1/files/?allowDuplicate=true", form);
        var payload = await BodyAsync(response);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private static Task<string> BodyAsync(HttpResponseMessage response) =>
        response.Content.ReadAsStringAsync();

    private static StringContent Body(object value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private static string? CodeOf(string problemBody) =>
        JsonDocument.Parse(problemBody).RootElement.GetProperty("code").GetString();

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }
}
