// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.PhotoLibraries;

namespace SilexGis.Api.Features.PhotoLibraries;

/// <summary>
/// One album a neighbouring library keeps, as this application is willing to describe it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The two products describe an album differently, and what only one of them says is marked
/// absent rather than filled in.</b> Every field but the identifier may be null, and null means
/// <em>the library did not say</em> rather than empty — the same rule the photograph listing
/// already follows. A span worked out from the pictures this application happened to see, or a
/// count assembled by paging, would be a claim about somebody's album that nobody over there made.
/// </para>
/// <para>
/// <b>There is no coordinate here and there is no shape of this record that has one.</b> Both
/// products will say where an album's photographs were taken; neither is asked. An album is a name
/// and a number on a chooser, and where a photograph was taken is the map's question.
/// </para>
/// </remarks>
/// <param name="AlbumId">
/// How the library names the album. What the listing takes as its narrowing, and nothing else.
/// </param>
/// <param name="Title">
/// What the library calls it, or null where it calls it nothing. Null is not "untitled": it is the
/// library declining to say, and the screen decides what to write in the gap.
/// </param>
/// <param name="PhotographCount">
/// How many photographs <b>the library</b> puts in this album, as the library states it, or null
/// where the product publishes no such number.
///
/// <para>
/// It describes the library and not the caller, and that is worth stating because nothing in the
/// number itself shows it: this installation reaches every library through one credential of its
/// own, so there is exactly one answer and every caller who may reach the feature gets that one.
/// The day a caller's own identity decides what a library will show them, this is one of the places
/// that has to change.
/// </para>
/// <para>
/// Zero is a number and not an absence — an album somebody has just emptied holds none, and the
/// library said so.
/// </para>
/// </param>
/// <param name="From">
/// The first moment the library says this album covers, or null where the product states none. One
/// of the two publishes the span of its albums and the other does not.
/// </param>
/// <param name="To">The last moment the library says this album covers, or null.</param>
public sealed record LibraryAlbumDto(
    string AlbumId,
    string? Title,
    int? PhotographCount,
    DateTimeOffset? From,
    DateTimeOffset? To);

/// <summary>
/// The albums one neighbouring library keeps, and the one thing a chooser needs in order to explain
/// itself.
/// </summary>
/// <remarks>
/// A library with no albums and a library that did not answer look identical on a screen, and only
/// the server can tell them apart — so an answer that exists is always an answer the library gave,
/// and a library that did not answer fails the request rather than arriving as an empty list nobody
/// can interpret. A chooser drawing "this library has no albums" over a stopped container is
/// exactly the mistake this surface would otherwise make while looking correct.
/// </remarks>
/// <param name="Source">Which library answered, as the address names it.</param>
/// <param name="LibraryName">What to call it on a screen, echoed so a page needs no second request.</param>
/// <param name="Items">The albums it reported, in the order it gave them.</param>
/// <param name="Truncated">
/// True when the library keeps more albums than this installation will offer, so this is a prefix
/// rather than the whole set. A short list that does not say it is short sends a reader whose album
/// is missing looking for what the library lost.
/// </param>
/// <param name="ReadAt">When this list was read from the library.</param>
public sealed record LibraryAlbumsDto(
    string Source,
    string LibraryName,
    IReadOnlyList<LibraryAlbumDto> Items,
    bool Truncated,
    DateTimeOffset ReadAt)
{
    public static LibraryAlbumsDto Of(LibraryAlbumPage answer, PhotoLibrarySource source)
    {
        ArgumentNullException.ThrowIfNull(answer);

        return new(
            PhotoLibrarySlugs.Slug(source),
            PhotoLibrarySlugs.Name(source),
            [
                .. answer.Albums.Select(album => new LibraryAlbumDto(
                    album.AlbumId,
                    album.Title,
                    album.PhotographCount,
                    album.From,
                    album.To)),
            ],
            answer.Truncated,
            answer.ReadAt);
    }
}
