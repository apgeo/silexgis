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
/// <para>
/// The mark behaves like the cave's default centerline, on purpose: the first model of a kind takes
/// it, and it stays there until somebody chooses another. A corrected re-export does not take over
/// by arriving, because for a line plot "current" decides what the cave's figures are measured
/// over, and a cave whose length changed because a file was uploaded — possibly a survey of one
/// side passage — is a cave reporting something nobody decided.
/// </para>
/// <para>
/// For a line plot the mark and the cave's default centerline are two views of one fact and are
/// kept in step: whenever the default centerline is one that was read out of a survey model, that
/// model holds the mark. The map draws the default centerline and the figures are measured over
/// the marked model, so letting them part would have the map and the numbers describe two
/// different surveys of the same cave with nothing on screen to say so. A default centerline that
/// came from no survey (a drawn or imported line) constrains nothing.
/// </para>
/// <para>
/// One current model per cave and kind is a partial unique index, which PostgreSQL checks per
/// statement. Demoting the holder and promoting the newcomer as two tracked edits in one flush
/// leaves their order to the change tracker, which sorts by primary key rather than by intent, so
/// giving the mark back to an older model would write the second flag while the first was still
/// set and hit the index. Demoting is therefore its own statement, issued before the promotion is
/// saved.
/// </para>
/// </remarks>
public static class SurveyModelCurrency
{
    /// <summary>
    /// Gives a model that has just been added the mark when its cave has no current model of that
    /// kind, and leaves it unmarked otherwise. The model is tracked and not yet saved.
    /// </summary>
    public static async Task TakeIfUnclaimedAsync(SilexGisDbContext db, SurveyModel model, CancellationToken ct)
    {
        var formats = SurveyModelKinds.FormatsOf(SurveyModelKinds.Of(model.Format));
        var claimed = await db.SurveyModels.AnyAsync(
            m => m.CaveFeatureId == model.CaveFeatureId && m.IsCurrent && m.Id != model.Id && formats.Contains(m.Format),
            ct);
        model.IsCurrent = !claimed;
    }

    /// <summary>
    /// Makes <paramref name="model"/> the current model of its kind for its cave, taking the mark
    /// from whichever model held it. The model is tracked; the caller saves it.
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
    /// Keeps the mark in step with a centerline that is, or has just become, the cave's default:
    /// when that centerline was read out of a survey model, the model takes the mark. Called from
    /// every place a centerline becomes the default or a default centerline is tied to a model.
    /// </summary>
    public static async Task FollowDefaultCenterlineAsync(SilexGisDbContext db, Centerline centerline, CancellationToken ct)
    {
        if (!centerline.IsDefault || centerline.SurveyModelId is not { } modelId)
        {
            return;
        }

        var model = db.SurveyModels.Local.FirstOrDefault(m => m.Id == modelId)
            ?? await db.SurveyModels.FirstOrDefaultAsync(m => m.Id == modelId, ct);
        if (model is null || model.IsCurrent || model.CaveFeatureId != centerline.CaveFeatureId)
        {
            return;
        }

        await MakeCurrentAsync(db, model, ct);
    }

    /// <summary>
    /// After a model has been removed and the removal saved: when its cave is left with no current
    /// model of that kind, the newest remaining one takes the mark, so the cave stays represented
    /// by something named rather than by a fallback rule.
    /// </summary>
    /// <remarks>
    /// Nothing happens when a model of the kind is current already — the removed one was not the
    /// holder, or removing a line plot promoted another centerline to default and the mark followed
    /// it. Written as its own statement, after the removal, for the per-statement index check.
    /// </remarks>
    public static async Task PromoteSuccessorAsync(
        SilexGisDbContext db, Guid caveFeatureId, SurveyModelFormat removedFormat, CancellationToken ct)
    {
        var formats = SurveyModelKinds.FormatsOf(SurveyModelKinds.Of(removedFormat));
        var remaining = db.SurveyModels.Where(m => m.CaveFeatureId == caveFeatureId && formats.Contains(m.Format));
        if (await remaining.AnyAsync(m => m.IsCurrent, ct))
        {
            return;
        }

        var successor = await remaining
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
