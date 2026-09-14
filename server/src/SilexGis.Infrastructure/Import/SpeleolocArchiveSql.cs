// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Infrastructure.Import;

/// <summary>
/// Every statement the device archive is read with, in one place, as this codebase keeps raw SQL.
///
/// <para>
/// This is somebody else's schema and it is read-only: the connection is opened read-only, the
/// session is put in query-only mode, and nothing here writes. The one parameter any of these
/// takes is a recording's identifier, and it is bound rather than pasted — an identifier arrives
/// from a request body, and a 16-byte value that came from a browser is a value like any other.
/// </para>
/// <para>
/// Soft deletes are excluded everywhere. The device marks rows deleted rather than removing them,
/// so a query that forgets the filter imports scans somebody deleted on the phone — which is the
/// one kind of wrongness nobody reviewing the list would be able to spot.
/// </para>
/// <para>
/// Every statement that returns rows is bounded, and the bound is the caller's rather than the
/// file's. The reader materialises what comes back before anything pages it — that is what a review
/// of one recording <em>is</em> — so an unbounded statement here makes the size of one request the
/// archive's choice, and an archive is a file somebody uploaded. The reader asks for one row past
/// its ceiling so that being at the ceiling and being past it are different answers.
/// </para>
/// </summary>
internal static class SpeleolocArchiveSql
{
    /// <summary>
    /// The tables this import needs to exist before the file is treated as a device archive. A
    /// SQLite database is a file format, not a schema: a stranger's database opens perfectly and
    /// then answers "no such table" from four different places.
    /// </summary>
    public static readonly string[] RequiredTables =
        ["caves", "cave_places", "cave_trips", "cave_trip_points", "documentation_files_to_cave_trips"];

    /// <summary>Which of the named tables the database actually holds.</summary>
    public const string PresentTables =
        "select name from sqlite_master where type = 'table'";

    /// <summary>
    /// The recordings, newest first, with the cave each was made in and how many documents were
    /// filed under it. The document count is a count and nothing else: a recording's attachments
    /// are somebody's photographs and notes, and this import has no business opening them.
    /// </summary>
    public const string Trips = """
        select
            hex(t.uuid)                    as trip_uuid,
            hex(t.cave_uuid)               as cave_uuid,
            t.title                        as title,
            t.description                  as description,
            t.log                           as log,
            cast(t.trip_started_at as text) as started_at,
            cast(t.trip_ended_at as text)   as ended_at,
            hex(t.created_by_user_uuid)     as device_user_uuid,
            c.title                         as cave_title,
            (
                select count(*)
                from documentation_files_to_cave_trips d
                where d.cave_trip_uuid = t.uuid and d.deleted_at is null
            )                               as document_count
        from cave_trips t
        left join caves c on c.uuid = t.cave_uuid and c.deleted_at is null
        where t.deleted_at is null
        order by t.trip_started_at desc, t.title
        limit @limit
        """;

    /// <summary>How many scans each recording holds, in one pass rather than one query per recording.</summary>
    public const string PointCounts = """
        select hex(cave_trip_uuid) as trip_uuid, count(*) as point_count
        from cave_trip_points
        where deleted_at is null
        group by cave_trip_uuid
        limit @limit
        """;

    /// <summary>
    /// One recording's scans in the order they were made, each with whatever the place it names
    /// records about itself. The place is joined rather than looked up per point because a scan
    /// without its place is a row nobody can review, and a thousand round trips to answer that is
    /// a thousand round trips.
    ///
    /// <para>
    /// Every value crosses out as text or as an integer this application chose the width of.
    /// Depth is read as text on purpose: the device's column is declared <c>NUMERIC(7,2)</c>,
    /// SQLite ignores the declaration and stores whatever it was given, and a reader that trusted
    /// the declared type would turn −87.4 m into −87 m without saying so.
    /// </para>
    /// </summary>
    public const string PointsOfTrip = """
        select
            hex(p.uuid)                       as point_uuid,
            hex(p.cave_place_uuid)            as place_uuid,
            cast(p.scanned_at as text)        as scanned_at,
            p.notes                            as notes,
            hex(p.created_by_user_uuid)        as device_user_uuid,
            pl.title                           as place_title,
            cast(pl.depth_in_cave as text)     as place_depth,
            pl.is_entrance                     as place_is_entrance
        from cave_trip_points p
        left join cave_places pl on pl.uuid = p.cave_place_uuid and pl.deleted_at is null
        where p.cave_trip_uuid = @trip and p.deleted_at is null
        order by p.scanned_at, hex(p.uuid)
        limit @limit
        """;
}
