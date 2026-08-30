// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Access;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Surveys;

/// <summary>
/// The one place that decides which body of line work a cave's statistics are computed over.
///
/// <para>
/// There are two producers and the choice between them is not a preference: parsed survey legs
/// carry the surveyor's own flags and are the definition of the statistic, so they answer
/// whenever they exist. A shape-based reduction of a stored centerline answers only for a cave
/// that never had a survey file to parse, and it says so on every row it returns.
/// </para>
///
/// <para>
/// Every consumer comes through here rather than picking a producer itself, so that no read
/// surface can quietly answer from the approximation for a cave that has the real thing, and so
/// the reason for the choice is written once.
/// </para>
///
/// <para>
/// There is a second choice inside the first, and it is made where the legs are read rather than
/// here: a cave may hold several uploaded survey files, and exactly one of them measures it. See
/// <see cref="SurveySegmentSql"/> for which and why. The chosen file's identity comes back on
/// <see cref="SurveySegmentSet.SurveyModelId"/>.
/// </para>
/// </summary>
public static class SurveySegmentSource
{
    /// <summary>
    /// The measured passage of one cave, and which body of line work it was measured over.
    /// Empty and <see cref="SurveySegmentBasis.Unavailable"/> when the cave has no line work
    /// the caller may see — which is also the answer a caller gets when the cave's exact
    /// position is closed to them, deliberately indistinguishable from a cave with no survey.
    /// </summary>
    public static async Task<SurveySegmentSet> ForCaveAsync(
        SilexGisDbContext db, AccessContext ctx, Guid caveFeatureId, CancellationToken ct)
    {
        var legs = await SurveySegmentSql.ForCaveAsync(db, ctx, caveFeatureId, ct);
        if (legs.Count > 0)
        {
            return new SurveySegmentSet(SurveySegmentBasis.SurveyFlags, legs);
        }

        var pieces = await CenterlineSegmentSql.ForCaveAsync(db, ctx, caveFeatureId, ct);
        return pieces.Count > 0
            ? new SurveySegmentSet(SurveySegmentBasis.SkeletonHeuristic, pieces)
            : SurveySegmentSet.Empty;
    }
}
