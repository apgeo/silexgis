// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Persistence.Configurations;

namespace SilexGis.Api.Features.Caves;

/// <summary>
/// A cave's passage trends set against the trends of the structure mapped around it.
/// </summary>
/// <remarks>
/// <para>
/// Both roses are folded onto the same half-circle and cut at the same ten-degree sectors, and both
/// bearings are measured the same way — on the spheroid, from the shape as drawn. That agreement is
/// the whole basis of the comparison: a passage bearing taken on the spheroid and a fault bearing
/// taken against a projected grid differ by the convergence of the meridians, which is a few
/// degrees in the middle latitudes and would be read as a real, small disagreement between the cave
/// and the rock.
/// </para>
/// <para>
/// <b>Two kinds of structure.</b> Mapped structural <i>lines</i> — traces somebody drew on a map —
/// are compared rose against rose by length. Readings taken at a point with a compass and
/// clinometer — bedding, joints, fault planes — are compared by their strikes in
/// <see cref="MeasuredStrikes"/>, with Watson's U² test, because a reading has no length to weight a
/// rose by and the question asked of it is the statistician's one: could the passage trends and
/// the strikes have come from one distribution.
/// </para>
/// </remarks>
/// <param name="CaveId">The cave.</param>
/// <param name="Basis">Which body of line work the passage rose came from.</param>
/// <param name="IsApproximation">True when the passage rose came from the shape-based reduction.</param>
/// <param name="SurveyModelId">Which uploaded survey file was measured, when one was.</param>
/// <param name="RadiusMetres">How far around the cave structure was gathered from; null when an
/// area was named, because then the area was the reach and no radius was used.</param>
/// <param name="AreaId">The area the structure was gathered under, when one was named.</param>
/// <param name="StructureTruncated">True when more traces qualified than one answer takes: the
/// rose is built from the nearest ones to the cave (by id under a named area), and
/// <paramref name="StructureFeatureCount"/> is a count of those, not of everything in reach.</param>
/// <param name="StructureFeatureCount">
/// How many mapped traces contributed — which is how many the caller may both read and place. A
/// trace whose position is closed to them is in no rose and in no count here.
/// </param>
/// <param name="Passage">The cave's own rose, or null when it has no measurable line work.</param>
/// <param name="Structure">
/// The structure's rose, or null when nothing was found in reach. Weighted by the ground length of
/// each straight piece of each trace, so a long fault counts for more than a short one.
/// </param>
/// <param name="Divergence">
/// How far apart the two roses are by length, or null when either is empty. Zero means the passage
/// runs along the structure; one means it runs across it.
/// </param>
/// <param name="MeasuredStrikes">The passage trends against the strikes of the readings in the
/// same reach.</param>
public sealed record CaveStructureComparisonDto(
    Guid CaveId,
    SurveySegmentBasis Basis,
    bool IsApproximation,
    Guid? SurveyModelId,
    double? RadiusMetres,
    Guid? AreaId,
    int StructureFeatureCount,
    bool StructureTruncated,
    OrientationSummary? Passage,
    OrientationSummary? Structure,
    RoseDivergence? Divergence,
    MeasuredStrikeComparisonDto MeasuredStrikes);

/// <summary>
/// A cave's passage trends against the strikes of the structural readings around it, by Watson's
/// two-sample U² test on axes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Each leg counts once and each reading counts once.</b> The test counts observations, so
/// neither side is weighted by length. Legs along one passage are not independent of one another —
/// a surveyor's stations follow the passage — so a small probability here says less than its
/// number suggests, and the panel says so.
/// </para>
/// <para>
/// A reading lying flat has no strike, and is left out of the strikes rather than given the
/// arbitrary one its dip direction would imply.
/// </para>
/// </remarks>
/// <param name="MeasurementType">The kind of plane compared, or null for every kind.</param>
/// <param name="ReadingCount">Readings with a strike that took part — only readings the caller
/// may both read and place exactly.</param>
/// <param name="PassageLegCount">Survey legs with a trend that took part.</param>
/// <param name="Strikes">The rose of those strikes, each reading counted once, or null when there
/// is none.</param>
/// <param name="Watson">The test, or null when either side has fewer than
/// <paramref name="MinimumSampleSize"/> observations.</param>
/// <param name="MinimumSampleSize">The smallest sample on either side the test is run on.</param>
public sealed record MeasuredStrikeComparisonDto(
    string? MeasurementType,
    int ReadingCount,
    int PassageLegCount,
    OrientationSummary? Strikes,
    WatsonU2Result? Watson,
    int MinimumSampleSize);

