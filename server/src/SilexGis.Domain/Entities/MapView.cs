// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// A saved workspace state: extent, base layer, overlays with opacities, filters and
/// panel layout, stored as a versioned client-owned JSON document. A non-null share
/// token makes the view link-readable regardless of visibility — but shared access
/// never overrides location protection.
/// </summary>
public class MapView : IProtectedEntity, ITimestamped, IAuditable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    public string? Description { get; set; }

    /// <summary>Client-owned config document (jsonb); carries its own configVersion.</summary>
    public string Config { get; set; } = "{}";

    /// <summary>Capability token (uuid v4 — maximum entropy, no embedded timestamp).</summary>
    public Guid? ShareToken { get; set; }

    /// <summary>The owner's default view, applied when the workspace opens.</summary>
    public bool IsHome { get; set; }

    public Guid OwnerUserId { get; set; }

    public Guid? TeamId { get; set; }

    public Visibility Visibility { get; set; } = Visibility.Private;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}
