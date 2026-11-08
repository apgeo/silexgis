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
/// One row of the scan map query: the one placed point cloud a cave is shown by, where its tiles'
/// zero point stands, and how much there is of it. No geometry travels here — the tiles are an
/// archive of files, and this row is what a scene needs in order to decide whether to open it.
/// </summary>
public sealed record CaveScanMapRow(
    Guid CaveId,
    string? CaveName,
    Guid SurveyModelId,
    string ModelName,
    Guid ConvertedFileId,
    double AnchorLongitude,
    double AnchorLatitude,
    double AnchorHeightM,
    double HeadingDeg,
    double PitchDeg,
    double RollDeg,
    long? PointCount,
    long? TiledPointCount,
    long SizeBytes);

/// <summary>
/// Dapper SQL for the placed point clouds of the caves in a view (raw SQL lives only in *Sql.cs
/// files). The same two statements, in the same order and for the same reason, as the wall-mesh
/// listing: the caves that could answer, filtered by what the caller may read; the exact-location
/// rule, evaluated over those ids where that rule lives; then the scans of the caves that passed.
/// A scan's anchor is its cave's exact position, so a cave that did not pass is never described.
/// <para>
/// A scan counts as placed when its anchor is complete — the point and the height both — and as
/// shown when it has a converted file, which for a point cloud is its tileset archive. Both are
/// needed, and today both are written by different steps: the archive by the upload's run, the
/// anchor by a placement that may happen long after, or never. A scan that was tiled and not
/// placed has nothing to stand on the globe, and a placement over a scan whose tiling failed has
/// nothing to draw; neither is in the answer.
/// </para>
/// <para>
/// The point counts are read out of the model's <c>point_cloud</c> document rather than from a
/// column: the document is the one place the facts about a cloud are kept, and reading two of
/// its numbers in the statement that already visits the row costs nothing a column would save.
/// </para>
/// </summary>
public static class CaveScanMapSql
{
    /// <summary>
    /// The caves the caller may read that have at least one tiled, placed point cloud anchored in
    /// the box. A superset of the answer on purpose: which of a cave's scans is the one it is
    /// shown by is decided in the second statement, after protection has been asked about these ids.
    /// </summary>
    public static async Task<IReadOnlyList<Guid>> CaveIdsWithScanInViewAsync(
        SilexGisDbContext db, AccessContext ctx, Bbox box, CancellationToken ct)
    {
        var (visibilitySql, parameters) = AccessSql.FeatureVisibleToFragment(ctx, "f");
        AddBox(parameters, box);
        AddScanFormats(parameters);

        var sql = $"""
            SELECT DISTINCT f.id
            FROM survey_models m
            JOIN features f ON f.id = m.cave_feature_id
            WHERE m.format = ANY(@scan_formats)
              AND m.converted_file_id IS NOT NULL
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
    /// One scan per eligible cave — the one a scene would show for it — for the caves whose chosen
    /// scan is anchored in the box, nearest the middle of the box first.
    /// </summary>
    /// <remarks>
    /// Every qualifying row is returned and the caller applies its cap, rather than a LIMIT here,
    /// so the caller can say how many caves there were beside the ones it describes; the first
    /// statement and the protection walk have already visited every candidate.
    /// </remarks>
    public static async Task<IReadOnlyList<CaveScanMapRow>> QueryAsync(
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
        AddScanFormats(parameters);
        // The middle of the box, held inside the globe: a box is parsed, not believed, and the
        // distance below is undefined for a point that is not on the sphere.
        parameters.Add("centre_lon", Math.Clamp((box.West + box.East) / 2d, -180d, 180d));
        parameters.Add("centre_lat", Math.Clamp((box.South + box.North) / 2d, -90d, 90d));

        // The choice is made over ALL of a cave's placed scans and the box is applied to the one
        // chosen, in that order, so that an older scan that happens to sit in the box cannot
        // stand in for the current one that was moved out of it.
        //
        // The preference: the cave's current point cloud, where it is tiled and placed; else the
        // newest scan that is. The second arm keeps a cave shown while the mark sits on a scan
        // whose tiling has not finished or whose placement has not been made.
        //
        // The cave id settles ties in the final order, so the same view always answers in the
        // same order.
        var sql = $"""
            WITH chosen AS (
                SELECT DISTINCT ON (m.cave_feature_id)
                       m.cave_feature_id, m.id, m.name, m.converted_file_id,
                       m.anchor, m.anchor_height_m, m.placement_heading_deg,
                       m.placement_pitch_deg, m.placement_roll_deg, m.point_cloud
                FROM survey_models m
                WHERE m.cave_feature_id = ANY(@exact_ids)
                  AND m.format = ANY(@scan_formats)
                  AND m.converted_file_id IS NOT NULL
                  AND m.anchor IS NOT NULL
                  AND m.anchor_height_m IS NOT NULL
                ORDER BY m.cave_feature_id,
                         CASE WHEN m.is_current THEN 0 ELSE 1 END,
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
                   COALESCE(c.placement_heading_deg, 0) AS "HeadingDeg",
                   COALESCE(c.placement_pitch_deg, 0) AS "PitchDeg",
                   COALESCE(c.placement_roll_deg, 0) AS "RollDeg",
                   (c.point_cloud->>'pointCount')::bigint AS "PointCount",
                   COALESCE((c.point_cloud->>'displayPointCount')::bigint,
                            (c.point_cloud->>'pointCount')::bigint) AS "TiledPointCount",
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
        var rows = await connection.QueryAsync<CaveScanMapRow>(
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

    /// <summary>
    /// The formats whose converted file is a tileset archive: the point-cloud formats, from the one
    /// place that defines the kinds. Named in both statements so that a wall mesh or a line plot,
    /// anchored the same way, is never handed out as a scan.
    /// </summary>
    private static void AddScanFormats(DynamicParameters parameters) =>
        parameters.Add("scan_formats", AccessSql.SmallintArray(
            [.. SurveyModelKinds.FormatsOf(SurveyModelKind.PointCloud).Select(format => (short)format)]));
}
