// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Import;

/// <summary>
/// Works out what each scan of a recording would mean as a position, and proposes nothing it
/// cannot justify.
///
/// <para>
/// The gap this closes is a vocabulary gap. A device records a <em>place</em> — a physical marker
/// with a label somebody stuck to the rock — and a tracked trip records a <em>station</em> of a
/// survey. The only thing the two share is a depth: a place carries the depth it was measured at,
/// and a station carries an altitude the same resolver the live path uses turns into one. So a
/// proposal here is "the station nearest the depth this marker is at", offered with the ones just
/// behind it, and it is a proposal rather than an answer because two branches of a survey
/// regularly share a horizon and nothing in the archive says which one the party was in.
/// </para>
/// <para>
/// It writes nothing and it proposes only out of what the caller can already see. The places are
/// matched through the feature register, under the same visibility walk everything else uses, so a
/// place somebody else's installation holds privately is simply a place that does not resolve.
/// </para>
/// </summary>
public sealed class SpeleolocTripImportResolver(SilexGisDbContext db)
{
    /// <summary>The feature type a device place is seeded as.</summary>
    private const string PlaceTypeCode = "cave_place";

    /// <summary>The property a device place carries its measured depth under.</summary>
    private const string DepthPropertyKey = "speleolocDepthInCave";

