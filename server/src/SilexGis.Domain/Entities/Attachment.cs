// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// Non-feature entity classes addressable through the polymorphic pair — by attachments,
/// taggings, ACL rows and resource-link members. Physical features are addressed by the
/// real `feature_id` FK instead and have no member here. The enum is the shared
/// vocabulary; which values a given consumer actually accepts is that consumer's own
/// validity rule — attachments and taggings take a narrow set of it, resource links a
/// wider one, and neither may widen the other by accident. Stored as smallint;
/// values are a schema contract — append only, never renumber (0–2 were the feature
/// kinds, retired with the supertype).
/// </summary>
public enum AttachedEntityType : short
{
    TripLog = 3,
    CavingGroup = 4,
    Geofile = 5,
    GeoreferencedMap = 6,
    MapView = 7,
    StoredFile = 8,

    /// <summary>The document itself — the stable identity above its versions and files.</summary>
    Document = 9,

    SurveyModel = 10,

    /// <summary>A roster caver (which may or may not have a linked account).</summary>
    Caver = 11,

    Cabinet = 12,

    /// <summary>Reserved for a comment entity that does not exist yet; no consumer accepts it.</summary>
    Comment = 13,

    /// <summary>A named, ordered set of photographs.</summary>
    Album = 14,

    /// <summary>A camp: the one thing a fortnight of trips is gathered into.</summary>
    Expedition = 15,
}

/// <summary>Maps non-feature protected entity instances to their polymorphic discriminator.</summary>
public static class ProtectedEntityTypes
{
    public static AttachedEntityType Of(IProtectedEntity entity) => entity switch
    {
        Geofile => AttachedEntityType.Geofile,
        TripLog => AttachedEntityType.TripLog,
        GeoreferencedMap => AttachedEntityType.GeoreferencedMap,
        MapView => AttachedEntityType.MapView,
        Expedition => AttachedEntityType.Expedition,
        _ => throw new ArgumentException($"No entity-type mapping for {entity.GetType().Name}.", nameof(entity)),
    };
}

/// <summary>Helpers over <see cref="AttachedEntityType"/>.</summary>
public static class AttachedEntityTypes
{
    /// <summary>
    /// CLR type name of the discriminated entity — matches the string the audit trail
    /// stores in <c>entity_type</c> (and history queries filter on), so a polymorphic
    /// child (attachment/tagging) can name its parent's audit root.
    /// </summary>
    public static string ClrName(AttachedEntityType type) => type switch
    {
        AttachedEntityType.TripLog => nameof(AttachedEntityType.TripLog),
        AttachedEntityType.CavingGroup => nameof(AttachedEntityType.CavingGroup),
        AttachedEntityType.Geofile => nameof(AttachedEntityType.Geofile),
        AttachedEntityType.GeoreferencedMap => nameof(AttachedEntityType.GeoreferencedMap),
        AttachedEntityType.MapView => nameof(AttachedEntityType.MapView),
        AttachedEntityType.StoredFile => nameof(AttachedEntityType.StoredFile),
        AttachedEntityType.Document => nameof(AttachedEntityType.Document),
        AttachedEntityType.SurveyModel => nameof(AttachedEntityType.SurveyModel),
        AttachedEntityType.Caver => nameof(AttachedEntityType.Caver),
        AttachedEntityType.Cabinet => nameof(AttachedEntityType.Cabinet),
        AttachedEntityType.Comment => nameof(AttachedEntityType.Comment),
        AttachedEntityType.Album => nameof(AttachedEntityType.Album),
        AttachedEntityType.Expedition => nameof(AttachedEntityType.Expedition),
        _ => type.ToString(),
    };
}

/// <summary>Semantic role of an attached file. Stored as smallint.</summary>
public enum AttachmentRole : short
{
    PhotoEntrance = 0,
    PhotoInterior = 1,
    PhotoSurface = 2,
    Document = 3,
    Map2d = 4,
    SurveyData = 5,
    Other = 6,

    /// <summary>The completed trip-report document (docx/pdf/…) — the report itself.</summary>
    Report = 7,
}

/// <summary>
/// Link between a stored file and a domain object. The target is EITHER a feature
/// (<see cref="FeatureId"/>, real FK — cascade cleans the row up with the feature) OR a
/// non-feature entity via the polymorphic pair (<see cref="EntityType"/> +
/// <see cref="EntityId"/>, no FK — the owning slice deletes its attachments in the same
/// transaction). Exactly one of the two shapes is set (CHECK-enforced). Access to a file
/// flows through the objects it is attached to (plus uploader/admin) — the attachment
/// row is where that lookup starts.
/// </summary>
public class Attachment : ITimestamped, IAuditable, IAuditChild
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid FileId { get; set; }

    /// <summary>Feature target (XOR with the polymorphic pair).</summary>
    public Guid? FeatureId { get; set; }

    /// <summary>Non-feature target discriminator (XOR with <see cref="FeatureId"/>).</summary>
    public AttachedEntityType? EntityType { get; set; }

    public Guid? EntityId { get; set; }

    public AttachmentRole Role { get; set; } = AttachmentRole.Other;

    public string? Caption { get; set; }

    public int SortOrder { get; set; }

    /// <summary>
    /// Whether this is the object's headline picture — the one a cave shows in a list, a card or
    /// a popup.
    ///
    /// <para>
    /// On the attachment rather than on the object, so one rule serves every kind of thing a
    /// picture can hang on rather than a column per table. At most one per target, which the
    /// database enforces with a partial unique index: two headline pictures is a state nothing
    /// downstream can resolve, and it would resolve itself differently on every query.
    /// </para>
    /// <para>
    /// The point of choosing one is that the alternative is "whichever sorted first", which
    /// changes when somebody uploads an unrelated picture.
    /// </para>
    /// </summary>
    public bool IsPrimary { get; set; }

    public Guid? AddedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();

    // Attachments surface in the timeline of whatever they are attached to.
    public string RootEntityType =>
        FeatureId is not null ? nameof(Feature) : AttachedEntityTypes.ClrName(EntityType!.Value);

    public string RootEntityId => (FeatureId ?? EntityId!.Value).ToString();
}
