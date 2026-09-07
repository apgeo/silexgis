// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Filters;
using SilexGis.Infrastructure.Filters;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// What a world seeds so the suite can prove it does not leak it.
/// </summary>
/// <remarks>
/// Each world contributes one: a row that exactly one person may see, and a condition that matches
/// it. The suite then asks that world, as somebody else, and requires nothing back. Written per
/// world because only the world knows how to make a row of its own kind that is genuinely private —
/// a shared fixture would end up asserting against whatever the most permissive world allowed.
/// </remarks>
public abstract class WorldFixture
{
    public abstract string World { get; }

    /// <summary>
    /// Creates a row only <paramref name="ownerId"/> may see, and returns a filter matching it.
    /// </summary>
    public abstract Task<FilterNode> SeedHiddenAsync(SilexGisDbContext db, Guid ownerId, string tag);

    /// <summary>
    /// Creates rows anybody may see, all sharing one sort key, and returns a filter matching them.
    /// </summary>
    /// <remarks>
    /// The shared key is the point. Paging is only unstable when the sort cannot tell rows apart,
    /// so a fixture whose rows each have a distinct timestamp would let a world with no tie-breaker
    /// pass. These are written in one transaction, which is exactly how a seeded taxonomy or an
    /// import arrives, and is where the problem actually shows up.
    /// </remarks>
    public abstract Task<FilterNode> SeedIndistinguishableAsync(
        SilexGisDbContext db, Guid ownerId, string tag, int count);

    /// <summary>
    /// Creates a row this world's own list endpoint withholds from an ordinary caller, or returns
    /// null when the world has no such rule.
    /// </summary>
    /// <remarks>
    /// Visibility is not the only thing a list withholds. A feature's protected centreline traces a
    /// cave's course underground, so it is kept back entirely from somebody without exact view —
    /// not shown without geometry, not counted. A world that composed only the visibility walk would
    /// return it, and the filter would quietly be a second way to ask the same question with a more
    /// generous answer. That happened once here; this is what would have caught it.
    /// </remarks>
    public virtual Task<FilterNode?> SeedWithheldAsync(SilexGisDbContext db, Guid ownerId, string tag) =>
        Task.FromResult<FilterNode?>(null);

    /// <summary>A value of the right shape for one of this world's declared fields.</summary>
    public virtual FilterValue[] ValuesFor(FieldDescriptor field, FilterOp op) =>
        ConformanceValues.Default(field, op);
}

/// <summary>Plausible values by field kind, so the suite can exercise an operator it has never seen.</summary>
public static class ConformanceValues
{
    public static FilterValue[] Default(FieldDescriptor field, FilterOp op)
    {
        var count = FilterOps.Arity(op) ?? 2;
        if (count == 0)
        {
            return [];
        }

        return [.. Enumerable.Range(0, count).Select(i => One(field.Kind, i))];
    }

    private static FilterValue One(FieldKind kind, int index) => kind switch
    {
        FieldKind.Text => new TextValue($"conformance-{index}"),
        FieldKind.Number => new NumberValue(index),
        FieldKind.Boolean => new BooleanValue(index == 0),
        // Ordered, because Between is checked low-first and an inverted pair is a different test.
        FieldKind.Instant => new InstantValue(DateTimeOffset.UtcNow.AddYears(index - 1)),
        _ => new IdValue($"conformance-{index}"),
    };
}

/// <summary>A feature nobody but its owner may see.</summary>
public sealed class FeatureWorldFixture : WorldFixture
{
    public override string World => FeatureFilterWorld.Key;

    public override async Task<FilterNode> SeedHiddenAsync(SilexGisDbContext db, Guid ownerId, string tag)
    {
        var typeId = await db.FeatureTypes.AsNoTracking().Select(t => t.Id).FirstAsync();
        Add(db, Rootless(typeId, ownerId, $"Conformance {tag}", Visibility.Private));
        await db.SaveChangesAsync();

        return new ConditionNode(FeatureFilterFields.Name, FilterOp.Contains, [new TextValue(tag)]);
    }

