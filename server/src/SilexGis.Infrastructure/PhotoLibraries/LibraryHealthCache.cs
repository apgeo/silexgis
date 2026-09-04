// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.PhotoLibraries;

namespace SilexGis.Infrastructure.PhotoLibraries;

/// <summary>
/// The last thing a neighbouring photo library said about itself, held briefly, and the promise
/// that asking never throws.
///
/// <para>
/// One of these per library, on the long-lived client that talks to it, because everything it
/// protects is process-wide: a status line refreshed in two browsers, or by one person twice, must
/// not become two rounds of requests against a neighbouring container, and a library that is down
/// must not be handed a fresh timeout by every poll.
/// </para>
/// <para>
/// <b>The window is deliberately short.</b> What this describes is the state of another running
/// container — an operator restarts it, fixes an address, mints a key with the right permissions,
/// and then looks at this line to find out whether it worked. A window measured in minutes would
/// make that loop unusable and would teach an operator to distrust the line, which is worse than
/// not drawing it. Half a minute is long enough that a page opened twice, or opened by two people
/// at once, costs one round of requests, and short enough that a fix looks like it worked as soon
/// as it did.
/// </para>
/// <para>
/// A failure is held for the same window as a success, and that is the point rather than an
/// oversight: an unreachable neighbour is exactly the case where re-asking on every poll costs a
/// full timeout each time.
/// </para>
/// </summary>
public sealed class LibraryHealthCache(TimeSpan window, TimeSpan deadline)
{
    /// <summary>How long one reading stands. See the note above on why this is short rather than long.</summary>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The longest one probe may take, whatever timeout an operator configured for the library.
    /// </summary>
    /// <remarks>
    /// A viewport query may legitimately be slow — it can be reading a whole library — but a health
    /// check that takes twenty seconds has already answered the question it was asked. This is not
    /// configurable because it is not a property of the far side: it is how long somebody is
    /// willing to look at a blank status line, and "did not answer within a few seconds" is the
    /// same errand for an operator as "did not answer".
    /// </remarks>
    public static readonly TimeSpan DefaultDeadline = TimeSpan.FromSeconds(5);

    /// <summary>
    /// De-duplicates concurrent probes so that several viewers arriving together cost one round of
    /// requests rather than one each. <b>Not a throttle</b>: nothing waits on it once a reading is
    /// in hand, and no other path in the client touches it.
    /// </summary>
    private readonly SemaphoreSlim gate = new(1, 1);

    private volatile LibraryHealth? held;

    public LibraryHealthCache()
        : this(DefaultWindow, DefaultDeadline)
    {
    }

    /// <summary>
    /// Forgets what was read, so the next caller asks the library again. What an operator's recheck
    /// button is for: a button that re-opened the picture path but went on showing a health line
    /// read before the fix would report the state the operator has just finished changing.
    /// </summary>
    public void Clear() => held = null;

    /// <summary>
    /// The reading in force, taking a new one when the last has aged out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This never throws for anything the library did or failed to do</b>, and that is a
    /// contract rather than a convenience: an unreachable neighbour is a result an operator needs
    /// to read, not an error that fails the request they asked it in. The one exception is the
    /// caller going away — a cancelled request has nobody left to tell.
    /// </para>
    /// <para>
    /// The deadline is applied here rather than inside each product's probe, so that a probe cannot
    /// be written that forgets it.
    /// </para>
    /// </remarks>
    public async Task<LibraryHealth> GetAsync(
        Func<CancellationToken, Task<LibraryHealth>> probe, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(probe);

        if (Fresh() is { } current)
        {
            return current;
        }

        await gate.WaitAsync(ct);
        try
        {
            // Somebody may have probed while this call waited for the permit. Checked again rather
            // than assumed, because the permit exists precisely so that concurrent callers share
            // one reading; taking it and then probing anyway would defeat it.
            if (Fresh() is { } arrived)
            {
                return arrived;
            }

            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
            bounded.CancelAfter(deadline);

            LibraryHealth read;
            try
            {
                read = await probe(bounded.Token);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // Including the deadline expiring, which arrives as a cancellation of the bounded
                // token rather than of the caller's. A library that took too long to say anything
                // has said the only thing this line needs.
                read = LibraryHealth.DidNotAnswer(PhotoLibraryException.UnavailableCode);
            }

            held = read;
            return read;
        }
        finally
        {
            gate.Release();
        }
    }

    private LibraryHealth? Fresh()
    {
        var current = held;

        return current?.ProbedAt is { } at && DateTimeOffset.UtcNow - at < window ? current : null;
    }
}
