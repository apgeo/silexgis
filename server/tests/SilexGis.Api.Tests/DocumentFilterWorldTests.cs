// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Filters;
using SilexGis.Infrastructure.Filters;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The one behaviour change this world was built for.
/// </summary>
/// <remarks>
/// <para>
/// A document hung on a cave is readable by whoever may read the cave — that has always been true
/// when following a link to it. It was not true when searching: the same person could open the
/// survey sheet from the cave's page and then fail to find it by name, which is how a club ends up
/// with the same sheet uploaded four times.
/// </para>
/// <para>
/// The conformance suite proves this world does not return what it must not. This file proves the
/// other half — that it does return what it should — because a filter that withholds everything is
/// trivially safe and useless.
/// </para>
/// </remarks>
public sealed class DocumentFilterWorldTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private string tag = null!;

    public DocumentFilterWorldTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public Task InitializeAsync()
    {
        tag = Guid.NewGuid().ToString("N")[..8];
        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_document_hung_on_a_readable_cave_can_be_found_and_not_only_opened()
    {
        var ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"dw-own-{tag}@t.local");
        var readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"dw-read-{tag}@t.local");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var world = scope.ServiceProvider.GetRequiredService<FilterWorldRegistry>()
            .Find(DocumentFilterWorld.Key)!;

        // A feature anybody may read, and a document nobody may read on its own account.
        var featureId = await SeedPublicFeatureAsync(db, ownerId);
        var documentId = await SeedAttachedDocumentAsync(db, ownerId, featureId, $"Survey {tag}");

        var reader = new AccessContext(readerId, isFullAdmin: false, [], []);
        var matching = new ConditionNode(
            DocumentFilterFields.Title, FilterOp.Contains, [new TextValue(tag)]);

        var answer = await world.QueryAsync(
            new WorldQuery(reader, matching, SortKey.Updated, true, null, 0, 20, true), default);

        answer.Hits.Select(h => h.Id).ShouldContain(documentId,
            "A document reachable by opening it must also be reachable by looking for it.");

        // And counted, since a total that omitted it would disagree with the rows beside it.
        answer.Total.ShouldBe(1);
    }

    [Fact]
    public async Task A_document_hung_on_nothing_stays_out_of_reach()
    {
        // The control for the case above: what admits the document is the cave it hangs on, not the
        // mere fact of being attached to something. Without this, the test above would pass just as
        // happily if the reach admitted every attached document to everybody.
        var ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"dw-own2-{tag}@t.local");
        var readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"dw-read2-{tag}@t.local");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var world = scope.ServiceProvider.GetRequiredService<FilterWorldRegistry>()
            .Find(DocumentFilterWorld.Key)!;

        await SeedAttachedDocumentAsync(db, ownerId, featureId: null, $"Loose {tag}");

        var reader = new AccessContext(readerId, isFullAdmin: false, [], []);
        var answer = await world.QueryAsync(
            new WorldQuery(
                reader,
                new ConditionNode(DocumentFilterFields.Title, FilterOp.Contains, [new TextValue(tag)]),
                SortKey.Updated, true, null, 0, 20, true),
            default);

        answer.Hits.ShouldBeEmpty();
        answer.Total.ShouldBe(0);
    }

    private static async Task<Guid> SeedPublicFeatureAsync(SilexGisDbContext db, Guid ownerId)
    {
        var typeId = await db.FeatureTypes.AsNoTracking().Select(t => t.Id).FirstAsync();
        var id = Guid.NewGuid();
        db.Features.Add(new Feature
        {
            Id = id,
            Name = "Host cave",
            Kind = FeatureKind.Generic,
            FeatureTypeId = typeId,
            OwnerUserId = ownerId,
            Visibility = Visibility.Public,
            // Both halves of the ancestry, as the write service stamps them.
            AncestorIds = [id],
        });
        db.FeatureAncestors.Add(new FeatureAncestor { FeatureId = id, AncestorId = id });
        await db.SaveChangesAsync();
        return id;
    }

    /// <summary>
    /// A private document, its current revision, the file that revision serves, and — when a
    /// feature is given — the attachment that hangs the file on it.
    /// </summary>
    /// <remarks>
    /// Built by hand rather than posted, because what is being tested is the read path and the
    /// authoring surface would insist on permissions this caller deliberately does not have. The
    /// chain has to be complete: reach is computed from the file the document's current revision
    /// serves, so a document without one is reachable by nobody however it is attached.
    /// </remarks>
    private static async Task<Guid> SeedAttachedDocumentAsync(
        SilexGisDbContext db, Guid ownerId, Guid? featureId, string title)
    {
        var document = new Document
        {
            Title = title,
            OwnerUserId = ownerId,
            Visibility = Visibility.Private,
        };
        db.Documents.Add(document);

        var version = new DocumentVersion
        {
            DocumentId = document.Id,
            VersionNumber = 1,
            IsCurrent = true,
            UploadedBy = ownerId,
        };
        db.DocumentVersions.Add(version);

        var file = new StoredFile
        {
            DocumentVersionId = version.Id,
            StoragePath = Guid.NewGuid().ToString("N"),
            OriginalName = $"{title}.pdf",
            MimeType = "application/pdf",
            SizeBytes = 1,
            Sha256 = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
        };
        db.StoredFiles.Add(file);

        if (featureId is not null)
        {
            db.Attachments.Add(new Attachment
            {
                FileId = file.Id,
                FeatureId = featureId,
                Role = AttachmentRole.Document,
                AddedBy = ownerId,
            });
        }

        await db.SaveChangesAsync();
        return document.Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}
