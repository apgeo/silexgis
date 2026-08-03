// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Geo;

/// <summary>
/// One association as the disclosure rule needs to see it: an attachment or a link that
/// ties a document to something, together with whether the document behind it carries a
/// position of its own.
/// </summary>
/// <param name="TargetFeatureId">
/// The feature the association names, or null when it names something with no position at
/// all — a trip, a club, a map view. An association that names nothing positioned cannot
/// place anything, so the rule leaves it alone.
/// </param>
/// <param name="DocumentCarriesItsOwnPosition">
/// Whether the document behind the association has coordinates of its own — a photo's
/// capture point read from its EXIF. Not "coordinates about the feature": coordinates the
/// document itself is stamped with.
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
/// whatever the setting says.
/// </para>
/// <para>
/// This is the only place the rule is written. Everything that can emit an association —
/// the document panel, the feature side, list metadata, search results, exports — asks
/// here rather than reasoning about protection for itself, because a second copy of this
/// reasoning is how one of those paths ends up disagreeing with the others.
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
        // Nothing positioned is being named, or the caller may place it exactly anyway:
        // either way there is no protected position for the pairing to give away.
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
