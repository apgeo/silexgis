// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Auth;
using SilexGis.Domain.Calendar;
using SilexGis.Domain.Entities;
using SilexGis.Domain.TestData;
using SilexGis.Infrastructure.Identity;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.TestData;

/// <summary>
/// Writes the people of a run: accounts through the user manager, clubs with the permission
/// groups every new club is given, roster entries and memberships, and the grants that make a
/// few accounts see more or less than the rest. Every generated account has its mail and text
/// messages switched off as well as a reserved address, so a configured transport never tries
/// to reach it.
/// </summary>
public static class TestDataPeopleWriter
{
    /// <summary>Accounts written per save and per scope: the user manager saves each one by itself.</summary>
    public const int AccountChunk = 20;

    /// <summary>Roster entries written per save and per scope.</summary>
    public const int CaverChunk = 100;

    public static async Task WriteAccountsAsync(
        SilexGisDbContext db,
        UserManager<SilexGisUser> users,
        TestDataSession session,
        IReadOnlyList<PlannedCaver> holders,
        TimeProvider clock,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(holders);
        ArgumentNullException.ThrowIfNull(clock);
        var now = clock.GetUtcNow();

        // The user manager saves every account it creates or changes on its own, so accounts are
        // created before anything else is staged; whatever else this step writes is staged after
        // them and saved with the run's listing in one go.
        var created = new List<(PlannedCaver Caver, SilexGisUser User)>();
        foreach (var caver in holders)
        {
            var account = caver.Account!;
            var email = await FreeEmailAsync(users, account.Email, session);

            // A sign-in number is one account's alone; one another account already has is left off.
            var phone = caver.Phone is { } number && !await db.Users.AnyAsync(u => u.PhoneNumber == number, ct) ? number : null;
            var user = new SilexGisUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = account.DisplayName,
                FirstName = account.FirstName,
                LastName = account.LastName,
                Bio = account.Bio,
                Locale = account.Locale,
                TimeZone = account.TimeZone,
                AvatarPreset = account.AvatarPreset,
                UploadsNeedReview = account.UploadsNeedReview,
                StorageQuotaBytes = account.StorageQuotaBytes,
                RealNameVisibility = account.RealNameVisibility,
                BioVisibility = account.BioVisibility,
                EmailVisibility = account.EmailVisibility,
                PhoneVisibility = account.PhoneVisibility,
                CavingClubVisibility = account.CavingClubVisibility,
                AddressVisibility = account.AddressVisibility,
                AddressPointVisibility = account.AddressPointVisibility,
                PhoneNumber = phone,
                PasswordHash = session.AccountPasswordHash,
                LockoutEnabled = true,
            };
            if (account.Kind == TestDataAccountKind.Deactivated)
            {
                // Shut as an administrator shuts an account: a lockout far in the future.
                user.LockoutEnd = now.AddYears(100);
            }

            if (account.Kind == TestDataAccountKind.TwoFactor)
            {
                user.TwoFactorEmailEnabled = true;
                user.PreferredTwoFactorMethod = TwoFactorMethod.Email;
            }

            var result = await users.CreateAsync(user);
            Require(result, email);
            Require(await users.AddToRoleAsync(user, Role(account.Kind)), email);
            session.Users[caver.Index] = user.Id;
            created.Add((caver, user));
        }

        foreach (var (_, user) in created)
        {
            session.Register(db, "users", user.Id);
            session.Count("users");
        }

        await db.SaveChangesAsync(ct);

        foreach (var (caver, user) in created)
        {
            var account = caver.Account!;
            await PermissionGroupSeeder.EnsureMembershipsAsync(db, user.Id, GroupSlugs(account.Kind), ct);
            var order = 0;
            foreach (var address in account.Addresses)
            {
                db.UserAddresses.Add(new UserAddress
                {
                    UserId = user.Id,
                    Label = address.Label,
                    Country = address.Country,
                    City = address.City,
                    AddressText = address.AddressText,
                    Geom = address is { Lon: { } lon, Lat: { } lat } ? new Point(lon, lat) { SRID = 4326 } : null,
                    SortOrder = order++,
                });
            }

            // Nothing a generated person is sent goes beyond the inbox: the address is a reserved
            // one already, and the channels are off besides, for every category the person could
            // switch off themselves.
            foreach (var category in NotificationCategories.All.Where(NotificationCategories.IsUserConfigurable))
            {
                foreach (var channel in new[] { NotificationChannelKind.Email, NotificationChannelKind.Sms })
                {
                    db.UserNotificationPreferences.Add(new UserNotificationPreference
                    {
                        UserId = user.Id,
                        Category = category,
                        Channel = channel,
                        Choice = NotificationChannelChoice.Off,
                    });
                }
            }

            if (account.CalendarFeedLabel is { } label)
            {
                // The token itself is thrown away: a feed address nobody holds, there to be listed and revoked.
                var (_, hash) = CalendarFeedTokens.Mint();
                db.CalendarFeedTokens.Add(new CalendarFeedToken { UserId = user.Id, TokenHash = hash, Label = label });
            }

            if (account.AccessKey is { } key)
            {
                var (_, hash, prefix) = AccessKeyText.Mint();
                db.ApiAccessKeys.Add(new ApiAccessKey
                {
                    UserId = user.Id,
                    Name = key.Name,
                    Purpose = AccessKeyPurpose.SurveyUpload,
                    Prefix = prefix,
                    SecretHash = hash,
                    CreatedAt = now,
                    ExpiresAt = now.AddDays(key.ExpiresInDays),
                    RevokedAt = key.Revoked ? now : null,
                });
            }
        }

