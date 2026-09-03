// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Persistence.Configurations;

namespace SilexGis.Api.Features.Caves;

/// <summary>
/// How one cave's passage is distributed vertically, and the levels it appears to be cut at.
/// </summary>
/// <param name="CaveId">The cave this was measured for.</param>
/// <param name="Basis">Which body of line work answered. Two caves' level proposals are the same
/// measurement only if this matches: the shape-based reduction retains most of the passage, not all
/// of it, and the metres it drops are not spread evenly up the cave.</param>
/// <param name="IsApproximation">True when the answer came from the shape-based reduction.</param>
/// <param name="SurveyModelId">Which uploaded survey file was measured, when one was.</param>
/// <param name="HasAltitudes">Whether the line work carried real altitudes. When false
/// <paramref name="Proposal"/> is null and there is nothing to draw: a cave recorded as a plan
/// drawing is not a cave on one level at height zero, and answering with a single band at zero
/// would be exactly that claim.</param>
/// <param name="Proposal">The histogram of passage length by height and the levels proposed from
/// it, or null when there are no altitudes to say it with.</param>
/// <param name="SpringAltitudesM">The altitudes of the spring-cave entrances in the area that
/// contains this cave, ascending and without repeats, for drawing across the histogram as
/// base-level reference lines. A level cut at the height water leaves the massif at means
/// something a level anywhere else does not, so proposed levels shown with nothing to set them
/// against are half the judgment the reader came to make. Empty when the cave sits in no area,
/// when the area holds no spring, or when the springs it holds are ones this caller may not
/// place — an altitude being a position, these are gathered under the same withhold-entirely rule
/// the area view uses, so a spring closed to the reader draws no line and is in no way visible as
/// a line that is missing.</param>
public sealed record CaveHypsometryDto(
    Guid CaveId,
    SurveySegmentBasis Basis,
    bool IsApproximation,
    Guid? SurveyModelId,
    bool HasAltitudes,
    ElevationBandProposal? Proposal,
    IReadOnlyList<double> SpringAltitudesM);

/// <summary>
/// How the entrances under one area are distributed vertically.
/// </summary>
/// <remarks>
/// This carries no basis because it is measured from no line work: an entrance altitude is a
/// number somebody recorded on the entrance, not a height read off a survey, and the two are not
/// the same measurement. A reader putting this distribution and a cave's passage distribution on
/// one axis is comparing where holes are with where passage is, which is a real question — but it
/// is not the same question, and nothing here should let it look like one.
/// </remarks>
/// <param name="AreaId">The area the entrances were gathered under.</param>
/// <param name="EntranceCount">How many entrance altitudes were counted — which is how many this
/// caller may place. It is not a count of the entrances in the area, and the two differ exactly
/// where somebody is being kept from a position.</param>
/// <param name="SpringAltitudesM">The altitudes of spring-cave entrances among them, ascending and
/// without repeats, for drawing as base-level reference lines. A level cut at the height water
/// leaves the massif at means something a level anywhere else does not.</param>
/// <param name="Proposal">The histogram and the levels proposed from it. Every entrance weighs one,
/// so this is a count distribution where the per-cave view is a length distribution.</param>
public sealed record AreaHypsometryDto(
    Guid AreaId,
    int EntranceCount,
    IReadOnlyList<double> SpringAltitudesM,
    ElevationBandProposal Proposal);

/// <summary>
/// One level of a saved reading, as a person wrote it down.
/// </summary>
/// <param name="FromM">Lower edge, metres.</param>
/// <param name="ToM">Upper edge, metres. Strictly above the lower edge — a band of no height is
/// not a level, it is a mistake in a form.</param>
/// <param name="Label">What the person calls this level, if anything.</param>
public sealed record SavedElevationBand(double FromM, double ToM, string? Label);

