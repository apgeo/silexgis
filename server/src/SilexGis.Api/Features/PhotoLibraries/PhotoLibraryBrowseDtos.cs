// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.PhotoLibraries;

namespace SilexGis.Api.Features.PhotoLibraries;

/// <summary>
/// One photograph a neighbouring library holds, as this application is willing to describe it
/// before anybody has said what it is of.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no latitude and no longitude on this record, and there is no shape of it that has
/// them.</b> A list of a neighbouring library is a way of looking through pictures rather than a
/// second map, and keeping the two apart structurally is what makes this surface simple: it has no
/// coordinate to emit, no rectangle to pan, and no count that moves as a reader changes one. Where
/// a photograph was taken is the map's question, and the map is the one path that answers it.
/// </para>
/// <para>
/// A later phase decides what may be said about where a photograph was taken and to whom. Whatever
/// it decides is decided where a position is emitted, and no position is emitted here.
/// </para>
/// </remarks>
/// <param name="PhotographId">
/// How the library names the photograph. What the detail route takes, and never what the picture
/// route takes.
/// </param>
/// <param name="Reference">
/// How the library names the picture of it — the same string on one product and a hash of the
/// picture's contents on the other. What goes into the picture address's placeholder.
/// </param>
/// <param name="Title">The library's own title, or null where it keeps none. One of the two products never sends one.</param>
/// <param name="TakenAt">When it was taken, where the library says. Null where it does not say.</param>
/// <param name="Kind">
/// <c>image</c>, <c>video</c>, or null meaning <b>the library did not say</b>. Null never means
/// image: a photograph whose kind is unknown may be drawn with the picture mark, which is an
/// admitted guess about a shape, but is never described as a photograph in words a person reads.
/// </param>
public sealed record LibraryPhotographDto(
    string PhotographId,
    string Reference,
    string? Title,
    DateTimeOffset? TakenAt,
    string? Kind);

/// <summary>
/// One page of a neighbouring library, plus the few things a grid needs in order to explain itself.
/// </summary>
/// <remarks>
/// A library holding nothing and a library that is not there look identical on a screen, and only
/// the server can tell them apart — so an answer that exists is always an answer the library gave,
/// and a library that did not answer fails the request rather than arriving as an empty page nobody
/// can interpret.
/// </remarks>
/// <param name="Source">Which library answered, as the address names it.</param>
/// <param name="LibraryName">What to call it on a screen, echoed so a page needs no second request.</param>
/// <param name="Items">The photographs it reported, in the order it gave them: newest first.</param>
/// <param name="Page">Which page this is, one-based.</param>
/// <param name="PageSize">
/// How many were asked for. This installation's own number, not the caller's, when the two differ —
/// see <paramref name="PageSizeCapped"/>.
/// </param>
/// <param name="Total">
/// How many photographs <b>the library</b> holds for this request, as the library reported it, or
/// null when it publishes no way to ask and null when what it sent contradicted its own paging.
///
/// <para>
/// It describes the library and not the caller, and that is worth stating because nothing in the
/// number itself shows it: this installation reaches every library through one credential of its
/// own, so there is exactly one answer and every caller who may reach the feature gets that one.
/// The day a caller's own identity decides what a library will show them, this is one of the places
/// that has to change.
/// </para>
/// <para>
/// Never synthesised by walking pages. A surface reading null says the total is unknown rather than
/// showing a number that was guessed.
/// </para>
/// </param>
/// <param name="HasMore">
/// Whether the library has another page behind this one. Carried beside <paramref name="Total"/>
/// rather than derived from it, because one of the two products answers this while answering
/// nothing about a total — and a grid needs to know whether to offer the next page either way.
/// </param>
/// <param name="PageSizeCapped">
/// True when the caller asked for a larger page than this installation is willing to ask a library
/// for. A shortened page that does not say it was shortened is a wrong answer, and this is the only
/// thing that says it.
/// </param>
/// <param name="PicturesAvailable">
/// Whether this library may be asked for image bytes. False when it answered a picture request with
/// something that was not a picture, which can mean it has lost the disk its originals live on —
/// and against one of these products, asking such an instance for a picture deletes the photograph
/// from its own index. A client reading false shows no pictures rather than broken ones.
/// </param>
/// <param name="PictureUrlTemplate">
/// Where one photograph's rendering is fetched from, with <c>{reference}</c> and <c>{size}</c> to
/// substitute. One template for the whole page rather than a finished address per photograph,
/// because the address carries a short-lived credential. Null when this library must not be asked
/// for pictures, which is how the byte gate reaches the browser.
/// </param>
/// <param name="ReadAt">When this page was read from the library.</param>
public sealed record LibraryPhotographPageDto(
    string Source,
    string LibraryName,
    IReadOnlyList<LibraryPhotographDto> Items,
    int Page,
    int PageSize,
    int? Total,
    bool HasMore,
    bool PageSizeCapped,
    bool PicturesAvailable,
    string? PictureUrlTemplate,
    DateTimeOffset ReadAt);