        await session.SaveAndRegisterAsync(db, ct);
    }

    /// <summary>
    /// Clubs and organisations, each with the two permission groups any new club is given, made
    /// for the club's owner when the owner has an account and for the run's administrator otherwise.
    /// </summary>
    public static async Task WriteClubsAsync(SilexGisDbContext db, TestDataSession session, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(session);
        var people = session.Plan.People;
        var slugs = people.Clubs.Select(c => c.Slug).ToList();
        var takenSlugs = (await db.CavingGroups.Where(g => slugs.Contains(g.Slug)).Select(g => g.Slug).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);
        var names = people.Clubs.Select(c => c.Name).ToList();
        var takenNames = (await db.CavingGroups.Where(g => names.Contains(g.Name)).Select(g => g.Name).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var club in people.Clubs)
        {
            var owner = people.Cavers.FirstOrDefault(c => c.Memberships.Any(m => m.ClubIndex == club.Index && m.Role == CavingGroupRole.Owner));
            var creator = owner is not null && session.Users.TryGetValue(owner.Index, out var ownerUser) ? ownerUser : session.OwnerUserId;

            // A name or slug another club already has (a real one, or another run's) is told apart
            // by a number, as a second club of the same name would have to be.
            var name = club.Name;
            var slug = club.Slug;
            for (var n = 2; takenSlugs.Contains(slug) || takenNames.Contains(name); n++)
            {
                name = $"{club.Name} ({n})";
                slug = $"{club.Slug}-{n}";
            }

            takenSlugs.Add(slug);
            takenNames.Add(name);
            var group = new CavingGroup
            {
                Name = name,
                Slug = slug,
                Type = club.Type,
                Description = club.Description,
                Website = club.Website,
            };
            db.CavingGroups.Add(group);
            await CavingGroupPermissionSeeder.SeedForNewGroupAsync(db, group, creator, ct);
            session.Clubs[club.Index] = group.Id;
        }

        await session.SaveAndRegisterAsync(db, ct);
    }

    /// <summary>Roster entries with their memberships; an account holder's entry is linked to the account, as registration links it.</summary>
    public static async Task WriteCaversAsync(
        SilexGisDbContext db, TestDataSession session, IReadOnlyList<PlannedCaver> cavers, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(cavers);
        var userIds = cavers.Where(c => session.Users.ContainsKey(c.Index)).Select(c => session.Users[c.Index]).ToList();
        var users = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);

        foreach (var planned in cavers)
        {
            Guid? userId = session.Users.TryGetValue(planned.Index, out var id) ? id : null;
            var caver = new Caver
            {
                FullName = planned.FullName,
                ShortName = planned.ShortName,
                Email = userId is { } u ? users[u].Email : planned.Email,
                Phone = planned.Phone,
                Notes = planned.Notes,
                UserId = userId,
            };
            db.Cavers.Add(caver);
            session.Cavers[planned.Index] = caver.Id;

            foreach (var membership in planned.Memberships)
            {
                db.CavingGroupMemberships.Add(new CavingGroupMembership
                {
                    CaverId = caver.Id,
                    CavingGroupId = session.Clubs[membership.ClubIndex],
                    Role = membership.Role,
                });
            }

            // The club an account names as its own on its profile is its first club, not an organisation.
            if (userId is { } owner
                && planned.Memberships.FirstOrDefault(m => session.Plan.People.Clubs[m.ClubIndex].Type != CavingGroupType.Organization) is { } first)
            {
                users[owner].CavingClubId = session.Clubs[first.ClubIndex];
            }
        }

        await session.SaveAndRegisterAsync(db, ct);
    }

    /// <summary>The planned direct grants, each checked by the same rules a person's grant is checked by.</summary>
    public static async Task WriteGrantsAsync(SilexGisDbContext db, TestDataSession session, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(session);
        foreach (var grant in session.Plan.People.Grants)
        {
            var (subjectKind, subjectId) = grant.Subject == PlannedGrantSubject.Account
                ? (AccessSubjectKind.User, session.Users.GetValueOrDefault(grant.SubjectIndex))
                : (AccessSubjectKind.CavingGroup, session.Clubs.GetValueOrDefault(grant.SubjectIndex));
            if (subjectId == Guid.Empty)
            {
                continue;
            }

            Guid? scopeFeature = grant switch
            {
                { ScopeCaveIndex: { } cave } => session.Caves.TryGetValue(cave, out var c) ? c : null,
                { ScopeRegionCode: { } region } => session.Massifs.TryGetValue(region, out var m) ? m : null,
                _ => null,
            };
            Guid? scopeId = grant.ScopeClubIndex is { } club && session.Clubs.TryGetValue(club, out var g) ? g : null;
            if ((grant.ScopeCaveIndex is not null || grant.ScopeRegionCode is not null) && scopeFeature is null)
            {
                continue;
            }

            var entry = new AccessEntry
            {
                SubjectKind = subjectKind,
                SubjectId = subjectId,
                Effect = grant.Effect,
                Domain = grant.Domain,
                Actions = grant.Actions,
                ScopeKind = grant.Scope,
                ScopeFeatureId = scopeFeature,
                ScopeId = scopeId,
                GrantedBy = session.OwnerUserId,
            };
            if (AccessEntryRules.Validate(entry.ToSnapshot()) is { } problem)
            {
                throw new InvalidOperationException($"A planned grant is one the permission model refuses ({problem}): {grant}.");
            }

            db.AccessEntries.Add(entry);
        }

        await session.SaveAndRegisterAsync(db, ct);
    }

    /// <summary>
    /// The planned address, or the same address told apart by a tag when an account (a real one,
    /// or another run's that is still here) already uses it; the run says how many were.
    /// </summary>
    private static async Task<string> FreeEmailAsync(UserManager<SilexGisUser> users, string email, TestDataSession session)
    {
        if (await users.FindByEmailAsync(email) is null && await users.FindByNameAsync(email) is null)
        {
            return email;
        }

        session.NoteCount(TestDataNotes.EmailsTaken);
        var at = email.IndexOf('@', StringComparison.Ordinal);
        for (var n = 2; ; n++)
        {
            var candidate = $"{email[..at]}+{n}{email[at..]}";
            if (await users.FindByEmailAsync(candidate) is null && await users.FindByNameAsync(candidate) is null)
            {
                return candidate;
            }
        }
    }

    private static string Role(TestDataAccountKind kind) => kind switch
    {
        TestDataAccountKind.FullAdministrator => GlobalRoles.Admin,
        TestDataAccountKind.CavingGroupManager => GlobalRoles.Manager,
        TestDataAccountKind.Editor => GlobalRoles.Editor,
        _ => GlobalRoles.Viewer,
    };

    /// <summary>The permission groups an account of each kind is in; every kind but one is in the group all accounts join.</summary>
    public static IReadOnlyList<string> GroupSlugs(TestDataAccountKind kind) => kind switch
    {
        TestDataAccountKind.FullAdministrator => [SeededPermissionGroups.FullAdministratorsSlug, SeededPermissionGroups.AllUsersSlug],
        TestDataAccountKind.Administrator => [SeededPermissionGroups.AdministratorsSlug, SeededPermissionGroups.AllUsersSlug],
        TestDataAccountKind.Editor => [SeededPermissionGroups.EditorsSlug, SeededPermissionGroups.AllUsersSlug],
        TestDataAccountKind.Reviewer => [SeededPermissionGroups.ReviewersSlug, SeededPermissionGroups.AllUsersSlug],
        TestDataAccountKind.CavingGroupManager =>
            [SeededPermissionGroups.EditorsSlug, SeededPermissionGroups.CavingGroupManagersSlug, SeededPermissionGroups.AllUsersSlug],
        TestDataAccountKind.NothingGranted => [],
        _ => [SeededPermissionGroups.AllUsersSlug],
    };

    private static void Require(IdentityResult result, string email)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"A generated account ({email}) could not be created: {string.Join("; ", result.Errors.Select(e => e.Description))}");
        }
    }
}
