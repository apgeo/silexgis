// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Api.Common;

/// <summary>
/// The bound on how often a shared map view, and the data it draws, may be read from one address.
/// </summary>
/// <remarks>
/// <para>
/// Cost control, not a confidentiality control. The link's token is a random GUID and cannot be
/// guessed; what keeps the surface safe is what it declines to carry — public rows only, no
/// position placed exactly, no identifiers, and one 404 for every refusal. The limit exists
/// because these are anonymous routes that each run a spatial query, and a shared link travels to
/// places that crawlers read.
/// </para>
/// <para>
/// A window of its own rather than the published-trip surface's, because the shapes differ: a
/// followed trip is read once a minute, while a map asks again on every pan — the view and its
/// two data layers, a few requests per move. The default leaves room for a handful of people
/// browsing one shared map from behind a single connection; an installation behind a large shared
/// address raises it.
/// </para>
/// </remarks>
public static class SharedViewRateLimits
{
    /// <summary>The policy name, registered in the application's rate-limiter setup.</summary>
    public const string PolicyName = "shared-view";

    /// <summary>Requests a minute from one address, across the shared view and its data routes.</summary>
    public const string ConfigurationKey = "Map:SharedViewRateLimitPerMinute";

    /// <summary>The default for <see cref="ConfigurationKey"/>.</summary>
    public const int DefaultPerMinute = 240;
}
