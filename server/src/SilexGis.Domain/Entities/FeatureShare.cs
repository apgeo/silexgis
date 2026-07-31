// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>Audience of a feature share link. Stored as smallint.</summary>
public enum FeatureShareMode : short
{
    /// <summary>Anyone holding the link.</summary>
    Public = 0,

    /// <summary>Link resolves only for signed-in callers, whose own permissions are enforced.</summary>
    RequiresLogin = 1,
}

/// <summary>
/// A per-feature share link. The URL identity is a high-entropy opaque token — never the
/// feature id — stored only as a hash (the plaintext is shown once at mint). Revocable;
/// covers the feature and, when <see cref="IncludeSubtree"/>, its primary-chain
/// containment subtree. A share link never bypasses location protection.
/// </summary>
public class FeatureShare : ITimestamped, IAuditable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid FeatureId { get; set; }

    /// <summary>SHA-256 of the URL token, base64url. The plaintext token is never stored.</summary>
    public required string TokenHash { get; set; }

    public FeatureShareMode Mode { get; set; } = FeatureShareMode.Public;

    public bool IncludeSubtree { get; set; } = true;

    public Guid CreatedBy { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}
