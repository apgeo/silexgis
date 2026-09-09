// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Infrastructure.Grottocenter;

/// <summary>
/// How this installation reaches the international community cave database at
/// grottocenter.org, and whether it reaches it at all.
/// </summary>
/// <remarks>
/// <para>
/// <b>Off unless an operator turns it on, and off is a supported installation rather than a
/// broken one.</b> This is the only part of the application that makes a request to a service
/// nobody here controls on behalf of a signed-in person, and the name of a cave in the request
/// is a small disclosure whether or not the far end keeps it. An operator who has not decided
/// to make that disclosure has not made it: with this off nothing is registered, no screen
/// offers it, and the endpoint says plainly that the installation does not do this rather than
/// failing on first use.
/// </para>
/// <para>
/// The defaults are timid for the same reason the neighbouring catalogue client's are: the far
/// end is a volunteer-run register, and a lookup a person is watching costs nothing by waiting
/// a second.
/// </para>
/// </remarks>
public sealed class GrottocenterOptions
{
    public const string SectionName = "Grottocenter";

    /// <summary>
    /// Whether this installation may call grottocenter.org at all. False, and deliberately
    /// false: an integration that reaches somebody else's service is opted into.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Where the database answers. Configurable so a test or a future move needs no release,
    /// but the shipped value is the real one.
    /// </summary>
    public string Endpoint { get; set; } = "https://api.grottocenter.org/api/v1";

    /// <summary>
    /// Where a person is sent to read the entry a stored identifier names. Kept beside the API
    /// address because the two move together and neither is derivable from the other.
    /// </summary>
    public string EntryUrlTemplate { get; set; } = "https://www.grottocenter.org/ui/entry/{0}";

    /// <summary>How long one call may take before it is given up on. Clamped when used.</summary>
    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// Shortest gap between two calls leaving this installation, in milliseconds. Calls are
    /// also made one at a time, so this is a floor on the rate rather than a floor per caller.
    /// </summary>
    public int MinRequestIntervalMs { get; set; } = 1000;

    /// <summary>How many candidates one lookup offers. A person picking from a long list has stopped reading it.</summary>
    public int MaxCandidates { get; set; } = 10;

    /// <summary>
    /// What this installation calls itself when it asks, so the far end can tell an
    /// application's traffic from a scraper's and reach somebody if it needs to.
    /// </summary>
    public string UserAgent { get; set; } = "SilexGIS";

    /// <summary>True when the integration is on and has somewhere to call.</summary>
    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(Endpoint);

    /// <summary>The page a stored identifier stands for, or null when the template says nothing.</summary>
    public string? EntryUrl(string externalId) =>
        string.IsNullOrWhiteSpace(EntryUrlTemplate) || string.IsNullOrWhiteSpace(externalId)
            ? null
            : string.Format(System.Globalization.CultureInfo.InvariantCulture, EntryUrlTemplate, externalId);
}