/// <summary>
/// One photograph in full, as far as the library that holds it will say.
/// </summary>
/// <remarks>
/// <para>
/// Every field but the two identifiers may be absent, and absent means <b>the library did not
/// say</b> rather than empty. The two products describe a photograph differently and neither
/// describes one completely, so what nobody answered is left out and never filled in from somewhere
/// else: a camera model this application inferred would be a claim about somebody's photograph that
/// nobody made.
/// </para>
/// <para>
/// <b>No coordinate, and no address into the library's own interface.</b> The first for the reason
/// the listing gives. The second because a link that does not resolve is worse than no link, and
/// what shape a permalink into either of these products takes — and whether that interface is
/// published to a browser at all, which is a deployment choice and often no — is not established
/// here. When it is, it is one field and one configured address, and it belongs to whoever
/// establishes it.
/// </para>
/// </remarks>
/// <param name="Source">Which library it came from, as the address names it.</param>
/// <param name="LibraryName">What to call that library on a screen.</param>
/// <param name="PhotographId">How the library names the photograph.</param>
/// <param name="Reference">How the library names the picture of it, for the delivery route.</param>
/// <param name="Title">The library's own title, or null.</param>
/// <param name="Description">The library's own longer text about it, or null.</param>
/// <param name="TakenAt">When it was taken, where the library says.</param>
/// <param name="Kind"><c>image</c>, <c>video</c>, or null meaning the library did not say.</param>
/// <param name="CameraMake">The camera's maker, as written in the picture.</param>
/// <param name="CameraModel">The camera, as written in the picture.</param>
/// <param name="Lens">The lens, as written in the picture.</param>
/// <param name="Aperture">The f-number the library states.</param>
/// <param name="ShutterSpeed">The exposure time the library states, as it states it.</param>
/// <param name="Iso">The sensitivity the library states.</param>
/// <param name="FocalLengthMm">The focal length in millimetres the library states.</param>
/// <param name="PicturesAvailable">Whether this library may be asked for image bytes at all.</param>
/// <param name="PictureUrlTemplate">Where its renderings are fetched from, or null when they must not be.</param>
public sealed record LibraryPhotographDetailDto(
    string Source,
    string LibraryName,
    string PhotographId,
    string Reference,
    string? Title,
    string? Description,
    DateTimeOffset? TakenAt,
    string? Kind,
    string? CameraMake,
    string? CameraModel,
    string? Lens,
    double? Aperture,
    string? ShutterSpeed,
    int? Iso,
    double? FocalLengthMm,
    bool PicturesAvailable,
    string? PictureUrlTemplate);

/// <summary>
/// Turning what a library answered into what this application sends, in one place for both routes.
/// </summary>
internal static class LibraryPhotographMapping
{
    /// <summary>
    /// How a kind is named on the wire. Written out rather than taken from the enumeration's own
    /// <c>ToString</c>, which is a C# identifier a refactoring tool is allowed to change under a
    /// client that reads it. Null stays null: the library did not say.
    /// </summary>
    public static string? KindSlug(LibraryPhotoKind? kind) => kind switch
    {
        LibraryPhotoKind.Image => "image",
        LibraryPhotoKind.Video => "video",
        null => null,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// How a library's way of answering words is named on the wire. Written out rather than taken
    /// from the enumeration's own <c>ToString</c>, which is a C# identifier a refactoring tool is
    /// allowed to change under a client that reads it — and this one is read by a client to decide
    /// what a person is invited to type, so a silent rename would leave a box inviting a
    /// description of a picture over a library that matches words literally.
    /// </summary>
    public static string MatchingSlug(LibrarySearchMatching matching) => matching switch
    {
        LibrarySearchMatching.Text => "text",
        LibrarySearchMatching.Meaning => "meaning",
        _ => throw new ArgumentOutOfRangeException(nameof(matching)),
    };

    public static LibraryPhotographDto Of(LibraryListedPhoto photo) =>
        new(photo.PhotographId, photo.Reference, photo.Title, photo.TakenAt, KindSlug(photo.Kind));

    public static LibraryPhotographDetailDto Of(
        LibraryPhotoDetail detail,
        PhotoLibrarySource source,
        bool picturesAvailable,
        string? pictureUrlTemplate)
    {
        ArgumentNullException.ThrowIfNull(detail);

        return new(
            PhotoLibrarySlugs.Slug(source),
            PhotoLibrarySlugs.Name(source),
            detail.PhotographId,
            detail.Reference,
            detail.Title,
            detail.Description,
            detail.TakenAt,
            KindSlug(detail.Kind),
            detail.CameraMake,
            detail.CameraModel,
            detail.Lens,
            detail.Aperture,
            detail.ShutterSpeed,
            detail.Iso,
            detail.FocalLengthMm,
            picturesAvailable,
            pictureUrlTemplate);
    }
}
