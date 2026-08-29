// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Events;

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

    /// <summary>
    /// The series this occurrence belongs to, or absent when the event stands on its own. A
    /// grouping key rather than a reference: there is no series to fetch, only the other events
    /// that carry the same value.
    /// </summary>
    public Guid? SeriesId { get; init; }

    /// <summary>
    /// How the series repeats, in the words its author used. Shown to a reader and read by
    /// nothing else — no date on this row or any other is derived from it.
    /// </summary>
    public string? SeriesRule { get; init; }

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

    /// <summary>
    /// How this event repeats, or absent when it happens once. Read only when an event is
    /// created: the occurrences are written then, and from that moment each is an ordinary event
    /// that is edited, moved and deleted like any other.
    /// </summary>
    public EventRecurrenceRequest? Recurrence { get; init; }
}

/// <summary>
/// A request to write a run of occurrences rather than one event: how often it comes round, where
/// the repetition stops, and the words that describe it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The words and the repetition are two different things and are deliberately not the same
/// field.</b> <see cref="Rule"/> is a sentence for a person — "every Tuesday, term time" — and
/// nothing ever parses it. <see cref="Frequency"/> is the short closed list the generator steps by
/// once, at creation, and it is not stored at all. Keeping them apart is what stops the stored
/// sentence from becoming a rule the application is expected to honour later, which is the design
/// this one exists instead of.
/// </para>
/// <para>
/// One of <see cref="Count"/> and <see cref="Until"/> must be given and both may be. A request
/// naming neither says where it starts and never where it stops, and is refused rather than
/// answered with somebody's guess at how long "for ever" ought to be.
/// </para>
/// </remarks>
public sealed record EventRecurrenceRequest
{
    /// <summary>
    /// How often it comes round. Nullable so that a body naming no repetition can be told so:
    /// the vocabulary's first member is the zero value, so a non-nullable field would read an
    /// absent one as "every day" and write a month of rows nobody asked for.
    /// </summary>
    public EventRecurrenceFrequency? Frequency { get; init; }

    /// <summary>How many occurrences in total, the first included, or absent to be bounded by the last day.</summary>
    public int? Count { get; init; }

    /// <summary>
    /// The last day an occurrence may fall on, included, or absent to be bounded by the count.
    /// </summary>
    public DateOnly? Until { get; init; }

    /// <summary>
    /// How the repetition would be described to somebody reading the calendar. Required, because
    /// it is the only explanation a reader ever gets for why the same evening appears a dozen
    /// times — the repetition itself is not kept.
    /// </summary>
    public string? Rule { get; init; }
}

public sealed class EventRecurrenceRequestValidator : AbstractValidator<EventRecurrenceRequest>
{
    public EventRecurrenceRequestValidator()
    {
        // Shape only. Whether the bounds describe a series this application will write — that
        // there is a bound at all, that it repeats more than once, that it does not run past the
        // ceilings — is one question with one home, the generator, and it answers each with its
        // own stable code. Asking half of it here as well would be a second rule about the same
        // thing, free to drift from the first and to refuse under a code no surface expects.
        RuleFor(x => x.Frequency).NotNull();
        RuleFor(x => x.Rule).NotEmpty().MaximumLength(200);
    }
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

        RuleFor(x => x.Recurrence!)
            .SetValidator(new EventRecurrenceRequestValidator())
            .When(x => x.Recurrence is not null);
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

/// <summary>
/// What one edit aimed at a run of occurrences did.
/// </summary>
/// <remarks>
/// The count is the number of occurrences the edit reached, and it is the honest one rather than
/// the number asked for: a series may hold occurrences this caller cannot see, and the act is
/// refused entire rather than applied to the visible part, so a number arriving here is a number
/// of rows that really changed. A surface that says "twelve evenings were changed" when four were
/// is worse than one that says nothing.
/// </remarks>
public sealed record EventSeriesEditResultDto
{
    /// <summary>The series the edit was aimed at.</summary>
    public required Guid SeriesId { get; init; }

    /// <summary>How many occurrences were changed, the addressed one included.</summary>
    public required int Changed { get; init; }

    /// <summary>
    /// The occurrence the caller addressed, as it now stands — so a detail page that asked for
    /// the edit can redraw itself without a second read.
    /// </summary>
    public required EventDto Anchor { get; init; }
}

/// <summary>
/// What calling off the rest of a repeating event did, and what it left standing.
/// </summary>
/// <remarks>
/// <see cref="Kept"/> is not a leftover of the arithmetic — it is the part a caller most needs to
/// be told, because it is the part that surprises them. Occurrences that have already begun are
/// records of something that happened and are never removed by an act aimed at the rest of the
/// run, so a surface that reports only the deletions leaves somebody believing a series is gone
/// while half of it is still in the calendar, correctly.
/// </remarks>
public sealed record EventSeriesDeleteResultDto
{
    /// <summary>The series that was called off.</summary>
    public required Guid SeriesId { get; init; }

    /// <summary>How many occurrences were removed.</summary>
    public required int Deleted { get; init; }

    /// <summary>
    /// How many occurrences of the series stayed, having already happened — counted over the ones
    /// this caller may read. A count over all of them would tell somebody holding rights on the
    /// future of a run how many past evenings of it exist that they may not open.
    /// </summary>
    public required int Kept { get; init; }
}
