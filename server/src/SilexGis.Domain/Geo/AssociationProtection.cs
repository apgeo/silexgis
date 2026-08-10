// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Geo;

/// <summary>
/// One association as the disclosure rule needs to see it: an attachment or a link that
/// ties a document to something, together with whether the document behind it carries a
/// position of its own.
/// </summary>
/// <param name="TargetFeatureId">
/// The feature the association names, or null when it names something that is not a
/// feature — a club, a map view, a trip. An association that names no feature names
/// nothing this rule guards the position of, so the rule leaves it alone. That is not the
/// same as naming something with no position: a trip carries a sketch of its own, served
/// exactly to everyone who may read the trip, and so a trip standing in a resource link
/// counts as a sibling showing coordinates even though it is never itself withheld.
/// </param>
/// <param name="DocumentCarriesItsOwnPosition">
/// Whether the association itself puts exact coordinates in front of the caller — a
/// photo's capture point read from its EXIF, or, for a resource-link membership, a
/// sibling member of the same link that shows this caller exact coordinates. Not
/// "coordinates about the feature": coordinates the association's own content carries.
/// The resource-link mapping deliberately feeds its sibling fact through this arm so the
/// one written rule decides both worlds — tune the arm's meaning only with that second
/// consumer in view.
/// </param>
public readonly record struct FeatureAssociation(Guid? TargetFeatureId, bool DocumentCarriesItsOwnPosition);

/// <summary>
/// Whether a caller is told that a document points at a particular feature.
/// </summary>
/// <remarks>
/// <para>
/// The document is never the thing withheld. Being attached or linked to a
/// position-protected feature does not hide a document, keep it out of a listing or a
/// search result, or withhold its text: what a caller may read is decided by the rules
/// written about the document, by who uploaded it and by what it hangs on — never by the
/// protection status of something it references. What is withheld instead is the
/// association itself, in both directions: the caller sees the document and sees the
/// feature, and is not told that the one is about the other.
/// </para>
/// <para>
/// That default is conservative rather than absolute, which is why an installation can
/// switch it off: naming the cave a report is about discloses that the cave exists and
/// has a report, both of which are already public here. Only where the cave is has ever
/// been guarded, and revealing the association does not reveal that — the feature is
/// authorised exactly as it always is, so a caller without exact-location rights still
/// gets the protected view when they follow it.
/// </para>
/// <para>
/// One pairing the setting does not open: a document stamped with coordinates of its own
/// placed next to a feature's name is not a name at all, it is the position, to within
/// however far the photographer stood from the entrance. That association stays withheld
/// whatever the setting says. Resource-link memberships ride the same arm with a widened
/// fact: there "the document carries its own position" means "a sibling member of the
/// same link shows this caller exact coordinates", which is the same pairing spelled
/// n-ary.
/// </para>
/// <para>
/// This is the only place the rule is written. Everything that can emit an association —
/// the document panel, the feature side, list metadata, search results, exports, and the
/// resource-link membership reads — asks here rather than reasoning about protection for
/// itself, because a second copy of this reasoning is how one of those paths ends up
/// disagreeing with the others.
/// </para>
/// </remarks>
public static class AssociationProtection
{
    /// <summary>
    /// Whether the association must be kept from the caller.
    /// </summary>
    /// <param name="association">The association and the document behind it.</param>
    /// <param name="exactViewOfTarget">
    /// Whether the caller may see the named feature's exact position — the same answer the
    /// coordinates themselves get. Meaningless, and ignored, when the association names no
    /// feature.
    /// </param>
    /// <param name="revealProtectedAssociations">
    /// The installation's setting. False — the default — withholds; true shows the
    /// association to everyone who can read the document.
    /// </param>
    public static bool IsWithheld(
        FeatureAssociation association, bool exactViewOfTarget, bool revealProtectedAssociations)
    {
        // The association names no feature whose position this rule guards, or the caller may
        // place that feature exactly anyway: either way there is no protected position for the
        // pairing to give away. Naming no feature is not the same as naming something with no
        // position — a trip carries a sketch of its own, and it counts through the arm below,
        // as a sibling showing coordinates, rather than through this one.
        if (association.TargetFeatureId is null || exactViewOfTarget)
        {
            return false;
        }

        // The pairing places the feature by proximity instead of merely naming it, so it
        // is the position and not a fact about it. The setting reveals names; it does not
        // reveal positions, and this would be one.
        if (association.DocumentCarriesItsOwnPosition)
        {
            return true;
        }

        return !revealProtectedAssociations;
    }
}
