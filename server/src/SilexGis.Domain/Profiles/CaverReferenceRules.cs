// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Profiles;

/// <summary>
/// How a written record says which person it means — by their entry in the directory, or by a
/// name for the directory to make sense of — and the shape either has to have.
/// </summary>
/// <remarks>
/// <para>
/// A record that names people is written by whoever was there, about people most of whom never
/// sign in. So it may name somebody the directory already holds, by that entry, or somebody it
/// may not hold yet, by their name. Which of the two a row is has to be unambiguous, because the
/// two are read differently afterwards: a row carrying both leaves no rule for which one wins,
/// and a row carrying neither names nobody.
/// </para>
/// <para>
/// One statement of it, for every record that names a person this way — the people on a trip,
/// the stays on a camp's roster. It is a rule about identity, and two statements of such a rule
/// agree only until one of them is edited: after that one surface accepts a row another refuses,
/// and the directory comes to hold an entry no screen would have let anybody make.
/// </para>
/// </remarks>
public static class CaverReferenceRules
{
    /// <summary>
    /// The longest name a record may give a person. It is the length the directory itself holds
    /// a name in, so a name accepted on a record always fits the entry it may become.
    /// </summary>
    public const int NameMaxLength = 200;

    /// <summary>
    /// Whether a row names exactly one person: an entry in the directory or a name, never both
    /// and never neither. A name of nothing but spaces is no name.
    /// </summary>
    public static bool NamesOnePerson(Guid? caverId, string? newCaverName) =>
        (caverId is not null) ^ !string.IsNullOrWhiteSpace(newCaverName);
}
