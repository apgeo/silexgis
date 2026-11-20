// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace SilexGis.Api.Tests.Support;

/// <summary>
/// Runs something of the test's own in the moment between a request's last look at the database
/// and its write — the first time a save adds an entity the test names, and only that once.
/// </summary>
/// <remarks>
/// What a "two at once" test can only hope for, made certain: a duplicate sent from inside this
/// hook is written and committed while the request it duplicates has already decided there was
/// nothing to find, so that request's own insert is the one the database refuses. Nothing is
/// locked while the hook runs — the save has not opened its transaction yet — so the duplicate
/// cannot wait on the request it interrupts.
/// </remarks>
public sealed class BeforeFirstSave<TEntity>(Func<TEntity, bool> matches, Func<Task> run) : SaveChangesInterceptor
    where TEntity : class
{
    private int fired;

    /// <summary>Whether the hook ran: a test that relies on it asserts it, or proves nothing.</summary>
    public bool Fired => Volatile.Read(ref fired) == 1;

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context
            && context.ChangeTracker.Entries<TEntity>().Any(e => e.State == EntityState.Added && matches(e.Entity))
            && Interlocked.Exchange(ref fired, 1) == 0)
        {
            await run();
        }

        return result;
    }
}
