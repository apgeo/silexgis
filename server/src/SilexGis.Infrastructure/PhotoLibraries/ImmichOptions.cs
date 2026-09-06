// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Infrastructure.PhotoLibraries;

/// <summary>
/// How this installation reaches a neighbouring Immich. Off unless an operator supplies an address
/// and a key, and an installation that leaves it off is a supported installation rather than a
/// broken one: nothing is asked of the library, and every surface reports it absent instead of
/// failing.
/// </summary>
public sealed class ImmichOptions
{
    public const string SectionName = "PhotoLibraries:Immich";

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
    /// The key this library's own settings screen issues, sent on every request as a header.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What belongs in the install guide rather than in a comment nobody reads, but which shapes
    /// this whole integration: <b>the key is the account it was minted for</b>. It carries that
    /// account's entire reach over that library, cannot be narrowed to an album or a photograph,
    /// and does not expire. So the account it belongs to should be one made for this purpose, and
    /// what is shared to that account is the operator's only lever over what this application can
    /// see at all.
    /// </para>
    /// <para>
    /// It also cannot be replaced through the library's own interface: the version this
    /// integration is written against publishes no operation that mints a replacement in place, so
    /// changing the key means issuing a new one, re-pointing this setting at it and deleting the
    /// old one by hand — with a window in which both are live. Nothing here may be written as
    /// though a key could be rolled over automatically.
    /// </para>
    /// </remarks>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>How long one call may take. Ours, not theirs — their own request timeout is a day. Clamped at use, so a nonsensical configured value cannot disable it.</summary>
    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>
    /// How long one reading of the whole located library is used before it is read again.
    /// </summary>
    /// <remarks>
    /// This library publishes no way to ask what is inside a rectangle, so a viewport is answered
    /// by holding every located photograph's position in memory and scanning it. Zero means every
    /// viewport re-reads the whole library and waits for it, which is a real setting for a very
    /// small one rather than a switch that only makes sense in a test.
    /// </remarks>
    public int PositionCacheSeconds { get; set; } = 300;

    /// <summary>
    /// The most located photographs this installation will hold in memory. The far end applies no
    /// limit of its own to the answer this reads — no page size, no offset, no rectangle — so this
    /// is the only limit there is. Thirty-two bytes each, so the default is eight megabytes.
    /// </summary>
    /// <remarks>
    /// A library holding more than this is refused rather than truncated. Truncating would drop
    /// whichever photographs happened to arrive last and draw a map that is silently missing whole
    /// regions, which is a worse answer than an honest refusal and one nobody can see is wrong.
    /// </remarks>
    public int MaxPositions { get; set; } = 250_000;

    /// <summary>
    /// How many times a call that failed for a reason that might pass is tried again. Applies to
    /// questions asked in JSON only. A request for picture bytes is never retried whatever this
    /// says, because a library that cannot reach its originals can destroy its own index while
    /// answering one.
    /// </summary>
    public int MaxRetries { get; set; } = 1;

    /// <summary>Whether an operator has supplied both an address and a key.</summary>
    public bool IsConfigured => Enabled
        && !string.IsNullOrWhiteSpace(BaseUrl)
        && !string.IsNullOrWhiteSpace(ApiKey);
}
