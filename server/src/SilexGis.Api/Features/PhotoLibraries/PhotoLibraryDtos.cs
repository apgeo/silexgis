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
/// <param name="Unconfigured">
/// The products this build can read that no address or credential has been supplied for.
/// </param>
/// <remarks>
/// <para>
/// <see cref="Unconfigured"/> is empty for everybody but a full administrator, and that is the
/// server's decision rather than something a screen declines to draw. Which products an
/// installation could run is a fact about the installation, not about the caves in it, and an
/// ordinary account has no errand that begins with it — while the administrator who has just set
/// three environment variables and seen no overlay appear has exactly that errand, and today has
/// nothing anywhere that distinguishes "switched off" from "typed the address wrong".
/// </para>
/// <para>
/// A product listed here is not a fault. Running one of the two, or neither, is a supported
/// installation; the list says what could be connected and is not, and says nothing about whether
/// anything should be.
/// </para>
/// </remarks>
public sealed record PhotoLibraryStatusDto(
    bool MayRead,
    IReadOnlyList<PhotoLibraryProviderDto> Providers,
    IReadOnlyList<PhotoLibraryProviderDto> Unconfigured);

/// <param name="Source">Which product it is, as the address names it.</param>
/// <param name="Name">What to call it on a screen. Never the product identifier a route is built from.</param>
/// <param name="Configured">
/// An address and a credential are set. Whether anything answers at that address is a different
/// question, and it is <see cref="Health"/> that answers it.
/// </param>
/// <param name="Health">
/// What the library said about itself when it was last asked, or that it was not asked.
/// </param>
public sealed record PhotoLibraryProviderDto(
    string Source,
    string Name,
    bool Configured,
    PhotoLibraryHealthDto Health);

/// <summary>
/// Whether a neighbouring library is actually working, which is a different question from whether
/// somebody configured it.
/// </summary>
/// <remarks>
/// <para>
/// This route used to answer "configured" alone, deliberately, so that a poll opened no socket. It
/// asks now because the thing it was protecting was never the load: an installation with a wrong
/// address, a revoked credential or a stopped container looked exactly like a healthy one until
/// somebody opened the map and found it empty, and every one of those is invisible from this side
/// without asking. The load is bounded instead — each library holds its last answer for a short
/// window, so a page refreshed twice, or read by two people at once, costs one round of requests.
/// </para>
/// <para>
/// The fields are separate rather than one verdict because they fail separately. A library can
/// answer and refuse the credential; it can accept the credential and withhold a right; and it can
/// be perfectly healthy while its pictures are stopped, which is not a failure at all but this
/// application's own guard.
/// </para>
/// </remarks>
/// <param name="Reach">
/// <c>reachable</c>, <c>unreachable</c>, or <c>unknown</c> when nothing was asked. Three values
/// because "nobody asked" must not be written down as "no".
/// </param>
/// <param name="Version">What the product says it is, where it says. Null where it does not, and never a guess.</param>
/// <param name="MissingPermissions">
/// The rights this integration needs that the configured credential does not carry, named as the
/// far side names them. Empty when the credential is sufficient and when the product publishes no
/// way to ask — so an empty list means nothing is known to be missing, not that everything is
/// present.
/// </param>
/// <param name="PicturesAvailable">
/// Whether this library may currently be asked for image bytes. Not a probe result: it is this
/// application's own sticky gate, closed by an answer to a picture request that was not a picture,
/// and published here because an operator reading a health line is asking exactly this.
/// </param>
/// <param name="FailureCode">
/// The stable code for what went wrong, or null when nothing did. Present alongside a reachable
/// library that refused the credential.
/// </param>
/// <param name="ProbedAt">When this was read. Null only when nothing was asked.</param>
public sealed record PhotoLibraryHealthDto(
    string Reach,
    string? Version,
    IReadOnlyList<string> MissingPermissions,
    bool PicturesAvailable,
    string? FailureCode,
    DateTimeOffset? ProbedAt)
{
    public static PhotoLibraryHealthDto Of(LibraryHealth health, bool picturesAvailable)
    {
        ArgumentNullException.ThrowIfNull(health);

        return new(
            ReachSlug(health.Reach),
            health.Version,
            health.MissingPermissions,
            picturesAvailable,
            health.FailureCode,
            health.ProbedAt);
    }

    /// <summary>
    /// How a reach is named on the wire. Written out rather than taken from the enumeration's own
    /// <c>ToString</c>, which is a C# identifier a refactoring tool is allowed to change under a
    /// client that reads it.
    /// </summary>
    private static string ReachSlug(LibraryReach reach) => reach switch
    {
        LibraryReach.Reachable => "reachable",
        LibraryReach.Unreachable => "unreachable",
        LibraryReach.Unknown => "unknown",
        _ => throw new ArgumentOutOfRangeException(nameof(reach)),
    };
}

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
