// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Terrain;

namespace SilexGis.Infrastructure.Terrain;

/// <summary>
/// Rasters sent through the browser, held until a build claims them.
/// </summary>
/// <remarks>
/// <para>
/// They are put here rather than in the document store because they are not documents: nobody
/// reads them, nobody searches them, they belong to no cabinet and to no audience, and the next
/// thing that touches them is a raster tool that wants an absolute path. Keeping them beside the
/// builds also puts them on the disk an operator chose for size, which is the disk the rest of a
/// build's tens of gigabytes are going to land on anyway.
/// </para>
/// <para>
/// <b>Every name here is made by this class and never taken from a caller.</b> A reference handed
/// back to a client and quoted again when a build is submitted is checked twice on the way in — it
/// must have the shape this class issues, and the path built from it must still be inside this
/// directory once the filesystem has had its say. Names are the one thing an upload lets somebody
/// outside choose, and a name is a path.
/// </para>
/// </remarks>
public sealed class TerrainUploads(TerrainWorkspace workspace)
{
    /// <summary>Where uploaded rasters wait.</summary>
    /// <remarks>
    /// A sibling of the per-build folders rather than a child of one: a raster is sent before the
    /// build that will use it exists, and the same raster may be named by more than one build.
    /// </remarks>
    public string Directory => Path.Combine(workspace.Root, "uploads");

    /// <summary>
    /// Stores one raster and answers the reference a build quotes to name it.
    /// </summary>
    /// <remarks>
    /// The reference is a key this class invents plus the extension the file arrived with, so that
    /// the raster library can still tell from the name what it is being handed. The uploader's own
    /// file name is deliberately not part of it: it would be the only piece of a path here that
    /// somebody outside chose.
    /// </remarks>
    public async Task<string> SaveAsync(Stream content, string fileName, CancellationToken ct)
    {
        System.IO.Directory.CreateDirectory(Directory);

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var reference = $"{Guid.CreateVersion7():N}{extension}";
        var path = Path.Combine(Directory, reference);

        // The same partial-then-rename discipline the download path uses, for the same reason: an
        // upload that stops half way must not leave bytes at a name a build would later read as a
        // whole raster.
        var partial = path + CopernicusFetcher.PartialSuffix;
        try
        {
            await using (var file = File.Create(partial))
            {
                await content.CopyToAsync(file, ct);
            }

            File.Move(partial, path, overwrite: true);
            return reference;
        }
        catch
        {
            try
            {
                File.Delete(partial);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The failure being reported matters more than the leftover fragment, whose name
                // nothing will ever mistake for a finished raster.
            }

            throw;
        }
    }

    /// <summary>
    /// Where the raster named by <paramref name="reference"/> is, or <c>null</c> when the reference
    /// is not one this class issued or names nothing that is still there.
    /// </summary>
    /// <remarks>
    /// Two checks and both are load-bearing. The shape check refuses anything that is not a key
    /// and an extension, which is what stops a reference being a path at all. The containment check
    /// asks the filesystem where the result actually is, after links have been followed, and
    /// refuses it if that is not inside this directory — because a name that passes the first check
    /// can still be a link somebody put here, and following one would read whatever it points at.
    /// </remarks>
    public string? PathOf(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference) || !IsIssuedName(reference))
        {
            return null;
        }

        try
        {
            var directory = Path.GetFullPath(Directory);
            var file = new FileInfo(Path.Combine(directory, reference));
            if (!file.Exists)
            {
                return null;
            }

            var resolved = Path.GetFullPath(
                file.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? file.FullName);

            return SilexGis.Domain.Documents.ServerImportPaths.IsWithinRoots([directory], resolved)
                ? resolved
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // A path the process cannot even look at is answered the same way as one that is not
            // there: the difference between the two is itself information about the server's disk.
            return null;
        }
    }

    /// <summary>
    /// Whether a raster with this name is one this application will try to read at all.
    /// </summary>
    public static bool IsAcceptedRaster(string fileName) => TerrainRasterFiles.IsRaster(fileName);

    /// <summary>
    /// A key in the form this class issues them and nothing else — no separator, no parent, no
    /// device name, no second extension.
    /// </summary>
    private static bool IsIssuedName(string reference)
    {
        var extension = Path.GetExtension(reference);
        var stem = Path.GetFileNameWithoutExtension(reference);

        return reference.Length <= 64
            && Path.GetFileName(reference) == reference
            && stem.Length == 32
            && stem.All(char.IsAsciiHexDigitLower)
            && TerrainRasterFiles.IsRaster(reference)
            && extension.Length <= 8;
    }
}
