// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Scans;
using SilexGis.Domain;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Scans;

/// <summary>
/// Reads the centreline a scan's run drew from the scanner's path, in the scan's own frame. Asks
/// nothing about the caller: every route that hands the line out decides first whether the caller
/// may have the cave's exact location, because the line is that position.
/// </summary>
public static class ScanCentrelines
{
    // A centreline is a few thousand vertices at most; a file far past that is not one, and is
    // not read into memory to find out.
    private const long MaxBytes = 32L * 1024 * 1024;

    /// <summary>
    /// The newest centreline stored for the survey model, or null when there is none, its file is
    /// gone, or what is stored does not read as one.
    /// </summary>
    public static async Task<IReadOnlyList<CentrelinePoint>?> ReadAsync(
        SilexGisDbContext db, IFileStore fileStore, Guid surveyModelId, CancellationToken ct)
    {
        var path = await (
            from artefact in db.ScanArtifacts.AsNoTracking()
            join file in db.StoredFiles.AsNoTracking() on artefact.FileId equals file.Id
            where artefact.SurveyModelId == surveyModelId && artefact.Kind == ScanArtifactKind.Centreline
            orderby artefact.CreatedAt descending
            select file.StoragePath)
            .FirstOrDefaultAsync(ct);
        if (path is null)
        {
            return null;
        }

        var info = new FileInfo(fileStore.GetAbsolutePath(path));
        if (!info.Exists || info.Length > MaxBytes)
        {
            return null;
        }

        return ScanCentreline.ParseGeoJson(await File.ReadAllBytesAsync(info.FullName, ct));
    }
}