    public override async Task<FilterNode> SeedIndistinguishableAsync(
        SilexGisDbContext db, Guid ownerId, string tag, int count)
    {
        var typeId = await db.FeatureTypes.AsNoTracking().Select(t => t.Id).FirstAsync();
        var stamp = DateTimeOffset.UtcNow;

        for (var i = 0; i < count; i++)
        {
            var row = Rootless(typeId, ownerId, $"Paged {tag}", Visibility.Public);
            row.CreatedAt = stamp;
            row.UpdatedAt = stamp;
            Add(db, row);
        }

        await db.SaveChangesAsync();
        return new ConditionNode(FeatureFilterFields.Name, FilterOp.Contains, [new TextValue(tag)]);
    }

    /// <summary>
    /// A feature with no parent, stamped the way the write service stamps one.
    /// </summary>
    /// <remarks>
    /// A feature is visible through its ancestor chain, and a rootless feature's chain is itself —
    /// so a row inserted without one is invisible to everybody but its owner regardless of what its
    /// visibility says. Building fixtures by hand means building that too, or the fixture proves
    /// something about a row shape the application never creates.
    /// </remarks>
    public override async Task<FilterNode?> SeedWithheldAsync(
        SilexGisDbContext db, Guid ownerId, string tag)
    {
        // Public, so the visibility walk lets it straight through — which is the point. What keeps
        // it back is the centreline rule, and nothing else would.
        var typeId = await db.FeatureTypes.AsNoTracking().Select(t => t.Id).FirstAsync();
        var row = Rootless(typeId, ownerId, $"Centreline {tag}", Visibility.Public);
        row.Kind = FeatureKind.Centerline;
        // A type belongs to a generic feature and to nothing else; the schema checks both ways.
        row.FeatureTypeId = null;
        row.LocationProtected = true;
        row.IsProtectedEffective = true;
        // The schema insists a centreline has a course, which is the whole reason it is withheld:
        // the geometry is the survey, and the survey is where the cave goes.
        row.Geom = new NetTopologySuite.Geometries.MultiLineString(
        [
            new NetTopologySuite.Geometries.LineString(
            [
                new NetTopologySuite.Geometries.Coordinate(22.5, 46.5),
                new NetTopologySuite.Geometries.Coordinate(22.51, 46.51),
            ]),
        ])
        { SRID = 4326 };
        Add(db, row);
        await db.SaveChangesAsync();

        return new ConditionNode(FeatureFilterFields.Name, FilterOp.Contains, [new TextValue(tag)]);
    }

        // A rootless feature is its own ancestor, and that fact lives in two places the write
        // service keeps in step: the array on the row, which the visibility walk reads, and the
        // closure table, which the integrity check compares against the hierarchy edges. A fixture
        // that sets only the array leaves every row it creates looking corrupt to that check.
    private static void Add(SilexGisDbContext db, Feature feature)
    {
        db.Features.Add(feature);
        db.FeatureAncestors.Add(new FeatureAncestor { FeatureId = feature.Id, AncestorId = feature.Id });
    }

    private static Feature Rootless(long typeId, Guid ownerId, string name, Visibility visibility)
    {
        var id = Guid.NewGuid();
        return new Feature
        {
            Id = id,
            Name = name,
            Kind = FeatureKind.Generic,
            FeatureTypeId = typeId,
            OwnerUserId = ownerId,
            Visibility = visibility,
            AncestorIds = [id],
        };
    }
}

/// <summary>
/// A trip nobody but its owner may see.
///
/// No <c>SeedWithheldAsync</c>, deliberately, and the reason was re-checked when which caves a
/// trip is about stopped being a column of its own and became links like any other.
///
/// A trip row is disclosed whole to whoever may read the trip: no row is held back beyond
/// visibility, so there is no row this world's list would withhold and nothing to seed. One
/// <em>field</em> is held back — the account of what went wrong is told only to a caller who may
/// change the trip, since it names identifiable people making mistakes — and that still needs no
/// seeding here, because it withholds part of a row rather than the row, and the vocabulary
/// declares no field over it. What is also held back is a trip's <em>children</em> — the caves
/// it names are taken out of the reading for a caller who may not place them, and asking the trip
/// list for the trips at a particular cave answers with an empty page rather than a partial one.
/// None of the three is a rule
/// about which trips exist, and this world offers no way to ask either question: the vocabulary
/// declares no field naming a cave, on purpose, because a caller who may read a trip but not the
/// cave it went to could otherwise read the answer off a count of rows they never see.
///
/// So the world stays a plain visibility walk, and a trip stays not placeable — that is still
/// waiting on a rule about who may be shown a trip's own geometry, which no part of this
/// changes. If a cave field is ever admitted to the vocabulary, or a trip's geometry gains a
/// protection rule, both of those decisions move and this override arrives with them.
/// </summary>
public sealed class TripLogWorldFixture : WorldFixture
{
    public override string World => TripLogFilterWorld.Key;

