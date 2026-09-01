// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Infrastructure.Catalogue;

/// <summary>
/// How this installation reaches the Romanian community cave catalogue at speologie.org.
///
/// <para>
/// Off unless an operator supplies a key, and an installation that leaves it off is a supported
/// installation rather than a broken one: the catalogue is a Romanian register, most of what
/// this application is installed for has nothing to do with it, and a key is personal to whoever
/// obtained it. With no key the feature reports itself absent — its screens are not offered and
/// its endpoints say so plainly — instead of failing on first use.
/// </para>
/// <para>
/// The defaults here are deliberately timid. The catalogue is a volunteer-run service whose own
/// documentation asks not to be hammered, and this application has nothing to gain from being
/// fast against it: a person searching for a cave to import waits a second either way.
/// </para>
/// </summary>
public sealed class SpeologieOptions
{
    public const string SectionName = "Speologie";

    /// <summary>
    /// The personal API key, obtained from the catalogue's own profile page. Empty means the
    /// integration is off. Never committed — this is supplied through the environment or through
    /// the administrator settings screen, both of which keep it out of the repository.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Where the catalogue answers. Configurable so a test or a future move does not need a
    /// release, but the shipped value is the real one.
    /// </summary>
    /// <remarks>
    /// The <c>www</c> host is not decoration: the bare domain answers every request with a
    /// redirect to it, so pointing at the bare domain doubles every call for no reason.
    /// </remarks>
    public string Endpoint { get; set; } = "https://www.speologie.org/api/graphql";

    /// <summary>How long one call may take before it is given up on. Clamped when used.</summary>
    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>
    /// Shortest gap between two calls leaving this installation, in milliseconds. Calls are also
    /// made one at a time, so this is a true floor on the rate rather than a floor per caller.
    /// </summary>
    /// <remarks>
    /// One second is slow on purpose. The catalogue asks for restraint in its own documentation,
    /// and the cost of restraint here is a search that takes a moment; the cost of the opposite
    /// is somebody else's small service falling over.
    /// </remarks>
    public int MinRequestIntervalMs { get; set; } = 1000;

    /// <summary>
    /// How many times a call that failed for a reason that might pass is tried again. Bounded
    /// hard: a retry storm against a fragile service is worse than the error it was hiding.
    /// </summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>
    /// Largest page this installation will ask the catalogue for. The catalogue's own ceiling is
    /// 100 and it clamps silently, but this is checked here as well — a limit enforced only at
    /// the far end is a limit that stops existing the day the far end changes.
    /// </summary>
    public int MaxPageSize { get; set; } = 50;

    /// <summary>
    /// How many caves one import may create or refresh in a single confirmation. Small, because
    /// the wizard is for choosing caves rather than for taking the catalogue: a person selecting
    /// two hundred caves by hand has stopped reviewing them.
    /// </summary>
    public int MaxSelection { get; set; } = 100;

    /// <summary>
    /// How much of a description is kept. The catalogue's descriptions are usually a few hundred
    /// characters and occasionally over a megabyte of pasted markup; what is cut is marked as
    /// cut, and the page it came from is linked from the cave.
    /// </summary>
    public int MaxDescriptionChars { get; set; } = 20_000;

    /// <summary>
    /// What this installation calls itself when it asks. Sent so that the far end can tell one
    /// application's traffic from a scraper's, and reach somebody if it needs to.
    /// </summary>
    public string UserAgent { get; set; } = "SilexGIS";

    /// <summary>True when a key has been supplied and the integration can be used at all.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(Endpoint);

    /// <summary>The ceiling the far end enforces. Nothing here may ask for more, whatever it is configured with.</summary>
    public const int RemotePageCeiling = 100;

    /// <summary>The configured page size, clamped into what the far end will actually serve.</summary>
    public int EffectiveMaxPageSize => Math.Clamp(MaxPageSize, 1, RemotePageCeiling);
}
