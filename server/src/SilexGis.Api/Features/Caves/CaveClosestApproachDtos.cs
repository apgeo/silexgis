// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;

namespace SilexGis.Api.Features.Caves;

/// <summary>
/// Why there is no distance, when there is none. There is no member for "you may not be told":
/// a caller who may not place both caves is answered as though neither cave existed, so that
/// case never reaches this vocabulary.
/// </summary>
public enum ClosestApproachAbsence
{
    /// <summary>There is a distance. Every figure on the answer is filled in.</summary>
    None = 0,

    /// <summary>
    /// At least one of the two caves has no line work to measure from. A cave whose surveys have
    /// all been removed, and a cave whose line work is there but is not this caller's to see,
    /// answer identically on purpose.
    /// </summary>
    NoLineWork = 1,

    /// <summary>
    /// Both caves have line work but at least one of them was drawn in plan and never recorded a
    /// depth. There is no honest three-dimensional distance between a cave and a drawing, and
    /// reporting the plan separation as though it were one would describe two caves as touching
    /// that may be two hundred metres apart vertically.
    /// </summary>
    NoAltitudes = 2,
}

/// <summary>
/// One end of the shortest line between two caves, in the stored system so it can be drawn.
/// </summary>
/// <param name="Longitude">Stored system.</param>
/// <param name="Latitude">Stored system.</param>
/// <param name="AltitudeM">Metres, the same altitude the line work carries.</param>
public sealed record ClosestApproachPointDto(double Longitude, double Latitude, double AltitudeM);

/// <summary>
/// How close two caves come to each other.
/// </summary>
/// <remarks>
/// Present on every answer that has one, absent from every answer that does not — there is no
/// approximate form. The whole figure is withheld from a caller who may not place both caves
/// exactly, because a distance and a bearing from a cave whose position that caller does have
/// would place the other one to the metre.
/// </remarks>
/// <param name="CaveAId">The cave named first. The pair is reported lowest id first however it
/// was asked for, so the same pair is the same row wherever it appears.</param>
/// <param name="CaveAName">Its name.</param>
/// <param name="CaveBId">The cave named second.</param>
/// <param name="CaveBName">Its name.</param>
/// <param name="Absence">Why there is no measurement, or <see cref="ClosestApproachAbsence.None"/>
/// when there is one. Never null: an answer with nothing in it still says something, and a reader
/// that showed a blank where a reason belongs would present "these two caves have no depths
/// recorded" as "these two caves have not been compared".</param>
/// <param name="DistanceM">The shortest distance in three dimensions, metres.</param>
/// <param name="HorizontalDistanceM">The plan separation of the two ends of that shortest line.
/// Not the shortest plan distance between the caves, which is a different pair of points and a
/// smaller number.</param>
/// <param name="VerticalDistanceM">The altitude difference between those same two ends.</param>
/// <param name="BearingDegrees">True bearing from the first cave's end to the second's, degrees
/// clockwise from north. Null when the two ends coincide.</param>
/// <param name="From">The first cave's end of the line.</param>
/// <param name="To">The second cave's end of the line.</param>
public sealed record ClosestApproachDto(
    Guid CaveAId,
    string? CaveAName,
    Guid CaveBId,
    string? CaveBName,
    ClosestApproachAbsence Absence,
    double? DistanceM,
    double? HorizontalDistanceM,
    double? VerticalDistanceM,
    double? BearingDegrees,
    ClosestApproachPointDto? From,
    ClosestApproachPointDto? To);

/// <summary>
/// The nearest pairs of caves in a piece of country, nearest first.
/// </summary>
/// <param name="Pairs">The pairs, nearest first. Only pairs both of whose caves this caller may
/// place are in it, and it carries no count of what that left out: a table saying "and four more
/// you may not see" would answer the very question the omission exists to refuse.</param>
/// <param name="MaxDistanceM">The threshold the table was built with, so a reader knows whether an
/// empty table means "no caves near each other" or "none within this far".</param>
public sealed record ClosestApproachTableDto(
    IReadOnlyList<ClosestApproachDto> Pairs,
    double MaxDistanceM);

/// <summary>What the per-pair route is asked: two caves.</summary>
/// <param name="Id">The cave the route is under.</param>
/// <param name="Other">The cave to measure to.</param>
public sealed record CaveClosestApproachRequest(Guid Id, Guid Other);

