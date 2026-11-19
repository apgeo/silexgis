// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore.Storage;

namespace SilexGis.Infrastructure.Features;

/// <summary>
/// A creation's transaction, opened by <see cref="FeatureWriteService.BeginHierarchyWriteAsync"/>
/// or joined when the caller already had one; disposed without <see cref="CommitAsync"/>, what it
/// owns is rolled back.
/// </summary>
public sealed class HierarchyWrite(IDbContextTransaction? owned) : IAsyncDisposable
{
    /// <summary>Commits the transaction this opened; a joined one is left to its owner.</summary>
    public Task CommitAsync(CancellationToken ct = default) => owned?.CommitAsync(ct) ?? Task.CompletedTask;

    public ValueTask DisposeAsync() => owned?.DisposeAsync() ?? ValueTask.CompletedTask;
}
