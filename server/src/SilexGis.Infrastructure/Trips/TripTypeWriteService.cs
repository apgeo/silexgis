// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Metadata;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Trips;

/// <summary>What a caller asks a trip purpose to become, sections included.</summary>
public sealed record TripTypeInput(
    string Code,
    string Name,
    string? Description,
    int SortOrder,
    string? FieldDataSchema,
    string? LogisticsSchema,
    string? SafetySchema,
    // The list trips of this purpose settle before they set off, if an administrator names one.
    // Appended, and a reference rather than a copy: correcting a line corrects it everywhere at
    // once.
    Guid? DefaultChecklistId = null);

/// <summary>
/// The single mutator of a trip purpose and of the three schemas it carries. A schema is not a
/// plain column: changing one has to move a version number and record what the previous text
/// was, because trips stamp the version they were validated against and are re-checked against
/// that version rather than against whatever the schema became afterwards. Doing that in one
/// place is what keeps the stamp meaningful.
/// </summary>
public sealed class TripTypeWriteService(SilexGisDbContext db, ITypedPropertiesValidator validator)
{
    public const string InvalidSchemaCode = "trip_type.schema_invalid";
    public const string CodeTakenCode = "trip_type.code_taken";
    public const string NotFoundCode = "trip_type.not_found";
    public const string SeededImmutableCode = "trip_type.seeded_immutable";
    public const string InUseCode = "trip_type.in_use";

    /// <summary>
    /// Adds a purpose together with the first published version of each schema it carries.
    /// Owns its transaction: the purpose's identity is assigned by the database, so the schema
    /// rows that reference it cannot be written in the same statement, and a purpose whose
    /// schema rows never landed would carry version stamps nothing can resolve.
    /// </summary>
    /// <exception cref="TripWriteException">
    /// <c>trip_type.code_taken</c> when the code is already used;
    /// <c>trip_type.schema_invalid</c> when one of the supplied schemas is not usable.
    /// </exception>
    public async Task<TripType> CreateAsync(TripTypeInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var code = input.Code.Trim();
        if (await db.TripTypes.AsNoTracking().AnyAsync(t => t.Code == code, ct))
        {
            throw new TripWriteException(CodeTakenCode, "Another trip type already uses this code.");
        }

        var schemas = NormalizeAll(input);
        var type = new TripType
        {
            Code = code,
            Name = input.Name.Trim(),
            Description = input.Description,
            SortOrder = input.SortOrder,
            DefaultChecklistId = input.DefaultChecklistId,
        };
        foreach (var section in TripType.Sections)
        {
            type.SetInitialSchema(section, schemas[section]);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.TripTypes.Add(type);
        await db.SaveChangesAsync(ct);
        foreach (var section in TripType.Sections)
        {
            Publish(type, section, schemas[section]);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return type;
    }

    /// <summary>
    /// Applies <paramref name="input"/> to an existing purpose. A section's version moves only
    /// when that section's schema <em>means</em> something different — reformatting or
    /// reordering keys is not a new version, because every trip stamped with the old one would
    /// then be marked stale for nothing. The three sections move independently.
    /// </summary>
    /// <exception cref="TripWriteException">
    /// <c>trip_type.not_found</c>, <c>trip_type.code_taken</c>,
    /// <c>trip_type.seeded_immutable</c>, <c>trip_type.schema_invalid</c>.
    /// </exception>
    public async Task<TripType> UpdateAsync(long id, TripTypeInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var type = await db.TripTypes.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new TripWriteException(NotFoundCode, "The trip type no longer exists.");

        var code = input.Code.Trim();
        // A shipped code is what clients translate labels by and what installations exchange
        // records under, so renaming one would silently break both. The wording and the
        // ordering are presentation and stay editable.
        if (TripTypeSeeds.IsSeeded(type.Code) && code != type.Code)
        {
            throw new TripWriteException(SeededImmutableCode, "A shipped trip type keeps its code.");
        }

        if (code != type.Code && await db.TripTypes.AsNoTracking().AnyAsync(t => t.Code == code && t.Id != id, ct))
        {
            throw new TripWriteException(CodeTakenCode, "Another trip type already uses this code.");
        }

        var schemas = NormalizeAll(input);

        type.Code = code;
        type.Name = input.Name.Trim();
        type.Description = input.Description;
        type.SortOrder = input.SortOrder;
        type.DefaultChecklistId = input.DefaultChecklistId;

        foreach (var section in TripType.Sections)
        {
            var schema = schemas[section];
            if (JsonCanonical.Canonicalize(type.SchemaOf(section)) == JsonCanonical.Canonicalize(schema))
            {
                continue;
            }

            type.AdvanceSchema(section, schema);
            Publish(type, section, schema);
        }

        await db.SaveChangesAsync(ct);
        return type;
    }

    /// <summary>
    /// Deletes a club-authored purpose no trip names. A shipped row is refused outright and one
    /// still in use is refused with a code of its own; the database's restricting foreign key
    /// would refuse the second case too, but a request deserves an answer rather than a
    /// constraint violation.
    /// </summary>
    /// <exception cref="TripWriteException">
    /// <c>trip_type.not_found</c>, <c>trip_type.seeded_immutable</c>, <c>trip_type.in_use</c>.
    /// </exception>
    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var type = await db.TripTypes.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new TripWriteException(NotFoundCode, "The trip type no longer exists.");

        if (TripTypeSeeds.IsSeeded(type.Code))
        {
            throw new TripWriteException(SeededImmutableCode, "Shipped trip types cannot be deleted.");
        }

        if (await db.TripLogs.AnyAsync(t => t.TripTypeId == id, ct))
        {
            throw new TripWriteException(InUseCode, "Trips still use this trip type; retype them first.");
        }

        // The published history is the purpose's own bookkeeping and goes with it; the database
        // cascade says so too, and removing the rows here keeps the tracked graph honest.
        db.TripTypeSchemas.RemoveRange(db.TripTypeSchemas.Where(s => s.TripTypeId == id));
        db.TripTypes.Remove(type);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Records a section's current schema as a published version. A section with no schema
    /// publishes nothing: there is no text for a trip to have been validated against, and the
    /// stamp such a trip carries is null.
    /// </summary>
    internal void Publish(TripType type, TripSection section, string? schema)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (schema is null)
        {
            return;
        }

        db.TripTypeSchemas.Add(new TripTypeSchema
        {
            TripTypeId = type.Id,
            Section = section,
            Version = type.SchemaVersionOf(section),
            Schema = schema,
        });
    }

    private Dictionary<TripSection, string?> NormalizeAll(TripTypeInput input)
    {
        var schemas = new Dictionary<TripSection, string?>
        {
            [TripSection.FieldData] = Normalize(input.FieldDataSchema),
            [TripSection.Logistics] = Normalize(input.LogisticsSchema),
            [TripSection.Safety] = Normalize(input.SafetySchema),
        };
        foreach (var (section, schema) in schemas)
        {
            RejectUnusableSchema(section, schema);
        }

        return schemas;
    }

    private void RejectUnusableSchema(TripSection section, string? schema)
    {
        if (schema is null)
        {
            return;
        }

        var errors = validator.ValidateSchema(schema);
        if (errors.Count > 0)
        {
            throw new TripWriteException(
                InvalidSchemaCode, $"{SectionNames.Of(section)}: {string.Join(" ", errors)}");
        }
    }

    /// <summary>Blank is the same as absent: a section either has a schema or has none.</summary>
    private static string? Normalize(string? schema) =>
        string.IsNullOrWhiteSpace(schema) ? null : schema.Trim();
}
