// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Surveys;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Surveys;

/// <summary>
/// Moves the "current" mark between a cave's survey models of one kind. This is the only code
/// that writes <see cref="SurveyModel.IsCurrent"/>.
/// </summary>
/// <remarks>
/// One current model per cave and kind is a partial unique index, which PostgreSQL checks per
/// statement. Demoting the holder and promoting the newcomer as two tracked edits in one flush
/// leaves their order to the change tracker, which sorts by primary key rather than by intent, so
/// giving the mark back to an older model would write the second flag while the first was still
/// set and hit the index. Demoting is therefore its own statement, issued before the promotion is
/// saved, and the caller holds the two inside one transaction so a failure between them cannot
/// leave the cave with no current model of that kind.
/// </remarks>
public static class SurveyModelCurrency
{
    /// <summary>
    /// Makes <paramref name="model"/> the current model of its kind for its cave. The model may be
    /// a tracked row that is not yet inserted; the caller saves it.
    /// </summary>
    public static async Task MakeCurrentAsync(SilexGisDbContext db, SurveyModel model, CancellationToken ct)
    {
        var formats = SurveyModelKinds.FormatsOf(SurveyModelKinds.Of(model.Format));
        await db.SurveyModels
            .Where(m => m.CaveFeatureId == model.CaveFeatureId
                && m.IsCurrent
                && m.Id != model.Id
                && formats.Contains(m.Format))
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.IsCurrent, false), ct);

        // Rows the context already tracks are not touched by a bulk statement, and a stale tracked
        // flag would be written back over the demotion at the next save.
        foreach (var tracked in db.SurveyModels.Local.Where(m =>
            m.CaveFeatureId == model.CaveFeatureId && m.Id != model.Id && m.IsCurrent
            && formats.Contains(m.Format)))
        {
            tracked.IsCurrent = false;
        }

        model.IsCurrent = true;
    }

    /// <summary>
    /// After the current model of a kind has been removed, gives the mark to the newest remaining
    /// model of that kind, if there is one. Nothing happens when the removed model was not current.
    /// </summary>
    /// <remarks>
    /// The newest, because that is what the mark would have gone to had the removed model never
    /// been uploaded: the mark follows uploads forward, and a deletion is read as "that upload was
    /// a mistake". The promotion is written as its own statement for the same per-statement
    /// reason as above, after the caller has saved the removal.
    /// </remarks>
    public static async Task PromoteSuccessorAsync(
        SilexGisDbContext db, Guid caveFeatureId, SurveyModelFormat removedFormat, bool removedWasCurrent, CancellationToken ct)
    {
        if (!removedWasCurrent)
        {
            return;
        }

        var formats = SurveyModelKinds.FormatsOf(SurveyModelKinds.Of(removedFormat));
        var successor = await db.SurveyModels
            .Where(m => m.CaveFeatureId == caveFeatureId && formats.Contains(m.Format))
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
            .Select(m => m.Id)
            .FirstOrDefaultAsync(ct);
        if (successor == Guid.Empty)
        {
            return;
        }

        await db.SurveyModels
            .Where(m => m.Id == successor)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.IsCurrent, true), ct);
    }
}
