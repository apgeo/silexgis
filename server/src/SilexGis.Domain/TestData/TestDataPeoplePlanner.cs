// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using SilexGis.Domain.Access;
using SilexGis.Domain.Profiles;

namespace SilexGis.Domain.TestData;

/// <summary>
/// Works out the people of a run — clubs, cavers, accounts and the grants that restrict a few of
/// them — from the profile and the caves already planned. Pure and deterministic, like the rest
/// of the plan; each club, caver and account draws from a stream of its own, so a profile asking
/// for one more caver changes nobody else.
/// </summary>
public static class TestDataPeoplePlanner
{
    /// <summary>The domain every generated address is under: reserved, so nothing sent to it is ever delivered.</summary>
    public const string EmailDomain = "testgenerated.invalid";

    /// <summary>
    /// The prefix of every generated telephone number: a range the United Kingdom's regulator keeps
    /// for fiction, so no number written here belongs to anybody.
    /// </summary>
    public const string PhonePrefix = "+44 7700 900";

    /// <summary>The account kinds in the order a run hands them out first, so a small run has one of each.</summary>
    public static IReadOnlyList<TestDataAccountKind> KindsFirst { get; } =
    [
        TestDataAccountKind.FullAdministrator, TestDataAccountKind.Administrator, TestDataAccountKind.Editor,
        TestDataAccountKind.Reviewer, TestDataAccountKind.CavingGroupManager, TestDataAccountKind.Member,
        TestDataAccountKind.NothingGranted, TestDataAccountKind.Deactivated, TestDataAccountKind.TwoFactor,
    ];

    private static readonly IReadOnlyDictionary<string, int> LaterKinds = new Dictionary<string, int>
    {
        [nameof(TestDataAccountKind.Member)] = 65,
        [nameof(TestDataAccountKind.Editor)] = 20,
        [nameof(TestDataAccountKind.CavingGroupManager)] = 6,
        [nameof(TestDataAccountKind.Reviewer)] = 5,
        [nameof(TestDataAccountKind.Administrator)] = 3,
        [nameof(TestDataAccountKind.FullAdministrator)] = 1,
    };

    private static readonly IReadOnlyDictionary<string, int> VisibilityWeights = new Dictionary<string, int>
    {
        [nameof(ProfileVisibility.Private)] = 40,
        [nameof(ProfileVisibility.CavingGroup)] = 35,
        [nameof(ProfileVisibility.Authenticated)] = 25,
    };

    public static TestDataPeoplePlan Plan(
        TestDataProfile profile, IReadOnlyList<TestDataRegion> regions, IReadOnlyList<PlannedCave> caves)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(caves);
        var people = profile.People;
        if (people.Clubs + people.Organisations + people.Cavers == 0)
        {
            return TestDataPeoplePlan.Empty;
        }

