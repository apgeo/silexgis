// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Trips;

/// <summary>Wording for a section, for the codes and messages a refusal carries.</summary>
public static class SectionNames
{
    public static string Of(TripSection section) => section switch
    {
        TripSection.FieldData => "field data",
        TripSection.Logistics => "logistics",
        TripSection.Safety => "safety",
        _ => throw new ArgumentOutOfRangeException(nameof(section)),
    };

    /// <summary>
    /// The refusal code for a bag that does not fit its schema. One per section rather than one
    /// shared code, because a caller filling in three forms needs to be told which of them the
    /// answer is about, and a single code would make that a matter of reading prose.
    /// </summary>
    public static string InvalidCode(TripSection section) => section switch
    {
        TripSection.FieldData => "trip_log.field_data_invalid",
        TripSection.Logistics => "trip_log.logistics_invalid",
        TripSection.Safety => "trip_log.safety_invalid",
        _ => throw new ArgumentOutOfRangeException(nameof(section)),
    };
}

/// <summary>What a trip write says about the three sections. A null section is not being edited.</summary>
public sealed record TripSectionWrite(string? FieldData, string? Logistics, string? Safety)
{
    public string? Of(TripSection section) => section switch
    {
        TripSection.FieldData => FieldData,
        TripSection.Logistics => Logistics,
        TripSection.Safety => Safety,
        _ => throw new ArgumentOutOfRangeException(nameof(section)),
    };
}

/// <summary>
/// Measures a trip's three section bags against the schemas its purpose carries, and stamps
/// each with the version it was measured against. The single home of that rule: every path
/// that writes a trip goes through here, so a bag can never reach the database unmeasured.
/// </summary>
public sealed class TripSectionWriter(SilexGisDbContext db, ITypedPropertiesValidator validator)
{
    /// <summary>
    /// Applies the supplied sections to <paramref name="trip"/>, validating each and moving its
    /// stamp. A section the caller did not supply keeps its stored value and is re-measured
    /// against the version it already carries — so tightening a purpose's schema does not
    /// retroactively invalidate reports written under the looser one, and a caller editing only
    /// the title of an old report is not made to fix a section they never opened.
    /// </summary>
    /// <param name="trip">The row being written; its stored bags and stamps are read and replaced.</param>
    /// <param name="typeChanged">
    /// Whether this write moves the trip to a different purpose. It counts as a rewrite of all
    /// three bags, because the schemas they answer to are not the ones they were measured
    /// against any more.
    /// </param>
    /// <exception cref="TripWriteException">
    /// <c>trip_log.field_data_invalid</c>, <c>trip_log.logistics_invalid</c> or
    /// <c>trip_log.safety_invalid</c> when a supplied bag does not fit the schema it is
    /// measured against.
    /// </exception>
    public async Task ApplyAsync(
        TripLog trip, TripSectionWrite write, bool typeChanged, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(trip);
        ArgumentNullException.ThrowIfNull(write);

        // Read without tracking: the purpose is consulted here, never edited, and tracking it
        // would put an unrelated row in the same unit of work as the trip.
        var type = trip.TripTypeId is { } typeId
            ? await db.TripTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == typeId, ct)
            : null;

        foreach (var section in TripType.Sections)
        {
            var supplied = write.Of(section);
            var bag = supplied ?? Stored(trip, section);
            var stamp = await StampAsync(type, section, bag, Stamp(trip, section), supplied is not null || typeChanged, ct);
            Store(trip, section, bag, stamp);
        }
    }

    /// <summary>
    /// The version a bag is measured against, and the stamp it therefore carries: the current
    /// schema for a bag this write supplies, and the stamped version's own text for one it does
    /// not. Null means the bag was never measured — either the purpose carries no schema for
    /// the section, or nobody has supplied a value for it yet.
    /// </summary>
    private async Task<int?> StampAsync(
        TripType? type, TripSection section, string bag, int? stamped, bool rewritten, CancellationToken ct)
    {
        var schema = type?.SchemaOf(section);
        if (schema is null)
        {
            return null;
        }

        var version = type!.SchemaVersionOf(section);
        if (!rewritten)
        {
            if (stamped is not { } carried)
            {
                // Never measured, and this write does not supply the bag either. It stays
                // unmeasured, and the empty stamp keeps saying so, until a caller actually
                // fills the section in.
                return null;
            }

            if (carried != version)
            {
                // Fall forward to the current schema only when the stamped version's text is
                // missing, which means the history lost a row rather than that the report is
                // stale — failing the write instead would strand the trip permanently.
                var published = await TripTypeSchemaOfVersionAsync(type.Id, section, carried, ct);
                if (published is not null)
                {
                    version = carried;
                    schema = published;
                }
            }
        }

        var errors = validator.Validate(schema, bag);
        return errors.Count == 0
            ? version
            : throw new TripWriteException(SectionNames.InvalidCode(section), string.Join(" ", errors));
    }

    private Task<string?> TripTypeSchemaOfVersionAsync(
        long tripTypeId, TripSection section, int version, CancellationToken ct) =>
        db.TripTypeSchemas.AsNoTracking()
            .Where(s => s.TripTypeId == tripTypeId && s.Section == section && s.Version == version)
            .Select(s => s.Schema)
            .FirstOrDefaultAsync(ct);

    private static string Stored(TripLog trip, TripSection section) => section switch
    {
        TripSection.FieldData => trip.FieldData,
        TripSection.Logistics => trip.Logistics,
        TripSection.Safety => trip.Safety,
        _ => throw new ArgumentOutOfRangeException(nameof(section)),
    };

    private static int? Stamp(TripLog trip, TripSection section) => section switch
    {
        TripSection.FieldData => trip.FieldDataSchemaVersion,
        TripSection.Logistics => trip.LogisticsSchemaVersion,
        TripSection.Safety => trip.SafetySchemaVersion,
        _ => throw new ArgumentOutOfRangeException(nameof(section)),
    };

    private static void Store(TripLog trip, TripSection section, string bag, int? stamp)
    {
        switch (section)
        {
            case TripSection.FieldData:
                trip.FieldData = bag;
                trip.FieldDataSchemaVersion = stamp;
                break;
            case TripSection.Logistics:
                trip.Logistics = bag;
                trip.LogisticsSchemaVersion = stamp;
                break;
            case TripSection.Safety:
                trip.Safety = bag;
                trip.SafetySchemaVersion = stamp;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(section));
        }
    }
}
