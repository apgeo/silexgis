// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Where one answer about coming lands on a timeline.
/// </summary>
/// <remarks>
/// Both subjects are asserted here, and the trip one is not the redundant half. A wrong answer
/// throws nothing and breaks no page — the row is written onto some other object's history, or
/// onto none — so a suite that pinned only the newer subject would go on passing while the older
/// one quietly stopped naming who had been asked.
/// </remarks>
public class TripInvitationSubjectTests
{
    private static readonly Guid TripId = Guid.Parse("2f7b0f2e-0000-4000-8000-000000000001");
    private static readonly Guid EventId = Guid.Parse("2f7b0f2e-0000-4000-8000-000000000002");

    [Fact]
    public void An_answer_about_a_trip_surfaces_on_that_trips_timeline()
    {
        var row = new TripInvitation { Id = 7, TripLogId = TripId, CaverId = Guid.NewGuid() };

        row.RootEntityType.ShouldBe(nameof(TripLog));
        row.RootEntityId.ShouldBe(TripId.ToString());
        row.AuditId.ShouldBe("7");
    }

    [Fact]
    public void An_answer_about_an_event_surfaces_on_that_events_timeline()
    {
        var row = new TripInvitation { Id = 8, EventId = EventId, CaverId = Guid.NewGuid() };

        row.RootEntityType.ShouldBe(nameof(Event));
        row.RootEntityId.ShouldBe(EventId.ToString());
        row.AuditId.ShouldBe("8");
    }

    [Fact]
    public void The_two_subjects_never_answer_the_same_timeline()
    {
        var onTrip = new TripInvitation { Id = 1, TripLogId = TripId, CaverId = Guid.NewGuid() };
        var onEvent = new TripInvitation { Id = 2, EventId = EventId, CaverId = Guid.NewGuid() };

        onTrip.RootEntityType.ShouldNotBe(onEvent.RootEntityType);
        onTrip.RootEntityId.ShouldNotBe(onEvent.RootEntityId);
    }

    /// <summary>
    /// A row carrying no subject at all is refused by the database, and it has to reach the
    /// database to be refused. Asking where it belongs therefore answers "nowhere" rather than
    /// throwing, so that the refusal a caller sees is the clear one about the row's shape.
    /// </summary>
    [Fact]
    public void An_answer_about_nothing_belongs_to_no_timeline_rather_than_failing_to_say()
    {
        var row = new TripInvitation { Id = 9, CaverId = Guid.NewGuid() };

        row.RootEntityType.ShouldBeNull();
        row.RootEntityId.ShouldBeNull();
    }

    [Fact]
    public void The_name_an_answer_files_itself_under_is_the_one_the_history_route_looks_for()
    {
        // The timeline matches an audit row's root against the CLR name of the entity kind
        // asked for, so these two spellings have to agree or an answer lands on no timeline.
        new TripInvitation { Id = 1, TripLogId = TripId, CaverId = Guid.NewGuid() }.RootEntityType
            .ShouldBe(AttachedEntityTypes.ClrName(AttachedEntityType.TripLog));
        new TripInvitation { Id = 2, EventId = EventId, CaverId = Guid.NewGuid() }.RootEntityType
            .ShouldBe(AttachedEntityTypes.ClrName(AttachedEntityType.Event));
    }
}
