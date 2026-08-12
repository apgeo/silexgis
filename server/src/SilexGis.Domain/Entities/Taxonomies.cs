// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// Admin-editable lookup taxonomies. bigint identity keys — exchanged
/// across installations by natural key <c>Code</c>.
/// </summary>
public abstract class TaxonomyBase : ITimestamped, IAuditable
{
    public long Id { get; set; }

    public required string Code { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public int SortOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}

public class CaveType : TaxonomyBase;

public class EntranceType : TaxonomyBase;

public class RockType : TaxonomyBase;

/// <summary>
/// Data-level document-kind registry: a survey report, a permit, a trip report. Each kind
/// may carry a JSON Schema describing the metadata documents of that kind hold, using the
/// same versioned-schema-over-jsonb mechanism the feature-kind registry uses — one
/// mechanism, so a client that can render one typed form can render both.
/// </summary>
public class DocumentType : TaxonomyBase
{
    /// <summary>Optional JSON schema for this kind's typed document metadata (jsonb).</summary>
    public string? MetadataSchema { get; set; }

    /// <summary>The version a kind carries before anyone has edited its schema.</summary>
    public const int FirstSchemaVersion = 1;

    /// <summary>
    /// Bumped whenever <see cref="MetadataSchema"/> changes. Documents stamp the version
    /// they validated against, and every version ever published is kept in the schema
    /// history, so a document written under an older schema can still be checked against
    /// the schema it was actually written against rather than one that arrived later.
    /// <para>
    /// Because every edit moves it, a kind still on <see cref="FirstSchemaVersion"/> is a
    /// kind nobody has edited — which is how a schema that is null because it was never set
    /// is told apart from one that is null because an administrator emptied it.
    /// </para>
    /// </summary>
    public int MetadataSchemaVersion { get; set; } = FirstSchemaVersion;
}

/// <summary>
/// One published version of a document type's metadata schema. Kept because a stored
/// document stamps the version it was validated against: without the text of that version
/// the stamp would be unreadable, and tightening a schema would retroactively invalidate
/// every document already written under the looser one.
/// </summary>
public class DocumentTypeSchema
{
    public long Id { get; set; }

    public long DocumentTypeId { get; set; }

    /// <summary>1-based; unique per document type.</summary>
    public int Version { get; set; }

    /// <summary>The schema text exactly as it was published (jsonb).</summary>
    public required string Schema { get; set; }

    /// <summary>
    /// When this version was published. Set here rather than by the timestamp maintenance
    /// that covers editable rows, because a published schema version is append-only: it is
    /// written once and never changes, so it has no "last updated" to maintain.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// What a trip was for: exploration, survey, training, and whatever else a club runs. A row
/// rather than a value fixed at build time, because an installation that runs a kind of trip
/// nobody thought of should not need a release to record it.
/// <para>
/// A purpose also carries what a trip of that purpose is asked to record, as three JSON
/// schemas over three separate bags: what was found underground, what the trip needed to
/// happen, and what went wrong. Three rather than one because they are read by different
/// people at different times — the safety section answers to a narrower audience than the
/// rest — and a single schema would force one answer for all three.
/// </para>
/// </summary>
public class TripType : TaxonomyBase
{
    /// <summary>The version a section carries before anyone has edited its schema.</summary>
    public const int FirstSchemaVersion = 1;

    /// <summary>
    /// The three sections, in the order a report is read in. Everything that treats them alike
    /// — seeding, publishing, validating, rendering — walks this rather than naming the three
    /// columns again, so a section added later is added in one place.
    /// </summary>
    public static readonly IReadOnlyList<TripSection> Sections =
        [TripSection.FieldData, TripSection.Logistics, TripSection.Safety];

    /// <summary>What the trip found underground: depths, leads, conditions (jsonb).</summary>
    public string? FieldDataSchema { get; set; }

    /// <inheritdoc cref="LogisticsSchemaVersion"/>
    public int FieldDataSchemaVersion { get; set; } = FirstSchemaVersion;

    /// <summary>What the trip needed to happen: permits, keys, access, costs (jsonb).</summary>
    public string? LogisticsSchema { get; set; }

    /// <summary>
    /// Bumped whenever the section's schema changes. Trips stamp the version they validated
    /// against, and every version ever published is kept in the schema history, so a report
    /// written under an older schema is still checked against the schema it was actually
    /// written against rather than one that arrived afterwards.
    /// <para>
    /// Because every edit moves it, a section still on <see cref="FirstSchemaVersion"/> is a
    /// section nobody has edited — which is how a schema that is null because it was never
    /// set is told apart from one that is null because an administrator emptied it.
    /// </para>
    /// </summary>
    public int LogisticsSchemaVersion { get; set; } = FirstSchemaVersion;

    /// <summary>What went wrong, and what was learned from it (jsonb).</summary>
    public string? SafetySchema { get; set; }