    public override async Task<FilterNode> SeedHiddenAsync(SilexGisDbContext db, Guid ownerId, string tag)
    {
        db.TripLogs.Add(Trip(ownerId, $"Conformance {tag}", Visibility.Private));
        await db.SaveChangesAsync();

        return new ConditionNode(TripLogFilterFields.Title, FilterOp.Contains, [new TextValue(tag)]);
    }

    public override async Task<FilterNode> SeedIndistinguishableAsync(
        SilexGisDbContext db, Guid ownerId, string tag, int count)
    {
        var stamp = DateTimeOffset.UtcNow;
        for (var i = 0; i < count; i++)
        {
            var row = Trip(ownerId, $"Paged {tag}", Visibility.Public);
            row.CreatedAt = stamp;
            row.UpdatedAt = stamp;
            db.TripLogs.Add(row);
        }

        await db.SaveChangesAsync();
        return new ConditionNode(TripLogFilterFields.Title, FilterOp.Contains, [new TextValue(tag)]);
    }

    /// <summary>
    /// Values a trip's two identity-shaped fields can actually be compared by, and the generic
    /// ones for everything else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The generated placeholder is a name no lifecycle state has, and an enum leaf answers an
    /// unknown name by matching nothing — so the suite would have proved only that a nonsense
    /// value is refused politely, never that the comparison the field exists for is translatable
    /// at all. Both values are real states, so the two-value operators compare two rows' worth of
    /// the vocabulary.
    /// </para>
    /// <para>
    /// The purpose field has the same problem for a different reason: it names a row in a
    /// vocabulary an installation extends, so the value is a numeric identity and the generated
    /// placeholder is not one. Row identities are assigned by the database and this runs before
    /// anything is seeded here, so what is fed is a well-formed identity rather than a particular
    /// row's — enough to prove the comparison compiles and runs, which is what this suite judges.
    /// </para>
    /// </remarks>
    public override FilterValue[] ValuesFor(FieldDescriptor field, FilterOp op)
    {
        var count = FilterOps.Arity(op) ?? 2;

        if (field.Key == TripLogFilterFields.State)
        {
            ActivityState[] states = [ActivityState.Draft, ActivityState.Published];
            return [.. Enumerable.Range(0, count).Select(i => new IdValue(states[i % states.Length].ToString()))];
        }

        if (field.Key == TripLogFilterFields.Type)
        {
            return
            [
                .. Enumerable.Range(1, count)
                    .Select(i => new IdValue(i.ToString(System.Globalization.CultureInfo.InvariantCulture))),
            ];
        }

        return base.ValuesFor(field, op);
    }

    private static TripLog Trip(Guid ownerId, string title, Visibility visibility) => new()
    {
        Title = title,
        TripDate = new DateOnly(2026, 5, 3),
        OwnerUserId = ownerId,
        Visibility = visibility,
    };
}

/// <summary>A document nobody but its owner may see.</summary>
public sealed class DocumentWorldFixture : WorldFixture
{
    public override string World => DocumentFilterWorld.Key;

    public override async Task<FilterNode> SeedHiddenAsync(SilexGisDbContext db, Guid ownerId, string tag)
    {
        Add(db, $"Conformance {tag}", ownerId, Visibility.Private);
        await db.SaveChangesAsync();

        return new ConditionNode(DocumentFilterFields.Title, FilterOp.Contains, [new TextValue(tag)]);
    }

