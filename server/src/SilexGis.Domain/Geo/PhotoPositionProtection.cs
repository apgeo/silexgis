// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

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

    /// <summary>
    /// Whether a file of this kind may carry where it was captured inside its own bytes, so
    /// that handing the bytes over could hand over a position.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Decided by kind, not by whether a position was read out of the file. A recorded position
    /// is what intake managed to read and what an editor left in place, not what the bytes hold:
    /// clearing a photo's position never rewrites the photo, nothing reads the location a phone
    /// writes into a video or a voice recording, a fix can sit only in an XMP or IPTC packet that
    /// is never read, and older uploads were never read at all. Photographs, videos and sound
    /// recordings are what cameras and phones stamp with a position, so those are treated as
    /// carrying one whatever the record says.
    /// </para>
    /// <para>
    /// Any other kind carries no capture location of its own, but a position may still have been
    /// set on it by hand, and then it is treated like a photograph that recorded one.
    /// </para>
    /// </remarks>
    public static bool MayCarryLocation(FileKind kind, bool recordsPosition) =>
        recordsPosition || kind is FileKind.Image or FileKind.Video or FileKind.Audio;

    /// <summary>
    /// Which bytes a reader of a file is handed when they ask for the file itself rather than a
    /// rendering of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two questions decide it, and they are kept apart on purpose. Whether this reader may be
    /// told where the picture was taken is <see cref="IsDisclosable"/>'s answer and is not
    /// re-asked here. Whether an installation lets location data travel inside photo files at
    /// all is its administrator's switch. The size of what is handed over is neither: a reader
    /// who may not have the location gets the whole picture with the location taken out of it,
    /// not a smaller one.
    /// </para>
    /// <para>
    /// A file whose location cannot be taken out — a video, a sound recording, or an image in a
    /// format that cannot be written back — keeps the older rule for whoever would have been
    /// given the stripped copy: renderings only, and nothing where none are drawn. A file that
    /// cannot carry a location (see <see cref="MayCarryLocation"/>) is outside the rule
    /// entirely: the stored bytes go to anybody who may read it.
    /// </para>
    /// <para>
    /// The second administrator switch reaches exactly one case: a reader who may be told the
    /// location, the first switch off, and a file whose location cannot be taken out. By the
    /// first switch alone nobody would get such a file at all; with the second on, that reader
    /// gets it as stored — it can hold nothing they may not already be told. A reader who may not
    /// be told the location is never reached by it.
    /// </para>
    /// </remarks>
    /// <param name="mayCarryLocation"><see cref="MayCarryLocation"/>'s answer for the file.</param>
    /// <param name="positionDisclosable">
    /// Whether a position of the file's may be disclosed to this reader —
    /// <see cref="IsDisclosable"/>'s answer over the file's protection chain.
    /// </param>
    /// <param name="keepLocationInFiles">The installation's switch; on by default.</param>
    /// <param name="locationRemovable">
    /// Whether this application can write the file back with its location data removed.
    /// </param>
    /// <param name="serveUnstrippableToLocationReaders">
    /// The installation's second switch: whether a reader who may be told the location still gets
    /// a file whose location cannot be removed, as stored, while <paramref name="keepLocationInFiles"/>
    /// is off. Off by default.
    /// </param>
    public static OriginalReach OriginalReachOf(
        bool mayCarryLocation,
        bool positionDisclosable,
        bool keepLocationInFiles,
        bool locationRemovable,
        bool serveUnstrippableToLocationReaders)
    {
        if (!mayCarryLocation)
        {
            return OriginalReach.Stored;
        }

        if (positionDisclosable && (keepLocationInFiles || (!locationRemovable && serveUnstrippableToLocationReaders)))
        {
            return OriginalReach.Stored;
        }

        return locationRemovable ? OriginalReach.LocationFree : OriginalReach.None;
    }
}

/// <summary>What a reader asking for a file itself is handed.</summary>
public enum OriginalReach
{
    /// <summary>No original at all: renderings where the application draws any, nothing else.</summary>
    None = 0,

    /// <summary>
    /// The whole picture at full size, rewritten without the location data it carried. The
    /// stored file is never touched; this is a copy.
    /// </summary>
    LocationFree = 1,

    /// <summary>The stored bytes exactly as uploaded.</summary>
    Stored = 2,
}
