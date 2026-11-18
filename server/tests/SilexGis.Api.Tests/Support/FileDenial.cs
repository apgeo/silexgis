// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests.Support;

/// <summary>
/// Makes a stored file that exists unreadable to one account, by a rule written onto its document.
/// </summary>
/// <remarks>
/// A rule on the object decides before anything wider does, so this holds even against an Editor,
/// who reads every document through the shipped ruleset, and against the file's own uploader. A
/// test that says "unreadable answers like missing" needs a file that really is there; an id that
/// was never uploaded proves only the missing half.
/// </remarks>
public static class FileDenial
{
    public static async Task DenyReadAsync(SilexGisApiFactory factory, Guid userId, Guid fileId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var file = await db.StoredFiles.AsNoTracking().SingleAsync(f => f.Id == fileId);
        var documentId = await db.DocumentVersions.AsNoTracking()
            .Where(v => v.Id == file.DocumentVersionId)
            .Select(v => v.DocumentId)
            .SingleAsync();

        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Domain = AccessDomain.Documents,
            Actions = AccessAction.Read,
            Effect = AccessEffect.Deny,
            ScopeKind = AccessScopeKind.Object,
            ScopeId = documentId,
        });
        await db.SaveChangesAsync();
    }
}
