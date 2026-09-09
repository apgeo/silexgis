// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.PhotoLibraries;

/// <summary>
/// One photograph as a list of a library reports it: the picture, what the library says about it in
/// words, and when it was taken.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no latitude and no longitude here, and there is no shape of this record that has
/// them.</b> That is the whole design of the browsing surface rather than an omission: a list of a
/// neighbouring library is a way of looking through pictures, and where each was taken is a
/// question a different surface asks — the map, which reads positions from the library for a
/// rectangle and is the only path in this application that emits them. Keeping the two apart
/// structurally means the browse route has no coordinate to leak, no viewport to probe and no
/// count that moves with geography, and none of that depends on anybody remembering a rule.
/// </para>
/// <para>
/// A later phase decides what may be said about where a photograph was taken and to whom; whatever
/// it decides is decided on the surfaces that carry a position, and this record is not one of them.
/// </para>
/// </remarks>
/// <param name="PhotographId">
/// How the far side names the <em>photograph</em>. What the detail route takes. Mutable at the far
/// side's discretion — a library may re-mint an identifier when a file moves or comes back out of
/// its trash — so it is never stored, never a key, and never lives longer than the response it
/// travelled in.
/// </param>
/// <param name="Reference">
/// How the far side names the <em>picture</em> of it, which is not always how it names the
/// photograph: for one product it is the same string and for another it is a content hash. The
/// picture route takes this and never the identifier.
/// </param>
/// <param name="Title">The library's own title. Null where it has none.</param>
/// <param name="TakenAt">When the picture was taken, where the library says. Null where it does not.</param>
/// <param name="Kind">
/// Image, video, or <c>null</c> meaning <b>the library did not say</b>. Null never means image.
/// </param>
public readonly record struct LibraryListedPhoto(
    string PhotographId,
    string Reference,
    string? Title,
    DateTimeOffset? TakenAt,
    LibraryPhotoKind? Kind);

/// <summary>
/// What a library answered for one page of a listing.
/// </summary>
/// <param name="Photos">The photographs it reported, in the order it gave them.</param>
/// <param name="Total">
/// How many photographs <b>the library</b> holds for this request, as the library reported it, or
/// null where the product publishes no way to ask.
///
/// <para>
/// It is a fact about the library and not about the caller, and it is worth saying so here because
/// nothing in the number itself would show it: every account that may reach this feature reaches it
/// through one credential belonging to the installation, so there is exactly one answer and every
/// caller gets it. The day a caller's own identity decides what a library will show them, this is
/// one of the places that has to change, and it says so rather than being discovered.
/// </para>
/// <para>
/// Never synthesised by walking pages. A number that had to be computed to be reported is a number
/// that will be wrong the moment the library is large, and an unknown total said plainly is a
/// better answer than a guess a reader cannot tell from a fact.
/// </para>
/// </param>
/// <param name="HasMore">
/// Whether there is another page behind this one. Carried beside <paramref name="Total"/> rather
/// than derived from it, because one of the two products can answer this while answering nothing
/// about a total.
/// </param>
/// <param name="ReadAt">When this page was read from the library.</param>
public sealed record LibraryPhotoListPage(
    IReadOnlyList<LibraryListedPhoto> Photos,
    int? Total,
    bool HasMore,
    DateTimeOffset ReadAt);

