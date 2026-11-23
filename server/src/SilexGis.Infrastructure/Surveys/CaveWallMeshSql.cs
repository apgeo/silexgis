// SPDX-License-Identifier: AGPL-3.0-or-later
using Dapper;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Surveys;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Surveys;

/// <summary>
/// The one wall mesh a cave is drawn by: which file, where its zero point stands and how large it
/// is to read.
/// </summary>
public sealed record CaveWallMesh(
    Guid CaveId,
    Guid SurveyModelId,
    Guid ConvertedFileId,
    string StoragePath,
    double AnchorLongitude,
    double AnchorLatitude,
    double AnchorHeightM,
    long SizeBytes);

/// <summary>
/// Which of a cave's survey models is the mesh it is drawn by, and that mesh for named caves (raw
/// SQL lives only in *Sql.cs files).
///
/// <para>
/// <b>The choice has one home.</b> A cave can hold several models with a drawable mesh — an
/// uploaded wall mesh, a line plot given walls from its own measurements, an older generation of
/// either — and everything that draws "the cave's walls" has to pick the same one, or two pictures
/// of one cave show two surveys. The preference: the cave's current wall mesh; else its current
/// line plot, where that has been given a mesh; else the newest model that has one. The last arm
/// keeps a cave drawn while the mark sits on a model whose conversion has not finished or has
/// failed, the ordinary state of a cave for a minute after every re-upload.
/// </para>
///
/// <para>
/// A model counts as having a mesh when a converted file exists AND its anchor is complete: an
/// upload records the declared anchor the moment it arrives, long before the conversion that
/// produces the file has run, so the anchor alone says nothing about whether there is anything to
/// draw. Only wall meshes and line plots count — a point cloud's converted file is a tileset
/// archive, anchored the same way.
/// </para>
/// </summary>
public static class CaveWallMeshSql
{
    /// <summary>
    /// The body of a common table expression choosing one mesh per cave among the caves in the
    /// uuid-array parameter named, in columns <c>cave_feature_id, id, name, converted_file_id,
    /// anchor, anchor_height_m, triangle_count</c>. Its format lists are bound by
    /// <see cref="AddFormats"/>.
    /// </summary>
    /// <remarks>
    /// The choice is made over ALL of a cave's meshes; a caller that also has a window applies it to
    /// the one chosen, in that order. Filtering first would let an old mesh that happens to sit in
    /// the window stand in for the current one that does not.
    /// </remarks>
    public static string Chosen(string caveIdsParameter) => $"""
        SELECT DISTINCT ON (m.cave_feature_id)
               m.cave_feature_id, m.id, m.name, m.converted_file_id,
               m.anchor, m.anchor_height_m, m.triangle_count
        FROM survey_models m
        WHERE m.cave_feature_id = ANY({caveIdsParameter})
          AND m.format = ANY(@mesh_formats)
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
        """;

    /// <summary>Binds the format lists <see cref="Chosen"/> names.</summary>
    public static void AddFormats(DynamicParameters parameters)
    {
        parameters.Add("wall_formats", AccessSql.SmallintArray(Formats(SurveyModelKind.WallMesh)));
        parameters.Add("line_formats", AccessSql.SmallintArray(Formats(SurveyModelKind.LinePlot)));
        AddMeshFormats(parameters);
    }

    /// <summary>
    /// Binds <c>@mesh_formats</c> alone: the formats whose converted file is a mesh a scene can
    /// draw, for a statement that only asks whether a model has one.
    /// </summary>
    public static void AddMeshFormats(DynamicParameters parameters) =>
        parameters.Add("mesh_formats", AccessSql.SmallintArray(
            [.. Formats(SurveyModelKind.WallMesh), .. Formats(SurveyModelKind.LinePlot)]));

    /// <summary>
    /// The mesh each of the named caves is drawn by, for the caves that have one.
    /// </summary>
    /// <remarks>
    /// The caller has already decided which caves it may describe; the statement still repeats both
    /// halves of that decision — readable, and exactly placeable — on the rows it is about to read
    /// an anchor from. A cave hidden or newly protected between the caller's decision and this
    /// statement is then simply absent, where trusting the list would describe it.
    /// </remarks>
    public static async Task<IReadOnlyList<CaveWallMesh>> ForCavesAsync(
        SilexGisDbContext db, AccessContext ctx, IReadOnlyCollection<Guid> caveIds, CancellationToken ct)
    {
        if (caveIds.Count == 0)
        {
            return [];
        }

        var (visibleSql, exactSql, parameters) = AccessSql.FeatureLayerFragments(ctx, "f");
        parameters.Add("wall_cave_ids", AccessSql.UuidArray([.. caveIds]));
        AddFormats(parameters);

        // The feature kind is interpolated as the number it is so the planner sees a constant; it
        // is a compile-time enum value, never anything a caller supplies.
        var sql = $"""
            WITH chosen AS (
                {Chosen("@wall_cave_ids")}
            )
            SELECT c.cave_feature_id AS "CaveId",
                   c.id AS "SurveyModelId",
                   c.converted_file_id AS "ConvertedFileId",
                   sf.storage_path AS "StoragePath",
                   ST_X(c.anchor) AS "AnchorLongitude",
                   ST_Y(c.anchor) AS "AnchorLatitude",
                   c.anchor_height_m AS "AnchorHeightM",
                   sf.size_bytes AS "SizeBytes"
            FROM chosen c
            JOIN features f ON f.id = c.cave_feature_id
            JOIN files sf ON sf.id = c.converted_file_id
            WHERE f.kind = {(short)FeatureKind.Cave}
              AND f.deleted_at IS NULL
              AND {visibleSql}
              AND {exactSql}
            ORDER BY c.cave_feature_id
            """;

        var connection = db.Database.GetDbConnection();
        var rows = await connection.QueryAsync<CaveWallMesh>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));
        return [.. rows];
    }

    /// <summary>The stored format values of one kind of model, from the one place that defines the kinds.</summary>
    private static short[] Formats(SurveyModelKind kind) =>
        [.. SurveyModelKinds.FormatsOf(kind).Select(format => (short)format)];
}
