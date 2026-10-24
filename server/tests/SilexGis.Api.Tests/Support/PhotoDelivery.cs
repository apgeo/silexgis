// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using ImageMagick;
using Shouldly;

namespace SilexGis.Api.Tests.Support;

/// <summary>
/// What a caller is actually handed when they follow a file's content address: the stored bytes,
/// or the whole picture with its location taken out. Asked of the bytes, not of the flag beside
/// them, because both answers say the file may be downloaded and only the bytes tell them apart.
/// </summary>
internal static class PhotoDelivery
{
    /// <summary>Follows the file's content address and returns what it served.</summary>
    public static async Task<byte[]> ContentAsync(HttpClient client, JsonElement file)
    {
        var response = await client.GetAsync(file.GetProperty("contentUrl").GetString());
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadAsByteArrayAsync();
    }

    /// <summary>Whether the served bytes are the stored file's own, by the hash the file states.</summary>
    public static bool IsStored(byte[] served, JsonElement file) =>
        string.Equals(
            Convert.ToHexStringLower(SHA256.HashData(served)),
            file.GetProperty("sha256").GetString(),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>The caller is handed the stored file, byte for byte.</summary>
    public static async Task ShouldBeHandedStoredAsync(HttpClient client, JsonElement file)
    {
        file.GetProperty("mayDownloadOriginal").GetBoolean().ShouldBeTrue();
        IsStored(await ContentAsync(client, file), file).ShouldBeTrue("the stored bytes were not what was served");
    }

    /// <summary>
    /// The caller is handed the picture — offered, served — but not the stored bytes, and what
    /// they are handed carries no GPS fix and no XMP packet.
    /// </summary>
    public static async Task ShouldBeHandedWithoutLocationAsync(HttpClient client, JsonElement file)
    {
        file.GetProperty("mayDownloadOriginal").GetBoolean().ShouldBeTrue();
        var served = await ContentAsync(client, file);
        IsStored(served, file).ShouldBeFalse("the stored bytes, GPS and all, were handed over");
        ShouldCarryNoLocation(served);
    }

    /// <summary>Asserts a picture carries no GPS fix and no XMP packet.</summary>
    public static void ShouldCarryNoLocation(byte[] picture)
    {
        using var image = new MagickImage(picture);
        image.GetExifProfile()?.GetValue(ExifTag.GPSLatitude).ShouldBeNull("the picture kept its GPS latitude");
        image.GetExifProfile()?.GetValue(ExifTag.GPSLongitude).ShouldBeNull("the picture kept its GPS longitude");
        image.GetXmpProfile().ShouldBeNull("the picture kept its XMP packet");
    }
}
