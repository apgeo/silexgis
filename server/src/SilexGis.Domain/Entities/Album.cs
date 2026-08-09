// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// A named, ordered set of photographs somebody put together: the trip's pictures, the
/// entrance survey, what went in the club bulletin.
///
/// <para>
/// An entity rather than a saved filter, and the difference is the whole reason it exists. A
/// filter answers "everything matching this", which is a set nobody chose and whose order
/// nothing decides; an album is a set somebody picked, in an order they meant, with one
/// picture standing for it. Those three things — membership, order, cover — are exactly what a
/// query cannot express.
/// </para>
/// <para>
/// It holds <em>documents</em>, not a second kind of photo. Every stored file in this
/// application hangs off a document, so a photograph already is one; an album that referred to
/// anything else would be a parallel identity for the same picture, and the two would disagree
/// the first time one of them was edited.
/// </para>
/// <para>
/// Content in its own right, so it carries the owner / club / visibility trio and is governed
/// like any other owned row. Membership grants nothing: an album is a way of arranging
/// pictures, never a way of reaching one, and a listing of it shows only what its reader could
/// already have seen.
/// </para>
/// </summary>
public class Album : IProtectedEntity, ITimestamped, IAuditable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required string Title { get; set; }

    public string? Description { get; set; }

    /// <summary>
    /// The picture that stands for the album. A member, and checked to be one — a cover drawn
    /// from outside the album is a picture that vanishes from the shelf when somebody who may
    /// not read it looks at the list.
    /// </summary>
    public Guid? CoverDocumentId { get; set; }

    /// <summary>
    /// The object this album is about, when it is about one: a trip's photographs, a cave's.
    /// The polymorphic pair the rest of the application uses, so an album hangs off the same
    /// vocabulary an attachment does.
    /// </summary>
    public AttachedEntityType? SubjectEntityType { get; set; }

    public Guid? SubjectEntityId { get; set; }

    /// <summary>The feature half of the same choice (XOR with the pair above).</summary>
    public Guid? SubjectFeatureId { get; set; }

    public Guid OwnerUserId { get; set; }

    public Guid? CavingGroupId { get; set; }

    public Visibility Visibility { get; set; } = Visibility.Private;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}

/// <summary>
/// One picture's place in one album.
///
/// <para>
/// The order is stored rather than derived, because it is the thing somebody arranged. It is a
/// sparse integer sequence: reordering rewrites only the rows that moved, and the gaps are what
/// let a picture be dropped between two others without renumbering the album.
/// </para>
/// </summary>
public class AlbumItem : ITimestamped, IAuditable, IAuditChild
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid AlbumId { get; set; }

    public Guid DocumentId { get; set; }

    /// <summary>
    /// A caption for this picture <em>in this album</em>. Null falls back to the photograph's
    /// own, which is the usual case — the override exists because the same picture reads
    /// differently in a bulletin and in a survey report.
    /// </summary>
    public string? Caption { get; set; }

    public int SortOrder { get; set; }

    public Guid? AddedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();

    // Arranging an album is a change to the album, so it belongs on its timeline.
    public string RootEntityType => nameof(Album);

    public string RootEntityId => AlbumId.ToString();
}

/// <summary>
/// A share link for one album — the public surface a club puts on its website.
///
/// <para>
/// The same shape as a feature share, deliberately: an opaque high-entropy token, stored only
/// as a hash so the plaintext exists once at mint and nowhere afterwards, and revocable without
/// touching the album. Two mechanisms for "a link that lets somebody in" would be two places to
/// get revocation and protection right.
/// </para>
/// <para>
/// A share link never widens what it points at. It opens the album's pictures as renderings and
/// nothing else: no original bytes, no capture positions, and no path from a photograph to the
/// cave it was taken at. That is not a property of the link but of the route it opens, which is
/// why the route is the thing that has to be right.
/// </para>
/// </summary>
public class AlbumShare : ITimestamped, IAuditable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid AlbumId { get; set; }

    /// <summary>SHA-256 of the URL token, base64url. The plaintext token is never stored.</summary>
    public required string TokenHash { get; set; }

    /// <summary>
    /// Whether the link opens for anyone holding it, or only for a signed-in caller — whose own
    /// permissions then decide, exactly as they would without the link.
    /// </summary>
    public FeatureShareMode Mode { get; set; } = FeatureShareMode.Public;

    public Guid CreatedBy { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();

    public bool IsRevoked => RevokedAt is not null;
}
