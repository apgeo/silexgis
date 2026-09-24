// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Api.Features.TripTracking;

/// <summary>
/// The bound on how often the published-trip surface may be read from one address.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this is, and what it is deliberately not.</b> It is cost control on the only routes in
/// this application that are anonymous, uncached and back a whole database-derived envelope — a
/// party, its teams, every one of their positions folded from the trip's report log, and for the
/// followed route a freshly signed delivery URL. The workflow this application hands out puts such
/// an address into a club's own article, which is indexable and archivable, so the request rate is
/// set by strangers and crawlers rather than by anything this installation controls. Before this
/// existed, a single unauthenticated address could be read as fast as a network allowed.
/// </para>
/// <para>
/// It is <b>not</b> a confidentiality control and must never be argued as one. The token is 32
/// random bytes and unguessable, so there is no space to exhaust and nothing a limiter protects
/// that the token does not already. What keeps this surface safe is what it declines to carry —
/// no caver identity, no survey identity, no cave identity, a refused publication for a protected
/// cave, and one indistinguishable 404 for every failure. A limiter tightened in the belief that it
/// is guarding a secret would be tightened against the wrong threat, and a page that a club's
/// members cannot all read at once is a real cost paid for nothing.
/// </para>
/// <para>
/// <b>Why a window of its own rather than the sign-in surface's.</b> That one is sized against
/// password guessing, and these two surfaces have opposite shapes: signing in is a handful of
/// requests from one person, while a followed page is read repeatedly, by many people at once, for
/// as long as a party is underground. Sharing a partition would let a club watching a rescue from
/// behind one connection spend the sign-in allowance of everybody else behind it — the same argument
/// the printed-code route already makes for having its own.
/// </para>
/// <para>
/// <b>Sized against the page that reads it.</b> The followed page asks once a minute while a watch
/// is armed and stops when it closes, so a reader costs about one request a minute per route and a
/// page reading the envelope and the list of current parties costs two. The default therefore leaves
/// room for on the order of sixty simultaneous readers behind one address, which is past any club
/// and well short of unlimited. Installations behind a large shared address can raise it, exactly as
/// they can for the sign-in surface.
/// </para>
/// </remarks>
internal static class PublicTripRateLimits
{
    /// <summary>The policy name, registered in the application's rate-limiter setup.</summary>
    internal const string PolicyName = "public-trip";

    /// <summary>
    /// Requests a minute from one address, across the whole published-trip surface.
    /// </summary>
    /// <remarks>
    /// One budget for all four routes rather than one each, on purpose: a page reads the envelope,
    /// the current parties and the archive together, and what needs bounding is what that page costs
    /// altogether. Per-route budgets would sum to something nobody chose.
    /// </remarks>
    internal const int DefaultPerMinute = 120;

    /// <summary>Where an installation overrides it.</summary>
    internal const string ConfigurationKey = "TripTracking:PublicRateLimitPerMinute";
}
