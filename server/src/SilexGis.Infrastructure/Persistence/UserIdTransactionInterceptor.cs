// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SilexGis.Domain;

namespace SilexGis.Infrastructure.Persistence;

/// <summary>
/// Stamps the calling user's id into the transaction-local <c>app.user_id</c> setting
/// (SET LOCAL semantics via set_config). Inert today; when row-level security policies
/// are enabled they read this setting — the plumbing is proven long before that.
/// </summary>
public sealed class UserIdTransactionInterceptor(ICurrentUser currentUser) : DbTransactionInterceptor
{
    public override async ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction result,
        CancellationToken cancellationToken = default)
    {
        await StampAsync(connection, result, cancellationToken);
        return await base.TransactionStartedAsync(connection, eventData, result, cancellationToken);
    }

    public override DbTransaction TransactionStarted(
        DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
    {
        StampAsync(connection, result, CancellationToken.None).GetAwaiter().GetResult();
        return base.TransactionStarted(connection, eventData, result);
    }

    private async Task StampAsync(DbConnection? connection, DbTransaction? transaction, CancellationToken ct)
    {
        if (connection is null || currentUser.UserId is not { } userId)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT set_config('app.user_id', @user_id, true)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "user_id";
        parameter.Value = userId.ToString();
        command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync(ct);
    }
}
