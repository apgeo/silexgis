// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Import;

namespace SilexGis.Domain.Trips;

/// <summary>Why a trip made from a phone recording needs a person to look at it.</summary>
public static class TripAttentionReasons
{
    /// <summary>A scan whose place gives no single station: no depth, no model, nothing near, or a tie.</summary>
    public const string UnresolvedStation = "unresolved_station";

    /// <summary>A scan by a phone user who is mapped to no caver, or by nobody the phone named.</summary>
    public const string UnmappedPerson = "unmapped_person";

    /// <summary>A scan at no place, or at a place this server does not hold (any more) as a marked place.</summary>
    public const string PlaceUnknown = "place_unknown";

    /// <summary>A scan stamped later than the server's clock allows, which no position may be.</summary>
    public const string RecordedInFuture = "recorded_in_future";

    /// <summary>The recording changed after its trip was made from it; the trip was not rewritten.</summary>
    public const string RecordingChangedAfterConversion = "recording_changed_after_conversion";

    /// <summary>The order reasons are listed in, so two conversions of the same recording write the same row.</summary>
    public static IReadOnlyList<string> Order { get; } =
        [PlaceUnknown, UnresolvedStation, UnmappedPerson, RecordedInFuture, RecordingChangedAfterConversion];
}

/// <summary>One reason, with how many scans (or, for a change after conversion, how many changes) it covers.</summary>
/// <param name="PointIds">The phone's identifiers of the scans concerned, never a station or a place.</param>
public sealed record TripAttentionReason(string Code, int Count, IReadOnlyList<string> PointIds);

/// <summary>What the automatic conversion makes of one scan.</summary>
/// <param name="StationName">The station in the viewer's spelling when exactly one fits; otherwise null.</param>
/// <param name="Reasons">Empty when the scan becomes a position; otherwise every reason it does not.</param>
public sealed record TripRecordingScanVerdict(
    string PointId, Guid? CaverId, string? StationName, IReadOnlyList<string> Reasons)
{
    public bool Recordable => Reasons.Count == 0;
}

/// <summary>
/// The rules by which a recording a phone sent becomes a trip report with nobody choosing anything:
/// what counts as unambiguous, and how what is not is recorded.
/// </summary>
/// <remarks>
/// <para>
/// The station candidates themselves come from the archive import's resolver, the single home of
/// the rule that turns a marked place's depth into stations; these rules only decide whether its
/// answer for a scan is a single answer. A reviewer on the archive import is shown the nearest
/// stations and may pick any; a conversion with no reviewer may take a station only when it is the
/// one nearest the place's depth with nothing else as near. Two stations at the same distance —
/// two branches of a survey sharing a horizon, the case the reviewer exists for — are a tie, and a
/// tie is not settled by a name's sort order.
/// </para>
/// <para>
/// Nearness is compared at the tenth of a metre the resolver rounds to. That is the precision the
/// reviewer is shown, and a difference finer than it is not a difference anybody measured.
/// </para>
/// </remarks>
public static class TripRecordingConversionRules
{
    /// <summary>How many candidates to ask the resolver for: enough to see whether the nearest is alone.</summary>
    public const int CandidatesAsked = 2;

    /// <summary>The one station nearest the depth, or null when there is none or the nearest is tied.</summary>
    public static string? SoleStation(IReadOnlyList<SpeleolocStationCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        return candidates.Count switch
        {
            0 => null,
            1 => candidates[0].StationName,
            _ => candidates[0].DeltaM < candidates[1].DeltaM ? candidates[0].StationName : null,
        };
    }

