// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// A row removed for good takes every access row naming it; a row only binned keeps them, and
/// coming back gives back exactly the access it had.
/// </summary>
/// <remarks>
/// <para>
/// The theory runs once for every way an access row can name something, taken from the one list
/// that says so — so a reference added to that list fails here until somebody writes the case
/// that removes its row through its real route and shows nothing is left behind. Where the route
/// refuses instead (a feature set or a cabinet a rule still scopes to), the case shows the
/// refusal and that the rule stood.
/// </para>
/// <para>
/// Rules are written straight into the table: the object permission routes are tested where they
/// live, and what is under test here is only what a delete leaves.
/// </para>
/// </remarks>
public sealed class AccessAnchorCleanupTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient admin = null!;
    private HttpClient reader = null!;
    private Guid adminId;
    private Guid readerId;
    private long caveTypeId;

    public AccessAnchorCleanupTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                // The purge these tests run is off unless a window is set.
                ["DocumentRetention:RetentionDays"] = "30",
            },
            configureServices: JobWorkers.RemoveFrom);

    public async ValueTask InitializeAsync()
    {
        adminId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"aac-adm-{suffix}@t.local");
        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"aac-rdr-{suffix}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"aac-adm-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"aac-rdr-{suffix}@t.local");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        admin.Dispose();
        reader.Dispose();
        factory.Dispose();
    }

    public static TheoryData<string> Arms()
    {
        var data = new TheoryData<string>();
        foreach (var arm in AccessAnchors.Arms)
        {
            data.Add(arm.Name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Arms))]
    public async Task Removing_what_a_rule_names_for_good_removes_the_rule(string arm)
    {
        var (anchors, removed) = arm switch
        {
            "object:TripLogs" => await PurgedTripAsync(),
            "object:Geofiles" => await DeletedGeofileAsync(ruleOnFile: false),
            "object:Files" => await DeletedGeofileAsync(ruleOnFile: true),
            "object:GeoreferencedMaps" => await DeletedRasterAsync(),
            "object:MapViews" => await DeletedMapViewAsync(),
            "object:Documents" => await PurgedDocumentAndDeletedAlbumAsync(),
            "object:Expeditions" => await DeletedCampAsync(),
            "grantedViaExpedition" => await DeletedSharingCampAsync(),
            "object:Checklists" => await DeletedChecklistAsync(),
            "object:Events" => await DeletedEventAsync(),
            "object:CavingGroups" or "scope:CavingGroup" or "subject:CavingGroup" or "member:CavingGroup" =>
                await DeletedCavingGroupAsync(),
            "object:PermissionGroups" => await DeletedPermissionGroupAsync(),
            "object:FeatureSets" => await DeletedFeatureSetAsync(),
            "scope:FeatureSet" => await RefusedFeatureSetAsync(),
            "scope:Cabinet" => await RefusedCabinetAsync(),
            "subject:User" or "member:User" => await DeletedAccountAsync(),
            "feature" => await PurgedCaveAsync(),
            _ => throw new Xunit.Sdk.XunitException(
                $"Nothing here removes what a '{arm}' reference names through its own route. Write that case: "
                + "a reference with no case is one whose rows nobody has shown go with their row."),
        };

        foreach (var anchor in anchors)
        {
            if (removed)
            {
                (await RowsNamingAsync(anchor)).ShouldBe(0, $"rows still name {anchor} after it was removed");
            }
            else
            {
                (await RowsNamingAsync(anchor)).ShouldBeGreaterThan(0, "a refused delete removed the rule");
            }
        }

        // Whatever went, went cleanly: nothing the integrity check can see names a removed row.
        await using var scope = factory.Services.CreateAsyncScope();
        var orphans = await AccessOrphanSql.FindAsync(scope.ServiceProvider.GetRequiredService<SilexGisDbContext>());
        orphans.ShouldBeEmpty(string.Join("; ", orphans.Select(o => $"{o.Arm.Name} {o.AnchorId}")));
    }

    [Fact]
    public async Task A_binned_trip_keeps_its_rules_and_comes_back_with_its_access()
    {
        var trip = await CreateTripAsync();
        (await reader.GetAsync($"/api/v1/trip-logs/{trip}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await ObjectRuleAsync(AccessDomain.TripLogs, trip);
        (await reader.GetAsync($"/api/v1/trip-logs/{trip}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await admin.DeleteAsync($"/api/v1/trip-logs/{trip}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await reader.GetAsync($"/api/v1/trip-logs/{trip}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await RowsNamingAsync(trip)).ShouldBe(1);

        (await admin.PostAsync($"/api/v1/trip-logs/{trip}/restore", null)).IsSuccessStatusCode.ShouldBeTrue();
        (await reader.GetAsync($"/api/v1/trip-logs/{trip}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_binned_cave_keeps_its_rules_and_comes_back_with_its_access()
    {
        var cave = await CreateCaveAsync();
        (await reader.GetAsync($"/api/v1/caves/{cave}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await AddEntryAsync(e => { e.Domain = AccessDomain.Features; e.ScopeKind = AccessScopeKind.Object; e.ScopeFeatureId = cave; });
        (await reader.GetAsync($"/api/v1/caves/{cave}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await admin.DeleteAsync($"/api/v1/caves/{cave}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await reader.GetAsync($"/api/v1/caves/{cave}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await RowsNamingAsync(cave)).ShouldBe(1);

        (await admin.PostAsync($"/api/v1/features/{cave}/restore", null)).IsSuccessStatusCode.ShouldBeTrue();
        (await reader.GetAsync($"/api/v1/caves/{cave}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_binned_photograph_keeps_its_rules_and_comes_back_with_its_access()
    {
        var file = await UploadPictureAsync();
        var document = await DocumentOfAsync(file);
        (await reader.GetAsync($"/api/v1/photos/{document}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await ObjectRuleAsync(AccessDomain.Documents, document);
        (await reader.GetAsync($"/api/v1/photos/{document}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        await BinPhotoAsync(document);
        (await reader.GetAsync($"/api/v1/photos/{document}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await RowsNamingAsync(document)).ShouldBe(1);

        (await admin.PostAsync($"/api/v1/photos/{document}/restore", null)).IsSuccessStatusCode.ShouldBeTrue();
        (await reader.GetAsync($"/api/v1/photos/{document}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_rule_that_goes_with_its_row_is_written_to_the_audit_trail()
    {
        var view = await CreateMapViewAsync();
        var rule = await ObjectRuleAsync(AccessDomain.MapViews, view);

        (await admin.DeleteAsync($"/api/v1/map-views/{view}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.AccessEntries.AnyAsync(e => e.Id == rule)).ShouldBeFalse();
        var trail = await db.AuditEntries.AsNoTracking()
            .Where(a => a.EntityType == nameof(AccessEntry) && a.EntityId == rule.ToString())
            .ToListAsync();
        var deleted = trail.ShouldHaveSingleItem();
        deleted.Action.ShouldBe(AuditActions.Deleted);
        deleted.UserId.ShouldBe(adminId);
        deleted.Changes!.ShouldContain(view.ToString());
    }

    // ---- the cases, one per kind of row a rule can name

    private async Task<(Guid[], bool)> PurgedTripAsync()
    {
        var trip = await CreateTripAsync();
        await ObjectRuleAsync(AccessDomain.TripLogs, trip);
        (await admin.DeleteAsync($"/api/v1/trip-logs/{trip}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await RowsNamingAsync(trip)).ShouldBe(1, "a binned trip keeps its rules");

        await DeletedTrips.AgePastTheWindowAsync(factory, trip);
        await DeletedTrips.RunPurgeAsync(factory);
        (await DeletedTrips.RowAsync(factory, trip)).ShouldBeNull();
        return ([trip], true);
    }

    private async Task<(Guid[], bool)> DeletedGeofileAsync(bool ruleOnFile)
    {
        var file = await UploadTextAsync();
        var geofile = Guid.CreateVersion7();
        await WithDbAsync(db => db.Geofiles.Add(new Geofile
        {
            Id = geofile, Name = $"Track {suffix}", FileId = file, Format = GeofileFormat.Gpx, OwnerUserId = adminId,
        }));
        var anchor = ruleOnFile ? file : geofile;
        await ObjectRuleAsync(ruleOnFile ? AccessDomain.Files : AccessDomain.Geofiles, anchor);

        (await admin.DeleteAsync($"/api/v1/geofiles/{geofile}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ExistsAsync(db => db.StoredFiles.AnyAsync(f => f.Id == file))).ShouldBeFalse(
            "the geofile owned its upload outright, so the file went with it");
        return ([anchor], true);
    }

    private async Task<(Guid[], bool)> DeletedRasterAsync()
    {
        var file = await UploadTextAsync();
        var map = Guid.CreateVersion7();
        await WithDbAsync(db => db.GeoreferencedMaps.Add(new GeoreferencedMap
        {
            Id = map, Name = $"Plan {suffix}", FileId = file, OwnerUserId = adminId,
        }));
        await ObjectRuleAsync(AccessDomain.GeoreferencedMaps, map);
        (await admin.DeleteAsync($"/api/v1/georeferenced-maps/{map}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        return ([map], true);
    }

    private async Task<(Guid[], bool)> DeletedMapViewAsync()
    {
        var view = await CreateMapViewAsync();
        await ObjectRuleAsync(AccessDomain.MapViews, view);
        (await admin.DeleteAsync($"/api/v1/map-views/{view}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        return ([view], true);
    }

    /// <summary>
    /// The document domain governs albums as well as documents, and a document's files can carry
    /// rules of their own; the purge deletes the document in one statement, its files by cascade.
    /// </summary>
    private async Task<(Guid[], bool)> PurgedDocumentAndDeletedAlbumAsync()
    {
        var file = await UploadPictureAsync();
        var document = await DocumentOfAsync(file);
        await ObjectRuleAsync(AccessDomain.Documents, document);
        await ObjectRuleAsync(AccessDomain.Files, file);

        await BinPhotoAsync(document);
        (await RowsNamingAsync(document)).ShouldBe(1, "a binned document keeps its rules");
        (await RowsNamingAsync(file)).ShouldBe(1, "a binned document's files keep their rules");

        await WithDbAsync(db => db.Documents.IgnoreQueryFilters().Where(d => d.Id == document)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.DeletedAt, DateTimeOffset.UtcNow.AddYears(-1))));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var handler = scope.ServiceProvider.GetServices<IProcessingJobHandler>()
                .Single(h => h.Kind == ProcessingJobKinds.DocumentPurge);
            await handler.ExecuteAsync(new ProcessingJob { Kind = ProcessingJobKinds.DocumentPurge }, CancellationToken.None);
        }

        (await ExistsAsync(db => db.Documents.IgnoreQueryFilters().AnyAsync(d => d.Id == document))).ShouldBeFalse();

        var created = await admin.PostAsJsonAsync("/api/v1/albums", new
        {
            title = $"Album {suffix}",
            description = (string?)null,
            visibility = "private",
            cavingGroupId = (Guid?)null,
            subjectEntityType = (string?)null,
            subjectEntityId = (Guid?)null,
        });
        var album = await CreatedIdAsync(created);
        await ObjectRuleAsync(AccessDomain.Documents, album);
        (await admin.DeleteAsync($"/api/v1/albums/{album}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        return ([document, file, album], true);
    }

    private async Task<(Guid[], bool)> DeletedCampAsync()
    {
        var camp = await CreateCampAsync();
        await ObjectRuleAsync(AccessDomain.Expeditions, camp);
        (await admin.DeleteAsync($"/api/v1/expeditions/{camp}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        return ([camp], true);
    }

    /// <summary>A rule a camp's sharing wrote onto a trip goes with the camp; the trip stays.</summary>
    private async Task<(Guid[], bool)> DeletedSharingCampAsync()
    {
        var camp = await CreateCampAsync();
        var trip = await CreateTripAsync();
        await AddEntryAsync(e =>
        {
            e.Domain = AccessDomain.TripLogs;
            e.ScopeKind = AccessScopeKind.Object;
            e.ScopeId = trip;
            e.GrantedViaExpeditionId = camp;
        });
        (await admin.DeleteAsync($"/api/v1/expeditions/{camp}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ExistsAsync(db => db.TripLogs.AnyAsync(t => t.Id == trip))).ShouldBeTrue();
        return ([camp, trip], true);
    }

    private async Task<(Guid[], bool)> DeletedChecklistAsync()
    {
        var created = await admin.PostAsJsonAsync("/api/v1/checklists", new
        {
            title = $"List {suffix}",
            description = (string?)null,
            visibility = (int)Visibility.Private,
            items = new[] { new { text = "Permit obtained" } },
        });
        var list = await CreatedIdAsync(created);
        await ObjectRuleAsync(AccessDomain.Checklists, list);
        (await admin.DeleteAsync($"/api/v1/checklists/{list}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        return ([list], true);
    }

    private async Task<(Guid[], bool)> DeletedEventAsync()
    {
        var created = await admin.PostAsJsonAsync("/api/v1/events", new
        {
            title = $"Meeting {suffix}",
            kind = "clubMeeting",
            startDate = "2054-10-05",
            endDate = (string?)null,
            place = (string?)null,
            visibility = "private",
            cavingGroupId = (Guid?)null,
            recurrence = (object?)null,
        });
        var row = await CreatedIdAsync(created);
        await ObjectRuleAsync(AccessDomain.Events, row);
        (await admin.DeleteAsync($"/api/v1/events/{row}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        return ([row], true);
    }

    /// <summary>A club is named four ways: as an object, as a reach, as a subject and as a trustee.</summary>
    private async Task<(Guid[], bool)> DeletedCavingGroupAsync()
    {
        var created = await admin.PostAsJsonAsync("/api/v1/caving-groups/", new
        {
            name = $"Club {suffix} {Guid.NewGuid():N}"[..30],
            type = "cavingClub",
            description = (string?)null,
            website = (string?)null,
        });
        var club = await CreatedIdAsync(created);
        await ObjectRuleAsync(AccessDomain.CavingGroups, club);
        await AddEntryAsync(e => { e.Domain = AccessDomain.TripLogs; e.ScopeKind = AccessScopeKind.CavingGroup; e.ScopeId = club; });
        await AddEntryAsync(e => { e.SubjectKind = AccessSubjectKind.CavingGroup; e.SubjectId = club; e.Domain = AccessDomain.TripLogs; });
        await MemberAsync(await CreatePermissionGroupAsync(), AccessSubjectKind.CavingGroup, club);
        // Four written here, and the club's own starter ruleset besides, which is scoped to it.
        (await RowsNamingAsync(club)).ShouldBeGreaterThan(4);

        (await admin.DeleteAsync($"/api/v1/caving-groups/{club}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        return ([club], true);
    }

    private async Task<(Guid[], bool)> DeletedPermissionGroupAsync()
    {
        var group = await CreatePermissionGroupAsync();
        await ObjectRuleAsync(AccessDomain.PermissionGroups, group);
        (await admin.DeleteAsync($"/api/v1/permission-groups/{group}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        return ([group], true);
    }

    private async Task<(Guid[], bool)> DeletedFeatureSetAsync()
    {
        var set = await CreateFeatureSetAsync();
        await ObjectRuleAsync(AccessDomain.FeatureSets, set);
        (await admin.DeleteAsync($"/api/v1/feature-sets/{set}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        return ([set], true);
    }

    /// <summary>A set a rule still reaches through is refused, so a deny is never cancelled by a side effect.</summary>
    private async Task<(Guid[], bool)> RefusedFeatureSetAsync()
    {
        var set = await CreateFeatureSetAsync();
        await AddEntryAsync(e => { e.Domain = AccessDomain.Features; e.ScopeKind = AccessScopeKind.FeatureSet; e.ScopeId = set; });
        (await admin.DeleteAsync($"/api/v1/feature-sets/{set}")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        return ([set], false);
    }

    /// <summary>A cabinet a rule still reaches through is refused, for the same reason.</summary>
    private async Task<(Guid[], bool)> RefusedCabinetAsync()
    {
        var created = await admin.PostAsJsonAsync("/api/v1/cabinets", new
        {
            name = $"Shelf {suffix}",
            description = (string?)null,
            parentId = (Guid?)null,
        });
        var cabinet = await CreatedIdAsync(created);
        await AddEntryAsync(e => { e.Domain = AccessDomain.Documents; e.ScopeKind = AccessScopeKind.Cabinet; e.ScopeId = cabinet; });
        (await admin.DeleteAsync($"/api/v1/cabinets/{cabinet}")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        return ([cabinet], false);
    }

    /// <summary>
    /// An account is named as a subject and as a trustee, and deleting it takes the map views it
    /// owns by a database cascade — whose own rules have to go as well.
    /// </summary>
    private async Task<(Guid[], bool)> DeletedAccountAsync()
    {
        var leaver = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"aac-gone-{Guid.NewGuid():N}@t.local");
        await AddEntryAsync(e => { e.SubjectId = leaver; e.Domain = AccessDomain.TripLogs; });
        await MemberAsync(await CreatePermissionGroupAsync(), AccessSubjectKind.User, leaver);
        var view = Guid.CreateVersion7();
        await WithDbAsync(db => db.MapViews.Add(new MapView { Id = view, Name = $"Leaver's view {suffix}", OwnerUserId = leaver }));
        await ObjectRuleAsync(AccessDomain.MapViews, view);
        (await RowsNamingAsync(leaver)).ShouldBeGreaterThanOrEqualTo(2);

        (await admin.DeleteAsync($"/api/v1/users/{leaver}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ExistsAsync(db => db.MapViews.AnyAsync(v => v.Id == view))).ShouldBeFalse();
        return ([leaver, view], true);
    }

    /// <summary>
    /// A binned cave keeps its rules; removing it for good takes them. Nothing answers over HTTP
    /// with a feature purge, so the write service is asked directly, in its own transaction.
    /// </summary>
    private async Task<(Guid[], bool)> PurgedCaveAsync()
    {
        var cave = await CreateCaveAsync();
        await AddEntryAsync(e => { e.Domain = AccessDomain.Features; e.ScopeKind = AccessScopeKind.Subtree; e.ScopeFeatureId = cave; });
        (await admin.DeleteAsync($"/api/v1/caves/{cave}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await RowsNamingAsync(cave)).ShouldBe(1, "a binned cave keeps its rules");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var writer = scope.ServiceProvider.GetRequiredService<FeatureWriteService>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await writer.PurgeAsync(cave);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        (await ExistsAsync(db => db.Features.IgnoreQueryFilters().AnyAsync(f => f.Id == cave))).ShouldBeFalse();
        return ([cave], true);
    }

    // ---- building blocks

    private Task<long> ObjectRuleAsync(AccessDomain domain, Guid id) =>
        AddEntryAsync(e => { e.Domain = domain; e.ScopeKind = AccessScopeKind.Object; e.ScopeId = id; });

    private async Task<long> AddEntryAsync(Action<AccessEntry> shape)
    {
        var entry = new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = readerId,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.TripLogs,
            Actions = AccessAction.Read,
            ScopeKind = AccessScopeKind.All,
        };
        shape(entry);
        await WithDbAsync(db => db.AccessEntries.Add(entry));
        return entry.Id;
    }

    private Task MemberAsync(Guid group, AccessSubjectKind kind, Guid member) =>
        WithDbAsync(db => db.PermissionGroupMembers.Add(
            new PermissionGroupMember { PermissionGroupId = group, MemberKind = kind, MemberId = member }));

    /// <summary>Every access row naming the id, by any column and as anything.</summary>
    private async Task<int> RowsNamingAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.AccessEntries.CountAsync(e =>
                e.ScopeId == id || e.ScopeFeatureId == id || e.SubjectId == id || e.GrantedViaExpeditionId == id)
            + await db.PermissionGroupMembers.CountAsync(m => m.MemberId == id);
    }

    private async Task WithDbAsync(Func<SilexGisDbContext, object> act)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        if (act(db) is Task task)
        {
            await task;
        }

        await db.SaveChangesAsync();
    }

    private async Task<bool> ExistsAsync(Func<SilexGisDbContext, Task<bool>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<SilexGisDbContext>());
    }

    private async Task<Guid> CreateTripAsync()
    {
        var caver = await RosterHelper.CaverIdForAsync(factory, adminId);
        var created = await admin.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"Rule trip {suffix} {Guid.NewGuid():N}"[..40],
            tripDate = "2026-07-01",
            caveIds = Array.Empty<Guid>(),
            participants = new[] { new { caverId = caver } },
            visibility = "private",
        });
        return await CreatedIdAsync(created);
    }

    private async Task<Guid> CreateCaveAsync()
    {
        var created = await admin.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Rule cave {suffix} {Guid.NewGuid():N}"[..40],
            caveTypeId,
            visibility = "private",
            locationProtected = false,
            explorationStatus = "unknown",
            isShowCave = false,
        });
        return await CreatedIdAsync(created);
    }

    private async Task<Guid> CreateCampAsync()
    {
        var created = await admin.PostAsJsonAsync("/api/v1/expeditions/", new
        {
            name = $"Rule camp {suffix} {Guid.NewGuid():N}"[..40],
            description = "A camp.",
            startDate = "2026-06-28",
            endDate = "2026-07-05",
            geom = (object?)null,
            cavingGroupId = (Guid?)null,
            visibility = "private",
        });
        return await CreatedIdAsync(created);
    }

    private async Task<Guid> CreateMapViewAsync()
    {
        var created = await admin.PostAsJsonAsync("/api/v1/map-views/", new
        {
            name = $"View {suffix} {Guid.NewGuid():N}"[..30],
            description = (string?)null,
            config = new { },
            isHome = false,
            cavingGroupId = (Guid?)null,
            visibility = "private",
        });
        return await CreatedIdAsync(created);
    }

    private async Task<Guid> CreatePermissionGroupAsync()
    {
        var created = await admin.PostAsJsonAsync("/api/v1/permission-groups/", new
        {
            name = $"Ruleset {suffix} {Guid.NewGuid():N}"[..30],
            description = (string?)null,
        });
        return await CreatedIdAsync(created);
    }

    private async Task<Guid> CreateFeatureSetAsync()
    {
        var created = await admin.PostAsJsonAsync("/api/v1/feature-sets/", new
        {
            name = $"Set {suffix} {Guid.NewGuid():N}"[..30],
            description = "a set",
        });
        return await CreatedIdAsync(created);
    }

    private async Task BinPhotoAsync(Guid document)
    {
        var binned = await admin.PostAsJsonAsync("/api/v1/photos/bulk", new { documentIds = new[] { document }, delete = true });
        binned.StatusCode.ShouldBe(HttpStatusCode.OK, await binned.Content.ReadAsStringAsync());
    }

    private async Task<Guid> UploadPictureAsync()
    {
        using var picture = new MagickImage(MagickColors.SlateGray, 64, 48);
        var content = new ByteArrayContent(picture.ToByteArray(MagickFormat.Jpeg));
        content.Headers.ContentType = new("image/jpeg");
        return await UploadAsync(content, $"rule-{Guid.NewGuid():N}.jpg");
    }

    private async Task<Guid> UploadTextAsync()
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes($"Notes {Guid.NewGuid()}"));
        content.Headers.ContentType = new("text/plain");
        return await UploadAsync(content, $"rule-{Guid.NewGuid():N}.txt");
    }

    private async Task<Guid> UploadAsync(HttpContent content, string name)
    {
        using var form = new MultipartFormDataContent { { content, "file", name } };
        return await CreatedIdAsync(await admin.PostAsync("/api/v1/files/?allowDuplicate=true", form));
    }

    private async Task<Guid> DocumentOfAsync(Guid fileId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.StoredFiles.Where(f => f.Id == fileId)
            .Join(db.DocumentVersions, f => f.DocumentVersionId, v => v.Id, (_, v) => v.DocumentId)
            .SingleAsync();
    }

    private static async Task<Guid> CreatedIdAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }
}
