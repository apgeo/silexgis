// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using SilexGis.Domain.Profiles;

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
    /// The last day of the stay, absent only while the person is still there. A stay of a single
    /// day carries a last day equal to its first — unlike a camp or a trip, where one day is an
    /// absent end, because neither of those is ever written down as still going on.
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
/// <para>
/// The person is named in one of two ways and never both: by their entry in the club's
/// directory, or by a name. A name is how a camp records the people a roster exists for — the
/// cook who never signs in and never goes underground has no entry until somebody makes one,
/// and making it a separate errand to be run before the stay can be written is how the stay
/// comes not to be written. A name somebody is already recorded under means that person, the
/// oldest entry if several hold it; a name nobody holds adds them to the directory in the same
/// save as the stay. It is the rule a trip names its people by, and the same code.
/// </para>
/// <para>
/// Naming somebody asks no right over the directory, exactly as on a trip: it is part of
/// writing the camp's own record, and the right to write the camp is the right that is asked.
/// </para>
/// </remarks>
public sealed record ExpeditionRosterEntryWriteRequest
{
    /// <summary>
    /// The person's entry in the directory. Absent when <see cref="NewCaverName"/> says who.
    /// </summary>
    public Guid? CaverId { get; init; }

    /// <summary>
    /// The person's name, for somebody given no entry here. Matched against the directory as
    /// written, spaces around it aside, and added to it when nobody is recorded under it.
    /// </summary>
    public string? NewCaverName { get; init; }

    public long RoleId { get; init; }

    public DateOnly FromDate { get; init; }

    /// <summary>
    /// The last day, or nothing for somebody who is still there. Stored as sent: a last day
    /// equal to the first is a stay of one day and is kept as that, so leaving it out never means
    /// "one day". A last day before the first is a mistyped date and is refused.
    /// </summary>
    public DateOnly? ToDate { get; init; }

    public string? Note { get; init; }
}

public sealed class ExpeditionRosterEntryWriteRequestValidator
    : AbstractValidator<ExpeditionRosterEntryWriteRequest>
{
    public ExpeditionRosterEntryWriteRequestValidator()
    {
        // Exactly one way of saying who, and a name no longer than the directory can hold. Both
        // are asked of the rule every record that names a person answers to, rather than written
        // out here a second time: a trip's people and a camp's stays must not come to disagree
        // about what a person may be called.
        RuleFor(x => x)
            .Must(x => CaverReferenceRules.NamesOnePerson(x.CaverId, x.NewCaverName))
            .WithMessage("The person is either an existing caver or a new name, not both.");
        RuleFor(x => x.NewCaverName)
            .MaximumLength(CaverReferenceRules.NameMaxLength)
            .WithMessage($"Names are limited to {CaverReferenceRules.NameMaxLength} characters.");
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
