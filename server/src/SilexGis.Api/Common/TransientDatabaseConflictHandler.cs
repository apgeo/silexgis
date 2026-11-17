// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Diagnostics;
using Npgsql;

namespace SilexGis.Api.Common;

/// <summary>
/// Answers a request the database broke off because it collided with another transaction — a
/// deadlock it resolved by rolling this one back, or a serialization failure — as a 409 a client
/// may simply send again, instead of the bare 500 the exception would otherwise become.
/// </summary>
/// <remarks>
/// <para>
/// Both refusals are about timing, not about the request: nothing it carried was wrong, the
/// database undid everything it had written, and the same request sent a moment later normally
/// succeeds. A server fault says the opposite — that something is broken and retrying will not
/// help — and it is logged and alarmed on as such. So they are recognised in this one place, for
/// every route, rather than by each route that happens to take row locks in an order another
/// route can cross. A route that has a better answer of its own (one that can tell the caller
/// exactly what the other transaction did) still catches the exception itself and never reaches
/// this handler.
/// </para>
/// <para>
/// The database's answer is looked for through the whole chain of causes: how many layers the
/// persistence library wraps around it — a failed save, a failed commit, a retry strategy giving
/// up — is that library's business and no contract. Any other database error is left alone,
/// so a defect is never dressed up as a collision.
/// </para>
/// </remarks>
public sealed class TransientDatabaseConflictHandler : IExceptionHandler
{
    /// <summary>The database broke the request off for colliding with another; sending it again is safe.</summary>
    public const string Code = "concurrency.transaction_conflict";

    /// <summary>
    /// Seconds a client is told to wait before sending again: long enough for the transaction it
    /// collided with to have finished, short enough that a person does not notice the retry.
    /// </summary>
    private const int RetryAfterSeconds = 1;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (!IsTransientConflict(exception) || IsPhoneSync(httpContext.Request.Path))
        {
            return false;
        }

        httpContext.Response.Headers.RetryAfter = RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await ApiProblems.Conflict(
                Code,
                "This change collided with another one made at the same moment, and nothing of it was saved. "
                + "Sending it again should succeed.")
            .ExecuteAsync(httpContext);
        return true;
    }

    /// <summary>
    /// The phone synchronisation routes keep answering a collision as the server fault they always
    /// answered. Their callers are phone applications released outside this repository, which retry
    /// a 5xx but may treat any 409 as final; a new status on those routes would turn a step they
    /// used to send again into one they give up on. They move to the retryable answer only once the
    /// phone applications know its code.
    /// </summary>
    private static bool IsPhoneSync(PathString path) =>
        path.StartsWithSegments("/api/v1/sync", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the database refused the work for colliding with another transaction.</summary>
    public static bool IsTransientConflict(Exception exception)
    {
        for (var each = exception; each is not null; each = each.InnerException)
        {
            if (each is PostgresException
                {
                    SqlState: PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure,
                })
            {
                return true;
            }
        }

        return false;
    }
}
