// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.TripLogs;

/// <summary>What a listing is broken into slices by.</summary>
/// <remarks>
/// The word is the one the answer spells the dimension with, and — for every dimension that is
/// also a facet — a group's value is the word that facet's own parameter takes. That is what lets
/// a reader go from a slice to the trips in it without anything translating anything: the group
/// is already the filter.
/// </remarks>
internal enum TripGroupDimension
{
    None = 0,
    Year,
    Type,
    State,
    Visibility,
    Incident,
    Area,
    Participant,
}

/// <summary>
/// Slices a trip listing, twice over, and says what each slice holds.
/// </summary>
/// <remarks>
/// <para>
/// The rows are read through the same composition the page and the option counts are — the
/// audience walk and every narrowing already in the query — and the arithmetic is done over what
/// came back. Nothing is filtered after the read, so a slice is a count of what this caller may
/// read and two callers legitimately see different figures for the same slice.
/// </para>
/// <para>
/// Two of the dimensions hold more than one value per trip: a trip names several areas and
/// carries several people. A trip counts into every value it holds, so the slice counts add up to
/// more than the number of trips, and that is the right answer to "how many trips did each caver
/// do" rather than an error. The answer says so with its own flag rather than leaving the reader
/// to notice, because a total that quietly exceeds the population is the one figure nobody
/// double-checks.
/// </para>
/// <para>
/// Every count over the roster is distinct by person and every count over the trip roles is
/// distinct by trip: a roster row is one person doing one job, so leading and surveying is two
/// rows and one person; and a role names a feature once per link and per role, so one trip can
/// name one area twice. Both are carried by tests rather than by the schema.
/// </para>
/// </remarks>
internal static class TripLogGrouping
{
    /// <summary>
    /// How many trips are sliced. A grouping is a shape somebody reads at a glance, so it is
    /// bounded rather than streamed: past this the answer says it was cut short and the reader
    /// narrows the filter, which is the honest response to a question too big to answer.
    /// </summary>
    public const int MaxGroupedTrips = 2000;

    /// <summary>How many types and people a slice names before it stops.</summary>
    private const int TopValuesPerGroup = 3;

    /// <summary>How many slices come back per level, longest first.</summary>
    private const int MaxGroups = 40;

    /// <summary>A group value for the trips that hold nothing at all under a dimension.</summary>
    public const string Unassigned = "";

