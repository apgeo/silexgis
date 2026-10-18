// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Access;

namespace SilexGis.Api.Common;

/// <summary>
/// The stations of the survey a trip was followed in, with their depths, for a part of the
/// application that is not the tracking surface itself.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is an interface here.</b> A feature area does not reach into another's internals.
/// Something outside tracking that wants the station list — a trip's write-up is the first — asks
/// this, and the tracking area answers it with the very rows its own depth placement reads. So the
/// names a reader is given here are the names that surface would give the same reader, by
/// construction rather than by a second copy of the rule.
/// </para>
/// <para>
/// <b>Why it is guarded at all, and by which rule.</b> A station is a cave coordinate as much as
/// the cave's own point is, and a whole list of them is the cave's entire station vocabulary
/// leaving in one piece — a far larger disclosure than the one name a report carries. So the
/// answer states stations only for a reading that may both read the cave and place it exactly:
/// the rule the station search itself is gated by, asked here in one place so the two cannot come
/// apart. A reading that is refused is told so in so many words, because a list that simply came
/// back empty would read as a cave with no survey.
/// </para>
/// <para>
/// <b>What it deliberately does not decide.</b> Whether the trip may be read at all. The caller
/// has already found that out, and under a reading that may differ from this one: a document kept
/// on a trip is made because somebody who may write the trip asked for it, and says only what
/// every reader of the trip may be told.
/// </para>
/// </remarks>
public interface ITripSurveyStations
{
    /// <summary>
    /// The station list for one trip as <paramref name="reading"/> may be told it, or null when
    /// there is no list to speak of — a trip that was never followed, one whose watch names no
    /// survey, a survey with no stations, and a trip that has been deleted or was never there.
    /// </summary>
    /// <param name="tripLogId">The trip.</param>
    /// <param name="reading">
    /// Whose rights decide whether the stations are stated. Asked once, for the whole list: the
    /// list is one disclosure and is told or withheld whole.
    /// </param>
    /// <param name="ct">Cancels the read.</param>
    Task<TripSurveyStationList?> ReadAsync(
        Guid tripLogId, AccessContext reading, CancellationToken ct);
}

/// <summary>
/// The stations of one trip's survey as one reader may be told them.
/// </summary>
/// <param name="Shown">
/// Whether this reader is told the stations. False is a reader who may not be told where the cave
/// is: <paramref name="Stations"/> is then empty, <paramref name="Datum"/> null and
/// <paramref name="Total"/> zero, and nothing about the survey is carried at all. It is a separate
/// answer from an empty list on purpose, so that "this cave's survey holds nothing" and "you are
/// not told this cave's survey" are never printed as one thing.
/// </param>
/// <param name="Datum">
/// The name of the station the depths are measured below, or null when the survey holds nothing to
/// measure from. In the spelling the reader sees stations under elsewhere.
/// </param>
/// <param name="Stations">
/// The stations, shallowest first, up to the most one list carries. Empty for a withheld list.
/// </param>
/// <param name="Total">
/// How many stations the survey holds that could have been listed, so that a list cut short can
/// say how much it left out. Zero for a withheld list.
/// </param>
public sealed record TripSurveyStationList(
    bool Shown,
    string? Datum,
    IReadOnlyList<TripSurveyStation> Stations,
    int Total);

/// <summary>
/// One station of a trip's survey, as a printed record states it.
/// </summary>
/// <param name="Name">
/// The station, in the spelling the reader sees it under on every other surface — which for one of
/// the line-plot formats is not the spelling the survey rows hold.
/// </param>
/// <param name="DepthM">
/// How far it lies below the datum, in metres, positive down — the convention every depth in this
/// application is stated in. Null when the survey holds no datum to measure from.
/// </param>
/// <param name="IsEntrance">Whether the survey marks it as an entrance.</param>
public sealed record TripSurveyStation(string Name, decimal? DepthM, bool IsEntrance);
