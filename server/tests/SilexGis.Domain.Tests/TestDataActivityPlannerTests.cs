// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.TestData;

namespace SilexGis.Domain.Tests;

/// <summary>
/// What a test-data run's people did: the trips that drew each survey, on its day and with its
/// length; trips in every state, planned ones with proposers and invitations; summer camps with
/// rosters and their trips; club events in series — all deterministic.
/// </summary>
public class TestDataActivityPlannerTests
{
    private static readonly TestDataWorldPlan Default = TestDataPlanner.Plan(TestDataProfiles.Default);

    [Fact]
    public void The_same_profile_plans_the_same_trips()
    {
        var again = TestDataPlanner.Plan(TestDataProfiles.Default).Activity;

        again.Trips.Select(t => (t.Title, t.Date, t.State)).ShouldBe(Default.Activity.Trips.Select(t => (t.Title, t.Date, t.State)));
        again.Expeditions.Select(e => e.Name).ShouldBe(Default.Activity.Expeditions.Select(e => e.Name));
    }

    [Fact]
    public void A_survey_trip_is_dated_and_measured_by_the_survey_it_drew()
    {
        var activity = Default.Activity;
        activity.Trips.Count.ShouldBe(4000);
        var surveyTrips = activity.Trips.Where(t => t.TypeCode == "survey" && t.SurveyStations is not null).ToList();
        surveyTrips.ShouldNotBeEmpty();
        surveyTrips.Count.ShouldBeLessThanOrEqualTo(1600);
        foreach (var trip in surveyTrips.Take(100))
        {
            var network = Default.Caves[trip.CaveIndices[0]].Network!;
            var survey = network.Surveys.ToList().FindIndex(s => s.Date == trip.Date);
            survey.ShouldBeGreaterThanOrEqualTo(0, trip.Title);
            trip.LengthSurveyedM.ShouldBe(Math.Round((decimal)network.Legs.Where(l => l.Survey == survey && !l.Splay).Sum(network.Length), 1));
            trip.Date.Year.ShouldBeGreaterThanOrEqualTo(TestDataProfiles.Default.Timeline.TripsFromYear);
        }
    }

    [Fact]
    public void Trips_come_in_every_state_with_rosters_of_real_cavers_and_planned_ones_are_proposed_and_invited()
    {
        var trips = Default.Activity.Trips;
        foreach (var state in Enum.GetValues<ActivityState>())
        {
            trips.ShouldContain(t => t.State == state, $"no {state} trip");
        }

        trips.ShouldAllBe(t => t.Members.Count > 0 && t.Members.Select(m => m.CaverIndex).Distinct().Count() == t.Members.Count);
        trips.Where(t => t.Entry != null).SelectMany(t => t.Members.Where(m => m.Entry != null).Select(m => (t, m)))
            .ShouldAllBe(x => x.m.Entry >= x.t.Entry, "somebody went in before their trip started");
        trips.SelectMany(t => t.Members).ShouldAllBe(m => m.CaverIndex >= 0 && m.CaverIndex < Default.People.Cavers.Count);
        trips.Where(t => t.Future).ShouldAllBe(t => t.Proposers.Count == 1);
        trips.Where(t => t.Future).ShouldContain(t => t.Invitations.Count > 0);
        trips.Where(t => t.Invitations.Count > 0).ShouldAllBe(t => t.Invitations.All(i => t.Members.All(m => m.CaverIndex != i.CaverIndex)));
        trips.Where(t => t.Visibility == Visibility.CavingGroup).ShouldAllBe(t => t.ClubIndex != null);
        trips.ShouldContain(t => t.Teams.Count > 0);
        trips.ShouldContain(t => t.Deleted);
        trips.ShouldContain(t => t.TypeCode == TestDataActivityPlanner.AddedTripTypeCode);

        // Weekends and summers more than weekdays and winters.
        trips.Count(t => t.Date.DayOfWeek == DayOfWeek.Saturday).ShouldBeGreaterThan(trips.Count / 3);
        trips.Count(t => t.Date.Month is 7 or 8).ShouldBeGreaterThan(trips.Count(t => t.Date.Month is 1 or 2));
    }

    [Fact]
    public void Camps_have_rosters_and_the_trips_made_from_them()
    {
        var activity = Default.Activity;
        activity.Expeditions.Count.ShouldBeGreaterThanOrEqualTo(13);
        foreach (var camp in activity.Expeditions)
        {
            camp.Roster.Count.ShouldBeGreaterThan(3);
            camp.Roster.ShouldContain(r => r.RoleCode == "organiser");
            camp.Roster.ShouldAllBe(r => r.From >= camp.Start && (r.To == null || r.To <= camp.End) && (r.To == null || r.To >= r.From));
            camp.End.ShouldBeGreaterThan(camp.Start);
        }

        activity.Trips.ShouldContain(t => t.ExpeditionIndex != null);
        foreach (var camp in activity.Expeditions.Where(e => e.Start.Year >= TestDataProfiles.Default.Timeline.TripsFromYear))
        {
            // A camp goes underground on most of its days, with people from its roster.
            var own = activity.Trips.Where(t => t.ExpeditionIndex == camp.Index).ToList();
            own.Count.ShouldBeGreaterThan((camp.End.DayNumber - camp.Start.DayNumber) / 3, camp.Name);
            own.SelectMany(t => t.Members).ShouldContain(m => camp.Roster.Any(r => r.CaverIndex == m.CaverIndex));
        }

        foreach (var trip in activity.Trips.Where(t => t.ExpeditionIndex != null))
        {
            var camp = activity.Expeditions[trip.ExpeditionIndex!.Value];
            trip.Date.ShouldBeInRange(camp.Start, camp.End);
            Default.Caves[trip.CaveIndices[0]].RegionCode.ShouldBe(camp.RegionCode);
        }

        activity.Expeditions.ShouldContain(e => e.State == ActivityState.Planned);
    }

    [Fact]
    public void Every_club_meets_weekly_and_trains_and_the_organisations_hold_their_own_events()
    {
        var events = Default.Activity.Events;
        var clubs = Default.People.Clubs;
        foreach (var club in clubs.Where(c => c.Type == CavingGroupType.CavingClub))
        {
            events.ShouldContain(e => e.ClubIndex == club.Index && e.Kind == EventKind.ClubMeeting && e.Frequency != null && e.Count > 1);
            events.ShouldContain(e => e.ClubIndex == club.Index && e.Kind == EventKind.Training);
        }

        events.ShouldContain(e => e.Kind == EventKind.Conference);
        events.ShouldContain(e => e.Kind == EventKind.Deadline);
        events.ShouldAllBe(e => e.Count <= 104);
        events.ShouldContain(e => e.Invitations.Count > 0);
    }
}
