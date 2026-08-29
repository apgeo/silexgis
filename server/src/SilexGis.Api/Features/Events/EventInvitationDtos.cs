// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;

namespace SilexGis.Api.Features.Events;

/// <summary>
/// One person's standing answer about one event, as it travels: who, whether they were asked,
/// what they have said and when.
/// </summary>
/// <remarks>
/// <para>
/// The same row a trip's answer is, named for the thing it is about. There is one answering
/// mechanism and one table behind both, and this shape differs from the trip's only in which
/// subject it names — so a surface that draws one draws the other with the field renamed.
/// </para>
/// <para>
/// <see cref="MayAnswer"/> is the server's own answer and not a hint for the client to re-derive.
/// A caller working it out for itself would be guessing at a rule with three ways into it, and
/// every route re-asks the question before acting on it anyway.
/// </para>
/// <para>
/// This is intent and never attendance. An event keeps no list of who turned up — the answers are
/// the whole record of who was coming — so nothing here is ever turned into one.
/// </para>
/// </remarks>
public sealed record EventInvitationDto
{
    public required long Id { get; init; }

    public required Guid EventId { get; init; }

    public required Guid CaverId { get; init; }

    /// <summary>
    /// The name this caller may see the person under — an account's own label where they have
    /// one, so nobody appears under two names on the same page. Never their address.
    /// </summary>
    public required string CaverName { get; init; }

    public required TripInvitationResponse Response { get; init; }

    public Guid? InvitedByUserId { get; init; }

    /// <summary>When they were asked, absent when nobody asked them and they answered anyway.</summary>
    public DateTimeOffset? InvitedAt { get; init; }

    /// <summary>
    /// When the answer this row now carries was given. It is the order people are taken in when an
    /// event has a limit, so it moves every time somebody changes their mind rather than staying
    /// at whenever the row happened to be created.
    /// </summary>
    public DateTimeOffset? RespondedAt { get; init; }

    /// <summary>Who wrote the answer down — themselves, or whoever they told.</summary>
    public Guid? RespondedByUserId { get; init; }

    /// <summary>When whoever runs the event picked this person for it, absent while they have not.</summary>
    public DateTimeOffset? SelectedAt { get; init; }

    public string? Note { get; init; }

    /// <summary>Whether this caller may set or change this particular person's answer.</summary>
    public required bool MayAnswer { get; init; }

    /// <summary>
    /// Which place in the sign-up order this yes holds, one-based, and absent for a row that is
    /// not a yes — somebody who declined holds no place. It is the order people answered in and
    /// nothing else: being picked out by whoever runs the event does not move anybody up it.
    /// </summary>
    public int? Place { get; init; }

    /// <summary>
    /// Whether this person is in rather than waiting for a place. Always true where the event
    /// states no limit, and computed from the places above against the limit where it states one —
    /// never stored, so it cannot go stale against the answers it is derived from.
    /// </summary>
    public required bool Attending { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// Everybody on one event's list, in the order they answered in.
/// </summary>
/// <remarks>
/// Unpaged, deliberately: the people considering an evening are a bounded list read as one thing,
/// and the order they answered in is what decides who is in when the event has a limit — an order
/// that only held within a page would not be that order at all.
/// </remarks>
public sealed record EventInvitationListDto
{
    public required Guid EventId { get; init; }

    /// <summary>
    /// How many people the event has room for, or absent when it states no limit. Repeated here so
    /// that a surface drawing the list can say what the counts beside it are counting up to
    /// without having to read the event a second time.
    /// </summary>
    public int? MaxParticipants { get; init; }

    /// <summary>How many of the yeses are in.</summary>
    public required int AttendingCount { get; init; }

    /// <summary>
    /// How many said yes and are waiting for a place. Zero where the event states no limit, and
    /// the number the limit is holding back where it states one — which is the record the limit
    /// exists to keep, since nobody was ever refused for it.
    /// </summary>
    public required int WaitingCount { get; init; }

    public required IReadOnlyList<EventInvitationDto> Invitations { get; init; }
}

/// <summary>
/// Puts somebody on an event's list.
/// </summary>
/// <remarks>
/// The person is named by their entry in the club's directory and never by a bare name: being
/// asked to something is a fact about a person the club already knows about, and a name typed here
/// would make a second person out of one whose spelling somebody guessed at.
/// <para>
/// It carries no answer and no words. What somebody said, and anything they said beside it,
/// belongs to the answer and is written by whoever records it — so that inviting a person who has
/// already replied can never overwrite their reply.
/// </para>
/// </remarks>
public sealed record EventInvitationCreateRequest
{
    public required Guid CaverId { get; init; }
}

public sealed class EventInvitationCreateRequestValidator : AbstractValidator<EventInvitationCreateRequest>
{
    public EventInvitationCreateRequestValidator() => RuleFor(x => x.CaverId).NotEmpty();
}

/// <summary>
/// One person's answer, written whole: what they say now, and whatever needed saying with it.
/// </summary>
/// <remarks>
/// The answer replaces what the row said before, remark included — an answer given without words
/// is an answer without words, not an answer wearing the previous one's.
/// </remarks>
public sealed record EventInvitationResponseRequest
{
    /// <summary>
    /// Nullable so that a body which says nothing is refused rather than read as an answer. The
    /// first value of this vocabulary is "has not answered", so a non-nullable field would take an
    /// empty request as somebody withdrawing what they said and report success.
    /// </summary>
    public TripInvitationResponse? Response { get; init; }

    public string? Note { get; init; }
}

public sealed class EventInvitationResponseRequestValidator : AbstractValidator<EventInvitationResponseRequest>
{
    public EventInvitationResponseRequestValidator()
    {
        RuleFor(x => x.Response).NotNull().IsInEnum();

        // Shape only. What makes a note acceptable is stated once in the domain rules and re-asked
        // in the handler, so a caller cannot get two different answers depending on the door.
        RuleFor(x => x.Note).MaximumLength(TripInvitationRules.MaxNoteLength);
    }
}

/// <summary>
/// Picks somebody out for the event, or puts them back in the order.
/// </summary>
/// <remarks>
/// The pick sits beside the order people answered in rather than replacing it: a picked person is
/// in wherever they stand in that order, and everybody's place in it is unchanged. Making the
/// choice is running the event, so it takes the right to write the event and is not something
/// somebody does to themselves.
/// </remarks>
public sealed record EventInvitationSelectionRequest
{
    /// <summary>
    /// Nullable so a body that says nothing is refused rather than read as un-picking somebody:
    /// the false value is the one a missing field would decay to, and quietly dropping a person
    /// from an event is not a thing an empty request should be able to do.
    /// </summary>
    public bool? Selected { get; init; }
}

public sealed class EventInvitationSelectionRequestValidator : AbstractValidator<EventInvitationSelectionRequest>
{
    public EventInvitationSelectionRequestValidator() => RuleFor(x => x.Selected).NotNull();
}
