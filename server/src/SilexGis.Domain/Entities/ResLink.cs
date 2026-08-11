// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// Which part of a member's target the member points at; <see cref="Whole"/> means all
/// of it. Parts are addressed declaratively — page numbers, time offsets, station names,
/// waypoint indexes — never by FK to a derived row, because derived rows (document
/// pages, geofile features) are wiped and rebuilt wholesale and their ids are not
/// contracts. Payload shapes follow the W3C Web Annotation selector vocabulary where one
/// exists. Stored as smallint; values are a schema contract — append only, never
/// renumber. Which kinds a target type admits, and what each payload must contain, is
/// decided in the resource-link rules, not here.
/// </summary>
public enum AnchorKind : short
{
    /// <summary>The whole target. The only kind that carries no payload.</summary>
    Whole = 0,

    /// <summary>
    /// A text selection in a document: offsets into the extracted text stream plus the
    /// quoted text itself, so the selection can be re-anchored by quote after the
    /// offsets go stale. Payload <c>{page?, start, end, quote, prefix?, suffix?}</c>.
    /// </summary>
    TextRange = 1,

    /// <summary>One page of a paged document. Payload <c>{page}</c>, 1-based.</summary>
    Page = 2,

    /// <summary>An inclusive, forward page span. Payload <c>{fromPage, toPage}</c>.</summary>
    PageRange = 3,

    /// <summary>
    /// A region on an image (or on one page of a paged document), in natural pixels of
    /// the file the anchor was measured against. Payload
    /// <c>{page?, shape: "point"|"rect"|"circle"|"polygon", …}</c> with the shape's own
    /// coordinate fields.
    /// </summary>
    ImageRegion = 4,

    /// <summary>A moment in audio/video. Payload <c>{t}</c> — seconds.</summary>
    TimePoint = 5,

    /// <summary>A forward span of audio/video. Payload <c>{start, end}</c> — seconds.</summary>
    TimeRange = 6,

    /// <summary>
    /// A survey station, by its full survey-path-qualified name (content-addressed —
    /// survives re-import, unlike any derived row id). Payload <c>{station}</c>.
    /// </summary>
    ModelStation = 7,

    /// <summary>A station-to-station stretch. Payload <c>{fromStation, toStation}</c>.</summary>
    ModelStationRange = 8,

    /// <summary>A whole survey within the model, by path. Payload <c>{survey}</c>.</summary>
    ModelSurvey = 9,

    /// <summary>A survey-to-survey stretch. Payload <c>{fromSurvey, toSurvey}</c>.</summary>
    ModelSurveyRange = 10,

    /// <summary>
    /// A point in a 3D model file's own coordinate space. Payload <c>{x, y, z}</c>.
    /// Shape reserved ahead of a 3D viewer that can resolve it.
    /// </summary>
    ModelPoint = 11,

    /// <summary>
    /// A waypoint of a geofile, by position in the source file's waypoint sequence; the
    /// name is kept alongside for validation and re-anchoring after a re-import.
    /// Payload <c>{index, name?}</c>.
    /// </summary>
    Waypoint = 12,

    /// <summary>An inclusive, forward waypoint span. Payload
    /// <c>{fromIndex, toIndex, fromName?, toName?}</c>.</summary>
    WaypointRange = 13,
}

/// <summary>
/// Relation vocabulary for resource links: a seeded, admin-extensible taxonomy. Seeded
/// rows are the exchange vocabulary — their codes are immutable and the rows undeletable
/// (contract-tested), with display names translated client-side by code. Custom rows are
/// installation-local, shown as written, and deletable only while no link references
/// them. Semantics ride on the data flags below, so custom rows obey exactly the same
/// rules as seeded ones.
/// </summary>
public class ResLinkRelationType : TaxonomyBase
{
    /// <summary>
    /// Whether the relation reads from one distinguished member towards the rest
    /// ("contains", "documents"). A directed relation requires exactly one main member
    /// once the link has two or more; an undirected one forbids the marker.
    /// </summary>
    public bool Directed { get; set; }

    /// <summary>Optional display name reading back from the non-main side ("contained
    /// in" for "contains"); meaningful only on directed rows.</summary>
    public string? InverseName { get; set; }
}

