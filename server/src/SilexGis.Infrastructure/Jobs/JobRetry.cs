// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>
/// When a failed job is run again rather than left failed: only for a kind that allows it
/// (<see cref="IProcessingJobHandler.RetriesPassingFailures"/>), only for a failure that says
/// nothing about the job itself, and only twice.
/// </summary>
/// <remarks>
/// A failed job used to stay failed: a survey whose reading was interrupted by the database
/// restarting stayed unread until somebody uploaded it again, with nothing on screen saying why.
/// What is worth trying again is a failure of the surroundings — the database dropping the
/// connection, a timeout, the conversion service not answering for a moment — never one of the
/// input: an unreadable file read three times is three failures and the same answer.
/// </remarks>
public static class JobRetry
{
    /// <summary>Attempts in all, the first included.</summary>
    public const int MaxAttempts = 3;

    /// <summary>How long a job waits before the attempt after <paramref name="attempts"/> failed ones.</summary>
    public static TimeSpan DelayAfter(int attempts) =>
        attempts <= 1 ? TimeSpan.FromSeconds(30) : TimeSpan.FromMinutes(2);

    /// <summary>Whether a job that has had <paramref name="attempts"/> attempts and failed with
    /// <paramref name="failure"/> may be tried again.</summary>
    public static bool MayRetry(int attempts, Exception failure) =>
        attempts < MaxAttempts && IsPassing(failure);

    /// <summary>
    /// Whether a failure is of the surroundings rather than of the work: a transient database
    /// error, a timeout, a connection that could not be made or was dropped, a service answering
    /// that it is unavailable, and an I/O error that is not about the input.
    /// </summary>
    /// <remarks>
    /// Looked for through the wrappers the database and HTTP layers put around a cause — and only
    /// those. A handler that wraps an I/O error in its own exception has said what it means (a KMZ
    /// that is not a readable archive), and a truncated stream is an answer about the input, not
    /// about the machine; retrying either is three failures and the same answer.
    /// </remarks>
    public static bool IsPassing(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        switch (failure)
        {
            case NpgsqlException { IsTransient: true }:
            case TimeoutException:
            case SocketException:
                return true;
            case HttpRequestException http:
                // No answer at all, or an answer saying "not now"; a 4xx is about what was sent.
                return http.StatusCode is null
                    or System.Net.HttpStatusCode.RequestTimeout
                    or System.Net.HttpStatusCode.TooManyRequests
                    or >= System.Net.HttpStatusCode.InternalServerError;
            case EndOfStreamException or FileNotFoundException or DirectoryNotFoundException or PathTooLongException:
                return false;
            case IOException:
                return failure.GetType() == typeof(IOException) || failure.InnerException is SocketException;
            case DbUpdateConcurrencyException:
                return false;
            case DbUpdateException or AggregateException or TaskCanceledException when failure.InnerException is { } inner:
                return IsPassing(inner);
            default:
                return false;
        }
    }
}
