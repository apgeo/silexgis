// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.PhotoLibraries;

/// <summary>
/// How a neighbouring library answers words: by matching them against text somebody wrote down, or
/// by ranking what it holds according to how close the pictures are to what the words describe.
///
/// <para>
/// The distinction is published rather than assumed because it decides what a person is being
/// invited to type. A box reading "describe the picture" over a library that matches words
/// literally is a promise the far side cannot keep — somebody types "muddy crawl", nothing comes
/// back, and the conclusion they draw is that the library is empty rather than that they asked the
/// wrong kind of question.
/// </para>
/// <para>
/// It is a fact about the product rather than about the caller. Every account reaching this
/// integration reaches a library through one credential belonging to the whole installation, so
/// there is one answer to give and every caller gets it.
/// </para>
/// </summary>
public enum LibrarySearchMatching
{
    /// <summary>
    /// The words are matched against what the library has written down about a photograph — its
    /// title, its caption, its keywords and whatever labels it wrote itself. A word that is not
    /// there does not match, however well it describes the picture.
    /// </summary>
    Text = 0,

    /// <summary>
    /// The words are turned into a description of an image and everything the library holds is
    /// ordered by how close it is to that description. Nothing is matched and nothing is excluded:
    /// what comes back is the front of an ordering over the whole library.
    /// </summary>
    Meaning = 1,
}

/// <summary>
/// What is asked of a library when somebody has typed words. Deliberately without a rectangle, a
/// date window or a filter of any kind: this asks the far side its own question and narrows
/// nothing afterwards.
/// </summary>
/// <param name="Text">
/// The words, already trimmed and already found to be within the length this installation will put
/// in a request to a neighbour. Never empty — a search route with nothing to search for is a
/// listing, and the listing is a different question with a different answer.
/// </param>
/// <param name="Page">One-based.</param>
/// <param name="PageSize">How many at most, already clamped to what this installation will ask for.</param>
public sealed record LibraryPhotoSearchQuery(string Text, int Page, int PageSize);

/// <summary>
/// What a library answered for one page of a search.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no total here, and the absence is the design.</b> Neither of the products behind
/// this can say how many photographs match a set of words. One of them ranks everything it holds
/// by closeness to what the words describe, so there is no set of matches to count — every
/// photograph is in the ordering and what comes back is its front. The other matches text and
/// counts only the page it has just sent, which is a number the page already is. A total on this
/// surface would therefore be a number this application invented, and a reader cannot tell an
/// invented number from a counted one.
/// </para>
/// <para>
/// What is honest is what is here: how many came back, whether the library says there are more,
/// and — in <see cref="Matching"/> — which of the two questions was actually asked, so a screen
/// can say what its count is a count of instead of writing "results" over both.
/// </para>
/// </remarks>
/// <param name="Photos">What came back, in the order the library gave it.</param>
/// <param name="Matching">
/// Which of the two questions was put, which is decided by the route this integration called and
/// not by anything the far side reported about itself. Each product offers exactly one search, so
/// this follows from which product answered.
///
/// <para>
/// Nothing here probes whether the far side is currently able to do what its contract offers, and
/// the omission is deliberate rather than pending: on the product that ranks by meaning, whether
/// picture recognition is switched on is readable only by an administrator <em>of that product</em>,
/// and the one credential this installation holds need not be one. A guess dressed as a
/// capability check would be worse than the honest limit.
/// </para>
/// <para>
/// So this never silently becomes the other value. A library that ranks by meaning with its
/// recognition switched off does not quietly start matching text — it ranks nothing, or refuses the
/// question — and both of those are reported as themselves, by an empty ordering and by a failure,
/// rather than by this saying something else happened.
/// </para>
/// </param>
/// <param name="Searched">
/// The words actually put to the library, which are not always the words somebody typed: one of
/// these products reads a colon as naming one of its own fields, so the separators are taken out
/// before the text is sent and what is left is words. Published because a search whose text was
/// changed on the way out answers a different question from the one on the screen, and a reader
/// with no way to see the difference concludes the library disagrees with its own search box.
/// Empty when the reduction left nothing, which is the one case where no library was asked at all.
/// </param>
/// <param name="HasMore">
/// Whether the library says there is another page behind this one. For a ranking that means the
/// ordering continues, not that more photographs match — nothing was matched.
/// </param>
/// <param name="ReadAt">
/// When this answer was read from the library, or null when no library was asked — which happens
/// when the words reduced to nothing this product could search for. A time stamped for a reading
/// that never happened is a false statement of fact on the one line a reader would use to decide
/// whether the far side was reached at all.
/// </param>
public sealed record LibraryPhotoSearchPage(
    IReadOnlyList<LibraryListedPhoto> Photos,
    LibrarySearchMatching Matching,
    string Searched,
    bool HasMore,
    DateTimeOffset? ReadAt);
