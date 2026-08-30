// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Identity;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The checklist table and the domain that governs it: that the schema reaches a fresh
/// database, that a list is read exactly as far as its audience says and no further, that a
/// right over every trip confers nothing here, and that a list an administrator publishes for
/// the whole installation is an ordinary row rather than a case of its own.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ChecklistAccessTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private Guid ownerId;
    private Guid outsiderId;
    private Guid clubmateId;
    private Guid administratorId;
    private Guid cavingGroupId;

    public ChecklistAccessTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"cl-own-{suffix}@t.local");

        // Viewers, deliberately. The seeded Editors group reads past visibility at the widest
        // scope by design, so an "an editor cannot see it" assertion would prove nothing about
        // the audience column — it would only prove the account had no editor rights.
        outsiderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"cl-out-{suffix}@t.local");
        clubmateId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"cl-club-{suffix}@t.local");
        administratorId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"cl-adm-{suffix}@t.local");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var group = new CavingGroup { Name = $"Checklist Club {suffix}", Slug = $"checklist-club-{suffix}" };
        db.CavingGroups.Add(group);
        await db.SaveChangesAsync();
        cavingGroupId = group.Id;
        await RosterHelper.AddMemberAsync(db, cavingGroupId, ownerId);
        await RosterHelper.AddMemberAsync(db, cavingGroupId, clubmateId);
    }

    [Fact]
    public async Task The_schema_reaches_a_fresh_database_and_a_list_keeps_the_order_it_was_written_in()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var checklist = await AddChecklistAsync(db, ownerId, Visibility.Private, "Before we set off",
            "Permit obtained", "Key collected", "Callout arranged");

        var stored = await db.Checklists.AsNoTracking().SingleAsync(x => x.Id == checklist.Id);
        stored.Title.ShouldBe("Before we set off");
        stored.Visibility.ShouldBe(Visibility.Private);
        stored.OwnerUserId.ShouldBe(ownerId);
        stored.CreatedAt.ShouldNotBe(default);

        var items = await ItemsOfAsync(db, checklist.Id);
        items.Select(x => x.Text).ShouldBe(["Permit obtained", "Key collected", "Callout arranged"]);

        // The identity a confirmation will be recorded against survives rewording the line it
        // labels: the row keeps its key while its text changes, which is the whole reason an
        // item is a row rather than an element of a document on the list.
        var permit = items[0];
        var tracked = await db.ChecklistItems.SingleAsync(x => x.Id == permit.Id);
        tracked.Text = "Permit obtained from the landowner";
        await db.SaveChangesAsync();
        (await ItemsOfAsync(db, checklist.Id))[0].Id.ShouldBe(permit.Id);
    }

    [Fact]
    public async Task A_list_is_read_exactly_as_far_as_its_audience_says()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var mine = await AddChecklistAsync(db, ownerId, Visibility.Private, $"Private {suffix}", "Permit");
        var clubs = await AddChecklistAsync(db, ownerId, Visibility.CavingGroup, $"Club {suffix}", "Key");
        clubs.CavingGroupId = cavingGroupId;
        await db.SaveChangesAsync();
        var everyones = await AddChecklistAsync(db, ownerId, Visibility.Authenticated, $"Open {suffix}", "Callout");

        var owner = await RosterHelper.AccessContextOfAsync(db, ownerId);
        var clubmate = await RosterHelper.AccessContextOfAsync(db, clubmateId);
        var outsider = await RosterHelper.AccessContextOfAsync(db, outsiderId);

        // The positive half sits beside every negative one on purpose: a walk that returned
        // nothing at all would satisfy "the outsider cannot see it" and prove nothing.
        (await VisibleIdsAsync(db, owner)).ShouldBe([mine.Id, clubs.Id, everyones.Id], ignoreOrder: true);
        (await VisibleIdsAsync(db, clubmate)).ShouldBe([clubs.Id, everyones.Id], ignoreOrder: true);
        (await VisibleIdsAsync(db, outsider)).ShouldBe([everyones.Id]);
    }

    [Fact]
    public async Task A_right_over_every_trip_confers_nothing_over_checklists()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var shut = await AddChecklistAsync(db, ownerId, Visibility.Private, $"Shut {suffix}", "Permit");

        // Read over every trip in the installation, at the widest scope there is. This is the
        // only shape a checklist grant could have taken had checklists ridden the trip domain,
        // because an entry scoped to one object resolves what it is anchored to against the
        // table its domain names — a checklist id under the trips names nothing and is refused.
        // So the choice was between conferring everything and conferring nothing, and the
        // domain of its own is what makes the answer here "nothing".
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = outsiderId,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.TripLogs,
            Actions = AccessActions.Everything,
            ScopeKind = AccessScopeKind.All,
        });
        await db.SaveChangesAsync();

        var withTripRights = await RosterHelper.AccessContextOfAsync(db, outsiderId);
        (await VisibleIdsAsync(db, withTripRights)).ShouldNotContain(shut.Id);

        // And the grant that is supposed to work does — the same subject, the same row, one
        // entry in the checklist domain naming that one list. Without this the test above
        // would pass just as well against a walk that admitted nobody to anything.
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = outsiderId,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.Checklists,
            Actions = AccessAction.Read,
            ScopeKind = AccessScopeKind.Object,
            ScopeId = shut.Id,
        });
        await db.SaveChangesAsync();

        var withChecklistGrant = await RosterHelper.AccessContextOfAsync(db, outsiderId);
        (await VisibleIdsAsync(db, withChecklistGrant)).ShouldContain(shut.Id);
    }

    [Fact]
    public async Task A_published_list_is_an_ordinary_row_with_a_wide_audience()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var published = await AddChecklistAsync(db, administratorId, Visibility.Authenticated,
            $"Standard preparations {suffix}", "Permit", "Key", "Callout");

        // Read by an account holding no entry at all, through the same walk every other list
        // goes through: the audience column is the whole mechanism, and no reader asks whether
        // a list is "the published one" before deciding whether it may be opened.
        var outsider = await RosterHelper.AccessContextOfAsync(db, outsiderId);
        outsider.Entries.ShouldNotContain(e => e.Domain == AccessDomain.Checklists
            && e.ScopeId == published.Id);
        (await VisibleIdsAsync(db, outsider)).ShouldContain(published.Id);

        // Being able to read it is not being able to change it. Ownership and the audience are
        // separate answers, and only the first admits a write.
        var access = scope.ServiceProvider.GetRequiredService<IAccessService>();
        (await access.DecideAsync(outsider, AccessAction.Read, published)).Allowed.ShouldBeTrue();
        (await access.DecideAsync(outsider, AccessAction.Write, published)).Allowed.ShouldBeFalse();

        // And it outlives the administrator who published it. The owner reference refuses the
        // delete rather than cascading, which is what keeps the installation's shared lists
        // from disappearing with an account that leaves — the alternative is a row that is
        // "owned by the installation" in words and by one person in the schema.
        db.Model.FindEntityType(typeof(Checklist))!.GetForeignKeys()
            .Single(fk => fk.Properties.Any(p => p.Name == nameof(Checklist.OwnerUserId)))
            .DeleteBehavior.ShouldBe(DeleteBehavior.Restrict);

        await using var deleting = factory.Services.CreateAsyncScope();
        var deleteDb = deleting.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var author = await deleteDb.Users.SingleAsync(u => u.Id == administratorId);
        deleteDb.Users.Remove(author);
        await Should.ThrowAsync<DbUpdateException>(() => deleteDb.SaveChangesAsync());

        await using var after = factory.Services.CreateAsyncScope();
        var freshDb = after.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await freshDb.Checklists.AsNoTracking().AnyAsync(x => x.Id == published.Id)).ShouldBeTrue();
    }

    private static async Task<Checklist> AddChecklistAsync(
        SilexGisDbContext db, Guid ownerUserId, Visibility visibility, string title, params string[] items)
    {
        var checklist = new Checklist { Title = title, OwnerUserId = ownerUserId, Visibility = visibility };
        db.Checklists.Add(checklist);
        for (var i = 0; i < items.Length; i++)
        {
            db.ChecklistItems.Add(new ChecklistItem
            {
                ChecklistId = checklist.Id,
                Text = items[i],
                SortOrder = i,
            });
        }

        await db.SaveChangesAsync();
        return checklist;
    }

    private static Task<List<ChecklistItem>> ItemsOfAsync(SilexGisDbContext db, Guid checklistId) =>
        db.ChecklistItems.AsNoTracking()
            .Where(x => x.ChecklistId == checklistId)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Id)
            .ToListAsync();

    /// <summary>
    /// The lists this caller reaches, narrowed to the ones this class made — the fixture shares
    /// one database with every other suite, so an unnarrowed count would be a count of the
    /// afternoon's other tests.
    /// </summary>
    private Task<List<Guid>> VisibleIdsAsync(SilexGisDbContext db, AccessContext ctx) =>
        db.Checklists.AsNoTracking()
            .VisibleTo(ctx, AccessDomain.Checklists)
            .Where(x => x.Title.EndsWith(suffix))
            .Select(x => x.Id)
            .ToListAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}
