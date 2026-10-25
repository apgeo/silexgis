// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain;
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Persistence;

/// <summary>
/// The change context of one unit of work. It starts as a change set of its own from the source
/// it is created with — the web host creates a request's as <see cref="ChangeSource.Web"/>, and
/// work with no request behind it starts as <see cref="ChangeSource.Job"/>.
/// </summary>
public sealed class ChangeContext(ChangeSource initialSource) : IChangeContext
{
    public Guid ChangeSetId { get; private set; } = Guid.CreateVersion7();

    public ChangeSource Source { get; private set; } = initialSource;

    public Guid? ImportBatchId { get; private set; }

    public Guid? ActingUserId { get; private set; }

    public void Begin(ChangeSource source, Guid? importBatchId = null)
    {
        ChangeSetId = Guid.CreateVersion7();
        Source = source;
        ImportBatchId = importBatchId;
    }

    public void ActFor(Guid? userId) => ActingUserId = userId;
}