/// <summary>
/// An n-ary association with identity: any number of members — features, documents,
/// cavers, survey models, … — related under an optional relation type, with a free-text
/// description and a permalink short code. The link itself grants no access: every
/// member is shown or withheld under its target's own visibility rules, and an
/// unreadable member renders without display data rather than disappearing — except a
/// member naming a feature whose exact position the caller may not see, which the
/// association-disclosure rule drops from the response entirely, because that row would
/// name the guarded feature rather than merely admit something restricted exists; and
/// when a sibling member shows the caller exact coordinates, that drop holds whatever
/// the installation's reveal setting says, since the name would then stand beside a
/// position. A link keeps
/// at least one member; removing the last one is refused — deleting the link is the
/// explicit act, and deletion is hard (audited, no soft-delete state).
/// </summary>
public class ResLink : ITimestamped, IAuditable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>
    /// 8-character base62 permalink code: random (CSPRNG-drawn, retried on the rare
    /// unique-index collision), immutable for the row's life, and deliberately not
    /// derived from content — membership is mutable, a content hash would break on every
    /// edit. Resolved under normal auth: an address, never a capability.
    /// </summary>
    public required string ShortCode { get; set; }

    /// <summary>Optional vocabulary row; null is an untyped association.</summary>
    public long? RelationTypeId { get; set; }

    public string? Description { get; set; }

    /// <summary>
    /// Creator — one of the three who own edit/delete, alongside global admins and whoever
    /// may write the link's main member (a link with no main member has no such subject, so
    /// there the creator and the admins are the whole rule). Null once the account is gone.
    /// </summary>
    public Guid? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}

/// <summary>
/// One participation in a <see cref="ResLink"/>: a target plus an anchor saying which
/// part of the target is meant (<see cref="Entities.AnchorKind.Whole"/> by default). The
/// target is EITHER a feature (<see cref="FeatureId"/>, real FK — cascade cleans the row
/// up with the feature) OR a non-feature entity via the polymorphic pair
/// (<see cref="EntityType"/> + <see cref="EntityId"/>, no FK — the owning slice's delete
/// flow removes members with the entity, and the integrity verifier backstops). Exactly
/// one of the two shapes is set (CHECK-enforced), the same convention as
/// <see cref="Attachment"/>. Which entity types may join, which anchor kinds each type
/// admits, and what each payload must contain is decided in the resource-link rules —
/// the single write-path home.
/// </summary>
public class ResLinkMember : ITimestamped, IAuditable, IAuditChild
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid ResLinkId { get; set; }

    /// <summary>Feature target (XOR with the polymorphic pair).</summary>
    public Guid? FeatureId { get; set; }

    /// <summary>Non-feature target discriminator (XOR with <see cref="FeatureId"/>).</summary>
    public AttachedEntityType? EntityType { get; set; }

    public Guid? EntityId { get; set; }

    /// <summary>
    /// The distinguished member a directed relation reads from. At most one per link
    /// (partial-unique-enforced); whether one is required or forbidden follows the
    /// relation row's <see cref="ResLinkRelationType.Directed"/> flag and the member
    /// count.
    /// </summary>
    public bool IsMain { get; set; }

    public AnchorKind AnchorKind { get; set; } = AnchorKind.Whole;

    /// <summary>Kind-specific payload (jsonb); null exactly when the anchor is
    /// <see cref="Entities.AnchorKind.Whole"/>.</summary>
    public string? Anchor { get; set; }

    /// <summary>
    /// The stored file the anchor was measured against, for anchors addressing content
    /// inside a document. Files are immutable, so the anchor stays exact against this
    /// file even after the document moves to a newer version — readers on the current
    /// version get the re-anchor/degrade path instead of a silent mis-highlight. Dropped
    /// (SET NULL) when the file itself is purged.
    /// </summary>
    public Guid? AnchorFileId { get; set; }

    public int SortOrder { get; set; }

    /// <summary>Free-text note on this participation ("the 1974 survey mentions it on p. 7").</summary>
    public string? Note { get; set; }

    public Guid? AddedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();

    // Members surface in the timeline of whatever they target, not the link's.
    public string RootEntityType =>
        FeatureId is not null ? nameof(Feature) : AttachedEntityTypes.ClrName(EntityType!.Value);

    public string RootEntityId => (FeatureId ?? EntityId!.Value).ToString();
}
