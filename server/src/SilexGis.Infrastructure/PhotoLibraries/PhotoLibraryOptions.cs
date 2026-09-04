// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.PhotoLibraries;

namespace SilexGis.Infrastructure.PhotoLibraries;

/// <summary>
/// What this installation has decided about neighbouring photo libraries as a whole, as opposed to
/// how it reaches any one of them.
/// </summary>
/// <remarks>
/// <para>
/// Every value here comes from the environment rather than from an administration screen, and the
/// audience is a deliberate exception to the usual rule that a policy decision belongs on a screen.
/// The reason is what the switch does: it opens a library this application applies none of its own
/// protection to — the photographs there are governed by that product's rules, not by this one's —
/// to every account in the installation. A control on a page is a mistake one click away; a
/// variable takes a deliberate redeploy by the same person who decided what the far side's service
/// account can see in the first place.
/// </para>
/// </remarks>
public sealed class PhotoLibraryOptions
{
    public const string SectionName = "PhotoLibraries";

    /// <summary>Who may see them. Administrators unless an operator says otherwise.</summary>
    public PhotoLibraryAudience Audience { get; set; } = PhotoLibraryAudience.Administrators;
}
