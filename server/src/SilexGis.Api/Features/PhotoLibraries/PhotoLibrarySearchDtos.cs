// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.PhotoLibraries;

namespace SilexGis.Api.Features.PhotoLibraries;

/// <summary>
/// One page of what a neighbouring library made of a set of words.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no total on this record, and the absence is the design rather than a gap waiting to
/// be filled.</b> Neither of the products this application reads can say how many photographs match
/// a sentence. One of them ranks everything it holds by how close each picture is to what the words
/// describe: nothing is matched, nothing is excluded, and what comes back is the front of an
/// ordering over the whole library — so there is no set to count. The other matches text and counts
/// only the page it has just sent, which is a number the page already is. A total here would
/// therefore be a number this application invented, and nothing on a screen distinguishes an
/// invented number from a counted one.
/// </para>
/// <para>
/// What is honest is what is carried: how many came back, whether the library says the answer
/// continues, and which of the two questions was actually asked — so a surface can say what its
/// count is a count of instead of writing "results" over both.
/// </para>
/// <para>
/// It has no latitude and no longitude either, for the same reason the listing has none: a search
/// of a neighbouring library is a way of looking through pictures, and where each was taken is the
/// map's question.
/// </para>
/// </remarks>
/// <param name="Source">Which library answered, as the address names it.</param>
/// <param name="LibraryName">What to call it on a screen, echoed so a page needs no second request.</param>
/// <param name="Matching">
/// <c>text</c> when the library matched the words against what somebody had written down about a
/// photograph, <c>meaning</c> when it ordered what it holds by how close the pictures are to what
/// the words describe.
///
/// <para>
/// Set from the question this integration put and the answer it got, rather than from anything the
/// library says about itself: on one of the two products, whether picture recognition is switched
/// on at all is readable only by an administrator of that product, and a credential belonging to
/// this installation is not one. So this reports what happened, which is the only thing this
/// application is in a position to know.
/// </para>
/// </param>
/// <param name="Items">What came back, in the order the library put it — which for a ranking is the order that is the answer.</param>
/// <param name="Page">Which page this is, one-based.</param>
/// <param name="PageSize">How many were asked for, after this installation's own cap.</param>
/// <param name="HasMore">
/// Whether the library says there is another page behind this one. For a ranking that means the
/// ordering continues rather than that more photographs match — nothing was matched.
/// </param>
/// <param name="PageSizeCapped">
/// True when the caller asked for a larger page than this installation is willing to ask a library
/// for. A shortened page that does not say it was shortened is a wrong answer.
/// </param>
/// <param name="PicturesAvailable">Whether this library may be asked for image bytes at all.</param>
/// <param name="PictureUrlTemplate">
/// Where one photograph's rendering is fetched from, with <c>{reference}</c> and <c>{size}</c> to
/// substitute. Null when this library must not be asked for pictures.
/// </param>
/// <param name="ReadAt">When this answer was read from the library.</param>
public sealed record LibraryPhotographSearchPageDto(
    string Source,
    string LibraryName,
    string Matching,
    IReadOnlyList<LibraryPhotographDto> Items,
    int Page,
    int PageSize,
    bool HasMore,
    bool PageSizeCapped,
    bool PicturesAvailable,
    string? PictureUrlTemplate,
    DateTimeOffset ReadAt)
{
    public static LibraryPhotographSearchPageDto Of(
        LibraryPhotoSearchPage answer,
        PhotoLibrarySource source,
        int page,
        int pageSize,
        bool pageSizeCapped,
        bool picturesAvailable,
        string? pictureUrlTemplate)
    {
        ArgumentNullException.ThrowIfNull(answer);

        return new(
            PhotoLibrarySlugs.Slug(source),
            PhotoLibrarySlugs.Name(source),
            LibraryPhotographMapping.MatchingSlug(answer.Matching),
            [.. answer.Photos.Select(LibraryPhotographMapping.Of)],
            page,
            pageSize,
            answer.HasMore,
            pageSizeCapped,
            picturesAvailable,
            pictureUrlTemplate,
            answer.ReadAt);
    }
}
