// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Metadata;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Features;

/// <summary>What an administrator asks a feature kind to become.</summary>
public sealed record FeatureTypeInput(
    string Code,
    string Name,
    string? Description,
    int SortOrder,
    FeatureCategory Category,
    IReadOnlyList<GeometryClass> AcceptedGeometryClasses,
    bool RequiresParent,
    ProtectedDisplay ProtectedDisplay,
    string? SymbolFile,
    string? Style,
    string? PropertiesSchema);

/// <summary>A refused write to a feature kind, with the stable code the caller is answered with.</summary>
public sealed class FeatureTypeWriteException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// The single mutator of the feature-kind vocabulary: the kinds that ship and the ones an
/// installation adds.
/// <para>
/// A kind is not a plain label. Every feature copies its kind's category when it is written and is
/// checked against its accepted shapes and its need for a parent on every edit, so changing those
/// under stored features would leave rows that disagree with their own kind — filed under the
/// wrong map layer, or refused by their next save long after the change that broke them. Those
/// three are therefore settled while a kind is unused and only ever loosened afterwards: a shape
/// may be added, a need for a parent dropped. Deleted features count, since restoring one must not
/// bring back a row its kind no longer accepts.
/// </para>
/// <para>
/// A shipped kind keeps its code, its category, its protected display and whether it needs a
/// parent. Behaviours resolve shipped kinds by code and installations exchange features under
/// them; the protected display is part of what keeps a protected cave's surroundings from
/// outlining it, chosen per shipped kind on purpose. Its wording, order, symbol, style and
/// properties schema are the installation's own, and the seeder never overwrites a schema once
/// one is set.
/// </para>
/// </summary>
public sealed class FeatureTypeWriteService(SilexGisDbContext db, ITypedPropertiesValidator validator)
{
    public const string NotFoundCode = "feature_type.not_found";
    public const string CodeTakenCode = "feature_type.code_taken";
    public const string SeededImmutableCode = "feature_type.seeded_immutable";
    public const string InUseCode = "feature_type.in_use";
    public const string InvalidSchemaCode = "feature_type.schema_invalid";
    public const string InvalidStyleCode = "feature_type.style_invalid";

    /// <summary>Whether a kind with this code ships with the software.</summary>
    public static bool IsShipped(string code) => TaxonomySeeder.ShippedFeatureTypeCodes.Contains(code);

    /// <exception cref="FeatureTypeWriteException">
    /// <c>feature_type.code_taken</c>, <c>feature_type.schema_invalid</c>,
    /// <c>feature_type.style_invalid</c>.
    /// </exception>
    public async Task<FeatureType> CreateAsync(FeatureTypeInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var code = input.Code.Trim();
        // A shipped code that is not in the database yet is still taken: the next seeding run
        // would add the shipped kind beside the club's and the two would collide on the code.
        if (IsShipped(code) || await db.FeatureTypes.AsNoTracking().AnyAsync(t => t.Code == code, ct))
        {
            throw new FeatureTypeWriteException(CodeTakenCode, "Another feature type already uses this code.");
        }

        var schema = NormalizeSchema(input.PropertiesSchema);
        var type = new FeatureType
        {
            Code = code,
            Name = input.Name.Trim(),
            Description = Blank(input.Description),
            SortOrder = input.SortOrder,
            Category = input.Category,
            AcceptedGeometryClasses = [.. input.AcceptedGeometryClasses.Distinct()],
            RequiresParent = input.RequiresParent,
            ProtectedDisplay = input.ProtectedDisplay,
            SymbolFile = Blank(input.SymbolFile),
            Style = NormalizeStyle(input.Style),
            PropertiesSchema = schema,
            PropertiesSchemaVersion = 1,
        };

        db.FeatureTypes.Add(type);
        await db.SaveChangesAsync(ct);
        return type;
    }

