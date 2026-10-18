// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.TripTracking;

/// <summary>
/// The stations of the survey a trip was followed in, with their depths, for the rest of the
/// application.
/// </summary>
/// <remarks>
/// <para>
/// <b>Which survey.</b> The one the trip's watch was set on, and no other. A cave can hold several
/// surveys and a trip can touch several caves; the survey the party was placed against is the only
/// one whose station names mean anything in that trip's journal, which is what this list is read
/// beside.
/// </para>
/// <para>
/// <b>What is decided here, and by whose rule.</b> One question, once: whether this reading may be
/// told where the cave is, asked of the same rule the survey's own station search is gated by. The
/// list is one disclosure — a cave's whole station vocabulary — so it is told or withheld whole,
/// and a reading that is refused is told that it was rather than handed an empty list. Which it
/// cannot learn from that: a reader of a trip whose watch was set on a cave they may not place is
/// already told that its positions are withheld, on the trip's own screen and in its journal.
/// </para>
/// <para>
/// <b>Why the rows come from the depth placement's own loader.</b> The station altitudes and the
/// datum rule are what turns a station into a depth, and they already have one home, read by the
/// surface that places a reported depth. A second reading of the same two things is how a sheet
/// printed from a trip comes to disagree with the screen the trip was followed on.
/// </para>
/// </remarks>
public sealed class TripSurveyStationReads(
    SilexGisDbContext db,
    IAccessService access,
    FeatureProtection protection) : ITripSurveyStations
{
    /// <summary>
    /// The most stations one list carries.
    /// </summary>
    /// <remarks>
    /// A document somebody prints and carries, not the survey file: a cave of any size holds
    /// thousands of stations, and a sheet of six hundred pages is a sheet nobody takes
    /// underground. Past this the list says how many it left out, rather than growing without
    /// bound or stopping without a word.
    /// </remarks>
    public const int MaxStations = 300;

    /// <inheritdoc />
    public async Task<TripSurveyStationList?> ReadAsync(
        Guid tripLogId, AccessContext reading, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reading);

        // The ordinary set, so a deleted trip's watch is hidden with it and answers as a trip that
        // was never followed.
        var tracking = await db.TripTrackings.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TripLogId == tripLogId, ct);
        if (tracking?.SurveyModelId is not { } surveyModelId)
        {
            return null;
        }

        var model = await db.SurveyModels.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == surveyModelId, ct);
        if (model is null)
        {
            return null;
        }

        // The cave the survey belongs to answers for it, exactly as it does for the survey's own
        // endpoints: the watch's own note of which cave it was set on is a snapshot of that and is
        // not what rights are asked about.
        var cave = await db.Features.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == model.CaveFeatureId && f.Kind == FeatureKind.Cave, ct);
        if (cave is null)
        {
            return null;
        }

        if (!await SurveyModelAccess.VisibleAsync(access, protection, reading, cave, ct))
        {
            return new TripSurveyStationList(Shown: false, Datum: null, Stations: [], Total: 0);
        }

        var stations = await TripTrackingEndpoints.StationsOfAsync(db, model, ct);

        // A station the file gives no name and the viewer gives no label can be printed nowhere and
        // looked up by nobody; it is left out here as it is left out of every place somebody is
        // reported at, and so is not counted among what the list could have held.
        var listable = stations.Where(station => !station.NoViewerLabel).ToList();
        if (listable.Count == 0)
        {
            return null;
        }

        // The datum the trip's own depths are measured from, and failing that the survey's own:
        // a watch that names a station this survey does not hold is a misconfiguration, and the
        // answer to it on a printed sheet is the survey's highest entrance rather than a list of
        // stations with no depths on it.
        var datumStation = DatumOf(listable, tracking.ReferenceStationName);
        var datumZ = datumStation?.Z;
        var datum = datumStation?.ViewerName;

        var listed = listable
            // Shallowest first: the order somebody walking in meets them, and the order a sheet is
            // read down. Ties by name, so that one survey always prints in one order.
            .OrderBy(station => datumZ is { } from ? from - station.Z : 0)
            .ThenBy(station => station.ViewerName, StringComparer.Ordinal)
            .Take(MaxStations)
            .Select(station => new TripSurveyStation(
                station.ViewerName,
                datumZ is { } from
                    ? TripTrackingRules.RecordedDepthM((decimal)(from - station.Z))
                    : null,
                station.IsEntrance))
            .ToList();

        return new TripSurveyStationList(Shown: true, datum, listed, listable.Count);
    }

    /// <summary>
    /// The station every printed depth is measured below: the one the watch names, matched under
    /// either of its spellings, and failing that the survey's own datum — its highest entrance, or
    /// its highest station where it flags none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The station itself is carried out of here, not only its altitude, because the sheet prints
    /// its name beside the depths. Two stations of one survey sharing an altitude exactly is
    /// ordinary — survey files store rounded altitudes and a horizontal passage levels out — so
    /// recovering the name afterwards by searching back for that altitude would print whichever of
    /// them came first in a row order nothing declares: a name that need not be the station the
    /// depths are measured from, and need not even be the same in two downloads of one trip.
    /// </para>
    /// <para>
    /// Which altitude is the datum stays the one rule the tracking surface places reported depths
    /// by; what is settled here is only which station carries it, and between equally good ones by
    /// name, so that one survey always prints one datum.
    /// </para>
    /// </remarks>
    private static TrackingDepthResolver.Station? DatumOf(
        IReadOnlyList<TrackingDepthResolver.Station> stations, string? referenceStationName)
    {
        if (referenceStationName is not null
            && FirstByName(stations.Where(station =>
                string.Equals(station.Name, referenceStationName, StringComparison.Ordinal)
                || string.Equals(station.ViewerName, referenceStationName, StringComparison.Ordinal)))
                is { } named)
        {
            return named;
        }

        if (TrackingDepthResolver.ReferenceZ(stations, null) is not { } surface)
        {
            return null;
        }

        return FirstByName(stations.Where(station => station.IsEntrance && station.Z == surface))
            ?? FirstByName(stations.Where(station => station.Z == surface));
    }

    /// <summary>
    /// The first of a set of equally good datum stations by name, or null for an empty set. By
    /// name rather than by position, because the position is whatever order the rows arrived in.
    /// </summary>
    private static TrackingDepthResolver.Station? FirstByName(
        IEnumerable<TrackingDepthResolver.Station> candidates)
    {
        TrackingDepthResolver.Station? best = null;
        foreach (var candidate in candidates)
        {
            if (best is not { } chosen
                || string.CompareOrdinal(candidate.ViewerName, chosen.ViewerName) < 0)
            {
                best = candidate;
            }
        }

        return best;
    }
}
