// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Entities;

namespace SilexGis.Api.Features.Expeditions;

/// <summary>
/// An expedition as it travels: what it is called, when it runs, roughly where it works, who
/// governs it and where it has got to.
/// </summary>
/// <remarks>
/// <para>
/// Written with named properties rather than as a positional record, and deliberately. A
/// positional record is constructed by position, so a member inserted between two of the same
/// type is absorbed by the neighbour it displaces — the compiler is happy, the wire is wrong,
/// and nothing fails until somebody reads a date that is really another date. The trip's own
/// records carry warnings saying so and can now only be appended to. This one is going to grow
/// a great deal — membership, roll-up counts, a roster — so it starts in the shape where a
/// member can be added anywhere, named at every construction site, and a rename is a compile
/// error rather than a silent swap.
/// </para>
/// <para>
/// The cost is that every construction site names every member; that is the point.
/// </para>
/// </remarks>
public sealed record ExpeditionDto
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public required DateOnly StartDate { get; init; }

    /// <summary>
    /// The last day, or absent when the camp lasts a single day. Absent means "it did not run on
    /// past its first day" rather than "the end is unknown": a reader asks one field, never two.
    /// </summary>
    public DateOnly? EndDate { get; init; }

    /// <summary>
    /// Roughly where the camp works — the area drawn on the plan, not a position anybody
    /// navigates by and not derived from the caves its trips reach.
    /// </summary>
    public GeoJsonGeometry? Geom { get; init; }

    public required Guid OwnerUserId { get; init; }

    public Guid? CavingGroupId { get; init; }

    public required Visibility Visibility { get; init; }

    public required ActivityState State { get; init; }

    /// <summary>When it was first announced, or absent while it never has been.</summary>
    public DateTimeOffset? PublishedAt { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// A full write of an expedition. Named properties for the reason the reading above gives.
/// </summary>
/// <remarks>
/// The lifecycle pair is deliberately absent: a state moves through the transition endpoint,
/// which is the only place the legal moves are checked. A write request carrying a state would
/// be a second way to set one, past the table that says which moves exist.
/// </remarks>
public sealed record ExpeditionWriteRequest
{
    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public DateOnly StartDate { get; init; }

    /// <summary>
    /// The last day. An end equal to the start is accepted and stored as nothing, because a
    /// surface offering a date range has no way to say "one day" other than by picking the same
    /// day twice. An end before the start is a mistyped date and is refused rather than quietly
    /// turned into one day.
    /// </summary>
    public DateOnly? EndDate { get; init; }

    public GeoJsonGeometry? Geom { get; init; }

    public Guid? CavingGroupId { get; init; }

    public Visibility Visibility { get; init; }
}

/// <summary>The state to move an expedition into.</summary>
/// <remarks>
/// Nullable, and it has to be. The state is the whole of this request, and the vocabulary's first
/// member is the zero value, so a non-nullable field would read a body that names no state at all
/// as naming the workshop — and since every live state has a legal move back there, an empty body
/// would quietly un-announce a camp and answer 200. Nullable lets the shape tell "absent" from
/// "draft" and refuse the first.
/// </remarks>
public sealed record ExpeditionTransitionRequest
{
    public ActivityState? State { get; init; }
}

public sealed class ExpeditionWriteRequestValidator : AbstractValidator<ExpeditionWriteRequest>
{
    public ExpeditionWriteRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Description).MaximumLength(10000);

        RuleFor(x => x.EndDate)
            .GreaterThanOrEqualTo(x => x.StartDate)
            .When(x => x.EndDate is not null)
            .WithMessage("The end date must not precede the start date.");

        RuleFor(x => x.Visibility).IsInEnum();
    }
}

public sealed class ExpeditionTransitionRequestValidator : AbstractValidator<ExpeditionTransitionRequest>
{
    public ExpeditionTransitionRequestValidator()
    {
        // That a state was named at all, and that the value is one the vocabulary has. Whether an
        // expedition may hold it, and whether it may get there from where it is, are the
        // transition table's to answer — and it answers both with one refusal, so there is no
        // second place a state can be judged. The presence check cannot be left to the table: an
        // absent field arrives as the enum's zero value, which is a state the table admits.
        RuleFor(x => x.State).NotNull().IsInEnum();
    }
}
