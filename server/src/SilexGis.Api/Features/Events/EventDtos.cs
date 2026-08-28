// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using SilexGis.Domain;
using SilexGis.Domain.Entities;

namespace SilexGis.Api.Features.Events;

/// <summary>
/// A calendar event as it travels: what it is, when it is, where it is in words, who governs it
/// and where it has got to.
/// </summary>
/// <remarks>
/// <para>
/// Written with named properties rather than as a positional record, and deliberately. A
/// positional record is constructed by position, so a member inserted between two of the same
/// type is absorbed by the neighbour it displaces — the compiler is happy, the wire is wrong,
/// and nothing fails until somebody reads a date that is really another date. This record
/// carries two dates and two times of the same shapes, which is precisely the arrangement that
/// mistake hides in.
/// </para>
/// <para>
/// The dates travel as calendar days and the times as wall-clock times, both without a zone,
/// because that is what they are in the row and reading either as an instant is what puts an
/// evening in the wrong cell for readers west of the server.
/// </para>
/// </remarks>
public sealed record EventDto
{
    public required Guid Id { get; init; }

    public required string Title { get; init; }

    public string? Description { get; init; }

    public required EventKind Kind { get; init; }

    public required DateOnly StartDate { get; init; }

    /// <summary>
    /// The last day, or absent when it lasts a single day. Absent means "it did not run on past
    /// its first day" rather than "the end is unknown": a reader asks one field, never two.
    /// </summary>
    public DateOnly? EndDate { get; init; }

    /// <summary>When it starts, wall-clock, or absent when the day is all anybody needs.</summary>
    public TimeOnly? StartTime { get; init; }

    /// <summary>When it ends, read the same way as <see cref="StartTime"/>.</summary>
    public TimeOnly? EndTime { get; init; }

    /// <summary>Where it is, as somebody would write it for a person to read. Never a position.</summary>
    public string? Place { get; init; }

    /// <summary>
    /// How many people it has room for, or absent when it states no limit. Never a reason an
    /// answer is refused: it is what the answers are counted against to say who is in and who is
    /// waiting, and both facts are worked out from the answers rather than stored.
    /// </summary>
    public int? MaxParticipants { get; init; }

    public required Guid OwnerUserId { get; init; }

    public Guid? CavingGroupId { get; init; }

    public required Visibility Visibility { get; init; }

    public required ActivityState State { get; init; }

    /// <summary>When it was first announced, or absent while it never has been.</summary>
    public DateTimeOffset? PublishedAt { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>The state to move a calendar event into.</summary>
/// <remarks>
/// Nullable, and it has to be. The state is the whole of this request, and the vocabulary's first
/// member is the zero value, so a non-nullable field would read a body that names no state at all
/// as naming the workshop — and since every live state has a legal move back there, an empty body
/// would quietly un-announce an event and answer 200. Nullable lets the shape tell "absent" from
/// "draft" and refuse the first.
/// </remarks>
public sealed record EventTransitionRequest
{
    public ActivityState? State { get; init; }
}

public sealed class EventTransitionRequestValidator : AbstractValidator<EventTransitionRequest>
{
    public EventTransitionRequestValidator()
    {
        // That a state was named at all, and that the value is one the vocabulary has. Whether an
        // event may hold it, and whether it may get there from where it is, are the transition
        // table's to answer — and it answers both with one refusal, so there is no second place a
        // state can be judged. The presence check cannot be left to the table: an absent field
        // arrives as the enum's zero value, which is a state the table admits.
        RuleFor(x => x.State).NotNull().IsInEnum();
    }
}

/// <summary>
/// A calendar event as somebody writes it — everything about it except where it has got to.
/// </summary>
/// <remarks>
/// The audience is two nullable halves rather than a required value, because a form that has not
/// been asked about the audience must be able to say so. Both absent means "decide for me" and
/// the default rule answers; either one present means the author took the decision and both
/// halves are read exactly as sent, since quietly supplying the other would widen or narrow what
/// they asked for.
/// </remarks>
public sealed record EventWriteRequest
{
    public string Title { get; init; } = string.Empty;

    public string? Description { get; init; }

    public EventKind Kind { get; init; }

    public DateOnly StartDate { get; init; }

    /// <summary>
    /// The last day. An end equal to the start is accepted and stored as nothing, because a
    /// surface offering a date range has no way to say "one day" other than by picking the same
    /// day twice. An end before the start is a mistyped date and is refused rather than quietly
    /// turned into one day.
    /// </summary>
    public DateOnly? EndDate { get; init; }

    public TimeOnly? StartTime { get; init; }

    /// <summary>
    /// When it ends. Deliberately not required to follow the start: a wall-clock time carries no
    /// day, so an evening that runs from 21:00 to 00:30 is ordinary rather than mistyped.
    /// </summary>
    public TimeOnly? EndTime { get; init; }

    public string? Place { get; init; }

    /// <summary>
    /// How many people it has room for, or absent for no limit. Sent as written even on a kind
    /// that accepts no answers: the number is then simply nothing to count against, and refusing
    /// the save would throw away everything else the author typed.
    /// </summary>
    public int? MaxParticipants { get; init; }

    public Guid? CavingGroupId { get; init; }

    public Visibility? Visibility { get; init; }
}

public sealed class EventWriteRequestValidator : AbstractValidator<EventWriteRequest>
{
    public EventWriteRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleFor(x => x.Place).MaximumLength(255);
        RuleFor(x => x.Kind).IsInEnum();

        RuleFor(x => x.EndDate)
            .GreaterThanOrEqualTo(x => x.StartDate)
            .When(x => x.EndDate is not null)
            .WithMessage("The end date must not precede the start date.");

        RuleFor(x => x.Visibility).IsInEnum().When(x => x.Visibility is not null);

        // Room for nobody is not a limit anybody means to state; absent is how "no limit" is said.
        RuleFor(x => x.MaxParticipants).GreaterThan(0).When(x => x.MaxParticipants is not null);
    }
}

/// <summary>
/// The audience an event would get if its author named none — what a form shows before the event
/// exists, so that the answer the form displays is the answer the write applies.
/// </summary>
public sealed record EventDefaultsDto
{
    public required Visibility Visibility { get; init; }

    public Guid? CavingGroupId { get; init; }
}
