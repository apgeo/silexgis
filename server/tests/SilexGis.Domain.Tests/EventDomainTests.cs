// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Events;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The two rules an event carries that are decided by nothing in the row: what its kind admits,
/// and who reads it when its author names nobody.
/// </summary>
public class EventDomainTests
{
    [Fact]
    public void A_deadline_asks_nobody_whether_they_are_coming_and_every_other_kind_does()
    {
        // A deadline is a date, not a gathering. It is a first-class row with nothing on it
        // rather than an ordinary event with a flag turned off, so the answer is a property of
        // the kind and is the same for every row of it — there is no column to set wrongly.
        EventKinds.AcceptsResponses(EventKind.Deadline).ShouldBeFalse();

        // The positive half, so that a predicate returning false for everything would not pass
        // the assertion above.
        EventKinds.AcceptsResponses(EventKind.ClubMeeting).ShouldBeTrue();
        EventKinds.AcceptsResponses(EventKind.Training).ShouldBeTrue();
        EventKinds.AcceptsResponses(EventKind.MaintenanceDay).ShouldBeTrue();
        EventKinds.AcceptsResponses(EventKind.GearCheck).ShouldBeTrue();
        EventKinds.AcceptsResponses(EventKind.Conference).ShouldBeTrue();
    }

    [Fact]
    public void A_kind_nobody_has_decided_about_accepts_nothing()
    {
        // The default is the silent one deliberately: a kind added to the vocabulary and not
        // named in the rule opens no sign-up sheet until somebody decides that it should. This
        // stands in for that kind — the day a seventh is added, it is this that keeps it shut
        // rather than a reviewer noticing.
        EventKinds.AcceptsResponses((EventKind)99).ShouldBeFalse();
    }

    [Fact]
    public void An_event_starts_visible_to_the_club_that_runs_it()
    {
        var club = Guid.CreateVersion7();

        EventAudienceRules.DefaultAudience([club])
            .ShouldBe((Visibility.CavingGroup, club));
    }

    [Fact]
    public void An_author_with_no_single_club_gets_the_narrow_answer_and_never_the_whole_installation()
    {
        // No club at all, and more than one, are the same answer: private. Nothing here can say
        // which of several clubs an event is for, and guessing would show it to a club that has
        // nothing to do with it — while handing it to every signed-in account because its author
        // belongs to no club is a widening nobody asked for. An event only its author can read
        // is merely useless, and is fixed by naming an audience.
        EventAudienceRules.DefaultAudience([]).ShouldBe((Visibility.Private, null));
        EventAudienceRules.DefaultAudience([Guid.CreateVersion7(), Guid.CreateVersion7()])
            .ShouldBe((Visibility.Private, null));

        // And a group-visible row always names the group it is for: one that named none would
        // admit nobody, which is why the two values are decided together and travel together.
        var (visibility, cavingGroupId) = EventAudienceRules.DefaultAudience([Guid.CreateVersion7()]);
        visibility.ShouldBe(Visibility.CavingGroup);
        cavingGroupId.ShouldNotBeNull();
    }
}
