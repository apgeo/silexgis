// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.PhotoLibraries;

/// <summary>
/// One photograph, as far as a neighbouring library will say and as far as this application cares:
/// where it was taken, what names it, and what names the picture of it.
/// </summary>
/// <param name="ForeignId">
/// How the far side names the photograph today. Mutable at the far side's discretion — a library
/// may re-mint an identifier when a file moves or comes back out of its trash — so it is never
/// stored, never a key, and never lives longer than the response it travelled in.
/// </param>
/// <param name="Reference">
/// How the far side names the <em>derivative</em>, which is not always how it names the
/// photograph: for one product it is the same string and for another it is a content hash. The
/// picture route takes this and not the identifier.
/// </param>
/// <param name="Longitude">Degrees east, as the library reported them.</param>
/// <param name="Latitude">Degrees north, as the library reported them.</param>
/// <param name="TakenAt">When the picture was taken, where the library says. Null where it does not.</param>
/// <param name="Title">The library's own title. Null where it has none — one of the products never sends one.</param>
/// <param name="Kind">
/// Image, video, or <c>null</c> meaning <b>the library did not say</b>. Null never means image. A
/// photograph whose kind is unknown may be drawn with the picture marker — an admitted guess about
/// a shape — but is never described as a photograph in any string a person reads.
/// </param>
public readonly record struct LibraryPhoto(
    string ForeignId,
    string Reference,
    double Longitude,
    double Latitude,
    DateTimeOffset? TakenAt,
    string? Title,
    LibraryPhotoKind? Kind);

/// <summary>
/// What one library answered for one viewport.
/// </summary>
/// <param name="Photos">The photographs it reported inside the rectangle, in the order it gave them.</param>
/// <param name="Truncated">
/// True when a limit cut the answer short. Reported, because a layer that stopped at a limit and a
/// layer that ended look identical on a screen.
/// </param>
/// <param name="ReadAt">
/// When the positions in this answer were read from the library. Carried so a map drawing older
/// positions can say so; a stale coordinate that looks live is the failure this field prevents.
/// </param>
public sealed record LibraryPhotoPage(
    IReadOnlyList<LibraryPhoto> Photos,
    bool Truncated,
    DateTimeOffset ReadAt);