    /// <summary>What the conversion makes of one scan the resolver has read.</summary>
    public static TripRecordingScanVerdict Judge(SpeleolocPointResolution resolution, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        var reasons = new List<string>(2);
        string? station = null;
        switch (resolution.State)
        {
            case SpeleolocPointState.NoPlace:
            case SpeleolocPointState.PlaceUnknown:
                reasons.Add(TripAttentionReasons.PlaceUnknown);
                break;
            case SpeleolocPointState.Proposed:
                station = SoleStation(resolution.Candidates);
                if (station is null)
                {
                    reasons.Add(TripAttentionReasons.UnresolvedStation);
                }

                break;
            default:
                reasons.Add(TripAttentionReasons.UnresolvedStation);
                break;
        }

        if (resolution.CaverId is null)
        {
            reasons.Add(TripAttentionReasons.UnmappedPerson);
        }

        if (TripTrackingRules.MomentIsInFuture(resolution.ScannedAt, now))
        {
            reasons.Add(TripAttentionReasons.RecordedInFuture);
        }

        return new TripRecordingScanVerdict(resolution.PointId, resolution.CaverId, station, reasons);
    }

    /// <summary>The reasons a set of scans gives, one entry per reason, in the fixed order.</summary>
    public static IReadOnlyList<TripAttentionReason> Collect(IEnumerable<TripRecordingScanVerdict> verdicts)
    {
        ArgumentNullException.ThrowIfNull(verdicts);

        var byCode = verdicts
            .SelectMany(v => v.Reasons.Select(r => (Code: r, v.PointId)))
            .GroupBy(x => x.Code, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.PointId).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToList(),
                StringComparer.Ordinal);

        return [.. TripAttentionReasons.Order
            .Where(byCode.ContainsKey)
            .Select(code => new TripAttentionReason(code, byCode[code].Count, byCode[code]))];
    }

    /// <summary>
    /// The reason a change to an already converted recording adds: one more change, naming the scans
    /// the change touched (none when only the recording's own fields changed).
    /// </summary>
    public static TripAttentionReason ChangedAfterConversion(IEnumerable<string> changedPointIds) =>
        new(
            TripAttentionReasons.RecordingChangedAfterConversion,
            1,
            [.. changedPointIds.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal)]);

    /// <summary>
    /// The reasons once <paramref name="raised"/> is added to <paramref name="existing"/>. Scans are
    /// named once per reason however often they are raised; a change after conversion counts
    /// changes, so each one adds to the count.
    /// </summary>
    public static IReadOnlyList<TripAttentionReason> Merge(
        IReadOnlyList<TripAttentionReason> existing, IReadOnlyList<TripAttentionReason> raised)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(raised);

        var merged = new List<TripAttentionReason>();
        var codes = existing.Concat(raised).Select(r => r.Code).Distinct(StringComparer.Ordinal).ToList();
        foreach (var code in TripAttentionReasons.Order.Where(codes.Contains)
                     .Concat(codes.Where(c => !TripAttentionReasons.Order.Contains(c)).Order(StringComparer.Ordinal)))
        {
            var both = existing.Concat(raised).Where(r => r.Code == code).ToList();
            var points = both.SelectMany(r => r.PointIds)
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToList();
            var count = code == TripAttentionReasons.RecordingChangedAfterConversion
                ? both.Sum(r => r.Count)
                : points.Count;
            merged.Add(new TripAttentionReason(code, count, points));
        }

        return merged;
    }

    /// <summary>The reasons as the row stores them.</summary>
    public static string Serialize(IReadOnlyList<TripAttentionReason> reasons) => ImportJson.Serialize(reasons);

    /// <summary>The reasons a row stores; an unreadable value reads as none rather than failing the trip.</summary>
    public static IReadOnlyList<TripAttentionReason> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return ImportJson.Deserialize<List<TripAttentionReason>>(json) ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// A scan's note as a position carries it. A position's note is shorter than a scan's may be;
    /// the scan keeps every word on its recording, so the position takes the beginning rather than
    /// the scan being refused over its length.
    /// </summary>
    public static string? NoteOf(string? notes)
    {
        var note = notes?.Trim();
        if (string.IsNullOrEmpty(note))
        {
            return null;
        }

        return note.Length <= TripTrackingRules.MaxNoteLength ? note : note[..TripTrackingRules.MaxNoteLength].TrimEnd();
    }
}
