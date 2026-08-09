// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// What a photograph is, as opposed to what its bytes are: who took it, what it shows, what may
/// be done with it, and where — in words — it was taken.
///
/// <para>
/// A table of its own rather than columns on the document, because every one of these is
/// meaningless for a survey PDF and a document table carrying four always-null columns invites
/// the next feature to add a fifth. Keyed by the document, so it survives a re-scan: replacing
/// the bytes does not change who took the picture.
/// </para>
/// <para>
/// Columns rather than entries in the document's metadata bag, because the gallery filters and
/// sorts on them — the photographer especially, which is the whole point of naming a caver
/// rather than typing a name. A jsonb bag is not indexed for that.
/// </para>
/// </summary>
public class PhotoDetails : ITimestamped, IAuditable, IAuditChild
{
    /// <summary>The photograph this describes. One row per document, so this is the key.</summary>
    public Guid DocumentId { get; set; }

    /// <summary>
    /// Who took it, as a roster caver rather than a name.
    ///
    /// <para>
    /// A caver joins with everything else in the installation: the trips they were on, the caves
    /// they surveyed, the club they belong to. A typed name joins with nothing and is spelled
    /// three ways by the third person who enters it. An account-less caver is still a caver, so
    /// crediting somebody who never signed in works.
    /// </para>
    /// </summary>
    public Guid? PhotographerCaverId { get; set; }

    /// <summary>
    /// Whoever took it, when they are not on the roster at all — a guest, a visiting club, a
    /// picture out of an old bulletin. Free text, used only when there is no caver to name.
    /// </summary>
    public string? PhotographerName { get; set; }

    /// <summary>What the picture shows, in a sentence.</summary>
    public string? Caption { get; set; }

    /// <summary>
    /// What may be done with it, as one of the codes in <see cref="Documents.PhotoLicences"/>.
    ///
    /// <para>
    /// This is the field that makes a club bulletin possible: a picture whose licence nobody
    /// recorded cannot be reused, and asking the photographer years later is how a bulletin is
    /// not published. Null means nobody has said, which is different from "all rights reserved"
    /// and must read differently.
    /// </para>
    /// </summary>
    public string? LicenceCode { get; set; }

    /// <summary>
    /// Where it was taken, in words — "Entrance of Peștera Mare", "the third pitch".
    ///
    /// <para>
    /// Deliberately not a position and never derived from one. A place name somebody typed is a
    /// caption; the coordinates a camera recorded are location data governed by the protection
    /// rules, and the two must not be able to leak into each other. Anybody who may read the
    /// picture may read this.
    /// </para>
    /// </summary>
    public string? PlaceName { get; set; }

    /// <summary>
    /// Whether an administrator has put this picture in the installation's public gallery — the
    /// one surface an anonymous visitor sees.
    ///
    /// <para>
    /// A flag of its own rather than the document's visibility band, because the two answer
    /// different questions. <see cref="Visibility.Public"/> means "every account here may read
    /// it"; this means "somebody chose to publish it to the internet", which is a decision with
    /// a different weight and a different person making it. Curation is the whole point: a
    /// gallery that published everything readable would publish the first thing anybody got
    /// wrong.
    /// </para>
    /// </summary>
    public bool InPublicGallery { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => DocumentId.ToString();

    // These are facts about the document, so they surface on its timeline.
    public string RootEntityType => nameof(Document);

    public string RootEntityId => DocumentId.ToString();
}
