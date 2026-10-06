// SPDX-License-Identifier: AGPL-3.0-or-later
using Dapper;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Surveys;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Map;

/// <summary>
/// One row of the wall-mesh map query: the one mesh a cave is drawn by, where it stands and how
/// much there is of it. No geometry travels here — a mesh is a file, and this row is what a scene
/// needs in order to decide whether to fetch it.
/// </summary>
public sealed record CaveMeshMapRow(
    Guid CaveId,
    string? CaveName,
    Guid SurveyModelId,
    string ModelName,
    Guid ConvertedFileId,
    double AnchorLongitude,
    double AnchorLatitude,
    double AnchorHeightM,
    int? TriangleCount,
    long SizeBytes);

/// <summary>
/// Dapper SQL for the wall meshes of the caves in a view (raw SQL lives only in *Sql.cs files).
/// <para>
/// Two statements, in the shape the centerline overlay uses and for its reason: the first finds
/// the caves that could answer and is filtered by what the caller may read, the exact-location
/// rule is then evaluated over those ids where that rule lives, and the second describes meshes
/// only for the caves that passed. A mesh's anchor is its cave's exact position, so a cave that
/// did not pass is never described at all.
/// </para>
/// <para>
/// A model counts as having a mesh when a converted file exists AND its anchor is complete. Both
/// are needed: an upload records the declared anchor the moment it arrives, long before the
/// conversion that produces the file has run, so the anchor alone says nothing about whether
/// there is anything to draw.
/// </para>
/// <para>
/// The anchor column carries no spatial index, and the box test below reads the table. That is
/// affordable because the table holds one row per uploaded survey file rather than one per
/// feature, and what is read of each row is a point and a handful of scalars.
/// </para>
/// </summary>
public static class CaveMeshMapSql
{
    /// <summary>
    /// The caves the caller may read that have at least one converted mesh anchored in the box.
    /// A superset of the answer on purpose: which of a cave's meshes is the one it is drawn by is
    /// decided in the second statement, after protection has been asked about these ids.
    /// </summary>
    public static async Task<IReadOnlyList<Guid>> CaveIdsWithMeshInViewAsync(
        SilexGisDbContext db, AccessContext ctx, Bbox box, CancellationToken ct)
    {
        var (visibilitySql, parameters) = AccessSql.FeatureVisibleToFragment(ctx, "f");
        AddBox(parameters, box);

        var sql = $"""
            SELECT DISTINCT f.id
            FROM survey_models m
            JOIN features f ON f.id = m.cave_feature_id
            WHERE m.converted_file_id IS NOT NULL
              AND m.anchor IS NOT NULL
              AND m.anchor_height_m IS NOT NULL
              AND m.anchor && ST_MakeEnvelope(@west, @south, @east, @north, 4326)
              AND f.kind = {(short)FeatureKind.Cave}
              AND f.deleted_at IS NULL
              AND {visibilitySql}
            """;

        var connection = db.Database.GetDbConnection();
        var ids = await connection.QueryAsync<Guid>(new CommandDefinition(sql, parameters, cancellationToken: ct));
        return [.. ids];
    }

