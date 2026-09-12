// SPDX-License-Identifier: AGPL-3.0-or-later
using Therion.Blender;

namespace SilexGis.Infrastructure.Surveys;

/// <summary>
/// What an uploaded survey file has to be before anything is measured from it.
/// </summary>
public static class SurveySourceRules
{
    /// <summary>
    /// Refuses a file that is a drawing of a cave rather than a record of where its passages are.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An extended elevation unrolls the passages onto a single vertical plane so the cave can be
    /// printed side-on. The stations it contains are positions in that drawing, not positions in
    /// the ground: the horizontal distance between two of them is the distance along the unrolled
    /// path rather than across the surface, and the bearing between them means nothing at all.
    /// </para>
    /// <para>
    /// Every figure this application takes from the geometry afterwards is then wrong, and wrong
    /// quietly — total and projected length, the direction rose, hypsometry, the passage network,
    /// cross-section morphometry, closest approach between two caves. None of those can tell such
    /// a file from a plan-view model once the numbers are stored, and none of the results would
    /// look obviously wrong on a cave's page. They would simply be measurements of a shape the
    /// cave does not have.
    /// </para>
    /// <para>
    /// The reader has always reported which kind of file it read and nothing here asked. Ingest is
    /// the last point where the answer is still cheap.
    /// </para>
    /// </remarks>
    /// <exception cref="SurveySourceException">When the model is an extended elevation.</exception>
    public static void EnsureIsPlanView(CaveModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (model.IsExtendedElevation)
        {
            throw new SurveySourceException(
                "This is an extended elevation, which unrolls the cave onto one vertical plane "
                + "rather than recording where its passages are. Lengths, directions and depths "
                + "read from it would describe the drawing rather than the cave. Upload the "
                + "plan-view model instead.");
        }
    }
}
