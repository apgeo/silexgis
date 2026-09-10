// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;

namespace SilexGis.Api.Features.Caves;

/// <summary>
/// The shape of one cave's passage network, as the figures the karst literature uses.
/// </summary>
/// <remarks>
/// Every figure is either a number that was measured or null. Null is never a zero: a network of
/// two junctions has no connectivity ratio and a single branch has no spread of lengths, and
/// reporting either as zero would state something about the cave that was never measured.
/// </remarks>
/// <param name="CaveId">The cave asked about.</param>
/// <param name="SurveyModelId">Which of the cave's uploaded survey files was measured. A cave may
/// hold several and exactly one of them answers, so two readings describe the same passage only if
/// this is the same in both.</param>
/// <param name="DroppedShotCount">Legs the reading of that file could not attach to the station
/// network. Carried on every answer because an incomplete network still yields figures that look
/// entirely reasonable, and this is the only thing that says they were computed over less than the
/// file contained. Null means the file has not been read, which is not the same as nothing having
/// been lost.</param>
/// <param name="MergedStationCount">Stations that shared a position with an earlier one and so
/// became one node. Merging is silent, and it is what causes legs to be dropped.</param>
/// <param name="NodeCount">Stations taking part in at least one structural leg.</param>
/// <param name="EdgeCount">Distinct pairs of stations joined by a leg: a passage surveyed twice is
/// one passage.</param>
/// <param name="ComponentCount">Separate pieces the network falls into.</param>
/// <param name="ReducedNodeCount">Junctions and dead ends: what is left once every run of
/// two-legged stations is contracted into a single branch.</param>
/// <param name="ReducedEdgeCount">Connections between them.</param>
/// <param name="ReducedComponentCount">Separate pieces of the contracted network.</param>
/// <param name="CyclomaticNumber">Independent loops.</param>
/// <param name="ExtremityCount">Places the survey stops.</param>
/// <param name="JunctionCount">Places three or more passages meet.</param>
/// <param name="Alpha">Loops present against the most a network of this size could hold.</param>
/// <param name="Beta">Connections per junction.</param>
/// <param name="Gamma">Connections present against the most a network of this size could
/// hold.</param>
/// <param name="MeanDegree">Passages meeting at an average junction or dead end.</param>
/// <param name="DegreeStandardDeviation">Spread of that figure.</param>
/// <param name="DegreeCoefficientOfVariation">That spread relative to the average.</param>
/// <param name="CorrelationOfVertexDegree">Whether junctions tend to join junctions (positive) or
/// junctions tend to join dead ends (negative).</param>
/// <param name="BranchCount">Runs of passage between junctions and dead ends.</param>
/// <param name="LoopingBranchCount">Branches returning to the station they left, which have no
/// straight line to be measured against and take no part in the tortuosity.</param>
/// <param name="MeanBranchLengthM">Average length of a branch.</param>
/// <param name="BranchLengthCoefficientOfVariation">Spread of the branch lengths relative to their
/// average.</param>
/// <param name="MinBranchLengthM">Shortest branch.</param>
/// <param name="MaxBranchLengthM">Longest branch.</param>
/// <param name="LengthEntropy">How evenly the branch lengths are spread, nought to one.</param>
/// <param name="OrientationEntropy">How evenly the passage bearings are spread over the
/// half-circle, weighted by passage length, nought to one.</param>
/// <param name="MeanTortuosity">Branch length over the straight line between its ends.</param>
/// <param name="AverageShortestPathLength">Branches between two places, on average.</param>
/// <param name="CentralPointDominance">How far one junction dominates the routes through the
/// network.</param>
/// <param name="AverageClusteringCoefficient">How often two passages from one junction are
/// themselves joined.</param>
/// <param name="ComputedAt">When the figures were measured from the file.</param>
public sealed record CaveTopologyDto(
    Guid CaveId,
    Guid SurveyModelId,
    int? DroppedShotCount,
    int? MergedStationCount,
    int NodeCount,
    int EdgeCount,
    int ComponentCount,
    int ReducedNodeCount,
    int ReducedEdgeCount,
    int ReducedComponentCount,
    int CyclomaticNumber,
    int ExtremityCount,
    int JunctionCount,
    double? Alpha,
    double? Beta,
    double? Gamma,
    double? MeanDegree,
    double? DegreeStandardDeviation,
    double? DegreeCoefficientOfVariation,
    double? CorrelationOfVertexDegree,
    int BranchCount,
    int LoopingBranchCount,
    double? MeanBranchLengthM,
    double? BranchLengthCoefficientOfVariation,
    double? MinBranchLengthM,
    double? MaxBranchLengthM,
    double? LengthEntropy,
    double? OrientationEntropy,
    double? MeanTortuosity,
    double? AverageShortestPathLength,
    double? CentralPointDominance,
    double? AverageClusteringCoefficient,
    DateTime ComputedAt);

/// <summary>What the topology route is asked: a cave, and nothing else.</summary>
/// <param name="Id">The cave.</param>
public sealed record CaveTopologyRequest(Guid Id);

/// <summary>
/// The route constraint accepts the all-zero guid as a well-formed one and no cave is ever it, so
/// it is refused here as nonsense rather than answered as a cave nobody can see.
/// </summary>
public sealed class CaveTopologyRequestValidator : AbstractValidator<CaveTopologyRequest>
{
    public CaveTopologyRequestValidator() =>
        RuleFor(x => x.Id).NotEmpty().WithMessage("A cave id is required.");
}
