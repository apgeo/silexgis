// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Events;

namespace SilexGis.Domain.TestData;

/// <summary>
/// Works out what the run's people did: the trips that drew each cave's survey on the day it was
/// drawn, by members of the clubs that explore that massif; other trips over the years, weekends
/// and summers more than weekdays and winters; summer camps with their rosters and the trips made
/// from them; and the clubs' meetings, trainings and gear checks. Pure and deterministic; each
/// trip, camp and event draws from a stream of its own.
/// </summary>
public static class TestDataActivityPlanner
{
    /// <summary>The run's added trip type: a mapping trip whose field data has a schema of its own.</summary>
    public const string AddedTripTypeCode = "testgen_mapping_camp";

    private static readonly IReadOnlyDictionary<string, int> TypeWeights = new Dictionary<string, int>
    {
        ["exploration"] = 34,
        ["survey"] = 8,
        ["maintenance"] = 10,
        ["training"] = 12,
        ["tourism"] = 14,
        ["science"] = 5,
        ["rescue"] = 2,
        ["other"] = 7,
        [AddedTripTypeCode] = 8,
    };

    private static readonly IReadOnlyDictionary<string, string> TypeWords = new Dictionary<string, string>
    {
        ["exploration"] = "Explorare în",
        ["survey"] = "Cartare în",
        ["maintenance"] = "Echipare în",
        ["training"] = "Antrenament în",
        ["tourism"] = "Vizită în",
        ["science"] = "Cercetare în",
        ["rescue"] = "Exercițiu de salvare în",
        ["other"] = "Tură în",
        [AddedTripTypeCode] = "Cartare de tabără în",
    };

    private static readonly IReadOnlyDictionary<string, int> PastStates = new Dictionary<string, int>
    {
        [nameof(ActivityState.Done)] = 76,
        [nameof(ActivityState.Published)] = 12,
        [nameof(ActivityState.Cancelled)] = 6,
        [nameof(ActivityState.Draft)] = 6,
    };

    private static readonly IReadOnlyDictionary<string, int> FutureStates = new Dictionary<string, int>
    {
        [nameof(ActivityState.Planned)] = 40,
        [nameof(ActivityState.Proposed)] = 22,
        [nameof(ActivityState.Confirmed)] = 26,
        [nameof(ActivityState.Delayed)] = 6,
        [nameof(ActivityState.Draft)] = 6,
    };

    private static readonly IReadOnlyDictionary<string, int> TripVisibility = new Dictionary<string, int>
    {
        [nameof(Visibility.Authenticated)] = 50,
        [nameof(Visibility.Public)] = 25,
        [nameof(Visibility.CavingGroup)] = 15,
        [nameof(Visibility.Private)] = 10,
    };

    // Months by how often clubs go underground: summer most, the deep winter least.
    private static readonly IReadOnlyDictionary<string, int> MonthWeights = new Dictionary<string, int>
    {
        ["1"] = 4, ["2"] = 4, ["3"] = 6, ["4"] = 8, ["5"] = 10, ["6"] = 11,
        ["7"] = 14, ["8"] = 14, ["9"] = 11, ["10"] = 9, ["11"] = 5, ["12"] = 4,
    };

    private static readonly string[] Weather =
    [
        "Senin, cald.", "Înnorat, fără precipitații.", "Ploaie toată ziua; apă mare la intrare.", "Ninsoare ușoară.",
        "Ceață pe platou.", "Vânt puternic pe creastă.", "Vreme bună, drum de acces uscat.",
    ];

    private static readonly string[] Results =
    [
        "Am ajuns până la capătul cunoscut; continuarea rămâne de forțat.", "Am echipat puțurile până la -{0} m.",
        "Am cartat galeria laterală și am închis o buclă.", "Traseu parcurs fără probleme; echipamentul a fost verificat.",
        "Am găsit o continuare îngustă cu curent de aer.", "Am fotografiat sala principală și formațiunile.",
        "Am recuperat corzile vechi și am refăcut ancorajele.", "Exercițiul s-a încheiat în timpul planificat.",
    ];

    public static TestDataActivityPlan Plan(
        TestDataProfile profile,
        IReadOnlyList<TestDataRegion> regions,
        IReadOnlyList<PlannedCave> caves,
        TestDataPeoplePlan people)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(caves);
        ArgumentNullException.ThrowIfNull(people);
        var timeline = profile.Timeline;
        if (timeline.Trips <= 0 || caves.Count == 0 || people.Cavers.Count == 0)
        {
            return new TestDataActivityPlan([], [], Events(profile, people));
        }