/// <summary>What a caller asks when comparing a cave against the structure around it.</summary>
/// <param name="Id">The cave.</param>
/// <param name="RadiusMetres">
/// How far around the cave to gather structure from. Optional; the default is a kilometre. Ignored
/// when an area is named, because then the area is the reach.
/// </param>
/// <param name="AreaId">
/// An area of the containment hierarchy to gather the structure from instead of a buffer. A karst
/// area is a structural domain somebody drew a boundary around, and asking "do this cave's passages
/// follow the faults of this massif" is a different question from "of whatever is within a
/// kilometre" whenever the massif is not roughly circular. Optional; without it the buffer answers.
/// </param>
/// <param name="MeasurementType">
/// Which kind of structural reading to compare the passage against — bedding, joint, fault and so
/// on, by code. Optional; without it every reading in reach is compared, which mixes planes that
/// mean different things and is the caller's choice to make.
/// </param>
public sealed record CaveStructureComparisonRequest(
    Guid Id, double? RadiusMetres, Guid? AreaId, string? MeasurementType);

/// <summary>
/// An area's doline alignments set against the trends of the structure mapped in the same area.
/// </summary>
/// <remarks>
/// <para>
/// The surface half of the same question the cave view asks underground. A closed depression opens
/// where the rock is already broken, so a doline field whose long axes line up with the mapped
/// fracture set is evidence the two are the same structure seen twice; one that does not is
/// evidence the depressions are being steered by something else — a bedding dip, a buried contact,
/// a former surface drainage.
/// </para>
/// <para>
/// <b>How a doline is weighted.</b> By how much longer it is than it is wide — its long axis less
/// its short one — rather than by its size or by counting it once. A round doline has a long axis
/// only in the sense that a square has a longest side: the direction is whatever the outline's
/// least noise happened to favour, and counting it equally with a clearly elongated one fills the
/// rose with readings that mean nothing. This weight is zero for an equidimensional outline and
/// grows with elongation, which drops that noise out without a threshold anybody has to defend.
/// </para>
/// <para>
/// <b>Both roses are gated one shape at a time.</b> A doline outline places itself and so does a
/// fracture trace, so each is in the answer only for a caller who may place it exactly, and a
/// withheld one is in no rose, no count and no divergence.
/// </para>
/// </remarks>
/// <param name="AreaId">The area both roses were gathered under.</param>
/// <param name="DolineCount">How many outlines contributed — which is how many the caller may both
/// read and place, and which is deliberately not a count of the outlines in the area.</param>
/// <param name="UnmeasuredDolineCount">How many outlines, on the same footing, could not be
/// measured at all — an invalid ring, or one whose points are collinear and so have no rectangle
/// round them. Reported so an area whose depressions could not be read is not mistaken for an area
/// with none.</param>
/// <param name="StructureFeatureCount">How many mapped traces contributed, on the same footing.</param>
/// <param name="StructureTruncated">True when the area holds more traces than one answer takes,
/// so the rose was built from the first of them by id.</param>
/// <param name="Dolines">The doline rose, or null when no outline in the area has a usable
/// alignment.</param>
/// <param name="Structure">The length-weighted structure rose, or null when the area holds no
/// trace this caller may place.</param>
/// <param name="Divergence">How far apart the two are, or null when either is empty. Zero means the
/// depressions lie along the mapped structure; one means they lie across it.</param>
public sealed record AreaStructureComparisonDto(
    Guid AreaId,
    int DolineCount,
    int UnmeasuredDolineCount,
    int StructureFeatureCount,
    bool StructureTruncated,
    OrientationSummary? Dolines,
    OrientationSummary? Structure,
    RoseDivergence? Divergence);