/// <summary>
/// What is asked of a library for one page of a listing. Deliberately small, and deliberately
/// without a rectangle: see <see cref="LibraryListedPhoto"/>.
/// </summary>
/// <remarks>
/// It takes no words either, and that is not an omission. Asking a library what it holds and asking
/// it what it makes of a sentence are two different questions with two different answers — one of
/// these products replies to the second with an ordering over everything it holds rather than with
/// a narrowed listing — so they are two calls, and this is the one that has nothing to say about
/// words.
///
/// <para>
/// What it does take is a stretch of time, because that is a narrowing both products perform
/// themselves, over a fact they already hold about every photograph, and it is the same listing
/// either way: a page of the library, newest first, with fewer photographs in it. A search would
/// have been the wrong home for it — on one of the two products a search is an ordering of
/// everything rather than a narrowing of anything, so "the front of an ordering, restricted to a
/// weekend" is not a sentence with a meaning.
/// </para>
/// </remarks>
/// <param name="Page">One-based, as both products count pages and offsets from a page number here.</param>
/// <param name="PageSize">How many at most, already clamped to what this installation will ask for.</param>
/// <param name="Window">
/// The stretch of time to ask about, or null for the whole library.
///
/// <para>
/// A window and not a rectangle, and the difference is the whole reason one of these is allowed
/// here while the other is not: a moment says when a photograph was taken and not where, so
/// narrowing by it emits nothing about anybody's position and cannot be read backwards into one.
/// </para>
/// <para>
/// It is a window worked out on this side from a record this application holds, and never a pair
/// of dates somebody sent in. That is the point of it rather than a precaution: a window a caller
/// chooses is a date filter, and a date filter presented as the photographs of one trip is a claim
/// about where those pictures came from that nothing checked.
/// </para>
/// <para>
/// <b>What that does and does not guarantee, said plainly so it is not mistaken for more.</b> It
/// guarantees the shape of the request: no request anywhere carries a stretch of time, so no route
/// can be handed one. It does not make the dates unforgeable, because the record they are read
/// from is one somebody wrote — anybody who may create a trip may choose the days it says it
/// covered, and so may reach any window inside the span cap by writing a trip and asking about it.
/// That is worth two consequences being written down rather than rediscovered. The window is a
/// second way past the depth at which this application stops paging into a library, since a
/// narrower window reaches photographs that paging would have been refused for. And the day
/// something decides who may see what, the dates a trip carries are one of the inputs to it, which
/// is not obvious from a type that only ever sees two instants.
/// </para>
/// </param>
public sealed record LibraryPhotoQuery(int Page, int PageSize, LibraryPhotoWindow? Window = null);

/// <summary>
/// One photograph in full, as far as the library that holds it will say.
/// </summary>
/// <remarks>
/// Every field but the two identifiers is nullable and every one of them means <em>the library did
/// not say</em> rather than <em>empty</em>. The two products describe a photograph differently and
/// neither describes one completely, so a field nobody answered is marked absent and never filled
/// in from somewhere else: a camera model this application inferred would be a claim about
/// somebody's photograph that nobody made.
/// </remarks>
/// <param name="PhotographId">How the far side names the photograph. The value this was asked for.</param>
/// <param name="Reference">How the far side names the picture of it, for the delivery route.</param>
/// <param name="Title">The library's own title.</param>
/// <param name="Description">The library's own longer text about it, where it keeps one.</param>
/// <param name="TakenAt">When it was taken, where the library says.</param>
/// <param name="Kind">Image, video, or null meaning the library did not say.</param>
/// <param name="CameraMake">The camera's maker, as written in the picture.</param>
/// <param name="CameraModel">The camera, as written in the picture.</param>
/// <param name="Lens">The lens, as written in the picture.</param>
/// <param name="Aperture">The f-number, as the library states it.</param>
/// <param name="ShutterSpeed">The exposure time, as the library states it — a fraction, not a number.</param>
/// <param name="Iso">The sensitivity, as the library states it.</param>
/// <param name="FocalLengthMm">The focal length in millimetres, as the library states it.</param>
public sealed record LibraryPhotoDetail(
    string PhotographId,
    string Reference,
    string? Title,
    string? Description,
    DateTimeOffset? TakenAt,
    LibraryPhotoKind? Kind,
    string? CameraMake,
    string? CameraModel,
    string? Lens,
    double? Aperture,
    string? ShutterSpeed,
    int? Iso,
    double? FocalLengthMm);
