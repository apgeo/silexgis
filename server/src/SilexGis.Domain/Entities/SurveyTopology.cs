// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// The shape of one survey's passage network, expressed as the figures the karst literature
/// uses, computed once when the file is read and kept beside the model it was computed from.
/// </summary>
/// <remarks>
/// <para>
/// Two graphs are behind every figure here and they are not interchangeable. The <em>complete</em>
/// graph is every surveyed station joined by every structural leg. The <em>reduced</em> graph is
/// that graph with each run of two-legged stations contracted into a single branch, so what
/// remains is only the places passages meet or end: counting a survey's intermediate stations
/// would make a finely surveyed cave look more complex than a coarsely surveyed one of the same
/// shape. Every count, ratio and degree figure below is over the reduced graph; the length
/// figures are over the branches; <see cref="OrientationEntropy"/> alone is over the complete
/// graph, because folding a passage into one branch throws away the bearings it was walked on.
/// </para>
/// <para>
/// Stored rather than recomputed on demand, for one reason and with one obligation. The reason
/// is that the reduction and the betweenness behind
/// <see cref="CentralPointDominance"/> are superlinear in the station count, and a large system
/// is tens of thousands of stations. The obligation is that a stored figure that outlives the
/// reading it was computed from is worse than no figure at all, so this row is written and
/// rewritten inside the same transaction that writes the stations and shots, and it is removed
/// with them.
/// </para>
/// <para>
/// A figure that is not defined for a given network is null, never zero. A network of two nodes
/// has no connectivity ratio, a network of one branch has no spread of branch lengths, and a
/// branch that closes on itself has no straight line to be compared against. Reporting those as
/// zero would state something about the cave that was never measured.
/// </para>
/// </remarks>
public class SurveyTopology
{
    /// <summary>The survey model these figures were computed from; also the key, because a model
    /// has exactly one reading of its own shape.</summary>
    public Guid SurveyModelId { get; set; }

    public SurveyModel? SurveyModel { get; set; }

    /// <summary>Stations that took part in at least one structural leg.</summary>
    public int NodeCount { get; set; }

    /// <summary>Distinct pairs of stations joined by a leg. A passage surveyed twice is one
    /// edge here: it is one passage.</summary>
    public int EdgeCount { get; set; }

    /// <summary>Separate pieces the network falls into. More than one means the file holds
    /// survey that was never connected to the rest.</summary>
    public int ComponentCount { get; set; }

    public int ReducedNodeCount { get; set; }

    /// <summary>Edges of the reduced graph. Not the same as <see cref="BranchCount"/>: a branch
    /// that closes on itself becomes three edges and branches sharing both endpoints each become
    /// two, so that the reduced graph stays a simple graph without losing the loops.</summary>
    public int ReducedEdgeCount { get; set; }

    public int ReducedComponentCount { get; set; }

    /// <summary>Independent loops: reduced edges minus reduced nodes plus components. The number
    /// of passages that could be blocked before the network starts falling into pieces.</summary>
    public int CyclomaticNumber { get; set; }

    /// <summary>Reduced-graph nodes with exactly one branch: where the survey stopped.</summary>
    public int ExtremityCount { get; set; }

    /// <summary>Reduced-graph nodes with three or more branches: where passages meet.</summary>
    public int JunctionCount { get; set; }

    /// <summary>Loops present against the most a planar network of this size could hold. Null
    /// below three nodes, where the comparison has no denominator.</summary>
    public double? Alpha { get; set; }

    /// <summary>Branch ends per node — under 1 for a network with no loop, above it for one
    /// threaded with them.</summary>
    public double? Beta { get; set; }

    /// <summary>Connections present against the most a planar network of this size could hold.
    /// Null below three nodes.</summary>
    public double? Gamma { get; set; }

    public double? MeanDegree { get; set; }

    /// <summary>Spread of the reduced-graph degrees, as the sample estimate over the one network
    /// that was surveyed. Null for a single node, which has no spread.</summary>
    public double? DegreeStandardDeviation { get; set; }

    public double? DegreeCoefficientOfVariation { get; set; }

    /// <summary>Whether junctions tend to join junctions (positive) or junctions tend to join
    /// dead ends (negative). Null when there is no edge to measure across.</summary>
    public double? CorrelationOfVertexDegree { get; set; }

    public int BranchCount { get; set; }

    /// <summary>Branches that return to the station they left, and so have no straight line to
    /// be measured against. Excluded from <see cref="MeanTortuosity"/> and counted here, because
    /// a mean quietly taken over fewer branches than were surveyed is a confident wrong
    /// number.</summary>
    public int LoopingBranchCount { get; set; }

    public double? MeanBranchLengthM { get; set; }

    public double? BranchLengthCoefficientOfVariation { get; set; }

    public double? MinBranchLengthM { get; set; }

    public double? MaxBranchLengthM { get; set; }

    /// <summary>How evenly the branch lengths are spread, on a nought-to-one scale: near zero
    /// when every branch is much the same length, near one when they are spread across the whole
    /// range. Measured over ten bins of the lengths scaled by the longest branch.</summary>
    public double? LengthEntropy { get; set; }

    /// <summary>How evenly the passage bearings are spread over the half-circle, weighted by how
    /// much passage runs on each: near zero when the cave follows one direction, near one when it
    /// runs equally in all. Vertical legs have no bearing and take no part.</summary>
    public double? OrientationEntropy { get; set; }

    /// <summary>Mean of branch length over the straight distance between the branch's ends: 1 for
    /// straight passage, more for winding.</summary>
    public double? MeanTortuosity { get; set; }

    /// <summary>Mean number of branches between two places in the network, counted over the
    /// pairs that are connected at all and weighted by component size.</summary>
    public double? AverageShortestPathLength { get; set; }

    /// <summary>How far one junction dominates the routes through the network: 0 when no place is
    /// more central than any other, approaching 1 when everything passes through one point.</summary>
    public double? CentralPointDominance { get; set; }

    /// <summary>How often two branches from one junction are themselves joined. Not part of the
    /// published karst method; reported because a maze rings and a branchwork does not.</summary>
    public double? AverageClusteringCoefficient { get; set; }

    public DateTime ComputedAt { get; set; }
}
