// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Scans;
using SilexGis.Domain.Surveys;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Infrastructure.Scans;

/// <summary>The uploaded point cloud as the run found it archived: the upload's file row and the stored bytes.</summary>
/// <param name="FileRowId">The upload's file row, which is pointed at the model once there is one.</param>
/// <param name="Name">The file name as uploaded, whose extension says compressed or not.</param>
/// <param name="FileId">The stored file holding the bytes.</param>
/// <param name="SizeBytes">The size of the stored file.</param>
/// <param name="Sha256">The digest the upload recorded for the bytes, lower-case hex.</param>
public sealed record ArchivedPointCloud(Guid FileRowId, string Name, Guid FileId, long SizeBytes, string Sha256);

/// <summary>
/// What a point-cloud upload's run does in the API process: reads the header of the archived
/// file and records what it says — the model that shows the scan, its facts, and the artefact
/// row that makes the original an input a later step can be matched to.
/// </summary>
/// <remarks>
/// <para>
/// A rerun finds the model and the artefact the first run made and refreshes them rather than
/// adding a second of each: the header is read again (a later reader may know more), the facts
/// are written over, and the rows keep their ids, so nothing that linked to them is lost.
/// </para>
/// <para>
/// The model's current mark is taken when the cave has no current point cloud yet, the way a
/// model uploaded on the web takes it; a model that loses the race for it stays unmarked. This
/// is deliberately not the rule a survey archive's models follow, which take the mark only for
/// a cave the upload itself made: that rule guards a line plot of record, which the map and the
/// figures are measured from and which a corrected re-export must not quietly replace. A point
/// cloud is measured from nowhere; while a cave has no current scan it shows none, the first
/// one sent is the only candidate, and taking an unclaimed mark overrides nobody's choice. A
/// second scan never takes it — which scan a cave shows stays somebody's choice on the web.
/// </para>
/// </remarks>
public static class PointCloudUploadRun
{
    /// <summary>
    /// The header's facts, or a pipeline failure when the file is not a LAS or LAZ file the
    /// reader accepts. The stream is read from its start up to the end of the records; the
    /// points are never read.
    /// </summary>
    public static PointCloudFacts Inspect(Stream cloud)
    {
        var header = LasHeaderReader.Read(cloud, out var refusal);
        return header is null
            ? throw new SurveyPipelineException(
                SurveyPipelineFailures.PointCloudInvalid,
                "The file is not a LAS or LAZ point cloud this server can read: " + (refusal?.Detail ?? "its header could not be read") + ".")
            : PointCloudFacts.FromHeader(header);
    }

    /// <summary>Records the facts on the upload's model, making the model and the original's artefact row on the first run.</summary>
    public static async Task<SurveyModel> RecordAsync(
        SilexGisDbContext db, SurveyUpload upload, ArchivedPointCloud archive, PointCloudFacts facts, CancellationToken ct)
    {
        var format = SurveyUploadFiles.ExtensionOf(archive.Name) == ".las" ? SurveyModelFormat.Las : SurveyModelFormat.Laz;
        var formats = SurveyModelKinds.FormatsOf(SurveyModelKind.PointCloud);
        var now = DateTimeOffset.UtcNow;

        var model = await db.SurveyModels
            .FirstOrDefaultAsync(m => m.SurveyUploadId == upload.Id && formats.Contains(m.Format), ct);
        var made = model is null;
        if (model is null)
        {
            model = new SurveyModel
            {
                CaveFeatureId = upload.CaveFeatureId,
                Name = upload.SurveyTitle.Length > 200 ? upload.SurveyTitle[..200] : upload.SurveyTitle,
                FileId = archive.FileId,
                Format = format,
                SurveyedAt = upload.SurveyDate,
                // Ready at once: a point cloud has no reading of its own, and whether it has been
                // tiled is a fact of the upload's run, not a status of the model.
                Status = SurveyModelStatus.Ready,
                CaveSurveyId = upload.CaveSurveyId,
                SurveyUploadId = upload.Id,
            };
            db.SurveyModels.Add(model);
            await SurveyModelCurrency.TakeIfUnclaimedAsync(db, model, ct);
        }

        model.FileId = archive.FileId;
        model.Format = format;
        model.PointCloud = facts.ToJson();

        var artefact = await db.ScanArtifacts.FirstOrDefaultAsync(
            a => a.SurveyUploadId == upload.Id && a.StepId == ScanSteps.UploadStepId && a.Sha256 == archive.Sha256, ct);
        if (artefact is null)
        {
            db.ScanArtifacts.Add(new ScanArtifact
            {
                SurveyUploadId = upload.Id,
                SurveyModelId = model.Id,
                Kind = ScanArtifactKind.PointCloudLaz,
                Format = format == SurveyModelFormat.Las ? "las" : "laz",
                FileId = archive.FileId,
                SizeBytes = archive.SizeBytes,
                Sha256 = archive.Sha256,
                StepId = ScanSteps.UploadStepId,
                Tool = ScanSteps.ApiTool,
                ParamsHash = ScanRecipes.ParamsHash("{}"),
                CreatedAt = now,
            });
        }
        else
        {
            artefact.SurveyModelId = model.Id;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (made && model.IsCurrent)
        {
            // Another model took the cave's current point-cloud mark between the look and the
            // save; this one is kept without it.
            model.IsCurrent = false;
            await db.SaveChangesAsync(ct);
        }

        await db.SurveyUploadFiles
            .Where(f => f.Id == archive.FileRowId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(f => f.SurveyModelId, model.Id)
                .SetProperty(f => f.UpdatedAt, now), ct);

        return model;
    }
}
