// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Api.Auth;

/// <summary>
/// Demo sign-ins for a test installation (SILEXGIS__TestLogins__Enabled / __Password).
/// </summary>
/// <remarks>
/// When enabled, three accounts — a full administrator, an editor and a viewer — are seeded at
/// startup and their credentials are announced on the sign-in page to whoever reaches it. The
/// disclosure is the feature: a test installation wants strangers trying it out. It is exactly
/// why the switch defaults off and must never be set on an installation holding data anybody
/// cares about — anyone on the internet who can reach the page holds the administrator login.
/// </remarks>
public sealed class TestLoginOptions
{
    public const string SectionName = "TestLogins";

    public bool Enabled { get; set; }

    /// <summary>
    /// The password all three accounts get. It is a password only in the sense that the login
    /// form asks for one — the sign-in page prints it beside each account. An account that
    /// predates a change of this value is brought to the new one at the next start, so the
    /// page never shows a credential that does not work.
    /// </summary>
    public string Password { get; set; } = "test-login-pass-1";
}
