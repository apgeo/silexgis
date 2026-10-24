// SPDX-License-Identifier: AGPL-3.0-or-later
using ImageMagick;
using SilexGis.Domain;
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Files;

/// <summary>
/// Full-size copies of photographs with the location data they carry taken out, made on first
/// request and cached in the file store under <c>nolocation/</c>, beside the renderings.
/// </summary>
/// <remarks>
/// <para>
/// What a reader who may see a photograph but not be told where it was taken is handed when
/// they ask for the picture itself. Size and location are separate questions: such a reader is
/// owed the whole picture, just not the GPS fix and the place names inside it. So this keeps
/// everything a renderer throws away — full resolution, colour profile, the camera, lens and
/// exposure record, the orientation tag — and takes out exactly three things: the EXIF GPS
/// directory, the whole XMP packet (it can carry a fix and place names of its own, and is free
/// text nobody can audit field by field), and the IPTC location fields.
/// </para>
/// <para>
/// The stored upload is never touched. The copy is re-encoded in the source's own format at the
/// quality the source declares, so its bytes and its hash differ from the stored file's, and it
/// is a cache: deleting it costs one re-encode on the next request.
/// </para>
/// </remarks>
public sealed class LocationFreeCopyService(IFileStore fileStore)
{
    /// <summary>
    /// The formats this application can write back with their location removed, each with the
    /// extension its copy is cached under. Deliberately short. A TIFF can hold many pages and
    /// geographic tags of its own, a HEIC or AVIF depends on encoders a build may not have, and
    /// a GIF or BMP carries no camera metadata to keep — every one of those keeps the older rule
    /// for a reader who may not have the location, which is renderings only.
    /// </summary>
    private static readonly Dictionary<string, (MagickFormat Format, string Extension)> Writable =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"] = (MagickFormat.Jpeg, "jpg"),
            ["image/png"] = (MagickFormat.Png, "png"),
            ["image/webp"] = (MagickFormat.WebP, "webp"),
        };

    /// <summary>
    /// The IPTC fields that name a place. Everything else in the IPTC block — caption, credit,
    /// keywords, copyright — is about the picture rather than where it was taken, and stays.
    /// </summary>
    private static readonly IptcTag[] IptcLocationTags =
    [
        IptcTag.City,
        IptcTag.SubLocation,
        IptcTag.ProvinceState,
        IptcTag.Country,
        IptcTag.CountryCode,
        IptcTag.LocationName,
        IptcTag.LocationCode,
    ];

    private const string CacheDirectory = "nolocation";

    /// <summary>Whether a file of this kind and type can be handed out with its location removed.</summary>
    public static bool CanRemoveLocation(FileKind kind, string mimeType) =>
        kind == FileKind.Image && Writable.ContainsKey(mimeType);

    /// <summary>
    /// Where a file's copy is cached. The version is part of the name so a copy can never be
    /// mistaken for another revision's even if file identifiers were ever reused across them.
    /// </summary>
    private static string CachePath(StoredFile file) =>
        $"{CacheDirectory}/{file.Id:N}-{file.DocumentVersionId:N}.{Writable[file.MimeType].Extension}";

    /// <summary>
    /// The absolute path of the file's location-free copy, made now when there is none yet.
    /// </summary>
    /// <exception cref="InvalidOperationException">The file is not in a format this can write.</exception>
    /// <exception cref="MagickException">The stored image could not be read or written.</exception>
    public async Task<string> GetOrCreateAsync(StoredFile file, CancellationToken ct)
    {
        if (!CanRemoveLocation(file.Kind, file.MimeType))
        {
            throw new InvalidOperationException(
                $"A {file.MimeType} file cannot be written back without its location data.");
        }

        var cachePath = fileStore.GetAbsolutePath(CachePath(file));
        if (File.Exists(cachePath))
        {
            return cachePath;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

        // A collection rather than one image, so an animated picture keeps every frame instead
        // of being cut to its first.
        using var frames = new MagickImageCollection();
        await frames.ReadAsync(fileStore.GetAbsolutePath(file.StoragePath), ct);
        foreach (var frame in frames)
        {
            RemoveLocation(frame);
        }

        // Temp name, then move: two first requests racing must not serve a half-written file.
        var tempPath = $"{cachePath}.{Guid.NewGuid():N}.tmp";
        await frames.WriteAsync(tempPath, Writable[file.MimeType].Format, ct);
        try
        {
            File.Move(tempPath, cachePath, overwrite: false);
        }
        catch (IOException)
        {
            File.Delete(tempPath); // another request won the race; its copy is the same
        }

        return cachePath;
    }

    /// <summary>
    /// Takes the location data out of one image in place. Public so the rule can be proved on a
    /// generated picture without a file store.
    /// </summary>
    /// <remarks>
    /// Quality is left alone on purpose: an image read from a JPEG reports the quality its
    /// encoder used, and writing it back reuses that, which is the nearest re-encode to the
    /// source there is.
    /// </remarks>
    public static void RemoveLocation(IMagickImage image)
    {
        if (image.GetExifProfile() is { } exif)
        {
            foreach (var value in exif.Values.Where(v => IsGpsTag(v.Tag)).ToList())
            {
                exif.RemoveValue(value.Tag);
            }

            exif.RemoveValue(ExifTag.GPSIFDOffset);

            // And never write a GPS directory back, whatever a reader of some other camera's
            // profile might have kept under a tag this list does not name.
            exif.AllowedIfds = ExifIfds.Ifd0 | ExifIfds.Exif;

            // Removed and set again rather than only set: a profile left with no values has no
            // bytes to set, and setting nothing leaves the profile that was read — fix and all —
            // exactly where it was. A photo whose EXIF held nothing but its GPS fix is the
            // ordinary case of that, not an edge of it.
            image.RemoveProfile("exif");
            if (exif.Values.Count > 0)
            {
                image.SetProfile(exif);
            }

            // And proved on what will be written rather than trusted: a profile that still names
            // a fix after all that goes whole, camera record with it.
            if (image.GetExifProfile() is { } written
                && (written.GetValue(ExifTag.GPSLatitude) is not null || written.GetValue(ExifTag.GPSLongitude) is not null))
            {
                image.RemoveProfile("exif");
            }
        }

        // The IPTC block of a JPEG travels inside its Photoshop resource block, which can also
        // hold a second copy of the XMP packet. The resource block goes whole; the IPTC block is
        // put back without its place names.
        var iptc = image.GetIptcProfile();
        image.RemoveProfile("8bim");
        image.RemoveProfile("xmp");
        if (iptc is not null)
        {
            foreach (var tag in IptcLocationTags)
            {
                iptc.RemoveValue(tag);
            }

            image.RemoveProfile("iptc");
            if (iptc.Values.Any())
            {
                image.SetProfile(iptc);
            }
        }
    }

    private static bool IsGpsTag(ExifTag tag) => tag.ToString().StartsWith("GPS", StringComparison.Ordinal);

    /// <summary>Deletes every cached location-free copy of a file (no-op when there is none).</summary>
    public void Purge(Guid fileId)
    {
        var directory = fileStore.GetAbsolutePath(CacheDirectory);
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(directory, $"{fileId:N}-*"))
        {
            File.Delete(path);
        }
    }
}
