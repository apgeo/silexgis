// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Extensions.Logging;
using SilexGis.Domain;
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Files;

/// <summary>
/// Drops the bytes and the cached renderings of files whose rows are already gone.
/// </summary>
/// <remarks>
/// Rows first, then blobs, and the blobs best effort. A blob deleted before its rows would leave
/// a document pointing at content that is not there, which every reader would report as a fault;
/// a row deleted before its blob leaves an unreferenced file, which is litter rather than a fault
/// and is swept the next time an operator looks. So this runs after the save that removed the
/// rows, never inside it, and a blob that will not delete is logged and left. The purge sweep and
/// every delete that takes a document whole with its owner go through here, so the order is
/// stated once.
/// </remarks>
public sealed class StoredContentRemover(
    IFileStore fileStore,
    ThumbnailService thumbnails,
    PageRenderService pages,
    ILogger<StoredContentRemover> logger)
{
    public async Task DropAsync(IEnumerable<StoredFile> files)
    {
        foreach (var file in files)
        {
            try
            {
                await fileStore.DeleteAsync(file.StoragePath, CancellationToken.None);
                thumbnails.Purge(file.Id);
                pages.Purge(file.Id);
            }
            catch (IOException e)
            {
                logger.LogWarning(e, "Could not delete the content of removed file {FileId}", file.Id);
            }
        }
    }
}
