// SPDX-License-Identifier: AGPL-3.0-or-later
using Dapper;
using Npgsql;
using Shouldly;
using SilexGis.Api.Common;
using SilexGis.Api.Tests.Support;
using SilexGis.Infrastructure.Geodata;

namespace SilexGis.Api.Tests;

/// <summary>
/// What the database actually does when a stored geometry is moved into the installation's
/// working system — asserted against a real PostGIS rather than assumed.
///
/// <para>
/// Every one of these is a property later work depends on and would otherwise take on faith. The
/// altitude one in particular: a cave passage is a three-dimensional object, the whole reason a
/// projected system is introduced is to measure it, and if the transform quietly rewrote altitudes
/// the resulting depths would be wrong by an amount nobody would think to question.
/// </para>
/// </summary>
public sealed class WorkingSridBehaviourTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly int WorkingSrid = new SpatialOptions().WorkingSrid;

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    [Fact]
    public async Task The_working_system_is_one_the_database_also_knows()
    {
        // The application validates the code against its own projection library at startup. The
        // database carries a separate definition table, and a code known to one and not the other
        // would fail only when a query ran.
        await using var connection = await OpenAsync();

        var known = await connection.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM spatial_ref_sys WHERE srid = @srid", new { srid = WorkingSrid });

        known.ShouldBe(1);
    }

    [Fact]
    public async Task Altitude_passes_through_the_transform_untouched()
    {
        // A projected transform moves the plan position and leaves the height alone. Depths and
        // overburden are computed from that height, so this is load-bearing for everything the
        // cave-to-surface work will later say.
        await using var connection = await OpenAsync();

        var z = await connection.ExecuteScalarAsync<double>(
            $"SELECT ST_Z(ST_Transform(ST_SetSRID(ST_MakePoint(25.0, 45.5, -123.75), 4326), {WorkingSrid}))");

        z.ShouldBe(-123.75, tolerance: 1e-9);
    }

    [Fact]
    public async Task A_round_trip_returns_the_point_it_started_from()
    {
        await using var connection = await OpenAsync();

        var metresApart = await connection.ExecuteScalarAsync<double>(
            $"""
            SELECT ST_Distance(
                original::geography,
                ST_Transform(ST_Transform(original, {WorkingSrid}), 4326)::geography)
            FROM (SELECT ST_SetSRID(ST_MakePoint(25.0, 45.5, -123.75), 4326) AS original) s
            """);

        // Sub-millimetre. Anything larger would mean the working system is not appropriate for
        // this installation's area, which is the failure this test exists to catch when somebody
        // sets a zone that does not cover them.
        metresApart.ShouldBeLessThan(0.001);
    }

    [Fact]
    public async Task Distance_in_the_working_system_agrees_with_the_distance_on_the_spheroid()
    {
        // The two ways of asking for metres have to agree, because the code uses whichever is
        // cheaper for the question at hand: the spheroid answer for a plain proximity filter that
        // the stored index can serve, and the projected one where three dimensions are involved.
        // If they disagreed, a pre-filter would be silently discarding real answers.
        await using var connection = await OpenAsync();

        var (projected, geodesic) = await connection.QuerySingleAsync<(double Projected, double Geodesic)>(
            $"""
            SELECT
                ST_Distance(ST_Transform(a, {WorkingSrid}), ST_Transform(b, {WorkingSrid})) AS "Projected",
                ST_Distance(a::geography, b::geography) AS "Geodesic"
            FROM (SELECT
                    ST_SetSRID(ST_MakePoint(25.00, 45.50), 4326) AS a,
                    ST_SetSRID(ST_MakePoint(25.02, 45.51), 4326) AS b) s
            """);

        geodesic.ShouldBeGreaterThan(1000);
        // Within a metre over roughly a kilometre and a half — projection distortion, not error.
        projected.ShouldBe(geodesic, tolerance: 1.0);
    }

    [Fact]
    public async Task Three_dimensional_distance_needs_the_projected_system_to_mean_anything()
    {
        // The argument for having a working system at all, as a number. The same two points, one
        // metre apart vertically and a little apart horizontally: measured in the stored degrees
        // the answer is nonsense, because it adds degrees to metres.
        await using var connection = await OpenAsync();

        var (stored, working) = await connection.QuerySingleAsync<(double Stored, double Working)>(
            $"""
            SELECT
                ST_3DDistance(a, b) AS "Stored",
                ST_3DDistance(ST_Transform(a, {WorkingSrid}), ST_Transform(b, {WorkingSrid})) AS "Working"
            FROM (SELECT
                    ST_SetSRID(ST_MakePoint(25.000, 45.500, 0), 4326) AS a,
                    ST_SetSRID(ST_MakePoint(25.001, 45.500, 40), 4326) AS b) s
            """);

        // In the stored system the vertical 40 metres dominates two ordinates measured in degrees,
        // so the "distance" is essentially just the height difference: plainly not a distance.
        stored.ShouldBe(40, tolerance: 0.01);

        // In the working system all three ordinates are metres, so the answer is the real one:
        // about 78 m horizontally and 40 m down.
        working.ShouldBeGreaterThan(80);
        working.ShouldBeLessThan(95);
    }
}
