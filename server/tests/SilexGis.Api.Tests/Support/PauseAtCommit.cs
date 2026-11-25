// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace SilexGis.Api.Tests.Support;

/// <summary>
/// Holds one transaction at its commit — the first, after <see cref="Arm"/>, to run a statement the
/// given test picks out — until <see cref="Release"/>, so a second request can be started while
/// everything the first wrote is still uncommitted and every lock it took still held. Bounded, so a
/// test that never releases cannot hang the suite.
/// </summary>
public sealed class PauseAtCommit : DbCommandInterceptor, IDbTransactionInterceptor
{
    private TaskCompletionSource paused = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile Func<string, bool>? marks;
    private DbTransaction? held;

    public Task Paused => paused.Task;

    /// <summary>Picks the transaction to hold: the first to run a statement <paramref name="statement"/> accepts.</summary>
    public void Arm(Func<string, bool> statement)
    {
        paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref held, null);
        marks = statement;
    }

    public void Release() => released.TrySetResult();

    /// <summary>Waits until the picked transaction is held at its commit, failing after half a minute.</summary>
    public async Task WaitPausedAsync()
    {
        if (await Task.WhenAny(Paused, Task.Delay(TimeSpan.FromSeconds(30))) != Paused)
        {
            throw new TimeoutException("the transaction to hold never reached its commit");
        }
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Mark(command);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        Mark(command);
        return ValueTask.FromResult(result);
    }

    public async ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        if (marks is not null && ReferenceEquals(transaction, Volatile.Read(ref held)))
        {
            marks = null;
            paused.TrySetResult();
            await Task.WhenAny(released.Task, Task.Delay(TimeSpan.FromSeconds(60), CancellationToken.None));
        }

        return result;
    }

    private void Mark(DbCommand command)
    {
        if (marks is { } statement && command.Transaction is { } transaction && statement(command.CommandText))
        {
            Interlocked.CompareExchange(ref held, transaction, null);
        }
    }
}
