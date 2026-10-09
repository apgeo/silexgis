// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Trips;

/// <summary>
/// Which of a person's names is shown where a name has little room.
/// </summary>
/// <remarks>
/// <para>
/// A caver carries two: the roster name, which says who they are, and what their party calls
/// them, which may be a first name or a nickname. A marker drawn over a survey and a line of a
/// published log have room for the second and are read by people who know the party — nine full
/// names stacked in a shaft are a drawing nobody can read.
/// </para>
/// <para>
/// <b>One rule in one place, because three surfaces answer it</b> — the followed page, the cave's
/// other parties on that page, and a finished trip played back — and a reader moving between them
/// must not see the same person called two things. A second copy of this choice is how one of
/// those pages would go on showing the long name after the other two had stopped.
/// </para>
/// <para>
/// It decides only <em>which</em> name. Whether a name may be shown at all is settled before this
/// is asked — by the installation's setting, and by an administrator's own caption for a trip,
/// which outranks both of these.
/// </para>
/// </remarks>
public static class TripPartyNames
{
    /// <summary>
    /// The short name where one has been given, otherwise the roster name.
    /// </summary>
    /// <remarks>
    /// A short name of only spaces is no short name: it would draw a marker with nothing on it,
    /// which reads as a person the page was told not to name.
    /// </remarks>
    public static string Shown(string fullName, string? shortName) =>
        string.IsNullOrWhiteSpace(shortName) ? fullName : shortName.Trim();
}
