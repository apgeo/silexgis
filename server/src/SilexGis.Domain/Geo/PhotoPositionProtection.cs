// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Geo;

/// <summary>
/// Whether a caller may be given a photo's own recorded position — the GPS fix its camera
/// wrote into it, as opposed to any coordinate held about the thing it depicts.
/// </summary>
/// <remarks>
/// <para>
/// A capture point is not a fact about a location, it is the location, to within however
/// far the photographer stood from what they photographed. Nothing can be snapped or
/// rounded off it usefully: a point near the entrance is the entrance. So the only two
/// answers are the true point and nothing at all, and this decides which.
/// </para>
/// <para>
/// The answer is borrowed rather than invented. A photo has no protection of its own; it
/// inherits from everything it hangs on, and the strictest of those wins — the chain
/// covers the objects it is attached to, the features those reach through a link whose
/// kind is one that places its endpoints, and the caves of any trip it belongs to. If the
/// caller may place every one of them exactly, the photo tells them nothing they could not
/// already have; if even one is guarded, handing over the point walks around that guard.
/// </para>
/// <para>
/// A photo that hangs on nothing has an empty chain and is disclosable. That is not an
/// oversight: there is no protected feature in the picture for it to give away, because
/// nothing in the archive claims the photo is of one. Attaching it to a guarded cave is
/// what makes its point sensitive, and that is exactly when this starts saying no.
/// </para>
/// </remarks>
public static class PhotoPositionProtection
{
    /// <summary>
    /// Whether the photo's own position may be disclosed to the caller.
    /// </summary>
    /// <param name="protectionChain">
    /// Every feature the photo inherits protection from. Duplicates are harmless; order is
    /// irrelevant. Empty means nothing in the archive ties the photo to a place.
    /// </param>
    /// <param name="exactViewable">
    /// The features from that chain whose exact position the caller may already see — the
    /// same answer the coordinates themselves get, resolved once for the whole chain.
    /// </param>
    public static bool IsDisclosable(
        IReadOnlyCollection<Guid> protectionChain, IReadOnlySet<Guid> exactViewable) =>
        // All, not Any: inheriting from two places means obeying both. A photo attached to
        // an open cave and a guarded one is still a photo of the guarded one.
        protectionChain.All(exactViewable.Contains);
}
