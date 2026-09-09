// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.PhotoLibraries;

/// <summary>
/// One album a neighbouring library keeps: a set of photographs somebody over there put together
/// and gave a name to.
/// </summary>
/// <remarks>
/// <para>
/// Worth having because of what a club's albums usually are. A group files by expedition, and a
/// foreign album is most often one trip's worth of pictures — so after "when", "which album" is the
/// narrowing that gets a reader from a library to the photographs they came for.
/// </para>
/// <para>
/// <b>The two products describe an album differently, and what only one of them says is marked
/// absent rather than filled in from somewhere else.</b> Every field below except the identifier
/// may be null and null means <em>the library did not say</em>. A span worked out from the pictures
/// this application happened to see, or a count assembled by paging, would be a statement about
/// somebody's album that nobody over there made.
/// </para>
/// <para>
/// <b>There is no coordinate here and there is no shape of this record that has one.</b> Both
/// products will say where an album's photographs were taken; this asks neither. An album is a name
/// and a number on a chooser, and where a photograph was taken is the map's question.
/// </para>
/// </remarks>
/// <param name="AlbumId">
/// How the far side names the album — a value that goes back to it as the narrowing on a listing,
/// and nothing else. Mutable at the far side's discretion, so it is never stored and never lives
/// longer than the response it travelled in.
/// </param>
/// <param name="Title">
/// What the library calls it, or null where it calls it nothing. Null is not "untitled": it is the
/// library declining to say, and the surface that draws it decides what to write in the gap.
/// </param>
/// <param name="PhotographCount">
/// How many photographs <b>the library</b> puts in this album, as the library states it, or null
/// where the product publishes no such number.
///
/// <para>
/// A fact about the library rather than about whoever is looking, and it is worth saying so here
/// because nothing in the number itself would show it: this integration reaches every library
/// through one credential belonging to the whole installation, so there is exactly one answer and
/// every caller who may reach the feature gets that one. The day a caller's own identity decides
/// what a library will show them, this is one of the places that has to change, and it says so
/// rather than being discovered.
/// </para>
/// <para>
/// Never counted here. A number produced by asking for the album's photographs and counting them
/// would cost a request per album and would still be a count of one page.
/// </para>
/// </param>
/// <param name="From">
/// The first moment the library says this album covers, or null where the product states none. One
/// of the two publishes the span of its albums and the other does not, which is exactly the kind of
/// difference this record is shaped to carry without levelling.
/// </param>
/// <param name="To">The last moment the library says this album covers, or null.</param>
public readonly record struct LibraryAlbum(
    string AlbumId,
    string? Title,
    int? PhotographCount,
    DateTimeOffset? From,
    DateTimeOffset? To);

/// <summary>
/// What a library answered when it was asked what albums it keeps.
/// </summary>
/// <remarks>
/// Nothing is held between calls. The library is asked, its answer becomes a chooser, and the
/// answer is gone when the response is written — which is why there is no table and no job behind
/// any of it.
/// </remarks>
/// <param name="Albums">The albums it reported, in the order it gave them.</param>
/// <param name="Truncated">
/// True when the library holds more albums than this installation will ask a chooser to carry, so
/// what is here is a prefix rather than the whole set.
///
/// <para>
/// Published rather than hidden, because a short list that does not say it is short is the failure
/// this surface is most likely to make while looking correct: a reader whose album is missing
/// concludes the library has lost it. Nothing is re-ordered on this side once it has been cut —
/// a list sorted after cutting names a different set from the one that was read.
/// </para>
/// </param>
/// <param name="ReadAt">When this list was read from the library.</param>
public sealed record LibraryAlbumPage(
    IReadOnlyList<LibraryAlbum> Albums,
    bool Truncated,
    DateTimeOffset ReadAt);
