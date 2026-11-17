// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using SilexGis.Domain.Storage;

namespace SilexGis.Api.Common;

/// <summary>
/// The refusal for something that does not fit in the storage left, said before any of it is
/// written: which ceiling it is under, how much it needs, how much room there is and how much is
/// missing, each as a member of its own so a screen can say "free 3.2 GB or ask an administrator
/// to raise the cap" without reading English.
/// </summary>
public static class StorageProblems
{
    /// <summary>The stable code of every not-enough-room refusal outside the ordinary file uploads (which keep <c>file.quota_exceeded</c>).</summary>
    public const string InsufficientCode = "storage.insufficient";

    /// <summary>The ceiling's name as it appears in answers: <c>account</c>, <c>installation</c>, <c>scans</c>, <c>storeDisk</c>, <c>workerDisk</c>.</summary>
    public static string LimitName(StorageLimitKind kind) => JsonNamingPolicy.CamelCase.ConvertName(kind.ToString());

    public static ProblemHttpResult Insufficient(StorageShortfall shortfall)
    {
        ArgumentNullException.ThrowIfNull(shortfall);
        return TypedResults.Problem(
            detail: Sentence(shortfall),
            statusCode: StatusCodes.Status409Conflict,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = InsufficientCode,
                ["limit"] = LimitName(shortfall.Kind),
                ["neededBytes"] = shortfall.NeededBytes,
                ["roomBytes"] = shortfall.RoomBytes,
                ["missingBytes"] = shortfall.MissingBytes,
            });
    }

    /// <summary>
    /// The refusal of a write that the disk holding the file store had no room for at the moment
    /// of writing — found by the write failing, not foreseen — so how much room there was is not
    /// known and is not claimed. Insufficient Storage rather than a conflict: nothing the request
    /// says is wrong, and the same request is taken once room has been made.
    /// </summary>
    public static ProblemHttpResult StoreDiskFull(long neededBytes) =>
        TypedResults.Problem(
            detail: string.Create(
                CultureInfo.InvariantCulture,
                $"The disk that holds the file store has no room for these {neededBytes:N0} bytes. Nothing was stored; send it again once room has been made."),
            statusCode: StatusCodes.Status507InsufficientStorage,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = InsufficientCode,
                ["limit"] = LimitName(StorageLimitKind.StoreDisk),
                ["neededBytes"] = neededBytes,
            });

    /// <summary>Whether a failed write failed because the device, or the account's share of it, is full.</summary>
    /// <remarks>
    /// The runtime raises a plain <see cref="IOException"/> for these and tells them apart only by
    /// the number it carries: on Linux and macOS the error number itself (no space left on the
    /// device; over the disk quota — 122 on Linux), on Windows the system error wrapped as a
    /// result code (disk full; handle disk full).
    /// </remarks>
    public static bool IsOutOfSpace(IOException e)
    {
        ArgumentNullException.ThrowIfNull(e);
        const int NoSpaceLeft = 28;
        const int QuotaExceededLinux = 122;
        const int DiskFullWindows = unchecked((int)0x80070070);
        const int HandleDiskFullWindows = unchecked((int)0x80070027);
        return e.HResult is NoSpaceLeft or DiskFullWindows or HandleDiskFullWindows
            || (e.HResult == QuotaExceededLinux && OperatingSystem.IsLinux());
    }

    private static string Sentence(StorageShortfall shortfall)
    {
        var where = shortfall.Kind switch
        {
            StorageLimitKind.Account => "under this account's storage quota",
            StorageLimitKind.Installation => "under this installation's storage cap",
            StorageLimitKind.Scans => "under this installation's cap for point clouds",
            StorageLimitKind.StoreDisk => "on the disk that holds the file store",
            _ => "on the disk the processing worker uses",
        };
        return string.Create(
            CultureInfo.InvariantCulture,
            $"This needs {shortfall.NeededBytes:N0} bytes and only {shortfall.RoomBytes:N0} are left {where}: {shortfall.MissingBytes:N0} bytes are missing. Nothing was stored.");
    }
}
