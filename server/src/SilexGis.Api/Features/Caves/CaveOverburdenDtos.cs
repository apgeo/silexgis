// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Terrain;

namespace SilexGis.Api.Features.Caves;

/// <summary>
/// How much rock lies over the passage at one place along it.
/// </summary>
/// <param name="DistanceAlongM">How far along the passage this reading was taken, metres. See
/// <see cref="CaveOverburdenDto"/> for what the distance is measured along.</param>
/// <param name="Longitude">Where the reading was taken, degrees.</param>
/// <param name="Latitude">Where the reading was taken, degrees.</param>
/// <param name="PassageAltitudeM">Altitude of the passage there, metres, in the survey's own
/// vertical datum.</param>
/// <param name="Outcome">Whether the ground could be read there at all, and if not, why not.</param>
/// <param name="GroundAltitudeM">Altitude of the ground surface there, metres, in the survey's own
/// vertical datum; null wherever <paramref name="Outcome"/> is not a reading. It is <b>never zero
/// standing in for an absent reading</b> — a surface at sea level over a passage is a plausible,
/// alarming and entirely invented number.</param>
/// <param name="OverburdenM">Ground altitude minus passage altitude, metres: the thickness of rock
/// overhead. Null wherever the ground could not be read, which the chart must draw as a break in the
/// curve rather than join across. Negative where the elevation model puts the surface below the
/// passage, which is reported as measured rather than clamped to nothing — it means the two do not
/// agree, and hiding that hides the disagreement rather than the error.</param>
/// <param name="PathIndex">Which polyline of the line work the reading fell on; null where the line
/// work is a network of legs rather than ordered paths.</param>
/// <param name="SegmentIndex">Which piece of the line work the reading fell on, so that a reader
/// hovering a point on the curve can be told which piece of passage answered.</param>
public sealed record CaveOverburdenSampleDto(
    double DistanceAlongM,
    double Longitude,
    double Latitude,
    double PassageAltitudeM,
    DemSampleOutcome Outcome,
    double? GroundAltitudeM,
    double? OverburdenM,
    int? PathIndex,
    int SegmentIndex);

/// <summary>
/// How much rock is over your head along one cave's passages.
///
/// <para>
/// <b>This is cave data, not a terrain statistic.</b> It is the shape and the position of the
/// passage set against the surface above it, so anything holding it can work out where the passage
/// runs. It is withheld whole from a caller who may not place the cave exactly — no curve, no
/// readings, no length — and the refusal is the same "no such cave" a caller gets for a cave that
/// was never created.
/// </para>
///
/// <para>
/// <b>The horizontal axis is cumulative passage length in the order the line work records it.</b> A
/// survey is a network and a reduced centerline is a set of polylines; neither is one walk through
/// the cave. So a distance along says how much passage lies before that reading, and not that the
/// readings are strung along a route anybody could follow.
/// </para>
/// </summary>
/// <param name="CaveId">The cave measured.</param>
/// <param name="Basis">Which body of line work the passage positions came out of. This is not
/// decoration and a reader must not drop it: a depth to surface worked out over the surveyor's own
/// per-shot flags and one worked out over a shape-based reduction of a stored drawing are two
/// different claims, and only this separates them.</param>
/// <param name="IsApproximation">True when <paramref name="Basis"/> is the shape-based reduction —
/// the same statement, in the form a presentation layer can act on without knowing the vocabulary.</param>
/// <param name="SurveyModelId">Which uploaded survey file answered; null for the reduction and for a
/// cave with no line work.</param>
/// <param name="HasAltitudes">Whether the line work carried altitudes at all. False means no profile
/// could be built: with no passage altitude there is nothing to subtract a ground height from, and a
/// profile of zeros would draw a plan drawing as a cave lying on the surface.</param>
/// <param name="HasTerrain">Whether this installation has any prepared elevation data at all. It
/// separates "nobody has built terrain here" from "terrain is built and does not reach this cave",
/// which look identical in the readings and are entirely different problems.</param>
/// <param name="PassageLengthM">The length the readings were spread over, metres — real length along
/// the passage, duplicated legs excluded, so it is the same figure the cave's own length reports.</param>
/// <param name="CoveredSampleCount">How many readings actually got a ground height. Compared against
/// the number of samples it is the whole of the coverage story: a profile over a tenth of a cave and
/// a profile over all of it are otherwise indistinguishable.</param>
/// <param name="MinOverburdenM">Thinnest rock overhead among the readings that got one; null when
/// none did. Computed here rather than in a chart, because a figure derived a second way eventually
/// disagrees with the same figure printed beside it.</param>
/// <param name="MaxOverburdenM">Thickest rock overhead among the readings that got one; null when
/// none did.</param>
/// <param name="MeanOverburdenM">Mean thickness over the readings that got one, and over nothing
/// else; null when none did. Averaging an absent reading in as zero would report a cave as shallower
/// the less of it is covered.</param>
/// <param name="Samples">The readings, in increasing distance along.</param>
public sealed record CaveOverburdenDto(
    Guid CaveId,
    SurveySegmentBasis Basis,
    bool IsApproximation,
    Guid? SurveyModelId,
    bool HasAltitudes,
    bool HasTerrain,
    double PassageLengthM,
    int CoveredSampleCount,
    double? MinOverburdenM,
    double? MaxOverburdenM,
    double? MeanOverburdenM,
    IReadOnlyList<CaveOverburdenSampleDto> Samples);

/// <summary>Which cave to profile. The route takes nothing else.</summary>
/// <param name="Id">The cave.</param>
public sealed record CaveOverburdenRequest(Guid Id);

/// <summary>Refuses a request naming no cave before it reaches the access check.</summary>
public sealed class CaveOverburdenRequestValidator : AbstractValidator<CaveOverburdenRequest>
{
    public CaveOverburdenRequestValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