    /// <summary>
    /// One mesh per eligible cave — the one a scene would draw for it — for the caves whose chosen
    /// mesh is anchored in the box, nearest the middle of the box first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every qualifying row is returned and the caller applies its cap, rather than a LIMIT here.
    /// The first statement and the protection walk have already visited every candidate, so a
    /// limit would save only the transfer of rows this small, and it would cost the total: the
    /// caller has to be able to say how many caves there were beside the ones it describes.
    /// </para>
    /// </remarks>
    public static async Task<IReadOnlyList<CaveMeshMapRow>> QueryAsync(
        SilexGisDbContext db,
        AccessContext ctx,
        Bbox box,
        IReadOnlyCollection<Guid> exactViewCaveIds,
        CancellationToken ct)
    {
        if (exactViewCaveIds.Count == 0)
        {
            return [];
        }

        var (visibilitySql, parameters) = AccessSql.FeatureVisibleToFragment(ctx, "f");
        AddBox(parameters, box);
        // An allow-list, not a deny-list: this statement runs after the id and protection reads,
        // so a cave created — or newly protected — in between is absent from the list and is not
        // served, where a deny-list would serve it without ever having asked about it.
        parameters.Add("exact_ids", AccessSql.UuidArray([.. exactViewCaveIds]));
        parameters.Add("wall_formats", AccessSql.SmallintArray(Formats(SurveyModelKind.WallMesh)));
        parameters.Add("line_formats", AccessSql.SmallintArray(Formats(SurveyModelKind.LinePlot)));
        // The middle of the box, held inside the globe: a box is parsed, not believed, and the
        // distance below is undefined for a point that is not on the sphere.
        parameters.Add("centre_lon", Math.Clamp((box.West + box.East) / 2d, -180d, 180d));
        parameters.Add("centre_lat", Math.Clamp((box.South + box.North) / 2d, -90d, 90d));

        // The choice is made over ALL of a cave's meshes and the box is applied to the one chosen,
        // in that order. Filtering first would let an old mesh that happens to sit in the box
        // stand in for the current one that does not, and the scene would draw a superseded
        // survey exactly where the corrected one had been moved away from.
        //
        // The preference: the cave's current wall mesh; else its current line plot, where that
        // has been given a mesh; else the newest model that has one. The last arm is what keeps
        // a cave drawn when the mark sits on a model whose conversion has not finished or has
        // failed, which is the ordinary state of a cave for a minute after every re-upload.
        //
        // The cave id settles ties in the final order, so the same view always answers in the
        // same order: a caller that keeps the first few would otherwise swap one mesh for another
        // on every request for a view that had not moved.
        var sql = $"""
            WITH chosen AS (
                SELECT DISTINCT ON (m.cave_feature_id)
                       m.cave_feature_id, m.id, m.name, m.converted_file_id,
                       m.anchor, m.anchor_height_m, m.triangle_count
                FROM survey_models m
                WHERE m.cave_feature_id = ANY(@exact_ids)
                  AND m.converted_file_id IS NOT NULL
                  AND m.anchor IS NOT NULL
                  AND m.anchor_height_m IS NOT NULL
                ORDER BY m.cave_feature_id,
                         CASE WHEN m.is_current AND m.format = ANY(@wall_formats) THEN 0
                              WHEN m.is_current AND m.format = ANY(@line_formats) THEN 1
                              ELSE 2
                         END,
                         m.created_at DESC,
                         m.id DESC
            )
            SELECT c.cave_feature_id AS "CaveId",
                   f.name AS "CaveName",
                   c.id AS "SurveyModelId",
                   c.name AS "ModelName",
                   c.converted_file_id AS "ConvertedFileId",
                   ST_X(c.anchor) AS "AnchorLongitude",
                   ST_Y(c.anchor) AS "AnchorLatitude",
                   c.anchor_height_m AS "AnchorHeightM",
                   c.triangle_count AS "TriangleCount",
                   sf.size_bytes AS "SizeBytes"
            FROM chosen c
            JOIN features f ON f.id = c.cave_feature_id
            JOIN files sf ON sf.id = c.converted_file_id
            WHERE c.anchor && ST_MakeEnvelope(@west, @south, @east, @north, 4326)
              AND f.kind = {(short)FeatureKind.Cave}
              AND f.deleted_at IS NULL
              AND {visibilitySql}
            ORDER BY ST_DistanceSphere(
                         c.anchor, ST_SetSRID(ST_MakePoint(@centre_lon, @centre_lat), 4326)),
                     c.cave_feature_id
            """;

        var connection = db.Database.GetDbConnection();
        var rows = await connection.QueryAsync<CaveMeshMapRow>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));
        return [.. rows];
    }

    private static void AddBox(DynamicParameters parameters, Bbox box)
    {
        parameters.Add("west", box.West);
        parameters.Add("south", box.South);
        parameters.Add("east", box.East);
        parameters.Add("north", box.North);
    }

    /// <summary>The stored format values of one kind of model, from the one place that defines the kinds.</summary>
    private static short[] Formats(SurveyModelKind kind) =>
        [.. SurveyModelKinds.FormatsOf(kind).Select(format => (short)format)];
}
