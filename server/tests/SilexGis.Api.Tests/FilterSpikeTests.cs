// SPDX-License-Identifier: AGPL-3.0-or-later
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The three assumptions the shared feature filter rests on, each proved against a real PostGIS
/// rather than assumed.
/// </summary>
/// <remarks>
/// These are load-bearing rather than exploratory. Nothing in this repository queries a jsonb
/// column today, so whether a containment predicate reaches the database as <c>@&gt;</c> — and can
/// therefore use an index — is genuinely unknown; and the whole spatial half of the filter depends
/// on a distance predicate composing with the visibility and exact-view fragments inside one
/// statement. A design that guessed wrong about either would be found out late and expensively.
/// <para>
/// They stay in the suite afterwards. Each pins a fact about the database and the driver, not about
/// this project's own code, which is exactly the kind of fact that changes silently under a package
/// upgrade.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class FilterSpikeTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;

    public FilterSpikeTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_jsonb_containment_predicate_reaches_the_database_as_the_containment_operator()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        // The column is a string in the model and jsonb in the database. What matters is whether
        // the provider translates containment at all: in memory it would still answer correctly
        // while scanning every row, which is the failure that hides until the table is large.
        var sql = db.Features
            .Where(f => EF.Functions.JsonContains(f.Properties, """{"depth_m":4.5}"""))
            .Select(f => f.Id)
            .ToQueryString();

        sql.ShouldContain("@>", Case.Sensitive);
    }

    [Fact]
    public async Task A_jsonb_containment_predicate_selects_the_right_rows()
    {
        // Translation is not correctness. Containment on a nested value has to mean what the
        // condition builder will claim it means.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var connection = db.Database.GetDbConnection();

        var matching = await connection.QuerySingleAsync<bool>(
            "select @doc::jsonb @> @probe::jsonb",
            new { doc = """{"depth_m": 4.5, "diameter_m": 10}""", probe = """{"depth_m": 4.5}""" });
        matching.ShouldBeTrue();

        var different = await connection.QuerySingleAsync<bool>(
            "select @doc::jsonb @> @probe::jsonb",
            new { doc = """{"depth_m": 4.5}""", probe = """{"depth_m": 9}""" });
        different.ShouldBeFalse();

        // A number written as a string is not the same value. The builder must therefore emit
        // typed JSON rather than quoting everything, and this is where that is settled.
        var quoted = await connection.QuerySingleAsync<bool>(
            "select @doc::jsonb @> @probe::jsonb",
            new { doc = """{"depth_m": 4.5}""", probe = """{"depth_m": "4.5"}""" });
        quoted.ShouldBeFalse();
    }

    [Fact]
    public async Task A_gin_index_on_the_properties_column_serves_a_containment_predicate()
    {
        // The index the design proposes, proved to be the one the planner picks. Built here and
        // dropped again: this asserts a fact about PostgreSQL, not about the migration.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var connection = db.Database.GetDbConnection();

        await connection.ExecuteAsync(
            "create index if not exists ix_spike_features_properties on features using gin (properties jsonb_path_ops);");
        try
        {
            // The planner will not choose an index on a tiny table, so ask it not to consider the
            // alternative. What is being proved is that the index is *usable* for this predicate.
            var plan = string.Join(
                '\n',
                await connection.QueryAsync<string>(
                    """
                    set local enable_seqscan = off;
                    explain select id from features where properties @> '{"depth_m": 4.5}'::jsonb;
                    """));

            plan.ShouldContain("ix_spike_features_properties");
        }
        finally
        {
            await connection.ExecuteAsync("drop index if exists ix_spike_features_properties;");
        }
    }

    [Fact]
    public async Task A_distance_predicate_composes_with_the_visibility_and_exact_view_fragments()
    {
        // The whole spatial half of the filter rests on this: one statement in which a caller's
        // visibility, their right to place a row exactly, and a geographic distance are all
        // applied together. If these could not compose, the precise predicates would have to run
        // over a materialised pool and the design would change shape entirely.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var connection = db.Database.GetDbConnection();

        var ctx = new AccessContext(Guid.CreateVersion7(), isFullAdmin: false, [], []);
        var (visibleSql, exactSql, parameters) = AccessSql.FeatureLayerFragments(ctx, "f");

        parameters.Add("anchor_lon", 25.5);
        parameters.Add("anchor_lat", 45.6);
        parameters.Add("radius_m", 250d);

        var sql = $"""
            select f.id
            from features f
            where f.deleted_at is null
              and f.geom is not null
              and st_dwithin(
                    f.geom::geography,
                    st_setsrid(st_makepoint(@anchor_lon, @anchor_lat), 4326)::geography,
                    @radius_m)
              and ({visibleSql})
              and ({exactSql})
            limit 10;
            """;

        // Executed rather than merely planned: a fragment that does not compose fails here with a
        // syntax or ambiguity error, which is precisely what this is asked to rule out.
        var rows = await connection.QueryAsync<Guid>(sql, parameters);

        rows.ShouldNotBeNull();
    }

    [Fact]
    public async Task The_exact_view_fragment_still_agrees_with_the_rule_it_mirrors()
    {
        // Not new: the parity between the SQL fragment and the in-memory rule is already pinned
        // elsewhere. Asserted here because this design newly depends on that parity holding for a
        // predicate the fragment was not written for.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var connection = db.Database.GetDbConnection();

        var ctx = new AccessContext(Guid.CreateVersion7(), isFullAdmin: true, [], []);
        var (_, exactSql, parameters) = AccessSql.FeatureLayerFragments(ctx, "f");

        // A full administrator places everything; the fragment has to say so on its own, without
        // any row to test it against.
        var everything = await connection.QuerySingleAsync<bool>(
            $"select ({exactSql}) from (select null::uuid as id, null::uuid as owner_user_id, "
            + "null::uuid as caving_group_id, 0::smallint as visibility, false as is_protected_effective, "
            + "null::uuid[] as ancestor_ids) f",
            parameters);

        everything.ShouldBeTrue();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}
