// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Features.Notifications;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Every kind of thing a notification can be about has both halves of what makes it usable: a page
/// to send the reader to and a way to decide whether the reader may open it. Both halves are
/// <c>switch</c> expressions with a default, so a kind added to the vocabulary without them
/// compiles, passes every other test and ships as a notification that exists and that nobody can
/// ever read. These tests enumerate the vocabulary, so the missing half is a red build and not a
/// silent withholding.
/// </summary>
public sealed class NotificationTargetKindCoverageTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly NotificationTargetKind[] Every = Enum.GetValues<NotificationTargetKind>();

    // These two are read through walks of their own in ReadableAsync, because neither is an
    // ordinary protected row; the test below proves those walks by reading a real row of each.
    private static readonly NotificationTargetKind[] OwnWalk =
        [NotificationTargetKind.CavingGroup, NotificationTargetKind.Document];

    [Fact]
    public void The_vocabulary_is_not_empty_so_the_enumeration_below_proves_something()
    {
        Every.Length.ShouldBeGreaterThan(OwnWalk.Length);
    }

    [Fact]
    public void Every_kind_has_a_page_to_send_the_reader_to()
    {
        foreach (var kind in Every)
        {
            NotificationTargets.RouteTo(new NotificationTarget(kind, Guid.NewGuid()))
                .ShouldNotBeNullOrWhiteSpace($"{kind} has no route arm in NotificationTargets.RouteTo");
        }
    }

    [Fact]
    public async Task Every_kind_has_a_way_to_decide_whether_the_reader_may_open_it()
    {
        using var factory = new SilexGisApiFactory(postgres.ConnectionString);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        foreach (var kind in Every.Except(OwnWalk))
        {
            (await NotificationTargets.LoadAsync(db, kind, [Guid.NewGuid()], CancellationToken.None))
                .ShouldNotBeNull($"{kind} has no readability arm in NotificationTargets.LoadAsync");
        }
    }

    [Fact]
    public async Task The_two_kinds_with_walks_of_their_own_are_readable_through_them()
    {
        using var factory = new SilexGisApiFactory(postgres.ConnectionString);
        var ownerId = await AuthHelper.CreateUserAsync(
            factory, GlobalRoles.Admin, $"cover-{Guid.NewGuid().ToString("N")[..8]}@t.local");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var access = scope.ServiceProvider.GetRequiredService<IAccessService>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var group = new CavingGroup { Name = $"coverage {suffix}", Slug = $"coverage-{suffix}" };
        var document = new Document { Title = $"coverage {suffix}", OwnerUserId = ownerId };
        db.CavingGroups.Add(group);
        db.Documents.Add(document);
        await db.SaveChangesAsync();

        var admin = new AccessContext(ownerId, true, [], []);
        var asked = new[]
        {
            new NotificationTarget(NotificationTargetKind.CavingGroup, group.Id),
            new NotificationTarget(NotificationTargetKind.Document, document.Id),
        };

        var readable = await NotificationTargets.ReadableAsync(db, access, admin, asked, CancellationToken.None);
        readable.ShouldBe(asked, ignoreOrder: true);

        // The positive case above is what gives the absence below its meaning: the same walks
        // answer no for a row that does not exist.
        var absent = new[]
        {
            new NotificationTarget(NotificationTargetKind.CavingGroup, Guid.NewGuid()),
            new NotificationTarget(NotificationTargetKind.Document, Guid.NewGuid()),
        };
        (await NotificationTargets.ReadableAsync(db, access, admin, absent, CancellationToken.None)).ShouldBeEmpty();
    }
}
