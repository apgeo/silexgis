// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// Non-feature entity classes that can carry attachments/tags/ACL rows through the
/// polymorphic pair. Physical features are addressed by the real `feature_id` FK
/// instead and have no member here. Stored as smallint; values are a schema contract —
/// append only, never renumber (0–2 were the feature kinds, retired with the supertype).
/// </summary>
public enum AttachedEntityType : short
{
    TripLog = 3,
    Team = 4,
    Geofile = 5,
    GeoreferencedMap = 6,
    MapView = 7,
    StoredFile = 8,
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
        AttachedEntityType.Team => nameof(AttachedEntityType.Team),
        AttachedEntityType.Geofile => nameof(AttachedEntityType.Geofile),
        AttachedEntityType.GeoreferencedMap => nameof(AttachedEntityType.GeoreferencedMap),
        AttachedEntityType.MapView => nameof(AttachedEntityType.MapView),
        AttachedEntityType.StoredFile => nameof(AttachedEntityType.StoredFile),
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

    public Guid? AddedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();

    // Attachments surface in the timeline of whatever they are attached to.
    public string RootEntityType =>
        FeatureId is not null ? nameof(Feature) : AttachedEntityTypes.ClrName(EntityType!.Value);

    public string RootEntityId => (FeatureId ?? EntityId!.Value).ToString();
}