    /// <exception cref="FeatureTypeWriteException">
    /// <c>feature_type.not_found</c>, <c>feature_type.code_taken</c>,
    /// <c>feature_type.seeded_immutable</c>, <c>feature_type.in_use</c>,
    /// <c>feature_type.schema_invalid</c>, <c>feature_type.style_invalid</c>.
    /// </exception>
    public async Task<FeatureType> UpdateAsync(long id, FeatureTypeInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var type = await db.FeatureTypes.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new FeatureTypeWriteException(NotFoundCode, "The feature type no longer exists.");

        var code = input.Code.Trim();
        var classes = input.AcceptedGeometryClasses.Distinct().ToArray();
        var narrowed = type.AcceptedGeometryClasses.Except(classes).Any();
        var parentNowRequired = input.RequiresParent && !type.RequiresParent;

        if (IsShipped(type.Code))
        {
            if (code != type.Code
                || input.Category != type.Category
                || input.ProtectedDisplay != type.ProtectedDisplay
                || input.RequiresParent != type.RequiresParent)
            {
                throw new FeatureTypeWriteException(
                    SeededImmutableCode,
                    "A shipped feature type keeps its code, category, protected display and need for a parent.");
            }

            // The seeder widens a shipped kind's shapes by union on every run, so a shape taken
            // away here would be back after the next start — refused rather than undone later.
            if (narrowed)
            {
                throw new FeatureTypeWriteException(
                    SeededImmutableCode, "A shipped feature type keeps every shape it ships with.");
            }
        }
        else if (code != type.Code
            && (IsShipped(code) || await db.FeatureTypes.AsNoTracking().AnyAsync(t => t.Code == code && t.Id != id, ct)))
        {
            throw new FeatureTypeWriteException(CodeTakenCode, "Another feature type already uses this code.");
        }

        if ((narrowed || parentNowRequired || input.Category != type.Category)
            && await db.Features.IgnoreQueryFilters().AnyAsync(f => f.FeatureTypeId == id, ct))
        {
            throw new FeatureTypeWriteException(
                InUseCode,
                "Features of this type exist, so its category stays, no shape it accepts can be taken "
                    + "away and it cannot start needing a parent.");
        }

        var schema = NormalizeSchema(input.PropertiesSchema);
        var style = NormalizeStyle(input.Style);

        type.Code = code;
        type.Name = input.Name.Trim();
        type.Description = Blank(input.Description);
        type.SortOrder = input.SortOrder;
        type.Category = input.Category;
        type.AcceptedGeometryClasses = classes;
        type.RequiresParent = input.RequiresParent;
        type.ProtectedDisplay = input.ProtectedDisplay;
        type.SymbolFile = Blank(input.SymbolFile);
        type.Style = style;

        // Features stamp the schema version they were checked against and are re-checked on their
        // next edit, so the version moves when the schema means something different — and not when
        // it was only reformatted, which would mark every stored feature stale for nothing.
        if (JsonCanonical.Canonicalize(type.PropertiesSchema) != JsonCanonical.Canonicalize(schema))
        {
            type.PropertiesSchema = schema;
            type.PropertiesSchemaVersion++;
        }

        await db.SaveChangesAsync(ct);
        return type;
    }

    /// <exception cref="FeatureTypeWriteException">
    /// <c>feature_type.not_found</c>, <c>feature_type.seeded_immutable</c>, <c>feature_type.in_use</c>.
    /// </exception>
    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var type = await db.FeatureTypes.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new FeatureTypeWriteException(NotFoundCode, "The feature type no longer exists.");

        if (IsShipped(type.Code))
        {
            throw new FeatureTypeWriteException(SeededImmutableCode, "Shipped feature types cannot be deleted.");
        }

        // Deleted features count: one that can still be restored keeps its kind, and the foreign
        // key holding it does not know the feature is hidden.
        if (await db.Features.IgnoreQueryFilters().AnyAsync(f => f.FeatureTypeId == id, ct))
        {
            throw new FeatureTypeWriteException(InUseCode, "Features of this type exist; give them another type first.");
        }

        db.FeatureTypes.Remove(type);
        await db.SaveChangesAsync(ct);
    }

    private string? NormalizeSchema(string? schema)
    {
        var text = Blank(schema);
        if (text is null)
        {
            return null;
        }

        var errors = validator.ValidateSchema(text);
        if (errors.Count > 0)
        {
            throw new FeatureTypeWriteException(InvalidSchemaCode, string.Join(" ", errors));
        }

        return text;
    }

    /// <summary>A style is a JSON object of overrides the map reads; anything else is refused here.</summary>
    private static string? NormalizeStyle(string? style)
    {
        var text = Blank(style);
        if (text is null)
        {
            return null;
        }

        try
        {
            using var parsed = JsonDocument.Parse(text);
            if (parsed.RootElement.ValueKind == JsonValueKind.Object)
            {
                return text;
            }
        }
        catch (JsonException)
        {
            // Falls through to the refusal below.
        }

        throw new FeatureTypeWriteException(InvalidStyleCode, "The style must be a JSON object.");
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