    private static readonly Dictionary<string, TripGroupDimension> Words =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["none"] = TripGroupDimension.None,
            ["year"] = TripGroupDimension.Year,
            ["type"] = TripGroupDimension.Type,
            ["state"] = TripGroupDimension.State,
            ["visibility"] = TripGroupDimension.Visibility,
            ["incident"] = TripGroupDimension.Incident,
            ["area"] = TripGroupDimension.Area,
            ["participant"] = TripGroupDimension.Participant,
        };

    public static bool TryParseDimension(string? word, out TripGroupDimension dimension)
    {
        dimension = TripGroupDimension.None;
        return string.IsNullOrWhiteSpace(word) || Words.TryGetValue(word.Trim(), out dimension);
    }

    /// <summary>The word an answer spells a dimension with, which is the word it was asked by.</summary>
    public static string WordOf(TripGroupDimension dimension) =>
        JsonNamingPolicy.CamelCase.ConvertName(dimension.ToString());

    /// <summary>A trip counts into every value it holds under this dimension.</summary>
    public static bool IsMultiValued(TripGroupDimension dimension) =>
        dimension is TripGroupDimension.Area or TripGroupDimension.Participant;

    private sealed record Row(
        Guid Id,
        DateOnly TripDate,
        DateOnly? TripDateEnd,
        long? TripTypeId,
        ActivityState State,
        Visibility Visibility,
        bool HadIncident);

    public static async Task<TripListGroupingDto> BuildAsync(
        SilexGisDbContext db,
        FeatureProtection protection,
        AccessContext ctx,
        UserContext user,
        TripLogListing listing,
        TripGroupDimension primary,
        TripGroupDimension secondary,
        CancellationToken ct)
    {
        var narrowed = listing.Narrowed();

        // One more than the bound, so the answer can say it was cut short without a second count.
        var rows = await listing.Ordered(narrowed)
            .Select(x => new Row(
                x.Id, x.TripDate, x.TripDateEnd, x.TripTypeId, x.State, x.Visibility, x.HadIncident))
            .Take(MaxGroupedTrips + 1)
            .ToListAsync(ct);

        var truncated = rows.Count > MaxGroupedTrips;
        if (truncated)
        {
            rows = [.. rows.Take(MaxGroupedTrips)];
        }

        var tripIds = rows.Select(x => x.Id).ToList();

        // Distinct by person and by trip in the statement: the roster holds one row per job, so
        // somebody who led and surveyed one trip is one person who went once.
        var roster = await db.TripLogParticipants.AsNoTracking()
            .Where(p => tripIds.Contains(p.TripLogId))
            .Select(p => new { p.TripLogId, p.CaverId })
            .Distinct()
            .ToListAsync(ct);
        var peopleOfTrip = roster
            .GroupBy(x => x.TripLogId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)[.. g.Select(x => x.CaverId)]);
        var caverLabels = await CaverDirectory.ResolveLabelsAsync(
            db, user, roster.Select(x => x.CaverId), ct);

        var areasOfTrip = new Dictionary<Guid, IReadOnlyList<Guid>>();
        var areaNames = new Dictionary<Guid, string>();
        if (primary == TripGroupDimension.Area || secondary == TripGroupDimension.Area)
        {
            // The same walk the area narrowing and the area counts make, so a slice saying four
            // opens onto four trips: an area a trip reached is the area it named and every area
            // the containment hierarchy puts that one inside, each of them gated by the audience
            // and by the position rule. Sliced any other way, a massif was labelled with the trips
            // that named it directly and then filtered to every trip that named anything in it.
            var reach = await TripAreaReach.BuildAsync(db, protection, ctx, narrowed.Select(x => x.Id), ct);
            areaNames = reach.Names.ToDictionary(entry => entry.Key, entry => entry.Value ?? string.Empty);
            areasOfTrip = reach.AreasOfTrip
                .Where(entry => tripIds.Contains(entry.Key))
                .ToDictionary(entry => entry.Key, entry => entry.Value);
        }

        var groups = Slice(rows, primary, secondary, peopleOfTrip, areasOfTrip, areaNames, caverLabels);

        return new TripListGroupingDto(
            WordOf(primary),
            WordOf(secondary),
            rows.Count,
            IsMultiValued(primary) || IsMultiValued(secondary),
            truncated,
            groups);
    }

    private static IReadOnlyList<TripGroupDto> Slice(
        IReadOnlyList<Row> rows,
        TripGroupDimension dimension,
        TripGroupDimension then,
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> peopleOfTrip,
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> areasOfTrip,
        IReadOnlyDictionary<Guid, string> areaNames,
        IReadOnlyDictionary<Guid, string> caverLabels)
    {
        if (dimension == TripGroupDimension.None || rows.Count == 0)
        {
            return [];
        }

        var buckets = new Dictionary<string, List<Row>>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            foreach (var value in ValuesOf(row, dimension, peopleOfTrip, areasOfTrip))
            {
                if (!buckets.TryGetValue(value, out var bucket))
                {
                    bucket = [];
                    buckets[value] = bucket;
                }

                bucket.Add(row);
            }
        }

        return
        [
            .. buckets
                // The trips holding nothing under this dimension are a slice like any other and
                // are shown, because a grouping that silently dropped them would not add up to
                // the listing. It sorts last however big it is: it is the absence of an answer,
                // not the most popular one.
                .OrderBy(pair => pair.Key.Length == 0)
                .ThenByDescending(pair => pair.Value.Count)
                .ThenBy(pair => LabelOf(pair.Key, dimension, areaNames, caverLabels), StringComparer.CurrentCulture)
                .Take(MaxGroups)
                .Select(pair => new TripGroupDto(
                    pair.Key,
                    LabelOf(pair.Key, dimension, areaNames, caverLabels),
                    pair.Value.Count,
                    pair.Value.Min(x => x.TripDate),
                    pair.Value.Max(x => x.TripDateEnd ?? x.TripDate),
                    TopTypes(pair.Value),
                    TopPeople(pair.Value, peopleOfTrip, caverLabels),
                    Slice(pair.Value, then, TripGroupDimension.None, peopleOfTrip, areasOfTrip, areaNames, caverLabels))),
        ];
    }

    private static IEnumerable<string> ValuesOf(
        Row row,
        TripGroupDimension dimension,
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> peopleOfTrip,
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> areasOfTrip)
    {
        switch (dimension)
        {
            case TripGroupDimension.Year:
                // The year the trip started. A span that crosses midnight on New Year's Eve is
                // one trip and belongs to one slice, so the window's overlap reading — which is
                // what finds it from either side — is deliberately not repeated here.
                return [row.TripDate.Year.ToString()];
            case TripGroupDimension.Type:
                return [row.TripTypeId?.ToString() ?? Unassigned];
            case TripGroupDimension.State:
                return [JsonNamingPolicy.CamelCase.ConvertName(row.State.ToString())];
            case TripGroupDimension.Visibility:
                return [JsonNamingPolicy.CamelCase.ConvertName(row.Visibility.ToString())];
            case TripGroupDimension.Incident:
                return [row.HadIncident ? "true" : "false"];
            case TripGroupDimension.Area:
                return Spread(areasOfTrip.GetValueOrDefault(row.Id));
            case TripGroupDimension.Participant:
                return Spread(peopleOfTrip.GetValueOrDefault(row.Id));
            default:
                return [];
        }
    }

    private static IEnumerable<string> Spread(IReadOnlyList<Guid>? ids) =>
        ids is null || ids.Count == 0 ? [Unassigned] : ids.Select(id => id.ToString());

    private static string? LabelOf(
        string value,
        TripGroupDimension dimension,
        IReadOnlyDictionary<Guid, string> areaNames,
        IReadOnlyDictionary<Guid, string> caverLabels)
    {
        if (value.Length == 0 || !Guid.TryParse(value, out var id))
        {
            // Everything else is a word or an id the client already holds a name for, and it
            // translates those itself — a label written here would be in the server's language.
            return null;
        }

        return dimension switch
        {
            TripGroupDimension.Area => areaNames.GetValueOrDefault(id),
            TripGroupDimension.Participant => caverLabels.GetValueOrDefault(id),
            _ => null,
        };
    }

    // Named by id and never by a label: the client holds the purpose vocabulary and translates
    // it, so a name written here would arrive in the server's language.
    private static IReadOnlyList<TripFacetValueDto> TopTypes(IReadOnlyList<Row> rows) =>
        [
            .. rows
                .Where(x => x.TripTypeId is not null)
                .GroupBy(x => x.TripTypeId!.Value)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key)
                .Take(TopValuesPerGroup)
                .Select(g => new TripFacetValueDto(g.Key.ToString(), null, g.Count())),
        ];

    private static IReadOnlyList<TripFacetValueDto> TopPeople(
        IReadOnlyList<Row> rows,
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> peopleOfTrip,
        IReadOnlyDictionary<Guid, string> caverLabels) =>
        [
            .. rows
                // One entry per person per trip, because the rows a person appears in are already
                // distinct by trip: what is counted is trips gone on, not jobs held.
                .SelectMany(row => peopleOfTrip.GetValueOrDefault(row.Id) ?? [])
                .GroupBy(caverId => caverId)
                .OrderByDescending(g => g.Count())
                .Take(TopValuesPerGroup)
                .Select(g => new TripFacetValueDto(
                    g.Key.ToString(), caverLabels.GetValueOrDefault(g.Key), g.Count())),
        ];
}