        var locals = Locals(regions, caves, people);
        var expeditions = Expeditions(profile, regions, caves, people, locals);
        var trips = new List<PlannedTrip>(timeline.Trips);

        // The trips that drew the surveys, on the days the surveys are dated, while there is room.
        var surveyBudget = (int)(timeline.Trips * 0.4);
        foreach (var cave in caves.Where(c => c is { WithSurvey: true, Network: not null }))
        {
            var network = cave.Network!;
            for (var s = 0; s < network.Surveys.Count && trips.Count < surveyBudget; s++)
            {
                var date = network.Surveys[s].Date;
                if (date.Year < timeline.TripsFromYear)
                {
                    continue;
                }

                var rng = TestDataRng.For(profile.Seed, "trip:survey:" + cave.Index.ToString(CultureInfo.InvariantCulture), s);
                var legs = network.Legs.Where(l => l.Survey == s && !l.Splay).ToList();
                var stations = legs.SelectMany(l => new[] { l.From, l.To }).Distinct().Count();
                trips.Add(Trip(rng, trips.Count, "survey", date, future: false, [cave.Index], caves, people, locals, profile) with
                {
                    LengthSurveyedM = Math.Round((decimal)legs.Sum(network.Length), 1),
                    SurveyStations = stations,
                    State = rng.Chance(0.15) ? ActivityState.Published : ActivityState.Done,
                    Results = $"Am cartat {Math.Round(legs.Sum(network.Length))} m în {stations} stații ({network.Surveys[s].Prefix}).",
                });
            }
        }

        // Each camp's own trips: most days of a camp, a team goes underground in its massif, its
        // people taken from the camp's roster.
        var campBudget = trips.Count + (int)(timeline.Trips * 0.25);
        foreach (var camp in expeditions)
        {
            var inMassif = caves.Where(c => c.RegionCode == camp.RegionCode).ToList();
            var roster = camp.Roster.Select(r => r.CaverIndex).ToList();
            var campLocals = new Dictionary<string, List<int>>(locals, StringComparer.Ordinal) { [camp.RegionCode] = roster };
            for (var day = camp.Start; day <= camp.End && trips.Count < campBudget && inMassif.Count > 0; day = day.AddDays(1))
            {
                var rng = TestDataRng.For(profile.Seed, "trip:camp:" + camp.Index.ToString(CultureInfo.InvariantCulture), day.DayNumber);
                if (rng.Chance(0.25) || day.Year < timeline.TripsFromYear)
                {
                    continue;
                }

                var type = rng.Weighted(new Dictionary<string, int> { ["exploration"] = 50, [AddedTripTypeCode] = 30, ["maintenance"] = 20 });
                trips.Add(Trip(rng, trips.Count, type, day, day > TestDataPlanner.LastDay, [rng.Pick(inMassif).Index], caves, people, campLocals, profile) with
                {
                    ExpeditionIndex = camp.Index,
                    LengthSurveyedM = type == AddedTripTypeCode ? Math.Round((decimal)rng.Between(15.0, 180.0), 1) : null,
                    SurveyStations = type == AddedTripTypeCode ? rng.Between(4, 30) : null,
                });
            }
        }

        var first = new DateOnly(timeline.TripsFromYear, 1, 1).DayNumber;
        var last = TestDataPlanner.LastDay.AddDays(150).DayNumber;
        for (var i = trips.Count; i < timeline.Trips; i++)
        {
            var rng = TestDataRng.For(profile.Seed, "trip", i);
            var date = TripDay(rng, first, last);
            var future = date > TestDataPlanner.LastDay;
            var cave = rng.Pick(caves);
            var two = rng.Chance(0.15) ? caves.Where(c => c.RegionCode == cave.RegionCode && c.Index != cave.Index).Take(30).ToList() : [];
            int[] visited = two.Count > 0 ? [cave.Index, rng.Pick(two).Index] : [cave.Index];
            trips.Add(Trip(rng, i, rng.Weighted(TypeWeights), date, future, visited, caves, people, locals, profile));
        }

        // A trip on a camp's days, in its massif, is a trip of that camp.
        trips = [.. trips.Select(t =>
        {
            if (t.ExpeditionIndex is not null)
            {
                return t;
            }

            var region = caves[t.CaveIndices[0]].RegionCode;
            var camp = expeditions.FirstOrDefault(e => e.RegionCode == region && t.Date >= e.Start && t.Date <= e.End);
            return camp is null ? t : t with { ExpeditionIndex = camp.Index };
        })];

