// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.PhotoLibraries;
using SilexGis.Infrastructure.PhotoLibraries;

namespace SilexGis.Api.Features.PhotoLibraries;

/// <summary>
/// Putting words to a neighbouring photo library and showing what it makes of them.
///
/// <para>
/// <b>The far side answers, and nothing is narrowed here afterwards.</b> That is the only order
/// available: neither product will accept a list of identifiers to search within, so there is no
/// way to ask either of them about a chosen subset, and a subset chosen here after the answer
/// arrived would be a subset of whatever the far end happened to return rather than of what it
/// holds. This route therefore passes the words on, publishes what came back, and claims nothing
/// about what did not.
/// </para>
/// <para>
/// <b>The two products answer a completely different question, and the answer says which.</b> One
/// matches the words against what somebody wrote down — a title, a caption, a keyword, a label its
/// own classifier produced — so a word that is not written anywhere finds nothing however well it
/// describes the picture. The other turns the words into a description of an image and orders
/// everything it holds by closeness to that description, matching nothing and excluding nothing.
/// A surface that offered both under one sentence would be inviting somebody to describe a
/// photograph to a library that can only look up words, and the empty answer they got back would
/// read as an empty library.
/// </para>
/// <para>
/// <b>So there is no total, and there cannot be one.</b> A ranking has no set of matches to count,
/// and the product that does match text counts only the page it has just sent. What is published is
/// how many came back and whether the library says the answer continues — the same discipline every
/// other bounded list in this application follows, applied to a bound somebody else chose.
/// </para>
/// <para>
/// Nothing is stored and nothing is held between calls, and no position of any kind is carried:
/// this is the browsing surface with words in front of it, and where a photograph was taken is the
/// map's question.
/// </para>
/// </summary>
public static class PhotoLibrarySearchEndpoints
{
    /// <summary>
    /// A search was asked for with nothing to search for. Refused rather than answered with the
    /// library, because a page of everything under a heading saying it matched what somebody typed
    /// is the one answer this surface must never give.
    /// </summary>
    public const string SearchEmptyCode = "photo_library.search_empty";

    /// <summary>Words longer than this installation will put in a request to a neighbour.</summary>
    public const string SearchTooLongCode = "photo_library.search_too_long";

    /// <summary>
    /// The longest run of words passed to a library's own search. Long enough for a sentence and
    /// short enough that nothing unbounded is put into a request to a neighbour.
    /// </summary>
    public const int MaxSearchLength = 200;

    public static RouteGroupBuilder MapPhotoLibrarySearchEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        var libraries = api.MapGroup("/photo-libraries").WithTags("PhotoLibraries");

        libraries.MapGet("/{source}/search", SearchAsync)
            .WithSummary(
                "One page of what a neighbouring library makes of a set of words, and which of the "
                + "two questions it answered — matching text, or ordering by meaning. No total: "
                + "neither product counts what a sentence matches.");

        return api;
    }

    /// <summary>
    /// What one library makes of a set of words.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The words are checked here for being words at all and for being short enough to put in a
    /// request to somebody else's server, and everything else about them is the far side's business.
    /// In particular they are not parsed: what a sentence means is what the library decides it
    /// means, and a client that guessed at the library's grammar here would be a second, wrong copy
    /// of it.
    /// </para>
    /// <para>
    /// An empty search is refused rather than turned into a listing. The listing is a route of its
    /// own and it says what it is; a search that quietly became one would answer words that were
    /// never sent with a full page of the library, which is precisely the mistake this whole
    /// surface is arranged to avoid.
    /// </para>
    /// </remarks>
    private static async Task<Results<Ok<LibraryPhotographSearchPageDto>, UnauthorizedHttpResult, ProblemHttpResult>> SearchAsync(
        string source,
        string? q,
        int? page,
        int? pageSize,
        IEnumerable<IPhotoLibrary> libraries,
        ILibraryPhotoTokenService tokens,
        IOptions<PhotoLibraryOptions> options,
        IAccessContextAccessor accessAccessor,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!PhotoLibraryAudienceRule.MayRead(ctx, options.Value.Audience))
        {
            return ApiProblems.Forbidden(PhotoLibraryAudienceRule.ForbiddenCode);
        }

        if (!PhotoLibrarySlugs.TryParse(source, out var which))
        {
            return ApiProblems.NotFound(PhotoLibraryEndpoints.NotFoundCode);
        }

        var library = libraries.FirstOrDefault(l => l.Source == which);
        if (library is null || !library.IsConfigured)
        {
            // A library nobody has configured is absent rather than broken, and no socket is opened
            // on the way to saying so.
            return ApiProblems.NotFound(PhotoLibraryEndpoints.NotFoundCode);
        }

        var text = q?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return ApiProblems.BadRequest(
                SearchEmptyCode, "A search of a photo library needs something to search for.");
        }

        if (text.Length > MaxSearchLength)
        {
            return ApiProblems.BadRequest(
                SearchTooLongCode,
                $"A search of at most {MaxSearchLength} characters is passed to a photo library.");
        }

        var wanted = pageSize ?? PhotoLibraryBrowseEndpoints.DefaultPageSize;
        var size = Math.Clamp(wanted, 1, PhotoLibraryBrowseEndpoints.MaxPageSize);
        var number = Math.Max(1, page ?? 1);

        LibraryPhotoSearchPage answer;
        try
        {
            answer = await library.SearchAsync(new LibraryPhotoSearchQuery(text, number, size), ct);
        }
        catch (PhotoLibraryException e)
        {
            // A library that did not answer fails the request rather than arriving as a page with
            // nothing on it. The two are the states hardest to tell apart on this surface and the
            // ones a reader most needs told apart: one means nothing here matches those words, and
            // the other means nobody asked anything successfully — and a search that silently
            // returned nothing would send somebody looking for better words for a library that is
            // stopped.
            loggerFactory.CreateLogger(typeof(PhotoLibrarySearchEndpoints)).LogWarning(
                e, "The {Source} photo library did not answer a search.", which);

            return ApiProblems.ServiceUnavailable(e.Code, e.Message);
        }

        // Minted once for the whole page, and only for a caller the audience rule has just admitted.
        var picturesAvailable = library.PicturesAvailable;
        var template = picturesAvailable
            ? LibraryPictureAddress.Template(which, tokens.CreateToken(which))
            : null;

        return TypedResults.Ok(LibraryPhotographSearchPageDto.Of(
            answer, which, number, size, wanted > size, picturesAvailable, template));
    }
}
