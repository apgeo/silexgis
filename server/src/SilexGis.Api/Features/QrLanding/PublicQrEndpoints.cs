// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.QrLanding;

/// <summary>
/// Resolving a code printed on a label for somebody who is not signed in.
/// </summary>
/// <remarks>
/// <para>
/// This is the first anonymous route on this server whose address can be guessed. Every other
/// one is opened either by a token with enough randomness in it that holding the token is the
/// whole claim, or by a deliberate publication of an object as a whole. A code on a cave wall is
/// neither: it is short, it is meant to be transcribed by a human, and it is reproducible from
/// the same inputs by anybody who holds them. So the code is an address and never a capability,
/// and the only thing between the public and an answer is a decision somebody took about the
/// cave and can take back.
/// </para>
/// <para>
/// Because the address can be guessed, the safety cannot come from the address. It comes from
/// the answer: a code that resolves says which installation it resolves at and nothing else, so
/// walking the entire space of codes yields one number — how many are published — and no way to
/// attach any of them to a cave, a place or a position. The limiter below is there to keep a
/// scanning script off the database, not to keep a secret; at any rate a person would tolerate,
/// a short code space is exhaustible, and saying otherwise would be a claim the first arithmetic
/// anybody did would refute.
/// </para>
/// </remarks>
public static class PublicQrEndpoints
{
    /// <summary>
    /// The one answer for a code that does not resolve. A code that is not a code, a code
    /// nobody ever issued, a code on a cave nobody published, and a code on a cave somebody
    /// published and then withdrew are four different facts about the world and one answer
    /// here, byte for byte — because telling them apart is telling somebody which codes exist
    /// and which caves an installation holds, which is the entire question this route is not
    /// allowed to answer.
    /// </summary>
    internal const string NotFoundCode = "qr.not_found";

    /// <summary>
    /// The rate-limit policy this route runs under. Its own, deliberately not the one guarding
    /// the sign-in surface: that one is sized against password guessing and is partitioned per
    /// address, so sharing it would let a group scanning labels at a cave entrance, all behind
    /// one connection, spend the sign-in allowance of everyone else behind it.
    /// </summary>
    internal const string RateLimitPolicy = "public-qr";

    /// <summary>
    /// The longest code the device that prints them can store. Anything longer cannot be one, so
    /// it is refused before it reaches the database — with the same answer everything else that
    /// does not resolve gets, because the caller learning that their input was the wrong shape
    /// would be the caller learning something.
    /// </summary>
    private const int MaxCodeLength = 64;

    public static RouteGroupBuilder MapPublicQrEndpoints(this RouteGroupBuilder api)
    {
        var publicQr = api.MapGroup("/public").WithTags("QrLanding");

        // On the anonymous allow-list by intent, not by accident: a person standing at a cave
        // with a phone has no account and is not going to make one to find out whether the
        // label in front of them means anything.
        publicQr.MapGet("/qr/{code}", ResolveAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicy)
            .WithSummary("Resolves a printed cave code for a visitor who is not signed in.");

        return api;
    }

    private static async Task<Results<Ok<PublicQrDto>, ProblemHttpResult>> ResolveAsync(
        string code,
        SilexGisDbContext db,
        IOptions<AboutOptions> about,
        CancellationToken ct)
    {
        // Trimmed and lowered here rather than in the query, matching what the device does when
        // it reads a code back, so the two ends agree about what "the same code" means.
        var normalized = code.Trim().ToLowerInvariant();
        if (normalized.Length == 0 || normalized.Length > MaxCodeLength)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        return await QrLandingSql.ResolvesAsync(db, normalized, ct)
            ? TypedResults.Ok(new PublicQrDto(about.Value.InstanceName))
            : ApiProblems.NotFound(NotFoundCode);
    }
}
