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
    /// Whether a failure is of the surroundings rather than of the work: transient database
    /// errors, timeouts, network and transport errors, and an I/O error other than a missing file.
    /// Looked for through the exceptions it wraps, since the database and HTTP layers rarely throw
    /// the cause itself.
    /// </summary>
    public static bool IsPassing(Exception failure)
    {
        for (Exception? e = failure; e is not null; e = e.InnerException)
        {
            switch (e)
            {
                case NpgsqlException { IsTransient: true }:
                case TimeoutException:
                case HttpRequestException:
                case SocketException:
                case IOException and not FileNotFoundException and not DirectoryNotFoundException:
                    return true;
                case DbUpdateConcurrencyException:
                    return false;
            }
        }

        return false;
    }
}
