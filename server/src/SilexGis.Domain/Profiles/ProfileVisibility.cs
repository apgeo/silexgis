// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Profiles;

/// <summary>
/// Audience a user has chosen for one field of their own profile.
/// Stored as smallint; values are part of the schema contract — do not renumber.
/// </summary>
/// <remarks>
/// Deliberately a separate type from <see cref="Visibility"/>, which governs content objects:
/// <list type="bullet">
/// <item>there is no <c>Public</c> level, so no setting value can ever expose personal data to an
/// unauthenticated caller — the illegal state is unrepresentable rather than validator-rejected;</item>
/// <item><c>Team</c> means a different thing here. For content it means "members of the object's
/// own team"; for a profile it means "anyone who shares at least one team with the subject".</item>
/// </list>
/// </remarks>
public enum ProfileVisibility : short
{
    /// <summary>Only the subject.</summary>
    Private = 0,

    /// <summary>The subject, plus anyone sharing a team with them.</summary>
    Team = 1,

    /// <summary>Any signed-in user. Never anonymous callers.</summary>
    Authenticated = 2,
}
