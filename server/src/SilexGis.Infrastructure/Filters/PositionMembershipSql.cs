// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Dapper;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Filters;

/// <summary>A picture's current file that answers a spatial question by its stored fix.</summary>
public sealed record PhotoFixCandidate(Guid FileId, Guid DocumentId);

/// <summary>The tables whose rows carry a footprint a position condition is decided on.</summary>
public enum FootprintTable
{
    GeoreferencedMaps = 0,
    Geofiles = 1,
}

/// <summary>
/// The rows of the worlds other than features that answer a spatial question, each by the position
/// its reader is shown — the test itself is <see cref="SpatialConditionSql.Test"/>, so "within" and
/// "inside" mean one thing everywhere.
/// </summary>
/// <remarks>
/// <para>
/// Neither statement decides who may see a row. The ids they answer are only ever intersected with
/// a world's own visible set, so a row the reader cannot see is in no answer however it is placed.
/// </para>
/// <para>
/// A picture's fix is a candidate here and nothing more: whether this reader is shown it is decided
/// per picture by what the picture hangs on, which a statement cannot carry, so every candidate goes
/// through the photograph disclosure rule before it may match. A footprint needs no such step: the
/// worlds that read it return only rows whose footprint their lists show the reader whole.
/// </para>
/// </remarks>
public static class PositionMembershipSql
{
    /// <summary>The pictures whose current fix answers the question, before the disclosure rule.</summary>
    public static async Task<List<PhotoFixCandidate>> PhotoFixesAsync(
        SilexGisDbContext db, SpatialAsk ask, CancellationToken ct)
    {
        var (sql, parameters) = BuildPhotoFixes(ask);
        var rows = await db.Database.GetDbConnection().QueryAsync<PhotoFixCandidate>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));
        return [.. rows];
    }

    /// <summary>The statement and its parameters, exposed so a test can pin its shape.</summary>
    public static (string Sql, DynamicParameters Parameters) BuildPhotoFixes(SpatialAsk ask)
    {
        var parameters = new DynamicParameters();
        SpatialConditionSql.AddReach(parameters, ask);
        var test = SpatialConditionSql.Test(ask, parameters);

        // The picture kind is a constant, written into the statement like the feature kinds are, so
        // the planner can use the files table's index over kind.
        var image = ((short)FileKind.Image).ToString(CultureInfo.InvariantCulture);
        var sql = $"""
            SELECT sf.id AS "FileId", v.document_id AS "DocumentId"
            FROM files sf
            JOIN document_versions v ON v.id = sf.document_version_id
            JOIN documents d ON d.id = v.document_id
            WHERE v.is_current
              AND d.deleted_at IS NULL
              AND sf.kind = {image}
              AND sf.geom IS NOT NULL
              AND sf.geom && ST_MakeEnvelope(@sc_west, @sc_south, @sc_east, @sc_north, 4326)
              AND {test("sf.geom")}
            """;
        return (sql, parameters);
    }

    /// <summary>The rows whose footprint answers the question.</summary>
    public static async Task<List<Guid>> FootprintsAsync(
        SilexGisDbContext db, FootprintTable table, SpatialAsk ask, CancellationToken ct)
    {
        var (sql, parameters) = BuildFootprints(table, ask);
        var ids = await db.Database.GetDbConnection().QueryAsync<Guid>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));
        return [.. ids];
    }

    /// <summary>The statement and its parameters, exposed so a test can pin its shape.</summary>
    public static (string Sql, DynamicParameters Parameters) BuildFootprints(FootprintTable table, SpatialAsk ask)
    {
        var parameters = new DynamicParameters();
        SpatialConditionSql.AddReach(parameters, ask);
        var test = SpatialConditionSql.Test(ask, parameters);

        // A closed set of names, never caller text.
        var name = table switch
        {
            FootprintTable.GeoreferencedMaps => "georeferenced_maps",
            FootprintTable.Geofiles => "geofiles",
            _ => throw new ArgumentOutOfRangeException(nameof(table)),
        };
        var sql = $"""
            SELECT r.id
            FROM {name} r
            WHERE r.bbox IS NOT NULL
              AND r.bbox && ST_MakeEnvelope(@sc_west, @sc_south, @sc_east, @sc_north, 4326)
              AND {test("r.bbox")}
            """;
        return (sql, parameters);
    }
}
