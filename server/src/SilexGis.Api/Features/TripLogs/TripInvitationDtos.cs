// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;

namespace SilexGis.Api.Features.TripLogs;

/// <summary>
/// One person's standing answer about one trip, as it travels: who, whether they were asked,
/// what they have said and when.
/// </summary>
/// <remarks>
/// <para>
/// Named properties rather than positional, for the reason the trip's own reading gives: a member
/// inserted between two of the same type is absorbed by the neighbour it displaces, and nothing
/// fails until somebody reads one timestamp as another.
/// </para>
/// <para>
/// <see cref="MayAnswer"/> is the server's own answer and not a hint for the client to re-derive.
/// A caller working it out for itself would be guessing at a rule with three ways into it, and
/// every route re-asks the question before acting on it anyway.
/// </para>
/// <para>
/// This is intent and never attendance: a row here says somebody meant to come, and who was
/// actually on the trip is the trip's own list of people. Nothing that counts who went may count
/// these rows.
/// </para>
/// </remarks>
public sealed record TripInvitationDto
{
    public required long Id { get; init; }

    public required Guid TripLogId { get; init; }

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
    /// When the answer this row now carries was given. It is the order people are taken in when a
    /// trip has a limit, so it moves every time somebody changes their mind rather than staying at
    /// whenever the row happened to be created.
    /// </summary>
    public DateTimeOffset? RespondedAt { get; init; }

    /// <summary>Who wrote the answer down — themselves, or whoever they told.</summary>
    public Guid? RespondedByUserId { get; init; }

    /// <summary>When whoever runs the trip picked this person for it, absent while they have not.</summary>
    public DateTimeOffset? SelectedAt { get; init; }

    public string? Note { get; init; }

    /// <summary>Whether this caller may set or change this particular person's answer.</summary>
    public required bool MayAnswer { get; init; }

    /// <summary>
    /// Which place in the sign-up order this yes holds, one-based, and absent for a row that is
    /// not a yes — somebody who declined holds no place. It is the order people answered in and
    /// nothing else: being picked out by whoever runs the trip does not move anybody up it.
    /// </summary>
    public int? Place { get; init; }

    /// <summary>
    /// Whether this person is on the trip rather than waiting for a place on it. Always true
    /// where the trip states no limit, and computed from the places above against the limit where
    /// it states one — never stored, so it cannot go stale against the answers it is derived from.
    /// </summary>
    public required bool Attending { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// Everybody on one trip's list, in the order they answered in.
/// </summary>
/// <remarks>
/// Unpaged, deliberately: the people considering a trip are a bounded list read as one thing, and
/// the order they answered in is what decides who is in when a trip has a limit — an order that
/// only held within a page would not be that order at all.
/// </remarks>
public sealed record TripInvitationListDto
{
    public required Guid TripLogId { get; init; }

    /// <summary>
    /// How many people the trip has room for, or absent when it states no limit. Repeated here so
    /// that a surface drawing the list can say what the counts beside it are counting up to
    /// without having to read the trip a second time.
    /// </summary>
    public int? MaxParticipants { get; init; }

    /// <summary>How many of the yeses are on the trip.</summary>
    public required int AttendingCount { get; init; }

    /// <summary>
    /// How many said yes and are waiting for a place. Zero where the trip states no limit, and the
    /// number the limit is holding back where it states one — which is the record the limit exists
    /// to keep, since nobody was ever refused for it.
    /// </summary>
    public required int WaitingCount { get; init; }

    public required IReadOnlyList<TripInvitationDto> Invitations { get; init; }
}

/// <summary>
/// Puts somebody on a trip's list.
/// </summary>
/// <remarks>
/// The person is named by their entry in the club's directory and never by a bare name: being
/// asked on a trip is a fact about a person the club already knows about, and a name typed here
/// would make a second person out of one whose spelling somebody guessed at.
/// <para>
/// It carries no answer and no words. What somebody said, and anything they said beside it,
/// belongs to the answer and is written by whoever records it — so that inviting a person who has
/// already replied can never overwrite their reply.
/// </para>
/// </remarks>
public sealed record TripInvitationCreateRequest
{
    public required Guid CaverId { get; init; }
}

public sealed class TripInvitationCreateRequestValidator : AbstractValidator<TripInvitationCreateRequest>
{
    public TripInvitationCreateRequestValidator() => RuleFor(x => x.CaverId).NotEmpty();
}

/// <summary>
/// One person's answer, written whole: what they say now, and whatever needed saying with it.
/// </summary>
/// <remarks>
/// The answer replaces what the row said before, remark included — an answer given without words
/// is an answer without words, not an answer wearing the previous one's.
/// </remarks>
public sealed record TripInvitationResponseRequest
{
    /// <summary>
    /// Nullable so that a body which says nothing is refused rather than read as an answer. The
    /// first value of this vocabulary is "has not answered", so a non-nullable field would take an
    /// empty request as somebody withdrawing what they said and report success.
    /// </summary>
    public TripInvitationResponse? Response { get; init; }

    public string? Note { get; init; }
}

/// <summary>
/// Picks somebody out for the trip, or puts them back in the order.
/// </summary>
/// <remarks>
/// The pick sits beside the order people answered in rather than replacing it: a picked person is
/// on the trip wherever they stand in that order, and everybody's place in it is unchanged. Making
/// the choice is running the trip, so it takes the right to write the trip and is not something
/// somebody does to themselves.
/// </remarks>
public sealed record TripInvitationSelectionRequest
{
    /// <summary>
    /// Nullable so a body that says nothing is refused rather than read as un-picking somebody:
    /// the false value is the one a missing field would decay to, and quietly dropping a person
    /// from a trip is not a thing an empty request should be able to do.
    /// </summary>
    public bool? Selected { get; init; }
}

public sealed class TripInvitationSelectionRequestValidator : AbstractValidator<TripInvitationSelectionRequest>
{
    public TripInvitationSelectionRequestValidator() => RuleFor(x => x.Selected).NotNull();
}

public sealed class TripInvitationResponseRequestValidator : AbstractValidator<TripInvitationResponseRequest>
{
    public TripInvitationResponseRequestValidator()
    {
        RuleFor(x => x.Response).NotNull().IsInEnum();

        // Shape only. What makes a note acceptable is stated once in the domain rules and re-asked
        // in the handler, so a caller cannot get two different answers depending on the door.
        RuleFor(x => x.Note).MaximumLength(TripInvitationRules.MaxNoteLength);
    }
}

/// <summary>
/// What writing the answers into the trip's list of people did.
/// </summary>
/// <remarks>
/// <para>
/// Three numbers rather than a list, because the list is the trip's own: whoever asked for this
/// reads the trip back to see who is now on it, and a second copy of the roster answered from
/// here would be a version of it that could disagree with the one every other surface shows.
/// </para>
/// <para>
/// <see cref="AlreadyNamed"/> is not a failure and not a warning. Somebody already recorded as
/// having been on the trip — because they led it, drove to it, or because this was done once
/// already — is left exactly as they are, and the count says so plainly so that a proposer who
/// runs it twice sees the second run change nothing rather than wondering whether it worked.
/// </para>
/// </remarks>
public sealed record TripPromotionDto
{
    public required Guid TripLogId { get; init; }

    /// <summary>How many people said yes and hold a place on the trip.</summary>
    public required int Attending { get; init; }

    /// <summary>How many of them were written into the trip's list of people just now.</summary>
    public required int Promoted { get; init; }

    /// <summary>
    /// How many of them were already recorded as having been on the trip, in whatever capacity,
    /// and were therefore left alone.
    /// </summary>
    public required int AlreadyNamed { get; init; }
}
