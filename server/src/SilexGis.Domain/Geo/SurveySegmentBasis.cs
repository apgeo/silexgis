// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Geo;

/// <summary>
/// Which body of line work a survey measurement was computed over. Every segment, and every
/// statistic derived from a set of them, carries this.
///
/// <para>
/// It is not metadata. The two available bodies of line work do not describe the same passage:
/// one is the surveyor's own per-leg flags, the other is a shape-based guess at which legs were
/// wall shots. Measured on real exports the guess retains 92–95% of the surveyed length, so a
/// rose or a length computed from it is <i>close</i>, not equal, to the one computed from the
/// flags. A reader handed two numbers without being told which came from which will compare
/// them as though they were the same measurement, and the difference will read as a change in
/// the cave rather than a change in the method.
/// </para>
/// </summary>
public enum SurveySegmentBasis : short
{
    /// <summary>
    /// Nothing to measure: the cave has neither extracted survey legs nor a centerline the
    /// caller may see. Reported by a consumer that found no segments; no segment ever carries it.
    /// </summary>
    Unavailable = 0,

    /// <summary>
    /// The legs of a parsed survey file, filtered by the flags the surveyor's own software
    /// wrote — wall shots and surface legs excluded because the file says they are, not because
    /// their shape suggests it. This is the definition of the statistic; everything else is an
    /// approximation of it.
    /// </summary>
    SurveyFlags = 1,

    /// <summary>
    /// The segments of a shape-based reduction of a stored centerline, for a cave whose line
    /// work arrived in a format that carries no per-leg flags. A documented approximation:
    /// the reduction drops fans of loose shots at a station, which also costs the last few
    /// metres of every genuine dead end.
    /// </summary>
    SkeletonHeuristic = 2,
}