    public override async Task<FilterNode> SeedIndistinguishableAsync(
        SilexGisDbContext db, Guid ownerId, string tag, int count)
    {
        var stamp = DateTimeOffset.UtcNow;
        for (var i = 0; i < count; i++)
        {
            var row = Add(db, $"Paged {tag}", ownerId, Visibility.Public);
            row.CreatedAt = stamp;
            row.UpdatedAt = stamp;
        }

        await db.SaveChangesAsync();
        return new ConditionNode(DocumentFilterFields.Title, FilterOp.Contains, [new TextValue(tag)]);
    }

    /// <summary>
    /// A document and the revision it serves.
    /// </summary>
    /// <remarks>
    /// A document is three rows, not one: the document, the revision it currently serves, and the
    /// file that revision carries. The integrity check knows all three and scans the whole
    /// database, so a fixture that stops short fails somebody else's test rather than this one.
    /// </remarks>
    private static Document Add(SilexGisDbContext db, string title, Guid ownerId, Visibility visibility)
    {
        var document = new Document
        {
            Title = title,
            OwnerUserId = ownerId,
            Visibility = visibility,
        };
        db.Documents.Add(document);
        var version = new DocumentVersion
        {
            DocumentId = document.Id,
            VersionNumber = 1,
            IsCurrent = true,
            UploadedBy = ownerId,
        };
        db.DocumentVersions.Add(version);
        db.StoredFiles.Add(new StoredFile
        {
            DocumentVersionId = version.Id,
            StoragePath = Guid.NewGuid().ToString("N"),
            OriginalName = title + ".pdf",
            MimeType = "application/pdf",
            SizeBytes = 1,
            Sha256 = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
        });

        return document;
    }
}

/// <summary>A saved map view nobody but its owner may see.</summary>
public sealed class MapViewWorldFixture : WorldFixture
{
    public override string World => MapViewFilterWorld.Key;

    public override async Task<FilterNode> SeedHiddenAsync(SilexGisDbContext db, Guid ownerId, string tag)
    {
        db.MapViews.Add(new MapView
        {
            Name = $"Conformance {tag}",
            OwnerUserId = ownerId,
            Visibility = Visibility.Private,
        });
        await db.SaveChangesAsync();

        return new ConditionNode(CommonFilterFields.Name, FilterOp.Contains, [new TextValue(tag)]);
    }

    public override async Task<FilterNode> SeedIndistinguishableAsync(
        SilexGisDbContext db, Guid ownerId, string tag, int count)
    {
        var stamp = DateTimeOffset.UtcNow;
        for (var i = 0; i < count; i++)
        {
            db.MapViews.Add(new MapView
            {
                Name = $"Paged {tag}",
                OwnerUserId = ownerId,
                Visibility = Visibility.Public,
                CreatedAt = stamp,
                UpdatedAt = stamp,
            });
        }

        await db.SaveChangesAsync();
        return new ConditionNode(CommonFilterFields.Name, FilterOp.Contains, [new TextValue(tag)]);
    }
}

