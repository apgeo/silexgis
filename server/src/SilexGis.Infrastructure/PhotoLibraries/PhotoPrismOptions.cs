// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Infrastructure.PhotoLibraries;

/// <summary>
/// How this installation reaches a neighbouring PhotoPrism. Off unless an operator supplies an
/// address and a token, and an installation that leaves it off is a supported installation rather
/// than a broken one: nothing is asked of the library, and every surface reports it absent instead
/// of failing.
/// </summary>
public sealed class PhotoPrismOptions
{
    public const string SectionName = "PhotoLibraries:PhotoPrism";

    /// <summary>
    /// Whether such a library is deployed at all. Explicit rather than inferred from the address,
    /// for the reason the document converter already states: half a configuration is not a service,
    /// and a runtime state reading "enabled" for a container that is not in the deployment is a
    /// state nobody can explain.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Where it answers, reached over the internal network — a deployment-internal name rather than
    /// a public one. This address is never sent to a browser: pictures are proxied through this
    /// application, so no page ever names the library's host.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// A read-only access token, which is the mechanism that product's own documentation names for
    /// an external service: not tied to a person's session, and carrying a scope.
    /// </summary>
    /// <remarks>
    /// Two things about it belong in the install guide rather than in a comment nobody reads. Its
    /// default lifetime is a year, so it fails silently, once, twelve months after installation.
    /// And a read-only scope restrains only the questions asked in JSON: the picture routes check
    /// no scope at all, so scoping the token does not make the pictures read-only — it makes them
    /// irrelevant to the scope.
    /// </remarks>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>How long one call may take. Ours, not theirs. Clamped at use, so a nonsensical configured value cannot disable it.</summary>
    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>
    /// The most photographs asked for in one viewport. The library's own ceiling is a hundred
    /// thousand and it would serve them into a browser that cannot draw them; a map wants about a
    /// thousand, and the clamp that matters is the one applied before asking rather than the one
    /// applied to the answer.
    /// </summary>
    public int MaxViewportCount { get; set; } = 2_000;

    /// <summary>
    /// The minimum quality score asked for. Zero means everything the library holds. Sent on every
    /// call because the operation declares it required alongside the count, and what the far end
    /// does when it is absent has not been established.
    /// </summary>
    public int MinQuality { get; set; }

    /// <summary>
    /// How many times a call that failed for a reason that might pass is tried again. Applies to
    /// questions asked in JSON only. A request for picture bytes is never retried whatever this
    /// says, because a library that cannot reach its originals destroys its own index while
    /// answering one.
    /// </summary>
    public int MaxRetries { get; set; } = 1;

    /// <summary>Whether an operator has supplied both an address and a token.</summary>
    public bool IsConfigured => Enabled
        && !string.IsNullOrWhiteSpace(BaseUrl)
        && !string.IsNullOrWhiteSpace(AccessToken);
}
