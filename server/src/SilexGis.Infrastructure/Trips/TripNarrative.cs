// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Permissions;

namespace SilexGis.Infrastructure.Trips;

/// <summary>
/// What a trip says about itself, as one reading is told it: its account, its results and the
/// answers on the three parts of its form.
/// </summary>
/// <remarks>
/// The words of a trip, apart from its facts. Everybody who may read the trip is told the first
/// four; the account of what went wrong answers to a narrower audience and is here only for a
/// reading that may change the trip. Withheld it is null rather than an empty object, so a
/// surface can tell "not yours to read" from "nothing was written" and print nothing at all for
/// the first.
/// </remarks>
/// <param name="Description">The trip's own account of itself.</param>
/// <param name="Results">What the trip came to.</param>
/// <param name="FieldData">The answers on the observations part of the form.</param>
/// <param name="Logistics">The answers on the logistics part of the form.</param>
/// <param name="Safety">The answers on the safety part, or null for a reading not given them.</param>
/// <param name="SafetySchemaVersion">
/// Which version of the form the safety answers were written against; null with them.
/// </param>
public sealed record TripNarrative(
    string? Description,
    string? Results,
    JsonElement FieldData,
    JsonElement Logistics,
    JsonElement? Safety,
    int? SafetySchemaVersion);

/// <summary>
/// The one place that decides what a reading is told of what a trip wrote about itself.
/// </summary>
/// <remarks>
/// <para>
/// More than one surface carries a trip's own words: the trip's page and everything rendered from
/// its answer — its write-up among them — and the write-up of a camp the trip was gathered into.
/// They are different documents built in different parts of the application, and they must hand
/// the same reader the same words. So neither holds the rule: both ask here, and a narrowing made
/// here reaches all of them at once.
/// </para>
/// <para>
/// One reading goes in and decides everything, including who may change which trip. That is
/// deliberate. A copy filed where a whole audience reaches it is built from the reading any
/// account has, and a caller that had to work out "may write" for itself could pass the answer
/// for the person pressing the button beside the reading for everybody — which would put what
/// went wrong on a trip into a file every reader of the camp can open. Here there is nothing
/// to pass but the reading.
/// </para>
/// </remarks>
public static class TripNarrativeReads
{
    /// <summary>What <paramref name="reading"/> is told of each of these trips' own words.</summary>
    /// <param name="access">Answers who may change a trip, for the whole set at once.</param>
    /// <param name="reading">Whose reading it is. The only thing that decides what is told.</param>
    /// <param name="trips">
    /// Trips this reading may already read. Nothing here decides whether a trip is readable at
    /// all; a trip that is not must not be handed in.
    /// </param>
    public static async Task<IReadOnlyDictionary<Guid, TripNarrative>> ForAsync(
        IAccessService access,
        AccessContext reading,
        IReadOnlyList<TripLog> trips,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(reading);
        ArgumentNullException.ThrowIfNull(trips);

        // Decided for the whole set at once, so a longer listing does not cost more round trips.
        // It answers one question: who is told what went wrong, as against who is told that
        // something did.
        var writable = await ProtectedWrites.WritableAsync(access, reading, trips, ct);

        var told = new Dictionary<Guid, TripNarrative>(trips.Count);
        foreach (var trip in trips)
        {
            told[trip.Id] = Of(trip, writable.Contains(trip.Id));
        }

        return told;
    }

    private static TripNarrative Of(TripLog trip, bool mayWrite)
    {
        var (safety, safetyVersion) = TripDisclosure.Safety(trip, mayWrite);
        return new TripNarrative(
            trip.Description,
            trip.Results,
            JsonSerializer.Deserialize<JsonElement>(trip.FieldData),
            JsonSerializer.Deserialize<JsonElement>(trip.Logistics),
            safety is null ? null : JsonSerializer.Deserialize<JsonElement>(safety),
            safetyVersion);
    }
}