/// <summary>
/// The suite every filterable world passes, or is not shipped.
/// </summary>
/// <remarks>
/// <para>
/// The base class fixes the order in which visibility and a caller's filter are composed, so no
/// world can get that wrong. What it cannot check is whether a world's idea of "what this caller
/// may see" is honest — a world could return its whole table and every structural guarantee would
/// still hold while leaking everything. That is what this file is for.
/// </para>
/// <para>
/// It reads the registry rather than a list of its own, so registering a world is what enrols it.
/// A world registered without a fixture fails <see cref="Every_registered_world_has_a_fixture"/>
/// rather than quietly being exempt, which is the only arrangement where "every world is proved"
/// stays true after somebody adds the tenth one in a hurry.
/// </para>
/// </remarks>
public sealed class FilterWorldConformanceTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private static readonly WorldFixture[] Fixtures =
    [
        new FeatureWorldFixture(),
        new TripLogWorldFixture(),
        new DocumentWorldFixture(),
        new MapViewWorldFixture(),
    ];

    private readonly SilexGisApiFactory factory;
    private string tag = null!;

    public FilterWorldConformanceTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public Task InitializeAsync()
    {
        tag = Guid.NewGuid().ToString("N")[..8];
        return Task.CompletedTask;
    }

    private IReadOnlyList<IFilterWorld> Worlds(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<FilterWorldRegistry>().All;

    [Fact]
    public void Every_registered_world_has_a_fixture()
    {
        using var scope = factory.Services.CreateScope();

        var missing = Worlds(scope).Select(w => w.World)
            .Except(Fixtures.Select(f => f.World))
            .ToList();

        missing.ShouldBeEmpty(
            "A world with no fixture is a world nothing in this file proves anything about. Add "
            + "one that seeds a row only its owner may see.");
    }

    [Fact]
    public void A_world_answers_to_the_key_it_is_registered_under()
    {
        using var scope = factory.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<FilterWorldRegistry>();

        foreach (var world in registry.All)
        {
            registry.Find(world.World).ShouldBeSameAs(world);
        }

        registry.Find("no-such-world").ShouldBeNull();
    }

    // ---------- everything a vocabulary offers actually works ----------

    [Fact]
    public async Task Every_field_a_world_declares_works_with_every_operator_it_admits()
    {
        // A vocabulary is an offer, and this is what stops it from being an empty one. The builder
        // is generated from these descriptors, so a field listed with an operator nothing can
        // compile is a control somebody will use and a request that will fail in their face.
        using var scope = factory.Services.CreateScope();
        var caller = await CallerAsync(GlobalRoles.Viewer, "conf-vocab");

        foreach (var world in Worlds(scope))
        {
            var fixture = Fixtures.Single(f => f.World == world.World);
            var vocabulary = await world.VocabularyAsync(caller, default);

            vocabulary.World.ShouldBe(world.World);
            vocabulary.Fields.ShouldNotBeEmpty();

            foreach (var field in vocabulary.Fields)
            {
                foreach (var op in FilterOps.For(field.Kind))
                {
                    var condition = new ConditionNode(field.Key, op, fixture.ValuesFor(field, op));
                    var query = new WorldQuery(caller, condition, SortKey.Updated, true, null, 0, 5, true);

                    await Should.NotThrowAsync(
                        async () => await world.QueryAsync(query, default),
                        $"{world.World}.{field.Key} does not serve {op}, but its vocabulary offers it.");
                }
            }
        }
    }

    [Fact]
    public async Task Every_sort_a_world_declares_works_in_both_directions()
    {
        using var scope = factory.Services.CreateScope();
        var caller = await CallerAsync(GlobalRoles.Viewer, "conf-sort");

        foreach (var world in Worlds(scope))
        {
            var vocabulary = await world.VocabularyAsync(caller, default);
            vocabulary.Sorts.ShouldNotBeEmpty();

            foreach (var sort in vocabulary.Sorts)
            {
                foreach (var descending in new[] { true, false })
                {
                    var query = new WorldQuery(caller, null, sort, descending, null, 0, 5, false);
                    await Should.NotThrowAsync(
                        async () => await world.QueryAsync(query, default),
                        $"{world.World} declares the {sort} sort but cannot answer it.");
                }
            }
        }
    }

    // ---------- and none of it reaches past what the caller may see ----------

    [Fact]
    public async Task No_world_returns_a_private_row_to_somebody_else()
    {
        // The claim the whole feature stands on, asked of every world at once.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"conf-own-{tag}@t.local");
        var owner = new AccessContext(ownerId, isFullAdmin: false, [], []);
        var outsider = await CallerAsync(GlobalRoles.Viewer, "conf-out");

        foreach (var world in Worlds(scope))
        {
            var fixture = Fixtures.Single(f => f.World == world.World);
            var matching = await fixture.SeedHiddenAsync(db, ownerId, tag);

            var toOwner = await world.QueryAsync(
                new WorldQuery(owner, matching, SortKey.Updated, true, null, 0, 20, true), default);
            var toOutsider = await world.QueryAsync(
                new WorldQuery(outsider, matching, SortKey.Updated, true, null, 0, 20, true), default);

            // Without the owner's side the outsider's empty answer would prove nothing: a filter
            // matching no row anywhere returns nothing too, and looks identical.
            toOwner.Hits.ShouldNotBeEmpty($"{world.World}'s fixture does not match its own row.");
            toOwner.Total.ShouldBe(toOwner.Hits.Count);

            toOutsider.Hits.ShouldBeEmpty($"{world.World} returned a row its caller may not see.");

            // The count is the quieter half. A total taken before the visibility walk would report
            // the row without listing it — which is a disclosure with nothing on screen to show it.
            toOutsider.Total.ShouldBe(0, $"{world.World} counted a row it did not return.");
        }
    }

    [Fact]
    public async Task No_world_returns_a_row_its_own_list_would_withhold()
    {
        // The visibility walk is not the whole rule for every world, and a world that composed only
        // the walk would answer more generously than the screen it stands beside.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"conf-wh-{tag}@t.local");
        var outsider = await CallerAsync(GlobalRoles.Viewer, "conf-whout");

        var exercised = 0;
        foreach (var world in Worlds(scope))
        {
            var fixture = Fixtures.Single(f => f.World == world.World);
            var matching = await fixture.SeedWithheldAsync(db, ownerId, tag);
            if (matching is null)
            {
                continue;
            }

            exercised++;
            var answer = await world.QueryAsync(
                new WorldQuery(outsider, matching, SortKey.Updated, true, null, 0, 20, true), default);

            answer.Hits.ShouldBeEmpty($"{world.World} returned a row its own list withholds.");
            answer.Total.ShouldBe(0, $"{world.World} counted a row its own list withholds.");
        }

        // Said out loud rather than left to a silent pass: a run where no world contributed a
        // fixture proves nothing, and looks exactly like a run where every world passed.
        exercised.ShouldBeGreaterThan(0, "No world exercised the withholding case.");
    }

    [Fact]
    public async Task No_world_puts_a_position_in_a_hit()
    {
        // Structural rather than behavioural: there is nowhere on a hit for coordinates to go, and
        // this asserts that the shape stays that way. How much of a protected position somebody is
        // shown is decided in one place, and a selector answering it separately would be a second.
        typeof(FilterHit).GetProperties()
            .Select(p => p.Name)
            .ShouldBe(["World", "Id", "Title", "Subtitle", "Symbol", "Placeable"], ignoreOrder: true);

        using var scope = factory.Services.CreateScope();
        var caller = await CallerAsync(GlobalRoles.Viewer, "conf-pos");

        foreach (var world in Worlds(scope))
        {
            var page = await world.QueryAsync(
                new WorldQuery(caller, null, SortKey.Updated, true, null, 0, 10, false), default);

            foreach (var hit in page.Hits)
            {
                hit.World.ShouldBe(world.World);
                hit.Title.ShouldNotBeNullOrWhiteSpace($"{world.World} returned a row with nothing to call it.");
            }
        }
    }

    [Fact]
    public async Task Paging_a_world_never_shows_one_row_twice_or_skips_another()
    {
        // Equal sort keys are common — a seeded taxonomy imported in one transaction shares a
        // timestamp to the microsecond — and a database is free to return equal rows in any order.
        // Somebody scrolling sees that as rows appearing twice while others are never shown at all.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"conf-pg-{tag}@t.local");
        var caller = await CallerAsync(GlobalRoles.Viewer, "conf-page");

        foreach (var world in Worlds(scope))
        {
            var fixture = Fixtures.Single(f => f.World == world.World);

            // Seeded rather than taken from whatever happens to be in the database, so the test
            // cannot quietly become vacuous on an installation with three rows in this world.
            var matching = await fixture.SeedIndistinguishableAsync(db, ownerId, tag, 6);
            WorldQuery Page(int skip, int take) =>
                new(caller, matching, SortKey.Updated, true, null, skip, take, true);

            var whole = await world.QueryAsync(Page(0, 12), default);
            whole.Total.ShouldBe(6, $"{world.World}'s paging fixture did not seed six visible rows.");

            var first = await world.QueryAsync(Page(0, 3), default);
            var second = await world.QueryAsync(Page(3, 3), default);

            var paged = first.Hits.Concat(second.Hits).Select(h => h.Id).ToList();
            paged.Distinct().Count().ShouldBe(paged.Count, $"{world.World} returned a row on two pages.");
            paged.ShouldBe([.. whole.Hits.Select(h => h.Id)], $"{world.World} pages unstably.");
        }
    }

    private async Task<AccessContext> CallerAsync(string role, string prefix)
    {
        var id = await AuthHelper.CreateUserAsync(factory, role, $"{prefix}-{tag}@t.local");
        return new AccessContext(id, isFullAdmin: false, [], []);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}
