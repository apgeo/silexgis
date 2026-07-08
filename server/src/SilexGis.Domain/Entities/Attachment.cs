// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>Domain entity classes that can carry attachments/tags/ACL rows. Stored as smallint.</summary>
public enum AttachedEntityType : short
{
    Cave = 0,
    CaveEntrance = 1,
    SurfaceFeature = 2,
    TripLog = 3,
    Team = 4,
    Geofile = 5,
    GeoreferencedMap = 6,
    MapView = 7,
}

/// <summary>Maps protected entity instances to their polymorphic discriminator.</summary>
public static class ProtectedEntityTypes
{
    public static AttachedEntityType Of(IProtectedEntity entity) => entity switch
    {
        Cave => AttachedEntityType.Cave,
        SurfaceFeature => AttachedEntityType.SurfaceFeature,
        Geofile => AttachedEntityType.Geofile,
        TripLog => AttachedEntityType.TripLog,
        GeoreferencedMap => AttachedEntityType.GeoreferencedMap,
        MapView => AttachedEntityType.MapView,
        _ => throw new ArgumentException($"No entity-type mapping for {entity.GetType().Name}.", nameof(entity)),
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
}

/// <summary>
/// Polymorphic link between a stored file and a domain object. Access to a file flows
/// through the objects it is attached to (plus uploader/admin) — the attachment row is
/// where that lookup starts. The owning entity's slice deletes its attachments in the
/// same transaction as the entity (no FK to the polymorphic target).
/// </summary>
public class Attachment : ITimestamped, IAuditable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid FileId { get; set; }

    public AttachedEntityType EntityType { get; set; }

    public Guid EntityId { get; set; }

    public AttachmentRole Role { get; set; } = AttachmentRole.Other;

    public string? Caption { get; set; }

    public int SortOrder { get; set; }

    public Guid? AddedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}
