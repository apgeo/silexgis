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
/// The compiler against a real database.
///
/// <para>
/// The design rests on one claim — every condition a validated filter can hold translates to SQL,
/// and there is no in-memory fallback. That claim is only worth anything if something executes each
/// one, because an expression the provider cannot translate does not fail at compile time and, in
/// the versions that still allowed it, would answer correctly while reading the whole table.
/// </para>
/// <para>
/// The second half of the file is the more important one: a filter must never be able to widen the
/// set of rows a caller may see. Composing the caller's predicate around the visibility walk is
/// what guarantees that, and these cases are what stop a later refactor from reversing the order.
/// </para>
/// </summary>
public sealed class FeatureFilterCompilerTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private string tag = null!;

    public FeatureFilterCompilerTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public Task InitializeAsync()
    {
        tag = Guid.NewGuid().ToString("N")[..8];
        return Task.CompletedTask;
    }

    /// <summary>
    /// The rows the whole-table questions are asked about.
    ///
    /// <para>
    /// They used to be answered by whatever the rest of the suite had left in a shared database,
    /// which meant the assertions were about somebody else's rows and passed for reasons this class
    /// had no say in. Now the class owns its database, so a corpus it did not create is an empty
    /// one, and "is this column ever empty" over zero rows is true of nothing. The mix matters as
    /// much as the count: the type is the one identity column that is genuinely nullable, so the
    /// corpus carries rows both with and without it, or the last assertion is trivially satisfied.
    /// </para>
    /// </summary>
    private async Task SeedCorpusAsync()
    {
        var ownerId = await AuthHelper.CreateUserAsync(
            factory, GlobalRoles.Editor, $"fc-corpus-{tag}@t.local");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var typeId = await db.FeatureTypes.AsNoTracking().Select(t => t.Id).FirstAsync();

        Rootless(db, typeId, ownerId, $"Corpus typed public {tag}", Visibility.Public);
        Rootless(db, typeId, ownerId, $"Corpus typed private {tag}");
        // The schema pairs the two: ck_features_generic_type says a row is Generic exactly when it
        // carries a type. So the untyped row in the corpus has to be a kind that names itself.
        Rootless(db, null, ownerId, $"Corpus untyped {tag}", kind: FeatureKind.Cave);
        await db.SaveChangesAsync();
    }

    // ---------- every condition reaches the database ----------

    public static TheoryData<string, FilterOp, FilterValue[]> EveryLeaf()
    {
        var now = DateTimeOffset.UtcNow;
        var data = new TheoryData<string, FilterOp, FilterValue[]>
        {
            { FeatureFilterFields.Name, FilterOp.Contains, [new TextValue("urs")] },
            { FeatureFilterFields.Name, FilterOp.StartsWith, [new TextValue("Pe")] },
            { FeatureFilterFields.Name, FilterOp.Equals, [new TextValue("Ursilor")] },
            { FeatureFilterFields.Name, FilterOp.IsEmpty, [] },
            { FeatureFilterFields.Name, FilterOp.IsNotEmpty, [] },
            { FeatureFilterFields.Kind, FilterOp.Equals, [new IdValue("cave")] },
            { FeatureFilterFields.Kind, FilterOp.In, [new IdValue("cave"), new IdValue("generic")] },
            { FeatureFilterFields.Category, FilterOp.Equals, [new IdValue("surface")] },
            { FeatureFilterFields.TypeId, FilterOp.In, [new IdValue("1"), new IdValue("2")] },
            { FeatureFilterFields.TypeId, FilterOp.IsEmpty, [] },
            { FeatureFilterFields.Tag, FilterOp.In, [new IdValue("survey")] },
            { FeatureFilterFields.Tag, FilterOp.IsEmpty, [] },
            { FeatureFilterFields.Tag, FilterOp.IsNotEmpty, [] },
            { FeatureFilterFields.OwnerId, FilterOp.Equals, [new IdValue(Guid.Empty.ToString())] },
            { FeatureFilterFields.OwnerId, FilterOp.IsEmpty, [] },
            { FeatureFilterFields.CavingGroupId, FilterOp.IsNotEmpty, [] },
            { FeatureFilterFields.Visibility, FilterOp.In, [new IdValue("private"), new IdValue("public")] },
            { FeatureFilterFields.LocationProtected, FilterOp.Equals, [new BooleanValue(true)] },
            { FeatureFilterFields.CreatedAt, FilterOp.GreaterThan, [new InstantValue(now.AddYears(-1))] },
            { FeatureFilterFields.UpdatedAt, FilterOp.Between, [new InstantValue(now.AddYears(-1)), new InstantValue(now)] },
            { FeatureFilterFields.UpdatedAt, FilterOp.LessThan, [new InstantValue(now)] },
            // The typed-property arms, which nothing in this repository exercised before. The
            // presence pair matters most: it is the only place a function other than containment
            // reaches the stored document, and containment being translatable said nothing about it.
            { $"{FeatureFilterFields.PropertyPrefix}sinkhole:depth_m", FilterOp.Equals, [new NumberValue(4.5)] },
            { $"{FeatureFilterFields.PropertyPrefix}sinkhole:label", FilterOp.Equals, [new TextValue("x")] },
            { $"{FeatureFilterFields.PropertyPrefix}sinkhole:wet", FilterOp.Equals, [new BooleanValue(true)] },
            { $"{FeatureFilterFields.PropertyPrefix}sinkhole:depth_m", FilterOp.IsEmpty, [] },
            { $"{FeatureFilterFields.PropertyPrefix}sinkhole:depth_m", FilterOp.IsNotEmpty, [] },
        };

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryLeaf))]
    public async Task Every_condition_a_validated_filter_can_hold_runs_on_the_database(
        string field, FilterOp op, FilterValue[] values)
    {
        // Executed rather than merely built: an expression the provider cannot translate throws
        // here, which is the only place the "no in-memory fallback" claim can actually be tested.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var compiler = new FeatureFilterCompiler(db);

        var predicate = compiler.Compile(new ConditionNode(field, op, values));

        await Should.NotThrowAsync(async () =>
            await db.Features.AsNoTracking().Where(predicate).Take(1).ToListAsync());
    }

    [Fact]
    public async Task The_boolean_shapes_compose_into_one_translatable_expression()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var compiler = new FeatureFilterCompiler(db);

        // The shape the owner asked for: more than one AND-group, with a negation inside it.
        var tree = new AnyOfNode([
            new AllOfNode([
                new ConditionNode(FeatureFilterFields.Kind, FilterOp.Equals, [new IdValue("cave")]),
                new ConditionNode(FeatureFilterFields.Name, FilterOp.Contains, [new TextValue("urs")]),
            ]),
            new AllOfNode([
                new ConditionNode(FeatureFilterFields.Tag, FilterOp.In, [new IdValue("survey")]),
                new NotNode(new ConditionNode(
                    FeatureFilterFields.LocationProtected, FilterOp.Equals, [new BooleanValue(true)])),
            ]),
        ]);

        var predicate = compiler.Compile(tree);

        await Should.NotThrowAsync(async () =>
            await db.Features.AsNoTracking().Where(predicate).Take(1).ToListAsync());
    }

    [Fact]
    public async Task A_property_condition_tells_the_number_apart_from_the_text_that_looks_like_it()
    {
        // The reason a value carries its type from the browser all the way down. A depth recorded
        // as 4.5 and one recorded as "4.5" are different records — a survey that treats them as the
        // same finds sinkholes it should not, and a person filtering for a depth range gets rows
        // whose depth is a label. Containment is what keeps them apart, and this is where that is
        // proved rather than assumed.
        var ownerId = await AuthHelper.CreateUserAsync(
            factory, GlobalRoles.Editor, $"fc-prop-{tag}@t.local");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var typeId = await db.FeatureTypes.AsNoTracking().Select(t => t.Id).FirstAsync();

        Rootless(db, typeId, ownerId, $"Numeric {tag}", properties: """{"depth_m":4.5}""");
        Rootless(db, typeId, ownerId, $"Textual {tag}", properties: """{"depth_m":"4.5"}""");
        Rootless(db, typeId, ownerId, $"Silent {tag}", properties: """{"other":1}""");
        await db.SaveChangesAsync();

        var compiler = new FeatureFilterCompiler(db);
        var field = $"{FeatureFilterFields.PropertyPrefix}sinkhole:depth_m";

        async Task<List<string>> Matching(FilterOp op, FilterValue[] values)
        {
            var predicate = compiler.Compile(new ConditionNode(field, op, values));
            return await db.Features.AsNoTracking()
                .Where(predicate)
                .Where(f => f.Name!.EndsWith(tag))
                .Select(f => f.Name!)
                .ToListAsync();
        }

        (await Matching(FilterOp.Equals, [new NumberValue(4.5)]))
            .ShouldBe([$"Numeric {tag}"]);
        (await Matching(FilterOp.Equals, [new TextValue("4.5")]))
            .ShouldBe([$"Textual {tag}"]);

        // Presence answers about the key regardless of what is under it, and says nothing about
        // the row that never recorded a depth.
        (await Matching(FilterOp.IsNotEmpty, [])).Order()
            .ShouldBe([$"Numeric {tag}", $"Textual {tag}"]);
        (await Matching(FilterOp.IsEmpty, []))
            .ShouldBe([$"Silent {tag}"]);
    }

    [Fact]
    public async Task Asking_whether_a_column_that_is_never_empty_is_empty_gets_a_straight_answer()
    {
        await SeedCorpusAsync();

        // The vocabulary offers the emptiness pair on every identity field, so a person can pick it
        // for a feature's kind. The identity leaf reads "no values given" as "matches nothing" —
        // right for a multi-select somebody cleared, wrong for the two operators where no values is
        // what they mean. Left alone, "kind is not empty" returned nothing at all and its negation
        // returned everything, which is a filter answering the opposite of what it was asked.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var compiler = new FeatureFilterCompiler(db);

        var all = await db.Features.AsNoTracking().CountAsync();
        all.ShouldBeGreaterThan(0, "The corpus has to hold something for either answer to mean anything.");

        async Task<int> CountAsync(string field, FilterOp op) => await db.Features.AsNoTracking()
            .Where(compiler.Compile(new ConditionNode(field, op, [])))
            .CountAsync();

        foreach (var field in new[]
        {
            FeatureFilterFields.Kind, FeatureFilterFields.Category, FeatureFilterFields.Visibility,
        })
        {
            (await CountAsync(field, FilterOp.IsNotEmpty)).ShouldBe(all, $"{field} is never empty.");
            (await CountAsync(field, FilterOp.IsEmpty)).ShouldBe(0, $"{field} is never empty.");
        }

        // A column that genuinely can be empty still answers about itself rather than about nothing.
        (await CountAsync(FeatureFilterFields.TypeId, FilterOp.IsEmpty)
         + await CountAsync(FeatureFilterFields.TypeId, FilterOp.IsNotEmpty)).ShouldBe(all);
    }

    [Fact]
    public async Task An_empty_filter_matches_everything_rather_than_being_a_special_case()
    {
        await SeedCorpusAsync();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var compiler = new FeatureFilterCompiler(db);

        var all = await db.Features.AsNoTracking().CountAsync();
        var filtered = await db.Features.AsNoTracking().Where(compiler.Compile(null)).CountAsync();

        filtered.ShouldBe(all);
    }

    [Fact]
    public async Task A_spatial_condition_whose_anchor_resolved_to_nothing_matches_no_row()
    {
        // Not "everything", which is what an unresolved condition would silently become if the
        // absent case were treated as "no constraint". An anchor the caller may not place is a
        // real answer — nothing — and it must not widen the result.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var compiler = new FeatureFilterCompiler(db);

        var condition = new ConditionNode(
            FeatureFilterFields.Position, FilterOp.Within, [new NumberValue(200)]);

        var count = await db.Features.AsNoTracking().Where(compiler.Compile(condition)).CountAsync();

        count.ShouldBe(0);
    }

    // ---------- a filter can never widen what a caller may see ----------

    [Fact]
    public async Task A_filter_matching_a_private_row_still_does_not_return_it_to_an_outsider()
    {
        // The whole protection claim in one case. The caller's predicate matches the row exactly;
        // the visibility walk is composed around it; the row does not come back. If the order were
        // ever reversed, or the filter applied to the unfiltered set, this is what would notice.
        var ownerId = await AuthHelper.CreateUserAsync(
            factory, GlobalRoles.Editor, $"fc-owner-{tag}@t.local");
        var outsiderId = await AuthHelper.CreateUserAsync(
            factory, GlobalRoles.Viewer, $"fc-out-{tag}@t.local");

        var name = $"Secret {tag}";
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        // A generic feature carries a type — the schema insists, because a surface feature with
        // no type is a symbol nothing knows how to draw.
        var typeId = await db.FeatureTypes.AsNoTracking().Select(t => t.Id).FirstAsync();
        Rootless(db, typeId, ownerId, name);
        await db.SaveChangesAsync();

        var compiler = new FeatureFilterCompiler(db);
        var predicate = compiler.Compile(
            new ConditionNode(FeatureFilterFields.Name, FilterOp.Contains, [new TextValue(tag)]));

        var owner = new AccessContext(ownerId, isFullAdmin: false, [], []);
        var outsider = new AccessContext(outsiderId, isFullAdmin: false, [], []);

        var toOwner = await db.Features.AsNoTracking()
            .VisibleTo(owner, db.Features, db.FeatureSetMembers)
            .Where(predicate)
            .CountAsync();
        var toOutsider = await db.Features.AsNoTracking()
            .VisibleTo(outsider, db.Features, db.FeatureSetMembers)
            .Where(predicate)
            .CountAsync();

        // The owner proves the filter genuinely matches — without which the outsider's zero would
        // be meaningless, since a filter matching nothing would also return nothing.
        toOwner.ShouldBe(1);
        toOutsider.ShouldBe(0);
    }

    [Fact]
    public async Task A_negated_condition_cannot_reach_around_the_visibility_walk()
    {
        // Negation is the shape most likely to invert a set by accident: "not named X" over the
        // whole table would include rows the caller may not read. Composed around the walk, it
        // cannot — the complement is taken inside what they may already see.
        var ownerId = await AuthHelper.CreateUserAsync(
            factory, GlobalRoles.Editor, $"fc-nowner-{tag}@t.local");
        var outsiderId = await AuthHelper.CreateUserAsync(
            factory, GlobalRoles.Viewer, $"fc-nout-{tag}@t.local");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var typeId = await db.FeatureTypes.AsNoTracking().Select(t => t.Id).FirstAsync();
        Rootless(db, typeId, ownerId, $"Hidden {tag}");
        await db.SaveChangesAsync();

        var compiler = new FeatureFilterCompiler(db);
        var negated = compiler.Compile(new NotNode(
            new ConditionNode(FeatureFilterFields.Name, FilterOp.Contains, [new TextValue("zzz-no-match")])));

        var visibleToOutsider = await db.Features.AsNoTracking()
            .VisibleTo(new AccessContext(outsiderId, isFullAdmin: false, [], []), db.Features, db.FeatureSetMembers)
            .Where(negated)
            .Select(f => f.Name)
            .ToListAsync();

        // The negation matches every row in the database; the walk is what keeps the private one out.
        visibleToOutsider.ShouldNotContain($"Hidden {tag}");
    }

    /// <summary>
    /// A feature with no parent, stamped the way the write service stamps one.
    /// </summary>
    /// <remarks>
    /// A rootless feature is its own ancestor, and that fact lives in two places kept in step: the
    /// array on the row, which the visibility walk reads, and the closure table, which the integrity
    /// check compares against the hierarchy edges. A fixture that sets neither leaves every row it
    /// creates looking corrupt to a check that scans the whole database — which is somebody else's
    /// test failing for a reason that has nothing to do with them.
    /// </remarks>
    private static Feature Rootless(
        SilexGisDbContext db,
        long? typeId,
        Guid ownerId,
        string name,
        Visibility visibility = Visibility.Private,
        string properties = "{}",
        FeatureKind kind = FeatureKind.Generic)
    {
        var id = Guid.NewGuid();
        var feature = new Feature
        {
            Id = id,
            Name = name,
            Kind = kind,
            FeatureTypeId = typeId,
            OwnerUserId = ownerId,
            Visibility = visibility,
            AncestorIds = [id],
            Properties = properties,
        };

        db.Features.Add(feature);
        db.FeatureAncestors.Add(new FeatureAncestor { FeatureId = id, AncestorId = id });
        return feature;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}
