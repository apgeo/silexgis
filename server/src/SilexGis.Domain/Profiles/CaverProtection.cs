// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Profiles;

/// <summary>What a caller may see of one caver, decided in one place.</summary>
public sealed record PublicCaver(
    Guid Id,
    string FullName,
    Guid? UserId,
    string? Email,
    string? Phone,
    string? Notes);

/// <summary>
/// Server-side protection of roster contact data, beside <see cref="ProfileProtection"/> which
/// governs the same person's account profile.
/// </summary>
/// <remarks>
/// <para>
/// The roster has two tiers. A caver's <b>name</b> is the label every attribution row needs and is
/// readable by any signed-in caller, exactly as a club's member list has always been. Their
/// <b>contact details</b> are not: for someone with an account the account holder's own per-field
/// settings decide — the caller passes in that account's already-projected profile, so the roster
/// serves exactly what the profile would and can never become a way around the choices made there
/// (the caver row's own contact columns are not served for a linked person at all). For someone
/// without an account only whoever keeps the roster may read them, since nobody else consented on
/// their behalf. Roster remarks are always roster-keeper-only — they are written about a person,
/// not by them.
/// </para>
/// <para>
/// Nothing here is ever served to an anonymous caller, and administrators get no bypass, matching
/// the profile rule this sits beside.
/// </para>
/// </remarks>
public static class CaverProtection
{
    /// <summary>
    /// Projects a caver down to what the caller may see. <paramref name="accountProfile"/> is the
    /// linked account's profile as already projected for this caller (null for a caver with no
    /// account) — its fields already honour the holder's visibility settings.
    /// </summary>
    public static PublicCaver Project(Caver caver, bool canKeepRoster, PublicProfile? accountProfile) => new(
        caver.Id,
        Label(caver.FullName, accountProfile?.Label),
        caver.UserId,
        caver.UserId is null ? (canKeepRoster ? caver.Email : null) : accountProfile?.Email,
        caver.UserId is null ? (canKeepRoster ? caver.Phone : null) : accountProfile?.PhoneNumber,
        canKeepRoster ? caver.Notes : null);

    /// <summary>
    /// The roster name for a person, preferring their account's chosen label so one person is not
    /// shown under two different names on the same page.
    /// </summary>
    public static string Label(string fullName, string? accountLabel) =>
        string.IsNullOrWhiteSpace(accountLabel) ? fullName : accountLabel;
}