/// <summary>What a caller asks when comparing an area's depressions against its structure.</summary>
/// <param name="Id">The area.</param>
/// <param name="FeatureTypeId">
/// Which polygon kind to read as a depression. Optional, and without it every polygon under the
/// area is measured — which mixes karst areas and cave sectors in with the dolines and describes
/// nothing in particular, so a caller that means dolines names the kind this installation calls
/// dolines. The kind is a row a person may rename, so it is named by id here rather than by a word
/// this code would have to guess.
/// </param>
public sealed record AreaStructureComparisonRequest(Guid Id, long? FeatureTypeId);

/// <summary>The rules on the area comparison request.</summary>
public sealed class AreaStructureComparisonRequestValidator
    : AbstractValidator<AreaStructureComparisonRequest>
{
    public AreaStructureComparisonRequestValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("An area id is required.");
    }
}

/// <summary>
/// The bounds on the comparison request, in one place so the validator and the handler cannot drift
/// apart.
/// </summary>
public static class CaveStructureComparisonLimits
{
    /// <summary>
    /// The reach used when the caller names none. A kilometre is the scale at which a regional
    /// fracture set is still the same fracture set: much less and a cave in the middle of a block
    /// sees nothing, much more and traces belonging to a different structural domain are averaged
    /// into the answer.
    /// </summary>
    public const double DefaultRadiusMetres = 1_000d;

    /// <summary>The shortest reach worth asking about.</summary>
    public const double MinRadiusMetres = 50d;

    /// <summary>
    /// The longest reach allowed. Not a protection bound — the traces are gated one by one and a
    /// wider reach discloses nothing narrower ones would not — but a cost bound: past this the
    /// query is gathering a map sheet rather than a neighbourhood.
    /// </summary>
    public const double MaxRadiusMetres = 25_000d;

    /// <summary>
    /// How many traces at most feed the rose. Ordered by id so the same request cuts at the same
    /// place every time rather than at whatever the scan reached first.
    /// </summary>
    public const int MaxStructureFeatures = 2_000;

    /// <summary>
    /// How many depression outlines at most feed the surface rose, and how many may be measured to
    /// find them. Measuring an outline costs a projection and an oriented envelope, so the work is
    /// bounded ahead of the measuring the way the shape table bounds it.
    /// </summary>
    public const int MaxDolines = 2_000;

    /// <inheritdoc cref="MaxDolines"/>
    public const int MaxDolineCandidates = 5_000;

    /// <summary>
    /// How many structural readings at most are gathered for one answer, ordered by id so the same
    /// request cuts at the same place every time.
    /// </summary>
    public const int MaxReadings = 2_000;
}

/// <summary>
/// The rules on the comparison request.
/// </summary>
/// <remarks>
/// The reach is a genuine question about the world rather than a dial that moves a figure: how far
/// around a cave the structure is still the structure the cave was cut in is a judgment the person
/// reading the map makes, and no single default is right for a plateau and for a gorge. It is safe
/// to let the caller set it because both ends of the comparison are already gated on exact
/// placement — the cave is withheld whole from a caller who may not place it, and each trace is
/// withheld one by one — so varying the reach and watching what appears tells such a caller nothing
/// they could not already read off the map.
/// </remarks>
public sealed class CaveStructureComparisonRequestValidator
    : AbstractValidator<CaveStructureComparisonRequest>
{
    public CaveStructureComparisonRequestValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("A cave id is required.");
        RuleFor(x => x.RadiusMetres)
            .InclusiveBetween(
                CaveStructureComparisonLimits.MinRadiusMetres,
                CaveStructureComparisonLimits.MaxRadiusMetres)
            .When(x => x.RadiusMetres is not null)
            .WithMessage(
                $"The reach must be between {CaveStructureComparisonLimits.MinRadiusMetres} and "
                + $"{CaveStructureComparisonLimits.MaxRadiusMetres} metres.");
        RuleFor(x => x.MeasurementType)
            .Must(t => t is null || StructuralMeasurement.Types.Contains(t))
            .WithMessage($"The measurement type must be one of: {string.Join(", ", StructuralMeasurement.Types)}.");
    }
}