/// <summary>
/// What somebody decided one cave's levels are, or the fact that nobody has yet.
/// </summary>
/// <remarks>
/// Nothing saved is answered as a document with nulls rather than as a refusal: "no reading has
/// been recorded" is the ordinary state of every cave and is a real answer to the question, while a
/// 404 would be indistinguishable from the cave being kept from the reader.
/// </remarks>
/// <param name="CaveId">The cave.</param>
/// <param name="Confirmed">Whether a current reading exists.</param>
/// <param name="Bands">The levels, ascending; empty when nothing is saved.</param>
/// <param name="Note">What the person wrote about the reading.</param>
/// <param name="ConfirmedBy">Who recorded it.</param>
/// <param name="ConfirmedAt">When they did.</param>
public sealed record CaveLevelBandsDto(
    Guid CaveId,
    bool Confirmed,
    IReadOnlyList<SavedElevationBand> Bands,
    string? Note,
    Guid? ConfirmedBy,
    DateTimeOffset? ConfirmedAt);

/// <summary>What a caller sends to record a reading of a cave's levels.</summary>
/// <param name="Bands">The levels, at least one, ascending and not overlapping.</param>
/// <param name="Note">Optional remark about the reading.</param>
public sealed record SaveCaveLevelBandsRequest(IReadOnlyList<SavedElevationBand>? Bands, string? Note);

/// <summary>
/// The caps the validator and the handlers share, in one place so they cannot drift apart.
/// </summary>
public static class CaveLevelBandLimits
{
    /// <summary>
    /// The most levels a reading may name. The same cap the proposal is searched under, because a
    /// person confirming a proposal must be able to save what they were shown, and because past it
    /// this stops being a description of a cave and starts being a copy of the histogram.
    /// </summary>
    public const int MaxBands = ElevationBands.MaximumBands;

    /// <summary>The longest a label may be.</summary>
    public const int MaxLabelLength = 120;

    /// <summary>The longest a note may be.</summary>
    public const int MaxNoteLength = CaveLevelBandsConfiguration.NoteMaxLength;
}

/// <summary>
/// What has to hold of a reading before it is stored. The ordering rule is here rather than in the
/// handler because a set of levels that overlap or run backwards is not a reading of a cave that
/// happens to be unusual — it is a form that was filled in wrong, and it should be refused where
/// every other malformed request is.
/// </summary>
public sealed class SaveCaveLevelBandsRequestValidator : AbstractValidator<SaveCaveLevelBandsRequest>
{
    public SaveCaveLevelBandsRequestValidator()
    {
        RuleFor(x => x.Bands).NotNull().NotEmpty()
            .WithMessage("At least one level band is required.");

        RuleFor(x => x.Bands!.Count).LessThanOrEqualTo(CaveLevelBandLimits.MaxBands)
            .When(x => x.Bands is not null)
            .WithMessage($"At most {CaveLevelBandLimits.MaxBands} level bands may be recorded.");

        RuleFor(x => x.Note).MaximumLength(CaveLevelBandLimits.MaxNoteLength);

        RuleForEach(x => x.Bands).ChildRules(band =>
        {
            band.RuleFor(b => b.FromM).Must(double.IsFinite)
                .WithMessage("A band edge must be a number.");
            band.RuleFor(b => b.ToM).Must(double.IsFinite)
                .WithMessage("A band edge must be a number.");
            band.RuleFor(b => b.ToM).GreaterThan(b => b.FromM)
                .WithMessage("A band must be higher at the top than at the bottom.");
            band.RuleFor(b => b.Label).MaximumLength(CaveLevelBandLimits.MaxLabelLength);
        });

        RuleFor(x => x.Bands!).Must(BeAscendingAndDisjoint)
            .When(x => x.Bands is not null && x.Bands.Count > 1)
            .WithMessage("Level bands must be in ascending order and must not overlap.");
    }

    private static bool BeAscendingAndDisjoint(IReadOnlyList<SavedElevationBand> bands)
    {
        for (var i = 1; i < bands.Count; i++)
        {
            if (bands[i].FromM < bands[i - 1].ToM)
            {
                return false;
            }
        }

        return true;
    }
}
