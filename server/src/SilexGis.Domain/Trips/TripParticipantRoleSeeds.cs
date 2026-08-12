// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Trips;

/// <summary>One shipped participant-role row: the code installations exchange it by, and the
/// English name a client without wording of its own falls back to.</summary>
public readonly record struct TripParticipantRoleSeed(string Code, string Name);

/// <summary>
/// The shipped vocabulary of what somebody did on a trip — the single home for which codes
/// ship. The startup seeder inserts these rows, and the admin surface consults the same list to
/// refuse code changes and deletion on them: shipped codes are what clients translate labels by
/// and what installations exchange records under, so a renamed or deleted code would silently
/// break both. Rows a club adds are installation-local and fully editable.
/// </summary>
public static class TripParticipantRoleSeeds
{
    /// <summary>
    /// Being on a trip at all. The role a name carries when nothing else is said, and the one
    /// the roster falls back to, so it must exist for a trip to record anybody.
    /// </summary>
    public const string ParticipantCode = "participant";

    /// <summary>
    /// Whoever put the trip forward. Named as a constant because it is about to become the
    /// answer to "who may edit this plan": a row an administrator could delete would take that
    /// answer with it, which is why this code and <see cref="ParticipantCode"/> are shipped.
    /// </summary>
    public const string ProposerCode = "proposer";

    /// <summary>
    /// Append only. The seeder derives a row's sort order from its position here and never
    /// re-sorts a row that already exists, so a code inserted in the middle takes a different
    /// sort order on a fresh installation than on one being upgraded — silently, and only the
    /// vocabulary's display order gives it away.
    /// </summary>
    public static readonly IReadOnlyList<TripParticipantRoleSeed> All =
    [
        new(ParticipantCode, "Participant"),
        new(ProposerCode, "Proposer"),
        new("leader", "Leader"),
        new("driver", "Driver"),
        new("surveyor", "Surveyor"),
        new("photographer", "Photographer"),
        new("trainee", "Trainee"),
        new("instructor", "Instructor"),
        new("callout_contact", "Callout contact"),
    ];

    private static readonly HashSet<string> Codes = [.. All.Select(s => s.Code)];

    /// <summary>Whether a code names a shipped row (code immutable, row undeletable).</summary>
    public static bool IsSeeded(string code) => Codes.Contains(code);
}
