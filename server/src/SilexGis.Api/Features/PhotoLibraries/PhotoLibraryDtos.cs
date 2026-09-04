// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Api.Common;
using SilexGis.Domain.PhotoLibraries;

namespace SilexGis.Api.Features.PhotoLibraries;

/// <summary>
/// What this installation will say about the neighbouring photo libraries before anything is drawn.
/// </summary>
/// <param name="MayRead">
/// Whether this caller may see them at all. Answered first, so a client decides whether to offer
/// the layer with one request — and so that a caller outside the audience is told no without also
/// being told which products this installation runs.
/// </param>
/// <param name="Providers">
/// One entry per library this installation has been given an address and a credential for. Empty
/// when it has been given none, and empty for a caller who may not see them. An installation that
/// runs none of these products is a supported installation, not a half-broken one, so the list is
/// short rather than full of falses.
/// </param>
public sealed record PhotoLibraryStatusDto(
    bool MayRead,
    IReadOnlyList<PhotoLibraryProviderDto> Providers);

/// <param name="Source">Which product it is, as the address names it.</param>
/// <param name="Name">What to call it on a screen. Never the product identifier a route is built from.</param>
/// <param name="Configured">
/// An address and a credential are set. Whether it actually answers is a different question and a
/// different request, and is deliberately not asked here: a status route that opens a socket on
/// every poll is load this installation inflicts on itself.
/// </param>
public sealed record PhotoLibraryProviderDto(
    string Source,
    string Name,
    bool Configured);

/// <summary>
/// Located photographs held in one neighbouring photo library, as GeoJSON points, plus the few
/// things this overlay needs in order to explain itself.
/// </summary>
/// <remarks>
/// <para>
/// A library holding nothing in this viewport and a library that is not there look identical on a
/// map, and only the server can tell them apart — so an answer that exists is always an answer the
/// library gave, and a library that did not answer fails the request instead of arriving as an
/// empty collection nobody can interpret.
/// </para>
/// <para>
/// Everything except <c>type</c> and <c>features</c> is a GeoJSON foreign member, so a plain
/// GeoJSON reader still parses the collection.
/// </para>
/// </remarks>
/// <param name="Type">Always <c>FeatureCollection</c>.</param>
/// <param name="Features">One point per photograph the library reported inside the rectangle.</param>
/// <param name="Source">Which library answered.</param>
/// <param name="LibraryName">
/// What to call it in a balloon, echoed with the collection so a popup names the library without a
/// second request.
/// </param>
/// <param name="PicturesAvailable">
/// Whether this library may be asked for image bytes. False when it answered a picture request with
/// something that was not a picture — which can mean it has lost the disk its originals live on,
/// and against this product asking such an instance for a picture can delete the photograph from
/// its own index. A client reading false stops requesting pictures rather than showing broken ones.
/// </param>
/// <param name="PictureUrlTemplate">
/// Where to fetch one photograph's rendering, with <c>{reference}</c> and <c>{size}</c> to
/// substitute. One template for the whole collection rather than a finished address per photograph:
/// the address carries a short-lived credential, and repeating that credential across thousands of
/// features would add megabytes to a response for a picture nobody has clicked on yet. Null when
/// this library must not be asked for pictures, which is how the byte gate reaches the browser.
/// </param>
/// <param name="ReadAt">
/// When these positions were read from the library. Carried so a map drawing positions read some
/// time ago can say so; a stale coordinate that looks live is what this field prevents.
/// </param>
/// <param name="Truncated">
/// True when this installation's own point limit cut the answer short. A layer that stopped at a
/// limit and a layer that ended look the same on a screen, and only this says which happened.
/// </param>
/// <param name="OmittedCount">
/// How many the limit left out, where that is knowable. It is only knowable when the whole answer
/// was in hand before the limit was applied, which is not the case for a library asked for a
/// clamped count — so zero alongside <c>truncated</c> being true is a normal answer, and a surface
/// that keys its truncation message off this number instead of off <c>truncated</c> says nothing at
/// all. It counts the limit and only ever the limit.
/// </param>
public sealed record LibraryPhotoFeatureCollection(
    string Type,
    IReadOnlyList<GeoFeature> Features,
    string Source,
    string LibraryName,
    bool PicturesAvailable,
    string? PictureUrlTemplate,
    DateTimeOffset ReadAt,
    bool Truncated,
    int OmittedCount)
{
    public static LibraryPhotoFeatureCollection Of(
        IReadOnlyList<GeoFeature> features,
        PhotoLibrarySource source,
        bool picturesAvailable,
        string? pictureUrlTemplate,
        DateTimeOffset readAt,
        bool truncated,
        int omittedCount) =>
        new(
            "FeatureCollection",
            features,
            PhotoLibrarySlugs.Slug(source),
            PhotoLibrarySlugs.Name(source),
            picturesAvailable,
            pictureUrlTemplate,
            readAt,
            truncated,
            omittedCount);
}

/// <summary>
/// How a neighbouring library is named in an address, and on a screen.
/// </summary>
/// <remarks>
/// The two are kept apart on purpose. The slug is part of a URL and of a signed token, so changing
/// one breaks addresses already handed out; the name is prose and may be changed freely. Neither is
/// the enumeration's own <c>ToString</c>, which is a C# identifier and would tie both to a name a
/// refactoring tool is allowed to change.
/// </remarks>
internal static class PhotoLibrarySlugs
{
    public const string Immich = "immich";

    public const string PhotoPrism = "photoprism";

    public static string Slug(PhotoLibrarySource source) => source switch
    {
        PhotoLibrarySource.Immich => Immich,
        PhotoLibrarySource.PhotoPrism => PhotoPrism,
        _ => throw new ArgumentOutOfRangeException(nameof(source)),
    };

    /// <summary>
    /// What to call it on a screen. The product's own name: an installation running one of these
    /// beside this application calls it by the product's name, and a label of its own is a setting
    /// nobody has asked for yet.
    /// </summary>
    public static string Name(PhotoLibrarySource source) => source switch
    {
        PhotoLibrarySource.Immich => "Immich",
        PhotoLibrarySource.PhotoPrism => "PhotoPrism",
        _ => throw new ArgumentOutOfRangeException(nameof(source)),
    };

    /// <summary>
    /// Reads the segment of the address that names a library. A slug this build does not know is
    /// refused here rather than looked up, so a request naming a product this installation does not
    /// run is answered the same way as one naming a product that does not exist.
    /// </summary>
    public static bool TryParse(string? slug, out PhotoLibrarySource source)
    {
        if (string.Equals(slug, Immich, StringComparison.Ordinal))
        {
            source = PhotoLibrarySource.Immich;
            return true;
        }

        if (string.Equals(slug, PhotoPrism, StringComparison.Ordinal))
        {
            source = PhotoLibrarySource.PhotoPrism;
            return true;
        }

        source = default;
        return false;
    }
}
