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
