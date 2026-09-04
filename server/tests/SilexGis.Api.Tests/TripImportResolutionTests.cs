// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Domain.Import.TripCsv;
using SilexGis.Infrastructure.Import;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// What a row of a club's spreadsheet is taken to mean, and — the part that matters — what it is
/// never taken to mean.
/// </summary>
/// <remarks>
/// <para>
/// Two rules are on trial. The first is that proposing a cave is a read of that cave and a link
/// from a trip places it, so a name reaches a proposal only after both gates: readable, then
/// placeable. The second is that a guess is worse than a gap — a name two people answer to, or a
/// name that is barely a name, leaves the row for somebody to settle rather than resolving
/// itself quietly to whichever row the database returned first.
/// </para>
/// <para>
/// Every refusal here is checked beside the case that succeeds, with one thing changed between
/// them. An assertion that something was not proposed passes just as well against an importer
/// that proposes nothing at all.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class TripImportResolutionTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;

    private string tag = string.Empty;
    private Guid viewerId;
    private Guid strangerId;

    public TripImportResolutionTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        tag = Guid.NewGuid().ToString("N")[..8];

        // A viewer rather than an editor or an administrator, deliberately. The seeded editors
        // read past visibility at the widest scope, so a matrix built on one would prove that
        // the query ran and nothing about what it withheld. A viewer holds no exact-location
        // right over features either, which is what makes the middle row of the matrix real.
        viewerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tir-view-{tag}@t.local");
        strangerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tir-owner-{tag}@t.local");
    }

    // ---------- the protection matrix ----------

    /// <summary>
    /// Three caves under three names, one of each kind, resolved for a caller who may be told
    /// about exactly one of them.
    /// </summary>
    [Fact]
    public async Task A_cave_is_proposed_only_when_it_may_be_read_and_placed()
    {
        var readable = $"Pestera Deschisa {tag}";
        var guarded = $"Pestera Pazita {tag}";
        var hidden = $"Pestera Ascunsa {tag}";

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

            Seed(
                db,
                // Readable and placeable: public, and nothing above it guards a position.
                Cave(readable, strangerId, Visibility.Public, protectedLocation: false),

                // Readable but not placeable: equally public, guarded. The one thing that differs
                // between this cave and the one above it is the guard.
                Cave(guarded, strangerId, Visibility.Public, protectedLocation: true),

                // Neither: somebody else's private cave, with no grant of any kind reaching this
                // caller — not a share, not a set, not a group.
                Cave(hidden, strangerId, Visibility.Private, protectedLocation: false));
            await db.SaveChangesAsync();
        }

        var resolved = await ResolveAsync(viewerId, Row(1, caves: [readable, guarded, hidden]));
        var matches = resolved.Rows[1].Caves;
        matches.Count.ShouldBe(3);

        matches[0].State.ShouldBe(TripImportMatchState.Matched);
        matches[0].FeatureId.ShouldNotBeNull();

        // The guarded cave and the invisible one are answered identically, and the answer is the
        // one a caller gets when nothing of that name exists here. Neither the state, nor the
        // count of candidates, nor the name says that something was withheld — an answer that
        // differs from "nothing matched" is a disclosure anybody could ask for by uploading a
        // spreadsheet with a name in it.
        foreach (var withheld in new[] { matches[1], matches[2] })
        {
            withheld.State.ShouldBe(TripImportMatchState.Unmatched);
            withheld.FeatureId.ShouldBeNull();
            withheld.Name.ShouldBeNull();
            withheld.Candidates.ShouldBeEmpty();
        }

        // And the names nothing was taken from are kept, so the trip still records where it
        // says it went even though no link could be made.
        resolved.Rows[1].LocationNote.ShouldNotBeNull();
        resolved.Rows[1].LocationNote!.ShouldContain(guarded);
        resolved.Rows[1].LocationNote!.ShouldContain(hidden);
        resolved.Rows[1].LocationNote!.ShouldNotContain(readable);
    }

    /// <summary>
    /// The same guarded cave, resolved for its owner. Without this the test above would pass
    /// against a resolver that never proposes any cave at all.
    /// </summary>
    [Fact]
    public async Task The_same_guarded_cave_is_proposed_to_somebody_who_may_place_it()
    {
        var guarded = $"Pestera Pazita Proprie {tag}";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            Seed(db, Cave(guarded, strangerId, Visibility.Public, protectedLocation: true));
            await db.SaveChangesAsync();
        }

        var forOwner = await ResolveAsync(strangerId, Row(1, caves: [guarded]));
        forOwner.Rows[1].Caves[0].State.ShouldBe(TripImportMatchState.Matched);

        var forViewer = await ResolveAsync(viewerId, Row(1, caves: [guarded]));
        forViewer.Rows[1].Caves[0].State.ShouldBe(TripImportMatchState.Unmatched);
    }

    /// <summary>
    /// A cave nothing here answers to becomes a cave only when the switch says so, and the
    /// switch changes nothing about what did match.
    /// </summary>
    [Fact]
    public async Task An_unmatched_cave_is_offered_for_creation_only_while_the_switch_is_on()
    {
        var row = Row(1, caves: [$"Pestera Noua {tag}"]);

        var off = await ResolveAsync(viewerId, row);
        off.Rows[1].Caves[0].WillCreate.ShouldBeFalse();
        off.NewCaves.ShouldBeEmpty();

        var on = await ResolveAsync(viewerId, row, o => o with { CreateMissingCaves = true });
        on.Rows[1].Caves[0].WillCreate.ShouldBeTrue();
        on.NewCaves.Count.ShouldBe(1);
    }

    // ---------- people ----------

    /// <summary>
    /// A name two people answer to resolves to neither of them, and the same name with one
    /// claimant resolves to that one.
    /// </summary>
    [Fact]
    public async Task A_name_two_cavers_share_is_left_for_a_person_to_settle()
    {
        var shared = $"Ioana Campioana {tag}";
        var alone = $"Ion Anghel {tag}";

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            db.Cavers.Add(new Caver { FullName = shared });

            // Written with the diacritics the other one lacks: folding is what makes these one
            // name, and a resolver that compared them as written would call this unambiguous.
            db.Cavers.Add(new Caver { FullName = shared.Replace("Campioana", "Câmpioana") });
            db.Cavers.Add(new Caver { FullName = alone });
            await db.SaveChangesAsync();
        }

        var resolved = await ResolveAsync(
            viewerId,
            Row(1, participants: [shared, alone]),
            o => o with { CreateMissingCavers = true });

        var ambiguous = resolved.Rows[1].Participants[0];
        ambiguous.State.ShouldBe(TripImportMatchState.Ambiguous);
        ambiguous.CaverId.ShouldBeNull();
        // Both of them named, not merely counted: the review settles the name by offering these
        // two and no others, so a count alone would state a decision without affording it.
        ambiguous.Candidates.Count.ShouldBe(2);
        ambiguous.Candidates.Select(c => c.Id).ShouldBeUnique();

        // Not resolved to the older of the two, and not created a third time either — with the
        // switch on, which is the state in which the wrong answer would have been silent.
        ambiguous.WillCreate.ShouldBeFalse();
        resolved.NewCavers.ShouldNotContain(shared);

        resolved.Rows[1].Participants[1].State.ShouldBe(TripImportMatchState.Matched);
        resolved.Rows[1].Participants[1].CaverId.ShouldNotBeNull();
    }

    /// <summary>An initial creates nobody, however the switch stands; a full name does.</summary>
    [Fact]
    public async Task A_bare_initial_creates_nobody()
    {
        var resolved = await ResolveAsync(
            viewerId,
            Row(1, participants: ["Ion A.", $"Adrian Baritiu {tag}"]),
            o => o with { CreateMissingCavers = true });

        var initial = resolved.Rows[1].Participants[0];
        initial.State.ShouldBe(TripImportMatchState.Unmatched);
        initial.WillCreate.ShouldBeFalse();

        // The positive half: the switch is on and it does create somebody, so the refusal above
        // is about the name rather than about the switch.
        resolved.Rows[1].Participants[1].WillCreate.ShouldBeTrue();
        resolved.NewCavers.Count.ShouldBe(1);
    }

    /// <summary>
    /// A person already in the roster under a name nobody could be created from is still found:
    /// the refusal is about creating, never about matching.
    /// </summary>
    [Fact]
    public async Task An_initial_already_in_the_roster_is_matched_rather_than_missed()
    {
        var initial = $"Ion A. {tag}";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            db.Cavers.Add(new Caver { FullName = initial });
            await db.SaveChangesAsync();
        }

        var resolved = await ResolveAsync(viewerId, Row(1, participants: [initial]));
        resolved.Rows[1].Participants[0].State.ShouldBe(TripImportMatchState.Matched);
        resolved.Rows[1].Participants[0].CaverId.ShouldNotBeNull();
    }

    // ---------- vocabularies ----------

    /// <summary>
    /// A type is found by its code and by its name alike, folded, and one nothing answers to is
    /// offered rather than invented.
    /// </summary>
    [Fact]
    public async Task A_trip_type_is_resolved_by_what_it_says_rather_than_by_its_number()
    {
        long typeId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var type = new TripType { Code = $"explorare_{tag}", Name = $"Explorare {tag}" };
            db.TripTypes.Add(type);
            await db.SaveChangesAsync();
            typeId = type.Id;
        }

        var byName = await ResolveAsync(viewerId, Row(1, tripType: $"explorare {tag}"));
        byName.Rows[1].TripType!.Id.ShouldBe(typeId);

        var byCode = await ResolveAsync(viewerId, Row(1, tripType: $"EXPLORARE_{tag}"));
        byCode.Rows[1].TripType!.Id.ShouldBe(typeId);

        var missing = await ResolveAsync(viewerId, Row(1, tripType: $"Nimic {tag}"));
        missing.Rows[1].TripType!.State.ShouldBe(TripImportMatchState.Unmatched);
        missing.Rows[1].TripType!.WillCreate.ShouldBeFalse();
        missing.NewTripTypes.ShouldBeEmpty();

        var offered = await ResolveAsync(
            viewerId, Row(1, tripType: $"Nimic {tag}"), o => o with { CreateMissingTripTypes = true });
        offered.NewTripTypes.Count.ShouldBe(1);
    }

    /// <summary>The two shipped roles are found by their codes, which is how they travel.</summary>
    [Fact]
    public async Task The_shipped_roles_are_resolved_by_code()
    {
        var resolved = await ResolveAsync(viewerId, Row(1, participants: ["Ion Anghel"]));
        resolved.ParticipantRole.ShouldNotBeNull();
        resolved.ProposerRole.ShouldNotBeNull();
        resolved.ParticipantRole!.Id.ShouldNotBe(resolved.ProposerRole!.Id);
    }

    // ---------- areas ----------

    /// <summary>
    /// An area feature is matched the same way a cave is, and it answers to the placement gate
    /// too: a trip linked to a guarded area still says where that trip was.
    /// </summary>
    [Fact]
    public async Task An_area_is_matched_when_it_may_be_read_and_placed()
    {
        var open = $"Masivul Deschis {tag}";
        var guarded = $"Masivul Pazit {tag}";

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var typeId = await AreaTypeIdAsync(db);
            Seed(
                db,
                Area(open, typeId, strangerId, protectedLocation: false),
                Area(guarded, typeId, strangerId, protectedLocation: true));
            await db.SaveChangesAsync();
        }

        var resolved = await ResolveAsync(viewerId, Row(1, massif: open, subArea: guarded));
        resolved.Rows[1].Massif!.State.ShouldBe(TripImportMatchState.Matched);
        resolved.Rows[1].SubArea!.State.ShouldBe(TripImportMatchState.Unmatched);
        resolved.Rows[1].LocationNote!.ShouldContain(guarded);
    }

    // ---------- helpers ----------

    private async Task<TripImportResolutionSet> ResolveAsync(
        Guid userId, TripCsvRow row, Func<TripImportOptions, TripImportOptions>? options = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var ctx = await AccessContextResolver.ResolveAsync(db, userId);
        var resolver = scope.ServiceProvider.GetRequiredService<TripImportResolver>();
        var chosen = options is null ? TripImportOptions.Default : options(TripImportOptions.Default);
        return await resolver.ResolveAsync([row], chosen, ctx, CancellationToken.None);
    }

    private static TripCsvRow Row(
        int line,
        string? tripType = null,
        string? massif = null,
        string? subArea = null,
        IReadOnlyList<string>? caves = null,
        IReadOnlyList<string>? participants = null) =>
        new()
        {
            Line = line,
            Title = "O tura",
            TripType = tripType,
            Massif = massif,
            SubArea = subArea,
            Caves = caves ?? [],
            Participants = participants ?? [],
        };

    private static Feature Cave(string name, Guid ownerId, Visibility visibility, bool protectedLocation)
    {
        var id = Guid.NewGuid();
        return new Feature
        {
            Id = id,
            Name = name,
            Kind = FeatureKind.Cave,
            OwnerUserId = ownerId,
            Visibility = visibility,
            LocationProtected = protectedLocation,
            // Stamped here because the write service, which normally derives it, is not what
            // put this row in. A guard nothing reads is a guard that does not hold.
            IsProtectedEffective = protectedLocation,
            AncestorIds = [id],
        };
    }

    private static Feature Area(string name, long typeId, Guid ownerId, bool protectedLocation)
    {
        var id = Guid.NewGuid();
        return new Feature
        {
            Id = id,
            Name = name,
            Kind = FeatureKind.Generic,
            FeatureTypeId = typeId,
            Category = FeatureCategory.Area,
            OwnerUserId = ownerId,
            Visibility = Visibility.Public,
            LocationProtected = protectedLocation,
            IsProtectedEffective = protectedLocation,
            AncestorIds = [id],
        };
    }

    /// <summary>
    /// Puts features in with the derived hierarchy state the write service would have derived.
    ///
    /// <para>
    /// Stamping <c>AncestorIds</c> is only half of it: a feature's ancestry is held twice, once as
    /// the array a read path splices and once as the closure rows the containment queries join,
    /// and the write service always writes both. A fixture that writes only the array leaves the
    /// installation in a state the integrity verifier is right to call broken — and because that
    /// verifier reads the whole database, the failure lands on whichever unrelated test happens to
    /// run it, naming feature identifiers that belong to no test at all.
    /// </para>
    /// </summary>
    private static void Seed(SilexGisDbContext db, params Feature[] features)
    {
        foreach (var feature in features)
        {
            db.Features.Add(feature);
            db.FeatureAncestors.Add(new FeatureAncestor { FeatureId = feature.Id, AncestorId = feature.Id });
        }
    }

    private static async Task<long> AreaTypeIdAsync(SilexGisDbContext db) =>
        await db.FeatureTypes.AsNoTracking().Where(t => t.Code == "massif").Select(t => t.Id).FirstAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}