    /// <summary>
    /// What each scan would mean. <paramref name="model"/> is null when no model was chosen or the
    /// caller may not place its cave; every point then answers <see cref="SpeleolocPointState.ModelUnavailable"/>,
    /// which is one refusal said once rather than an empty list that reads like a clean recording.
    /// <paramref name="tracking"/> is the target trip's configuration and is honoured only where its
    /// own cave anchor is the model's cave — see the note at the top of the method.
    /// </summary>
    public async Task<SpeleolocImportResolutionSet> ResolveAsync(
        IReadOnlyList<SpeleolocArchivePoint> points,
        SpeleolocImportOptions options,
        SurveyModel? model,
        TripTracking? tracking,
        AccessContext ctx,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(ctx);

        var deviceUsers = points
            .Select(p => p.DeviceUserId)
            .Where(u => u is not null)
            .Select(u => u!.Value.ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // The trip's tracking configuration is used only when it is a configuration of the cave the
        // scans are being placed in. Its reference station and its depth filter are station
        // vocabulary, held under the tracking row's own cave anchor and withheld by the live surface
        // from anybody who cannot place that cave — so applying another cave's datum and filter here
        // would report, through which scans resolve and which of this caller's own stations survive
        // the filter, exactly what that surface refuses to say. It would also be meaningless: a
        // datum is an altitude in one survey and a filter is one survey's name prefixes.
        var configured = model is not null && tracking?.CaveFeatureId == model.CaveFeatureId
            ? tracking
            : null;

        var stations = model is null ? [] : await StationsOfAsync(model, ct);
        var referenceZ = model is null
            ? null
            : TrackingDepthResolver.ReferenceZ(stations, configured?.ReferenceStationName);
        var filter = configured?.DepthFilter ?? [];
        var take = Math.Clamp(options.CandidateCount, 1, 25);

        // The places the scans name, as this installation holds them and as this caller may see
        // them. The device's own identifier is the feature's identifier — both systems mint the
        // same kind of value and the device's is what the sync writes — so there is no mapping
        // table to consult and nothing to keep in step.
        var placeIds = points.Where(p => p.PlaceId is not null).Select(p => p.PlaceId!.Value).Distinct().ToList();
        var places = await KnownPlacesAsync(placeIds, ctx, ct);

        var resolutions = new List<SpeleolocPointResolution>(points.Count);
        foreach (var point in points)
        {
            var deviceUser = point.DeviceUserId?.ToString();
            var caverId = CaverOf(options, deviceUser);

            if (model is null)
            {
                resolutions.Add(Row(point, deviceUser, caverId, null, SpeleolocPointState.ModelUnavailable, []));
                continue;
            }

            if (point.PlaceId is null)
            {
                resolutions.Add(Row(point, deviceUser, caverId, null, SpeleolocPointState.NoPlace, []));
                continue;
            }

            if (!places.TryGetValue(point.PlaceId.Value, out var place))
            {
                resolutions.Add(Row(point, deviceUser, caverId, null, SpeleolocPointState.PlaceUnknown, []));
                continue;
            }

            // The depth this installation holds for the place, not the depth the archive states.
            // The two are usually the same value and where they differ the register is what the
            // survey was measured against, while the archive is a copy of a phone that may have
            // been offline for a season. The archive's own figure still reaches the reviewer, on
            // the row beside it, so a disagreement is visible rather than resolved silently.
            var depth = place.DepthM ?? point.PlaceDepthM;
            if (depth is null)
            {
                resolutions.Add(Row(point, deviceUser, caverId, place, SpeleolocPointState.DepthUnknown, []));
                continue;
            }

            if (referenceZ is null)
            {
                resolutions.Add(Row(point, deviceUser, caverId, place, SpeleolocPointState.NoStationAtDepth, []));
                continue;
            }

            // Proposed in the words the viewer uses, because a proposal here is a proposal of a
            // position, and a position is stored and drawn in those words. The resolver carries
            // both of a station's names for exactly this — for one of the two line-plot formats
            // they are different strings for the same station — so the proposal and the row it
            // will become are spelled the same without anything here choosing.
            var candidates = TrackingDepthResolver
                .Resolve(stations, referenceZ.Value, depth.Value, filter, take)
                .Select(c => new SpeleolocStationCandidate(
                    c.ViewerName,
                    c.SurveyName,
                    Math.Round(c.DepthM, 1),
                    Math.Round(c.DeltaM, 1)))
                .ToList();

            resolutions.Add(Row(
                point,
                deviceUser,
                caverId,
                place,
                candidates.Count == 0 ? SpeleolocPointState.NoStationAtDepth : SpeleolocPointState.Proposed,
                candidates));
        }

        var unmapped = resolutions
            .Where(r => r.CaverId is null && r.DeviceUserId is not null)
            .Select(r => r.DeviceUserId!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new SpeleolocImportResolutionSet(resolutions, deviceUsers, unmapped, model is not null);
    }

    /// <summary>
    /// Who this scan is about, out of the mapping the reviewer filled in by hand. There is no
    /// second path: a device account is a login on a phone, and nothing here turns one into a
    /// person by matching a name, an email or a device identifier. An account nobody mapped
    /// produces no caver, and every scan it made waits.
    /// </summary>
    private static Guid? CaverOf(SpeleolocImportOptions options, string? deviceUser)
    {
        if (deviceUser is null)
        {
            return null;
        }

        foreach (var (key, caverId) in options.Cavers)
        {
            if (string.Equals(key, deviceUser, StringComparison.OrdinalIgnoreCase))
            {
                return caverId;
            }
        }

        return null;
    }

    private static SpeleolocPointResolution Row(
        SpeleolocArchivePoint point,
        string? deviceUser,
        Guid? caverId,
        KnownPlace? place,
        SpeleolocPointState state,
        IReadOnlyList<SpeleolocStationCandidate> candidates) => new(
        point.Id.ToString(),
        point.ScannedAt,
        point.Notes,
        place is null ? null : point.PlaceId,
        place?.Name ?? point.PlaceTitle,
        place?.DepthM ?? point.PlaceDepthM,
        deviceUser,
        caverId,
        state,
        candidates);

    /// <summary>
    /// The places, as the register holds them, narrowed to what this caller may be told about.
    /// A feature that is not a device place is not a place: an identifier that happens to name a
    /// cave or an area answers exactly as an unknown one does.
    /// </summary>
    private async Task<Dictionary<Guid, KnownPlace>> KnownPlacesAsync(
        IReadOnlyList<Guid> ids, AccessContext ctx, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var rows = await db.Features.AsNoTracking()
            .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
            .Where(f => ids.Contains(f.Id))
            .Join(
                db.FeatureTypes.Where(t => t.Code == PlaceTypeCode),
                f => f.FeatureTypeId,
                t => t.Id,
                (f, t) => new { f.Id, f.Name, f.Properties })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.Id, r => new KnownPlace(r.Name, DepthOf(r.Properties)));
    }

    /// <summary>
    /// The depth a place carries, out of its own properties. Read in application code rather than
    /// as a SQL cast because the value is untyped JSON that somebody's sync wrote: a cast in the
    /// query fails the whole read for one place whose property is a string, and one bad place must
    /// not cost the review of everything scanned beside it.
    /// </summary>
    private static double? DepthOf(string? properties)
    {
        if (string.IsNullOrWhiteSpace(properties))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(properties);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty(DepthPropertyKey, out var depth))
            {
                return null;
            }

            return depth.ValueKind == JsonValueKind.Number && depth.TryGetDouble(out var value) ? value : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<TrackingDepthResolver.Station>> StationsOfAsync(
        SurveyModel model, CancellationToken ct)
    {
        var rows = await db.SurveyStations.AsNoTracking()
            .Where(s => s.SurveyModelId == model.Id)
            .Select(s => new { s.Name, s.SurveyName, s.Position, s.Flags })
            .ToListAsync(ct);
        return [.. rows.Select(r => TrackingDepthResolver.Station.Of(
            model.Format, model.RootSurveyName,
            r.Name, r.SurveyName, r.Position.Coordinate.Z, (r.Flags & SurveyStationFlags.Entrance) != 0))];
    }

    private sealed record KnownPlace(string? Name, double? DepthM);
}
