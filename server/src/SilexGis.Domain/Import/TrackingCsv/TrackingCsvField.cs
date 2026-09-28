// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Import.TrackingCsv;

/// <summary>
/// A column role a sheet of tracking reports can carry.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here is resolved against the database: a caver is a name as somebody wrote it, a
/// station is a string, and a depth is whatever the sheet said. Turning those into reports on a
/// trip's log is a later and separate act, exactly as it is for the trip sheet next door.
/// </para>
/// <para>
/// One row is one moment for one or more people — a party that reached a place reached it together
/// — which is why <see cref="Cavers"/> is multi-valued while everything else on the row is not.
/// </para>
/// </remarks>
public enum TrackingCsvField
{
    /// <summary>When the report was made, as the reporter gave it. Date and time.</summary>
    RecordedAt,

    /// <summary>
    /// The depth the reporter gave, in metres. Turned into a station by the trip's own datum and
    /// filter, or by the cave's declared station for that depth where it has one.
    /// </summary>
    Depth,

    /// <summary>
    /// A station named outright. Where a row carries both this and a depth, this one wins: a
    /// station is what somebody read off the survey, and a depth is a number that has to be
    /// guessed into one.
    /// </summary>
    Station,

    /// <summary>Who the report is about. Multi-valued — one row can be a whole party.</summary>
    Cavers,

    /// <summary>
    /// Which party they were with. Indicative only: a report belongs to the people on it, and a
    /// team is the grouping shown beside them.
    /// </summary>
    Team,

    /// <summary>The reporter's own words about the moment.</summary>
    Note,

    /// <summary>A second free-text column some sheets carry beside the first.</summary>
    Details,

    /// <summary>
    /// Whether this row is somebody going in or coming out, rather than a place inside.
    /// </summary>
    /// <remarks>
    /// Its own column rather than a depth of zero, because the two say different things and a
    /// sheet has both: a row at the entrance is a place, and a row saying "intrare" is a standing.
    /// The words it is written in are configured, not guessed — see
    /// <see cref="TrackingCsvStateWords"/>.
    /// </remarks>
    State,
}
