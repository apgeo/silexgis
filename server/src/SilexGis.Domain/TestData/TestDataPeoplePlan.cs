// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Access;
using SilexGis.Domain.Profiles;

namespace SilexGis.Domain.TestData;

/// <summary>The people a run writes, worked out in memory with the rest of the world.</summary>
public sealed record TestDataPeoplePlan(
    IReadOnlyList<PlannedClub> Clubs,
    IReadOnlyList<PlannedCaver> Cavers,
    IReadOnlyList<PlannedGrant> Grants)
{
    public static TestDataPeoplePlan Empty { get; } = new([], [], []);

    public IEnumerable<PlannedCaver> AccountHolders => Cavers.Where(c => c.Account is not null);
}

/// <summary>A caving club or an organisation, based in a real town; the name is invented.</summary>
public sealed record PlannedClub(
    int Index,
    string Name,
    string Slug,
    CavingGroupType Type,
    string Town,
    string HomeRegionCode,
    string Description,
    string? Website);

public sealed record PlannedMembership(int ClubIndex, CavingGroupRole Role);

/// <summary>One invented person on the roster, with the account they sign in with when they have one.</summary>
public sealed record PlannedCaver
{
    public required int Index { get; init; }

    public required string FullName { get; init; }

    public string? ShortName { get; init; }

    public string? Email { get; init; }

    public string? Phone { get; init; }

    public string? Notes { get; init; }

    public bool ForeignGuest { get; init; }

    public required IReadOnlyList<PlannedMembership> Memberships { get; init; }

    public PlannedAccount? Account { get; init; }
}

/// <summary>
/// What an account stands for in the permission model. Each one appears at least once whenever a
/// run has enough accounts, so every way an account can be set up has an example to sign in as.
/// </summary>
public enum TestDataAccountKind
{
    /// <summary>In the full administrators' group: everything, everywhere.</summary>
    FullAdministrator,

    /// <summary>In the administrators' group, without the full administrators' last word.</summary>
    Administrator,

    Editor,

    Reviewer,

    /// <summary>Manages caving groups: an editor who may also keep club rosters.</summary>
    CavingGroupManager,

    /// <summary>An ordinary signed-in member, in the groups every account joins.</summary>
    Member,

    /// <summary>In no permission group at all, so only what the built-in rules give anybody.</summary>
    NothingGranted,

    /// <summary>A member whose account an administrator has shut.</summary>
    Deactivated,

    /// <summary>A member who has switched on a second sign-in step.</summary>
    TwoFactor,
}

/// <summary>An address on an account; a point only where the person's home is in a town the pack knows.</summary>
public sealed record PlannedAddress(string Label, string Country, string City, string AddressText, double? Lon, double? Lat);

public sealed record PlannedAccessKey(string Name, int ExpiresInDays, bool Revoked);

public sealed record PlannedAccount
{
    public required string Email { get; init; }

    public required string DisplayName { get; init; }

    public string? FirstName { get; init; }

    public string? LastName { get; init; }

    public required TestDataAccountKind Kind { get; init; }

    public required string Locale { get; init; }

    public required string TimeZone { get; init; }

    public string? Bio { get; init; }

    public string? AvatarPreset { get; init; }

    public bool UploadsNeedReview { get; init; }

    public long? StorageQuotaBytes { get; init; }

    public required ProfileVisibility RealNameVisibility { get; init; }

    public required ProfileVisibility BioVisibility { get; init; }

    public required ProfileVisibility EmailVisibility { get; init; }

    public required ProfileVisibility PhoneVisibility { get; init; }

    public required ProfileVisibility CavingClubVisibility { get; init; }

    public required ProfileVisibility AddressVisibility { get; init; }

    public required ProfileVisibility AddressPointVisibility { get; init; }

    public required IReadOnlyList<PlannedAddress> Addresses { get; init; }

    /// <summary>A label for a calendar feed address the account has made, or null for none.</summary>
    public string? CalendarFeedLabel { get; init; }

    public PlannedAccessKey? AccessKey { get; init; }
}

/// <summary>Who a planned grant is given to: one account, or a club as a whole.</summary>
public enum PlannedGrantSubject
{
    Account,
    Club,
}

/// <summary>
/// A direct access entry. Objects are named by their place in the plan — a caver's index, a
/// club's, a cave's, a region's code — and turned into ids when the run writes it.
/// </summary>
public sealed record PlannedGrant(
    PlannedGrantSubject Subject,
    int SubjectIndex,
    AccessEffect Effect,
    AccessDomain Domain,
    AccessAction Actions,
    AccessScopeKind Scope,
    int? ScopeCaveIndex = null,
    string? ScopeRegionCode = null,
    int? ScopeClubIndex = null);