        var clubs = Clubs(profile, regions);
        var cavers = Cavers(profile, clubs);
        var grants = Grants(profile, clubs, cavers, caves);
        return new TestDataPeoplePlan(clubs, cavers, grants);
    }

    /// <summary>
    /// The club a club-only cave is bound to: one based near the cave's own massif when there is
    /// one, so a club's caves are where its members go.
    /// </summary>
    public static int? ClubFor(PlannedCave cave, IReadOnlyList<PlannedClub> clubs, TestDataRng rng)
    {
        ArgumentNullException.ThrowIfNull(cave);
        ArgumentNullException.ThrowIfNull(clubs);
        ArgumentNullException.ThrowIfNull(rng);
        var real = clubs.Where(c => c.Type != CavingGroupType.Organization).ToList();
        if (real.Count == 0)
        {
            return clubs.Count == 0 ? null : clubs[rng.Next(clubs.Count)].Index;
        }

        var near = real.Where(c => c.HomeRegionCode == cave.RegionCode).ToList();
        return (near.Count > 0 ? rng.Pick(near) : rng.Pick(real)).Index;
    }

    private static List<PlannedClub> Clubs(TestDataProfile profile, IReadOnlyList<TestDataRegion> regions)
    {
        var people = profile.People;
        var qualifiers = TestDataPeopleRo.ShuffledQualifiers(TestDataRng.For(profile.Seed, "clubs:names"));
        var towns = TestDataPeopleRo.Towns.OrderByDescending(t => t.Weight).ThenBy(t => t.Name, StringComparer.Ordinal).ToList();
        var regionCodes = regions.Select(r => r.Code).ToHashSet(StringComparer.Ordinal);
        var clubs = new List<PlannedClub>(people.Clubs + people.Organisations);
        var takenNames = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < people.Clubs + people.Organisations; i++)
        {
            var rng = TestDataRng.For(profile.Seed, "club", i);
            var organisation = i >= people.Clubs;
            var qualifier = qualifiers[i % qualifiers.Count];
            if (i >= qualifiers.Count)
            {
                qualifier = $"{qualifier} {(i / qualifiers.Count) + 1}";
            }

            // The towns in turn, the largest first, so a run of a few clubs still spreads them.
            var town = towns[i % towns.Count];
            var home = regionCodes.Contains(town.HomeRegionCode) ? town.HomeRegionCode : regions.Count > 0 ? regions[i % regions.Count].Code : town.HomeRegionCode;
            var massif = regions.FirstOrDefault(r => r.Code == home)?.MassifName ?? "munții din apropiere";
            var name = organisation
                ? TestDataPeopleRo.OrganisationName(rng, qualifier)
                : TestDataPeopleRo.ClubName(rng, qualifier, town.Name);
            name = TestDataNamesRo.Distinct(name, takenNames, rng);
            var founded = rng.Between(1955, 2018);
            var description = organisation
                ? TestDataPeopleRo.OrganisationDescription(name)
                : TestDataPeopleRo.ClubDescription(rng, name, town.Name, massif, founded);
            var marked = Mark(description, profile.Marking);
            var slug = Entities.Tag.Slugify(name);
            clubs.Add(new PlannedClub(
                i,
                name,
                slug,
                organisation ? CavingGroupType.Organization : CavingGroupType.CavingClub,
                organisation ? string.Empty : town.Name,
                home,
                marked,
                rng.Chance(0.5) ? $"https://{slug}.example.org" : null));
        }

        return clubs;
    }

    private sealed class Draft
    {
        public required int Index { get; init; }

        public required TestDataPeopleRo.PersonName Name { get; init; }

        public TestDataGuestCountry? Guest { get; init; }

        public TestDataTown? Town { get; set; }

        public List<(int Club, CavingGroupRole Role)> Memberships { get; } = [];
    }

    private static List<PlannedCaver> Cavers(TestDataProfile profile, IReadOnlyList<PlannedClub> clubs)
    {
        var people = profile.People;
        var count = people.Cavers;
        if (count == 0)
        {
            return [];
        }

        var quotaRng = TestDataRng.For(profile.Seed, "cavers:quotas");
        var guests = TestDataQuota.Flags(count, people.ForeignGuestPercent, quotaRng);
        var twoClubs = TestDataQuota.Flags(count, people.TwoClubsPercent, quotaRng);
        var realClubs = clubs.Where(c => c.Type != CavingGroupType.Organization).ToList();
        var organisations = clubs.Where(c => c.Type == CavingGroupType.Organization).ToList();
        var townsByName = TestDataPeopleRo.Towns.ToDictionary(t => t.Name, StringComparer.Ordinal);
        var drafts = new List<Draft>(count);

        for (var i = 0; i < count; i++)
        {
            var rng = TestDataRng.For(profile.Seed, "caver", i);
            if (guests[i])
            {
                var country = rng.Pick(TestDataPeopleRo.GuestCountries);
                drafts.Add(new Draft { Index = i, Name = TestDataPeopleRo.Guest(rng, country), Guest = country });
                continue;
            }

            // The first members go to each club in turn, so every club has a roster; after that,
            // larger towns' clubs draw more members.
            var home = i < realClubs.Count
                ? realClubs[i]
                : realClubs.Count == 0 ? null : WeightedClub(rng, realClubs, townsByName);
            var town = home is not null && townsByName.TryGetValue(home.Town, out var t) ? t : null;
            var draft = new Draft { Index = i, Name = TestDataPeopleRo.Person(rng, town?.HungarianShare ?? 0), Town = town };
            if (home is not null)
            {
                draft.Memberships.Add((home.Index, CavingGroupRole.Member));
                if (twoClubs[i] && realClubs.Count > 1)
                {
                    var second = rng.Pick(realClubs.Where(c => c.Index != home.Index).ToList());
                    draft.Memberships.Add((second.Index, CavingGroupRole.Member));
                }
            }

            foreach (var organisation in organisations)
            {
                if (rng.Chance(0.12))
                {
                    draft.Memberships.Add((organisation.Index, CavingGroupRole.Member));
                }
            }

            drafts.Add(draft);
        }

        // Every club and organisation is led: its first member owns it and the next one or two
        // keep it, so each has somebody who can manage its roster.
        foreach (var club in clubs)
        {
            var roster = drafts.Where(d => d.Memberships.Any(m => m.Club == club.Index)).ToList();
            for (var r = 0; r < roster.Count && r < (roster.Count >= 10 ? 3 : 2); r++)
            {
                var at = roster[r].Memberships.FindIndex(m => m.Club == club.Index);
                roster[r].Memberships[at] = (club.Index, r == 0 ? CavingGroupRole.Owner : CavingGroupRole.Admin);
            }
        }

        var holders = AccountHolders(profile, drafts);
        var planned = new List<PlannedCaver>(count);
        foreach (var draft in drafts)
        {
            var rng = TestDataRng.For(profile.Seed, "caver:details", draft.Index);
            var email = Email(draft.Name, draft.Index);
            var account = holders.TryGetValue(draft.Index, out var kind)
                ? Account(profile, draft, kind, email, clubs)
                : null;
            planned.Add(new PlannedCaver
            {
                Index = draft.Index,
                FullName = draft.Name.Full,
                ShortName = draft.Guest is null && rng.Chance(0.25) ? TestDataPeopleRo.Nickname(rng) : null,
                Email = account is not null || rng.Chance(0.6) ? email : null,
                Phone = rng.Chance(0.45) ? PhonePrefix + rng.Between(0, 999).ToString("D3", CultureInfo.InvariantCulture) : null,
                Notes = rng.Chance(0.15) ? TestDataPeopleRo.CaverNote(rng) : null,
                ForeignGuest = draft.Guest is not null,
                Memberships = [.. draft.Memberships.Select(m => new PlannedMembership(m.Club, m.Role))],
                Account = account,
            });
        }

        return planned;
    }

    private static PlannedClub WeightedClub(TestDataRng rng, IReadOnlyList<PlannedClub> clubs, IReadOnlyDictionary<string, TestDataTown> towns)
    {
        var weights = clubs.ToDictionary(
            c => c.Index.ToString(CultureInfo.InvariantCulture),
            c => towns.TryGetValue(c.Town, out var t) ? t.Weight : 1);
        var index = int.Parse(rng.Weighted(weights), CultureInfo.InvariantCulture);
        return clubs.First(c => c.Index == index);
    }

    /// <summary>
    /// Which cavers sign in, and as what. Those who lead a club come first — a club's owner is the
    /// person most likely to have an account — then the rest in an order drawn from the run.
    /// </summary>
    private static Dictionary<int, TestDataAccountKind> AccountHolders(TestDataProfile profile, List<Draft> drafts)
    {
        var count = Math.Min(profile.People.Accounts, drafts.Count);
        var result = new Dictionary<int, TestDataAccountKind>();
        if (count <= 0)
        {
            return result;
        }

        var rng = TestDataRng.For(profile.Seed, "accounts:holders");
        var leaders = drafts.Where(d => d.Memberships.Any(m => m.Role != CavingGroupRole.Member)).Select(d => d.Index).ToList();
        var others = drafts.Select(d => d.Index).Except(leaders).ToArray();
        for (var i = others.Length - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (others[i], others[j]) = (others[j], others[i]);
        }

        var order = leaders.Concat(others).Take(count).ToList();
        for (var i = 0; i < order.Count; i++)
        {
            var kind = i < KindsFirst.Count
                ? KindsFirst[i]
                : Enum.Parse<TestDataAccountKind>(rng.Weighted(LaterKinds));
            result[order[i]] = kind;
        }

        return result;
    }

    private static PlannedAccount Account(
        TestDataProfile profile, Draft draft, TestDataAccountKind kind, string email, IReadOnlyList<PlannedClub> clubs)
    {
        var rng = TestDataRng.For(profile.Seed, "account", draft.Index);
        var guest = draft.Guest;
        var addresses = new List<PlannedAddress>();
        if (rng.Chance(0.6))
        {
            // A point only at home in Romania, near the town's centre; a guest's address abroad is text alone.
            var (country, city) = guest is not null
                ? (guest.Country, rng.Pick(guest.Towns))
                : ("România", draft.Town?.Name ?? "București");
            double? lon = guest is null ? (draft.Town?.Lon ?? 26.103) + rng.Between(-0.02, 0.02) : null;
            double? lat = guest is null ? (draft.Town?.Lat ?? 44.427) + rng.Between(-0.015, 0.015) : null;
            addresses.Add(new PlannedAddress(
                "Acasă",
                country,
                city,
                $"{TestDataPeopleRo.Street(rng)} nr. {rng.Between(1, 120)}, {city}",
                lon,
                lat));
        }

        if (guest is null && rng.Chance(0.15))
        {
            var club = draft.Memberships.Count > 0 ? clubs[draft.Memberships[0].Club] : null;
            addresses.Add(new PlannedAddress(
                "Cabana clubului",
                "România",
                club?.Town ?? "Arieșeni",
                $"Cabana din {club?.Town ?? "Arieșeni"}",
                null,
                null));
        }

        PlannedAccessKey? key = rng.Next(10) switch
        {
            0 => new PlannedAccessKey("Telefonul de teren", 365, Revoked: false),
            1 when rng.Chance(0.5) => new PlannedAccessKey("Tableta veche", -10, Revoked: false),
            1 => new PlannedAccessKey("Cheie retrasă", 200, Revoked: true),
            _ => null,
        };

        return new PlannedAccount
        {
            Email = email,
            DisplayName = draft.Name.Full,
            FirstName = rng.Chance(0.8) ? draft.Name.Given : null,
            LastName = rng.Chance(0.8) ? draft.Name.Surname : null,
            Kind = kind,
            Locale = guest?.Locale ?? (rng.Chance(0.85) ? TestDataLocales.Romanian : TestDataLocales.English),
            TimeZone = guest?.TimeZone ?? "Europe/Bucharest",
            Bio = rng.Chance(0.4) ? TestDataPeopleRo.Bio(rng) : null,
            AvatarPreset = rng.Chance(0.5) ? rng.Pick(AvatarPresets.Ids) : null,
            UploadsNeedReview = rng.Chance(0.1),
            StorageQuotaBytes = rng.Chance(0.1) ? 2L * 1024 * 1024 * 1024 : null,
            RealNameVisibility = Visibility(rng),
            BioVisibility = Visibility(rng),
            EmailVisibility = Visibility(rng),
            PhoneVisibility = Visibility(rng),
            CavingClubVisibility = Visibility(rng),
            AddressVisibility = Visibility(rng),
            AddressPointVisibility = Visibility(rng),
            Addresses = addresses,
            CalendarFeedLabel = rng.Chance(0.2) ? "Calendarul de pe telefon" : null,
            AccessKey = key,
        };
    }

    private static ProfileVisibility Visibility(TestDataRng rng) => Enum.Parse<ProfileVisibility>(rng.Weighted(VisibilityWeights));

    /// <summary>
    /// A generated address: the person's name in plain letters and their place on the roster, under
    /// the reserved domain. The number makes it unique within a run whatever the names.
    /// </summary>
    public static string Email(TestDataPeopleRo.PersonName name, int index)
    {
        ArgumentNullException.ThrowIfNull(name);
        var local = Entities.Tag.Slugify(name.Full).Replace('-', '.');
        return $"{local}.{index.ToString(CultureInfo.InvariantCulture)}@{EmailDomain}";
    }

    /// <summary>
    /// The direct grants that make a few accounts and one club see what others do not. Most of the
    /// data is open; these are the restricted cases every restriction path needs one of.
    /// </summary>
    private static List<PlannedGrant> Grants(
        TestDataProfile profile, IReadOnlyList<PlannedClub> clubs, IReadOnlyList<PlannedCaver> cavers, IReadOnlyList<PlannedCave> caves)
    {
        var grants = new List<PlannedGrant>();
        var holders = cavers.Where(c => c.Account is { Kind: TestDataAccountKind.Member or TestDataAccountKind.Editor or TestDataAccountKind.Reviewer or TestDataAccountKind.TwoFactor })
            .Select(c => c.Index)
            .ToList();
        if (holders.Count == 0 || caves.Count == 0)
        {
            return grants;
        }

        var rng = TestDataRng.For(profile.Seed, "grants");
        var access = profile.Access;
        var protectedCaves = caves.Where(c => c.LocationProtected).Select(c => c.Index).ToList();
        var regions = caves.Select(c => c.RegionCode).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        const AccessAction exact = AccessAction.Read | AccessAction.ViewExactLocation;

        // Exact positions beyond what an account's own club gives, over the scopes such a grant
        // takes: one protected cave and what lies in it, a whole massif, a club's caves, everything.
        var exactFlags = TestDataQuota.Flags(holders.Count, access.ExactLocationGrantAccountsPercent, TestDataRng.For(profile.Seed, "grants:exact"));
        var turn = 0;
        for (var i = 0; i < holders.Count; i++)
        {
            if (!exactFlags[i])
            {
                continue;
            }

            var caver = cavers[holders[i]];
            var club = caver.Memberships.Count > 0 ? caver.Memberships[0].ClubIndex : (int?)null;
            var shape = (turn++ % 6) switch
            {
                1 => "region",
                3 => club is null ? "cave" : "club",
                5 => "all",
                _ => "cave",
            };
            if (shape == "cave" && protectedCaves.Count == 0)
            {
                shape = "region";
            }

            grants.Add(shape switch
            {
                "cave" => new PlannedGrant(PlannedGrantSubject.Account, caver.Index, AccessEffect.Allow, AccessDomain.Features, exact,
                    AccessScopeKind.Subtree, ScopeCaveIndex: rng.Pick(protectedCaves)),
                "region" => new PlannedGrant(PlannedGrantSubject.Account, caver.Index, AccessEffect.Allow, AccessDomain.Features, exact,
                    AccessScopeKind.Subtree, ScopeRegionCode: rng.Pick(regions)),
                "club" => new PlannedGrant(PlannedGrantSubject.Account, caver.Index, AccessEffect.Allow, AccessDomain.Features, exact,
                    AccessScopeKind.CavingGroup, ScopeClubIndex: club),
                _ => new PlannedGrant(PlannedGrantSubject.Account, caver.Index, AccessEffect.Allow, AccessDomain.Features, exact,
                    AccessScopeKind.All),
            });
        }

        // One object at a time, both ways: a private cave opened to one account, a public cave
        // closed to another.
        var privateCaves = caves.Where(c => c.Visibility == Domain.Visibility.Private).Select(c => c.Index).ToList();
        var publicCaves = caves.Where(c => c.Visibility == Domain.Visibility.Public).Select(c => c.Index).ToList();
        var objectCount = Math.Max(2, (int)Math.Round(holders.Count * access.DenyOrObjectGrantPercent / 100.0 * 2));
        for (var i = 0; i < objectCount; i++)
        {
            var holder = holders[(i * 7 + 3) % holders.Count];
            if (i % 2 == 0 && privateCaves.Count > 0)
            {
                grants.Add(new PlannedGrant(PlannedGrantSubject.Account, holder, AccessEffect.Allow, AccessDomain.Features,
                    AccessAction.Read, AccessScopeKind.Object, ScopeCaveIndex: rng.Pick(privateCaves)));
            }
            else if (publicCaves.Count > 0)
            {
                grants.Add(new PlannedGrant(PlannedGrantSubject.Account, holder, AccessEffect.Deny, AccessDomain.Features,
                    AccessAction.Read, AccessScopeKind.Object, ScopeCaveIndex: rng.Pick(publicCaves)));
            }
        }

        // Editing one's own records, granted on its own rather than through a group.
        grants.Add(new PlannedGrant(PlannedGrantSubject.Account, holders[0], AccessEffect.Allow, AccessDomain.Features,
            AccessAction.Write, AccessScopeKind.Own));

        // A club as a whole, given the exact positions of one protected cave its members explore.
        var realClubs = clubs.Where(c => c.Type != CavingGroupType.Organization).ToList();
        if (realClubs.Count > 0 && protectedCaves.Count > 0)
        {
            var club = realClubs[0];
            var near = caves.Where(c => c.LocationProtected && c.RegionCode == club.HomeRegionCode).Select(c => c.Index).ToList();
            grants.Add(new PlannedGrant(PlannedGrantSubject.Club, club.Index, AccessEffect.Allow, AccessDomain.Features, exact,
                AccessScopeKind.Subtree, ScopeCaveIndex: near.Count > 0 ? near[0] : protectedCaves[0]));
        }

        return grants;
    }

    private static string Mark(string text, TestDataMarkingProfile marking) =>
        marking.DescriptionFooter ? $"{text}\n\n{TestDataTextRo.Footer(marking.Tag)}" : text;
}