        return new TestDataActivityPlan(trips, expeditions, Events(profile, people));
    }

    /// <summary>The cavers of each massif: members of the clubs based near it, and everybody when none is.</summary>
    private static Dictionary<string, List<int>> Locals(IReadOnlyList<TestDataRegion> regions, IReadOnlyList<PlannedCave> caves, TestDataPeoplePlan people)
    {
        var all = people.Cavers.Select(c => c.Index).ToList();
        return regions.Select(r => r.Code).Concat(caves.Select(c => c.RegionCode)).Distinct(StringComparer.Ordinal).ToDictionary(
            code => code,
            code =>
            {
                var clubs = people.Clubs.Where(c => c.HomeRegionCode == code).Select(c => c.Index).ToHashSet();
                var members = people.Cavers.Where(c => c.Memberships.Any(m => clubs.Contains(m.ClubIndex))).Select(c => c.Index).ToList();
                return members.Count >= 3 ? members : all;
            },
            StringComparer.Ordinal);
    }

    private static PlannedTrip Trip(
        TestDataRng rng,
        int index,
        string type,
        DateOnly date,
        bool future,
        IReadOnlyList<int> visited,
        IReadOnlyList<PlannedCave> caves,
        TestDataPeoplePlan people,
        Dictionary<string, List<int>> locals,
        TestDataProfile profile)
    {
        var cave = caves[visited[0]];
        var pool = locals[cave.RegionCode];
        var size = Math.Min(pool.Count, type == "tourism" ? rng.Between(4, 10) : rng.Between(2, 7));
        var picked = Shuffle(rng, pool).Take(size).ToList();
        var entry = new TimeOnly(rng.Between(8, 11), rng.Pick([0, 15, 30, 45]));
        var hours = cave.CaveTypeCode == "pit" ? rng.Between(6, 14) : rng.Between(2, 9);
        var exit = entry.AddHours(Math.Min(hours, 23 - entry.Hour));
        var members = picked.Select((caver, n) => new PlannedTripMember(
            caver,
            n == 0 ? "leader"
            : type == "survey" || type == AddedTripTypeCode ? "surveyor"
            : type == "training" && n == 1 ? "instructor"
            : type == "training" ? "trainee"
            : rng.Chance(0.1) ? "photographer"
            : rng.Chance(0.05) ? "driver"
            : null,
            // Somebody who went in after the rest — on the trip's own day, never before it started.
            rng.Chance(0.3) ? entry.AddMinutes(rng.Between(1, 6) * 15) : null,
            null)).ToList();

        var club = people.Clubs.Count == 0 ? (int?)null
            : people.Cavers[picked[0]].Memberships.FirstOrDefault()?.ClubIndex ?? rng.Next(people.Clubs.Count);
        var visibility = Enum.Parse<Visibility>(rng.Weighted(TripVisibility));
        if (visibility == Visibility.CavingGroup && club is null)
        {
            visibility = Visibility.Authenticated;
        }

        var outsiders = people.Cavers.Select(c => c.Index).Except(picked).ToList();
        var invitations = future && outsiders.Count > 0
            ? Shuffle(rng, outsiders).Take(rng.Between(1, 4)).Select(c => new PlannedInvitation(
                c,
                rng.Pick([TripInvitationResponse.Pending, TripInvitationResponse.Yes, TripInvitationResponse.No, TripInvitationResponse.Maybe]),
                rng.Chance(0.3) ? rng.Pick(["Vin doar sâmbătă.", "Aduc corzile clubului.", "Depinde de vreme.", "Nu pot, sunt plecat."]) : null)).ToList()
            : [];
        var depth = cave.Depth is { } d && d > 0 ? Math.Round(d * (decimal)rng.Between(0.3, 1.0), 1) : (decimal?)null;
        var titleCave = visited.Count > 1 ? $"{cave.Name} și {caves[visited[1]].Name}" : cave.Name;
        return new PlannedTrip
        {
            Index = index,
            Title = $"{TypeWords[type]} {titleCave}",
            TypeCode = type,
            Date = date,
            DateEnd = rng.Chance(0.12) ? date.AddDays(rng.Between(1, 3)) : null,
            Entry = future ? null : entry,
            Exit = future ? null : exit,
            State = Enum.Parse<ActivityState>(rng.Weighted(future ? FutureStates : PastStates)),
            Future = future,
            CaveIndices = visited,
            Members = members,
            Proposers = future ? [picked[0]] : [],
            Invitations = invitations,
            Teams = members.Count >= 6 ? ["Echipa de front", "Echipa de suport"] : [],
            ClubIndex = visibility == Visibility.CavingGroup ? club : null,
            OrganizingClubIndex = club is not null && rng.Chance(0.7) ? club : null,
            Visibility = visibility,
            Description = Mark(future
                ? $"Tură planificată în {titleCave}. Întâlnire la {rng.Pick(["intrare", "parcarea din sat", "cabana clubului"])}."
                : $"Tură în {titleCave}, {members.Count} participanți.", profile.Marking),
            Results = future ? null : string.Format(CultureInfo.InvariantCulture, rng.Pick(Results), depth ?? 0),
            Weather = future ? null : rng.Pick(Weather),
            DepthReachedM = future ? null : depth,
            RopeMetres = cave.CaveTypeCode == "pit" && !future ? Math.Round((decimal)((double)(cave.Depth ?? 20) * rng.Between(1.1, 1.6))) : null,
            HadIncident = !future && rng.Chance(0.02),
            Deleted = rng.Chance(0.01),
        };
    }

    /// <summary>A day a trip falls on: a month by its weight, then the Saturday of a week in it, now and then a weekday.</summary>
    private static DateOnly TripDay(TestDataRng rng, int first, int last)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var year = DateOnly.FromDayNumber(rng.Between(first, last)).Year;
            var month = int.Parse(rng.Weighted(MonthWeights), CultureInfo.InvariantCulture);
            var day = new DateOnly(year, month, rng.Between(1, 28));
            if (!rng.Chance(0.15))
            {
                day = day.AddDays(((int)DayOfWeek.Saturday - (int)day.DayOfWeek + 7) % 7);
            }

            if (day.DayNumber >= first && day.DayNumber <= last)
            {
                return day;
            }
        }

        return DateOnly.FromDayNumber(rng.Between(first, last));
    }

    private static List<PlannedExpedition> Expeditions(
        TestDataProfile profile,
        IReadOnlyList<TestDataRegion> regions,
        IReadOnlyList<PlannedCave> caves,
        TestDataPeoplePlan people,
        Dictionary<string, List<int>> locals)
    {
        var camps = new List<PlannedExpedition>();
        var clubs = people.Clubs.Where(c => c.Type != CavingGroupType.Organization).ToList();
        if (clubs.Count == 0 || profile.Timeline.ExpeditionYears <= 0)
        {
            return camps;
        }

        // The massifs with the most caves, where a summer camp is worth its logistics.
        var big = caves.GroupBy(c => c.RegionCode, StringComparer.Ordinal).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Key).Take(3).ToList();
        var guests = people.Cavers.Where(c => c.ForeignGuest).Select(c => c.Index).ToList();
        var lastYear = TestDataPlanner.LastDay.Year;
        for (var year = lastYear - profile.Timeline.ExpeditionYears + 1; year <= lastYear + 1; year++)
        {
            var rng = TestDataRng.For(profile.Seed, "camp", year);
            var count = year > lastYear ? 1 : rng.Between(1, 2);
            for (var k = 0; k < count; k++)
            {
                var regionCode = big[(year + k) % big.Count];
                var region = regions.FirstOrDefault(r => r.Code == regionCode);
                var start = new DateOnly(year, rng.Between(7, 8), rng.Between(1, 20));
                var end = start.AddDays(rng.Between(9, 16));
                var club = clubs.FirstOrDefault(c => c.HomeRegionCode == regionCode) ?? rng.Pick(clubs);
                var pool = locals[regionCode];
                var roster = Shuffle(rng, pool).Take(Math.Min(pool.Count, rng.Between(8, 16)))
                    .Concat(Shuffle(rng, guests).Take(Math.Min(guests.Count, 2)))
                    .Select((caver, n) =>
                    {
                        var role = n switch { 0 => "organiser", 1 => "cook", 2 => "medic", 3 => "equipment", _ => guests.Contains(caver) ? "guest" : "member" };
                        var arrives = n < 4 ? start : start.AddDays(rng.Between(0, 4));
                        var leaves = n < 4 ? end : end.AddDays(-rng.Between(0, 4));
                        return new PlannedRosterEntry(caver, role, arrives, leaves < arrives ? arrives : leaves);
                    })
                    .ToList();
                var future = start > TestDataPlanner.LastDay;
                var areaName = region?.SubAreas.Count > 0 ? region.SubAreas[(year + k) % region.SubAreas.Count].Name : region?.MassifName ?? regionCode;
                camps.Add(new PlannedExpedition(
                    camps.Count,
                    $"Tabăra de explorare {areaName} {year}",
                    regionCode,
                    region?.SubAreas.Count > 0 ? (year + k) % region.SubAreas.Count : null,
                    start,
                    end,
                    future ? ActivityState.Planned : rng.Chance(0.3) ? ActivityState.Published : ActivityState.Done,
                    club.Index,
                    rng.Chance(0.2) ? Visibility.CavingGroup : Visibility.Authenticated,
                    Mark($"Tabăra de vară a clubului {club.Name} în {areaName}: explorare, cartare și echiparea puțurilor noi.", profile.Marking),
                    rng.Chance(0.5) ? "Tabăra este deschisă și membrilor altor cluburi; scrieți-ne înainte să veniți." : null,
                    roster));
            }
        }

        return camps;
    }

    /// <summary>Each club's weekly meeting, its yearly training and gear check, and a few one-off events.</summary>
    private static List<PlannedEventSeries> Events(TestDataProfile profile, TestDataPeoplePlan people)
    {
        var events = new List<PlannedEventSeries>();
        var year = TestDataPlanner.LastDay.Year;
        foreach (var club in people.Clubs)
        {
            var rng = TestDataRng.For(profile.Seed, "events", club.Index);
            var members = people.Cavers.Where(c => c.Memberships.Any(m => m.ClubIndex == club.Index)).Select(c => c.Index).ToList();
            var invitees = Shuffle(rng, members).Take(Math.Min(members.Count, 3))
                .Select(c => new PlannedInvitation(c, rng.Pick([TripInvitationResponse.Yes, TripInvitationResponse.No, TripInvitationResponse.Maybe, TripInvitationResponse.Pending]), null))
                .ToList();
            var place = club.Town.Length > 0 ? $"Sediul clubului, {club.Town}" : "Online";
            if (club.Type == CavingGroupType.Organization)
            {
                events.Add(Single(events.Count, $"Simpozionul anual {club.Name}", EventKind.Conference, new DateOnly(year, 11, 14), 2, place, club.Index, rng, profile, []));
                events.Add(Single(events.Count, "Termen: raportul anual de activitate", EventKind.Deadline, new DateOnly(year + 1, 1, 31), 0, null, club.Index, rng, profile, []));
                continue;
            }

            // The meeting: weekly from September, through the season.
            var weekday = rng.Between(1, 4);
            var start = new DateOnly(year, 9, 1);
            start = start.AddDays((weekday - (int)start.DayOfWeek + 7) % 7);
            events.Add(new PlannedEventSeries(
                events.Count,
                $"Ședința săptămânală {club.Name}",
                EventKind.ClubMeeting,
                start,
                0,
                new TimeOnly(19, 0),
                new TimeOnly(21, 0),
                place,
                club.Index,
                Visibility.CavingGroup,
                ActivityState.Planned,
                EventRecurrenceFrequency.Weekly,
                rng.Between(20, 40),
                "În fiecare săptămână, în aceeași zi",
                Mark("Ședința clubului: planificarea turelor, echipament, noutăți.", profile.Marking),
                null,
                []));
            events.Add(Single(events.Count, "Antrenament de tehnică pe coardă", EventKind.Training, new DateOnly(year, rng.Between(3, 5), rng.Between(1, 25)), 1, "Falezele din apropiere", club.Index, rng, profile, invitees));
            events.Add(Single(events.Count, "Verificarea echipamentului colectiv", EventKind.GearCheck, new DateOnly(year, 10, rng.Between(1, 25)), 0, place, club.Index, rng, profile, []));
            events.Add(Single(events.Count, "Zi de lucru la depozitul clubului", EventKind.MaintenanceDay, new DateOnly(year + 1, 2, rng.Between(1, 25)), 0, place, club.Index, rng, profile, []));
        }

        return events;
    }

    private static PlannedEventSeries Single(
        int index, string title, EventKind kind, DateOnly day, int span, string? place, int club, TestDataRng rng, TestDataProfile profile, IReadOnlyList<PlannedInvitation> invitations) =>
        new(index, title, kind, day, span, kind is EventKind.Deadline ? null : new TimeOnly(9, 0), kind is EventKind.Deadline ? null : new TimeOnly(17, 0),
            place, club, rng.Chance(0.5) ? Visibility.Authenticated : Visibility.CavingGroup,
            day > TestDataPlanner.LastDay ? ActivityState.Planned : ActivityState.Done, null, 1, null,
            Mark($"{title}.", profile.Marking), kind == EventKind.Training ? 12 : null, invitations);

    private static List<int> Shuffle(TestDataRng rng, IReadOnlyList<int> items)
    {
        var list = items.ToList();
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        return list;
    }

    private static string Mark(string text, TestDataMarkingProfile marking) =>
        marking.DescriptionFooter ? $"{text}\n\n{TestDataTextRo.Footer(marking.Tag)}" : text;
}
