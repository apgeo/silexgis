// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>
/// Gives a model that has just been read the cave's current mark, when its uploader asked for that
/// and nobody has chosen otherwise since.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why at the end of the reading.</b> Only a finished model can be the current one: the cave's
/// figures are measured over the marked line plot and the scene draws the marked mesh, and neither
/// has anything to work from while the file is still being read. So the upload records the wish
/// and the reading carries it out, in the commit that publishes the model as finished.
/// </para>
/// <para>
/// <b>Why only from the model that held the mark when asked.</b> The wish was "this one instead of
/// that one". Somebody who chooses a third model while the file is being read made the later
/// choice, and a reading that finished afterwards and took the mark anyway would quietly undo it.
/// The same holds when the holder was removed in the meantime and the mark passed on.
/// </para>
/// <para>
/// <b>Why it cannot fail the reading.</b> It runs inside the reading's transaction behind a
/// savepoint, as the walls do: a statement that fails here — two choices racing for the one mark
/// the database allows — is rolled back to the savepoint, the change tracker forgets what was
/// staged, and the reading commits without the mark. A survey that was read correctly and is not
/// yet the current one can be chosen on the cave's page; a survey recorded as unreadable because
/// the mark could not be moved would be a false report about the file.
/// </para>
/// </remarks>
public static class SurveyReadingMark
{
    public static async Task TakeIfStillAskedAsync(
        SilexGisDbContext db,
        IDbContextTransaction transaction,
        FeatureWriteService writes,
        SurveyModel model,
        Guid? takesMarkFrom,
        ILogger logger,
        CancellationToken ct)
    {
        if (takesMarkFrom is not { } holder || model.IsCurrent)
        {
            return;
        }

        if (!await db.SurveyModels.AnyAsync(m => m.Id == holder && m.IsCurrent, ct))
        {
            logger.LogInformation(
                "Survey model {SurveyModelId} was to take the current mark from {HolderId}, which no longer holds it; the later choice stands",
                model.Id,
                holder);
            return;
        }

        const string savepoint = "survey_current";
        await transaction.CreateSavepointAsync(savepoint, ct);
        try
        {
            await writes.MakeSurveyModelCurrentAsync(model, ct);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(
                e,
                "Could not make survey model {SurveyModelId} the current one after reading it; it was read and is not current",
                model.Id);

            await transaction.RollbackToSavepointAsync(savepoint, CancellationToken.None);

            // The database is back where the reading left it; the change tracker is brought to
            // agree, so that nothing staged here is sent again by a later save.
            foreach (var entry in db.ChangeTracker.Entries().ToList())
            {
                if (entry.State == EntityState.Added)
                {
                    entry.State = EntityState.Detached;
                }
                else if (entry.State is EntityState.Modified or EntityState.Deleted)
                {
                    entry.CurrentValues.SetValues(entry.OriginalValues);
                    entry.State = EntityState.Unchanged;
                }
            }
        }
    }
}