    /// <inheritdoc cref="LogisticsSchemaVersion"/>
    public int SafetySchemaVersion { get; set; } = FirstSchemaVersion;

    /// <summary>The schema this purpose asks <paramref name="section"/> to conform to, if any.</summary>
    /// <remarks>
    /// The three sections are separate columns rather than rows of a child table because they
    /// are a fixed, named set that the trip form renders in a fixed order — but every rule
    /// about them (bumping, publishing, validating) is the same rule three times, so it is
    /// written once against this accessor rather than three times against three columns.
    /// </remarks>
    public string? SchemaOf(TripSection section) => section switch
    {
        TripSection.FieldData => FieldDataSchema,
        TripSection.Logistics => LogisticsSchema,
        TripSection.Safety => SafetySchema,
        _ => throw new ArgumentOutOfRangeException(nameof(section)),
    };

    /// <summary>The version <see cref="SchemaOf"/> currently returns for <paramref name="section"/>.</summary>
    public int SchemaVersionOf(TripSection section) => section switch
    {
        TripSection.FieldData => FieldDataSchemaVersion,
        TripSection.Logistics => LogisticsSchemaVersion,
        TripSection.Safety => SafetySchemaVersion,
        _ => throw new ArgumentOutOfRangeException(nameof(section)),
    };

    /// <summary>Replaces a section's schema and moves its version on. Callers publish the text.</summary>
    public void AdvanceSchema(TripSection section, string? schema)
    {
        switch (section)
        {
            case TripSection.FieldData:
                FieldDataSchema = schema;
                FieldDataSchemaVersion++;
                break;
            case TripSection.Logistics:
                LogisticsSchema = schema;
                LogisticsSchemaVersion++;
                break;
            case TripSection.Safety:
                SafetySchema = schema;
                SafetySchemaVersion++;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(section));
        }
    }

    /// <summary>Sets a section's schema without moving its version — for a row being created.</summary>
    public void SetInitialSchema(TripSection section, string? schema)
    {
        switch (section)
        {
            case TripSection.FieldData: FieldDataSchema = schema; break;
            case TripSection.Logistics: LogisticsSchema = schema; break;
            case TripSection.Safety: SafetySchema = schema; break;
            default: throw new ArgumentOutOfRangeException(nameof(section));
        }
    }
}

/// <summary>
/// One published version of one section's schema on one trip purpose. Kept because a stored
/// trip stamps the version it was validated against: without the text of that version the
/// stamp would be unreadable, and tightening a schema would retroactively invalidate every
/// report already written under the looser one.
/// </summary>
public class TripTypeSchema
{
    public long Id { get; set; }

    public long TripTypeId { get; set; }

    /// <summary>Which of the purpose's three sections this text belongs to.</summary>
    public TripSection Section { get; set; }

    /// <summary>1-based; unique per trip type and section.</summary>
    public int Version { get; set; }

    /// <summary>The schema text exactly as it was published (jsonb).</summary>
    public required string Schema { get; set; }

    /// <summary>
    /// When this version was published. Set here rather than by the timestamp maintenance
    /// that covers editable rows, because a published schema version is append-only: it is
    /// written once and never changes, so it has no "last updated" to maintain.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Data-level feature-kind registry: every generic feature names its kind here (subtyped
/// kinds — caves, entrances, centerlines — keep their own taxonomies instead). Adding a
/// feature kind is a row, not DDL.
/// </summary>
public class FeatureType : TaxonomyBase
{
    public FeatureCategory Category { get; set; } = FeatureCategory.Surface;

    /// <summary>OGC geometry classes rows of this kind may carry (incl. Multi* variants, opt-in).</summary>
    public GeometryClass[] AcceptedGeometryClasses { get; set; } = [GeometryClass.Point];

    /// <summary>
    /// Display of protected rows for callers without exact view. Security-bearing:
    /// admin-only edits, audited; seeded values contract-tested.
    /// </summary>
    public ProtectedDisplay ProtectedDisplay { get; set; } = ProtectedDisplay.SnapPoint;

    /// <summary>Kinds that only make sense inside a parent (e.g. a cave sector) refuse rootless rows.</summary>
    public bool RequiresParent { get; set; }

    /// <summary>File name inside the bundled symbol set (e.g. "sinkhole.png").</summary>
    public string? SymbolFile { get; set; }

    /// <summary>Optional OpenLayers style overrides (jsonb).</summary>
    public string? Style { get; set; }

    /// <summary>Optional JSON schema for typed feature properties (jsonb).</summary>
    public string? PropertiesSchema { get; set; }

    /// <summary>
    /// Bumped whenever <see cref="PropertiesSchema"/> changes; feature rows stamp the
    /// version they validated against and are revalidated lazily on their next edit.
    /// </summary>
    public int PropertiesSchemaVersion { get; set; } = 1;
}
