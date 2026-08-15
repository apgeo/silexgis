// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;

namespace SilexGis.Api.Features.Expeditions;

/// <summary>
/// One stretch of days somebody was at a camp, as it travels: who, in what role, from when to
/// when, and why the row reads as it does.
/// </summary>
/// <remarks>
/// <para>
/// Named properties rather than positional, for the reason the camp's own reading gives: a member
/// inserted between two of the same type would be absorbed by the neighbour it displaces, and
/// nothing would fail until somebody read a date that was really another date.
/// </para>
/// <para>
/// A row is a person <em>and</em> a role, so somebody who cooked and surveyed travels as two rows
/// and is one person. Never count these rows to answer "how many people were there" — the reading
/// carries that count already, worked out distinctly by person.
/// </para>
/// </remarks>
public sealed record ExpeditionRosterEntryDto
{
    public required long Id { get; init; }

    public required Guid ExpeditionId { get; init; }

    public required Guid CaverId { get; init; }

    /// <summary>
    /// The name this caller may see the person under — an account's own label where they have
    /// one, so nobody appears under two names on the same page. Never their address.
    /// </summary>
    public required string CaverName { get; init; }

    public required long RoleId { get; init; }

    public required DateOnly FromDate { get; init; }

    /// <summary>
    /// The last day of the stay, absent when nothing ran on past the first day. While the camp is
    /// still going that reads as somebody who has not left; on a camp that is over it reads as the
    /// single day it was. A reader asks one field, never two.
    /// </summary>
    public DateOnly? ToDate { get; init; }

    public string? Note { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// A camp's whole roster: every stay recorded against it, and how many people that comes to.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="People"/> is worked out on the server and travels beside the rows because counting
/// the rows is wrong and looks right: a roster holds one row per person per role, so a count of
/// rows reports a cook who also surveyed as two people. That mistake has been made on this shape
/// before and raises nothing when it happens — the number is simply too big.
/// </para>
/// <para>
/// Unpaged, deliberately: a camp's people are a bounded list read as one thing, and paging it
/// would make the count beside it disagree with the rows on the page.
/// </para>
/// </remarks>
public sealed record ExpeditionRosterDto
{
    public required Guid ExpeditionId { get; init; }

    public required IReadOnlyList<ExpeditionRosterEntryDto> Entries { get; init; }

    /// <summary>How many distinct people the rows name.</summary>
    public required int People { get; init; }
}

/// <summary>
/// One stay, written or rewritten whole.
/// </summary>
/// <remarks>
/// The person is named by their entry in the club's directory and never by a bare name. A trip
/// may name somebody who is not in the directory yet, because a trip is written up on the evening
/// it happened by whoever was there; a camp's roster is kept over a fortnight by whoever is
/// running it, and the person it records is somebody the club has already had to know about.
/// </remarks>
public sealed record ExpeditionRosterEntryWriteRequest
{
    public Guid CaverId { get; init; }

    public long RoleId { get; init; }

    public DateOnly FromDate { get; init; }

    /// <summary>
    /// The last day. An end equal to the first day is accepted and stored as nothing, the way the
    /// camp's own dates are, because a surface offering a range has no way to say "one day" other
    /// than by picking the same day twice. An end before the first day is a mistyped date and is
    /// refused rather than quietly turned into one day.
    /// </summary>
    public DateOnly? ToDate { get; init; }

    public string? Note { get; init; }
}

public sealed class ExpeditionRosterEntryWriteRequestValidator
    : AbstractValidator<ExpeditionRosterEntryWriteRequest>
{
    public ExpeditionRosterEntryWriteRequestValidator()
    {
        RuleFor(x => x.CaverId).NotEmpty();
        RuleFor(x => x.RoleId).GreaterThan(0);

        // A stay with no first day is not a stay. The default of a date is a real value the
        // database would take, so the shape has to refuse it rather than leaving it to a
        // constraint that has no opinion about the year 1.
        RuleFor(x => x.FromDate).NotEqual(default(DateOnly))
            .WithMessage("The first day of the stay is required.");

        RuleFor(x => x.ToDate)
            .GreaterThanOrEqualTo(x => x.FromDate)
            .When(x => x.ToDate is not null)
            .WithMessage("The last day must not precede the first day.");

        RuleFor(x => x.Note).MaximumLength(500);
    }
}
