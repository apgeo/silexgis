// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Scans;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Scans;

/// <summary>
/// The one rule about the bytes of an uploaded point cloud: a file whose header names a
/// coordinate system is the cave's position — every point in it is a place — and so is the
/// thinned copy made from it, which keeps the coordinates and the header's records; so is every
/// file of a raw recording that heard GNSS, and the cloud exported from it. Such a file is handed
/// out only to a caller who may see the cave's exact location, and so, always, are the scanner's
/// trajectory, the HDMapping session that holds it and the centreline drawn from it. The tiles are not
/// covered: they are written in the cloud's own frame, and the shift that would place them is
/// withheld on its own.
/// </summary>
/// <remarks>
/// Asked at the point where a file's bytes are decided, for whichever route names the file: the
/// upload's own answer withholds the link, but the file is an ordinary document attached to the
/// cave, and its id travels. The facts a run wrote say whether the file names a coordinate
/// system; a file of the scan's document the run has not read — a revision uploaded over the
/// original since — is treated as if it did, as the upload's answer treats it.
/// </remarks>
public static class PointCloudFileAccess
{
    /// <summary>
    /// Whether this file is a point cloud, or a copy of one, that the caller may not take
    /// without the cave's exact location. False for every file that is not a scan's.
    /// </summary>
    public static async Task<bool> WithheldWithoutExactLocationAsync(
        SilexGisDbContext db, IAccessService access, AccessContext ctx, FileSubject subject, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(subject);

        // The scan's artefacts stored as files of this document, whichever revision of the
        // document each belongs to: the original the upload archived, the display copy, the
        // tiles and the estimate a run made.
        var artefacts = await (
            from artefact in db.ScanArtifacts.AsNoTracking()
            join file in db.StoredFiles.AsNoTracking() on artefact.FileId equals file.Id
            join version in db.DocumentVersions.AsNoTracking() on file.DocumentVersionId equals version.Id
            where version.DocumentId == subject.Document.Id
            select new { artefact.SurveyUploadId, artefact.FileId, artefact.Kind })
            .ToListAsync(ct);
        // The scanner's path, the HDMapping session that holds it, and the centreline drawn from
        // it are where the scanner went: a position whatever frame they are written in.
        var track = artefacts.FirstOrDefault(a =>
            a.FileId == subject.File.Id
            && a.Kind is ScanArtifactKind.Trajectory or ScanArtifactKind.HdmSession or ScanArtifactKind.Centreline);
        var clouds = artefacts
            .Where(a => a.Kind is ScanArtifactKind.PointCloudLaz or ScanArtifactKind.PointCloudLazDisplay or ScanArtifactKind.RawRecording)
            .ToList();
        if (track is not null)
        {
            var trackCave = await db.SurveyUploads.AsNoTracking()
                .Where(u => u.Id == track.SurveyUploadId)
                .Select(u => (Guid?)u.CaveFeatureId)
                .FirstOrDefaultAsync(ct);
            return trackCave is null
                || !(await new FeatureProtection(db, access).ExactViewIdsAsync(ctx, [trackCave.Value], ct)).Contains(trackCave.Value);
        }

        if (clouds.Count == 0 || artefacts.Any(a => a.FileId == subject.File.Id && !clouds.Contains(a)))
        {
            // Not a scan's document; or a product of the run that is not the cloud — the tiles
            // in their own frame, the estimate — which the document's own rules decide.
            return false;
        }

        var uploadId = clouds[0].SurveyUploadId;
        var caveId = await db.SurveyUploads.AsNoTracking()
            .Where(u => u.Id == uploadId)
            .Select(u => (Guid?)u.CaveFeatureId)
            .FirstOrDefaultAsync(ct);
        if (caveId is null)
        {
            // An upload that is gone leaves nothing to decide the position by: withheld.
            return true;
        }

        // A file the run read is a position when its header says so; one it has not read is
        // taken to be one until a run says otherwise.
        var facts = PointCloudFacts.FromJson(await db.SurveyModels.AsNoTracking()
            .Where(m => m.SurveyUploadId == uploadId && m.PointCloud != null)
            .Select(m => m.PointCloud)
            .FirstOrDefaultAsync(ct));
        var inspected = clouds.Any(c => c.FileId == subject.File.Id);
        var georeferenced = !inspected || facts is null || facts.IsGeoreferenced;
        if (!georeferenced)
        {
            return false;
        }

        var exact = await new FeatureProtection(db, access).ExactViewIdsAsync(ctx, [caveId.Value], ct);
        return !exact.Contains(caveId.Value);
    }
}
