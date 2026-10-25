// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Domain;

/// <summary>
/// The logical action the work being done belongs to, stamped on every history row written while
/// it runs: which change set, from which source, and on behalf of which import batch.
/// </summary>
/// <remarks>
/// <para>
/// One per unit of work (an HTTP request, a job run). It starts as a change set of its own — a
/// web request as <see cref="ChangeSource.Web"/>, work with no request behind it as
/// <see cref="ChangeSource.Job"/> — and the code that starts a different kind of action (an upload
/// from a phone, the confirmation of an import, an undo) says so once, at the start, with
/// <see cref="Begin"/>. The history interceptor reads it on every save and stamps every row added
/// in that save, its own and the ones a feature adds by hand, so no write path can forget.
/// </para>
/// <para>
/// A history row whose columns are empty was written before the history named its source.
/// </para>
/// </remarks>
public interface IChangeContext
{
    /// <summary>Groups every history row one logical action wrote.</summary>
    Guid ChangeSetId { get; }

    ChangeSource Source { get; }

    /// <summary>The import batch the action belongs to, when it belongs to one.</summary>
    Guid? ImportBatchId { get; }

    /// <summary>
    /// Whom the work is done for when no request says who the caller is — the account that asked
    /// for a job. Null in a request, where the caller is the request's own.
    /// </summary>
    Guid? ActingUserId { get; }

    /// <summary>
    /// Starts a new logical action: a fresh change set, from <paramref name="source"/>, belonging
    /// to <paramref name="importBatchId"/> when given. The acting user is kept: a job that confirms
    /// an import is still done for the account that asked for it.
    /// </summary>
    void Begin(ChangeSource source, Guid? importBatchId = null);

    /// <summary>Records whom work with no request behind it is done for.</summary>
    void ActFor(Guid? userId);
}