/// <summary>
/// The rules there are to state about a request carrying two cave ids.
/// </summary>
/// <remarks>
/// The all-zero guid is a well-formed route value and is no cave, so it is refused here as a
/// malformed request rather than answered as a cave nobody can see — the two genuinely different
/// failures should not arrive looking identical. A cave against itself is refused for the same
/// reason: its answer would be zero, which is true and is not what was asked.
/// </remarks>
public sealed class CaveClosestApproachRequestValidator : AbstractValidator<CaveClosestApproachRequest>
{
    public CaveClosestApproachRequestValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("A cave id is required.");
        RuleFor(x => x.Other).NotEmpty().WithMessage("A second cave id is required.");
        RuleFor(x => x.Other).NotEqual(x => x.Id)
            .WithMessage("The two caves must be different.");
    }
}

/// <summary>What the area route is asked: a box, how far apart is still interesting, and how many
/// rows to return.</summary>
/// <param name="West">Bounding box, stored system.</param>
/// <param name="South">Bounding box, stored system.</param>
/// <param name="East">Bounding box, stored system.</param>
/// <param name="North">Bounding box, stored system.</param>
/// <param name="MaxDistanceM">How far apart two caves may be and still be listed.</param>
/// <param name="Limit">How many pairs to return.</param>
public sealed record ClosestApproachTableRequest(
    double West,
    double South,
    double East,
    double North,
    double? MaxDistanceM,
    int? Limit);

/// <summary>
/// Bounds on the area request: the box has to be a box, and the threshold and the row count are
/// each capped.
/// </summary>
/// <remarks>
/// Neither cap is what keeps the request finite, and it is worth being clear about that. The work
/// is quadratic in how many caves the box admits; the threshold is the predicate that pairing
/// evaluates rather than a filter ahead of it, and the row count is applied to what the pairing
/// produced. A box is free to be the whole world, so the bound that matters is a ceiling on how
/// many caves enter the pairing at all — <see cref="ClosestApproachLimits.MaxCavesPaired"/>, which
/// the query applies inside its own narrowing.
/// </remarks>
public sealed class ClosestApproachTableRequestValidator : AbstractValidator<ClosestApproachTableRequest>
{
    public ClosestApproachTableRequestValidator()
    {
        RuleFor(x => x.West).InclusiveBetween(-180, 180);
        RuleFor(x => x.East).InclusiveBetween(-180, 180);
        RuleFor(x => x.South).InclusiveBetween(-90, 90);
        RuleFor(x => x.North).InclusiveBetween(-90, 90);
        RuleFor(x => x.East).GreaterThan(x => x.West)
            .WithMessage("The eastern edge must be east of the western one.");
        RuleFor(x => x.North).GreaterThan(x => x.South)
            .WithMessage("The northern edge must be north of the southern one.");
        RuleFor(x => x.MaxDistanceM)
            .InclusiveBetween(1, ClosestApproachLimits.MaxDistanceCeilingM)
            .When(x => x.MaxDistanceM is not null);
        RuleFor(x => x.Limit)
            .InclusiveBetween(1, ClosestApproachLimits.MaxRows)
            .When(x => x.Limit is not null);
    }
}

/// <summary>The bounds the area route is held to, in one place so the validator and the handler
/// cannot drift apart about them.</summary>
public static class ClosestApproachLimits
{
    /// <summary>Ten kilometres. Beyond this two caves are not near each other in any sense a
    /// caver means by the word.</summary>
    public const double MaxDistanceCeilingM = 10_000d;

    /// <summary>Two hundred metres, the default: close enough to be worth knowing about, and the
    /// distance at which two systems start being talked about as one.</summary>
    public const double DefaultDistanceM = 200d;

    /// <summary>How many pairs a table may hold at most.</summary>
    public const int MaxRows = 200;

    /// <summary>How many it holds when nobody said.</summary>
    public const int DefaultRows = 25;

    /// <summary>
    /// How many caves may be paired against each other for one table.
    ///
    /// <para>
    /// The pairing compares every admitted cave with every other one, so this number squared is
    /// the size of the request: five hundred caves is a hundred and twenty-five thousand pairs,
    /// which is bounded work, and no bound at all is a signed-in caller being able to ask an
    /// installation to compare every survey it holds with every other one. It is a ceiling rather
    /// than a page: a box holding more caves than this is a box nobody drew on purpose, and the
    /// answer it gets is built from the first caves in id order.
    /// </para>
    /// </summary>
    public const int MaxCavesPaired = 500;
}
