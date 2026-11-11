// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>
/// What the handlers that close their record in <see cref="IProcessingJobHandler.AbandonedAsync"/>
/// share: reading the payload without throwing, and the words the record is closed with.
/// </summary>
internal static class AbandonedJob
{
    /// <summary>
    /// The words a record is left with when its job was given up. Fixed rather than any exception's:
    /// nothing threw that this process saw, and the reason is the same whatever the kind.
    /// </summary>
    public const string Reason = "It was interrupted on every attempt it was allowed, and was stopped.";

    /// <summary>
    /// The job's payload, or null when it does not read — a payload that never parsed has no record
    /// to close, and the sweep giving the job up must not fail over it.
    /// </summary>
    public static T? PayloadOf<T>(ProcessingJob job)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(job.Payload, JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
