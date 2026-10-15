// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
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
/// Rules left naming nothing — older than the rule that removes them with their row, or come
/// round it — are reported by the integrity check and removed by its scheduled pass, which then
/// fails, naming how many of each kind it took.
/// </summary>
/// <remarks>
/// One orphan is planted for every kind of reference the anchor list knows, from that list, so a
/// kind added later is planted here without anybody remembering to. Beside them stand the rows the
/// pass must never take: a rule on a binned trip, on a binned document, on an album (which the
/// document domain governs, so it lives in a table the domain's name does not suggest), and on a
/// live trip.
/// </remarks>
public sealed class AccessOrphanSweepTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient admin = null!;
    private Guid adminId;
    private Guid readerId;

    public AccessOrphanSweepTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString, configureServices: JobWorkers.RemoveFrom);

    public async Task InitializeAsync()
    {
        adminId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"aos-adm-{suffix}@t.local");
        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"aos-rdr-{suffix}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"aos-adm-{suffix}@t.local");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        admin.Dispose();
        factory.Dispose();
    }

    [Fact]
    public async Task The_check_reports_and_the_pass_removes_every_rule_left_naming_nothing()
    {
        var keep = await KeptRulesAsync();
        var planted = await PlantOrphansAsync();
        var searched = AccessAnchors.Arms.Where(a => !a.HasForeignKey).Select(a => a.Name).ToList();
        planted.Keys.ShouldBe(searched, ignoreOrder: true);

        // Reported first, each under the check its kind belongs to, and nothing kept among them.
        IReadOnlyList<IntegrityProblem> problems;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            problems = await scope.ServiceProvider.GetRequiredService<FeatureIntegrityVerifier>().VerifyAsync();
        }

        foreach (var (arm, (rowId, anchorId)) in planted)
        {
            var check = arm.StartsWith("subject:", StringComparison.Ordinal) ? "access_subject_orphan"
                : arm.StartsWith("member:", StringComparison.Ordinal) ? "permission_member_orphan"
                : "access_scope_orphan";
            problems.ShouldContain(
                p => p.Check == check && p.Detail.Contains(anchorId.ToString()) && p.Detail.Contains($" {rowId})"),
                $"{arm} was not reported");
        }

        problems.Count.ShouldBe(planted.Count, string.Join("; ", problems.Select(p => $"{p.Check}: {p.Detail}")));

        // The pass removes them and fails, naming the count of each kind.
        var failed = await Should.ThrowAsync<InvalidOperationException>(RunPassAsync);
        failed.Message.ShouldContain($"Removed {planted.Count} orphaned access row(s)");
        foreach (var arm in planted.Keys)
        {
            failed.Message.ShouldContain($"{arm}=1");
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var entryIds = planted.Where(p => !p.Key.StartsWith("member:", StringComparison.Ordinal)).Select(p => p.Value.RowId).ToArray();
            var memberIds = planted.Where(p => p.Key.StartsWith("member:", StringComparison.Ordinal)).Select(p => p.Value.RowId).ToArray();
            (await db.AccessEntries.CountAsync(e => entryIds.Contains(e.Id))).ShouldBe(0);
            (await db.PermissionGroupMembers.CountAsync(m => memberIds.Contains(m.Id))).ShouldBe(0);
            (await db.AccessEntries.CountAsync(e => keep.Contains(e.Id))).ShouldBe(keep.Length, "the pass took a rule whose row exists");

            // Through the trail, so the rule that went is still on record whole.
            var removedIds = entryIds.Select(id => id.ToString()).ToArray();
            (await db.AuditEntries.CountAsync(a =>
                    a.EntityType == nameof(AccessEntry) && a.Action == AuditActions.Deleted && removedIds.Contains(a.EntityId!)))
                .ShouldBe(entryIds.Length);
        }

        // Nothing left: the next pass is quiet.
        await RunPassAsync();
    }

    [Fact]
    public async Task A_pass_with_nothing_to_remove_removes_nothing()
    {
        var keep = await KeptRulesAsync();
        await RunPassAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.AccessEntries.CountAsync(e => keep.Contains(e.Id))).ShouldBe(keep.Length);
        (await scope.ServiceProvider.GetRequiredService<AccessOrphanSweep>().RemoveAsync()).ShouldBeEmpty();
    }

    // ---- building blocks

    private async Task RunPassAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetServices<IProcessingJobHandler>()
            .Single(h => h.Kind == ProcessingJobKinds.FeatureIntegrityVerify);
        await handler.ExecuteAsync(new ProcessingJob { Kind = ProcessingJobKinds.FeatureIntegrityVerify }, CancellationToken.None);
    }

    /// <summary>One access row per searched kind of reference, each naming an id no table holds.</summary>
    private async Task<Dictionary<string, (long RowId, Guid AnchorId)>> PlantOrphansAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var ruleset = new PermissionGroup { Name = $"Orphans {suffix}", Slug = $"orphans-{suffix}" };
        db.PermissionGroups.Add(ruleset);
        await db.SaveChangesAsync();

        var planted = new Dictionary<string, object>();
        foreach (var arm in AccessAnchors.Arms.Where(a => !a.HasForeignKey))
        {
            var gone = Guid.CreateVersion7();
            object row = arm.Column switch
            {
                AccessReferenceColumn.ObjectScope => Rule(arm.Domain, AccessScopeKind.Object, gone),
                AccessReferenceColumn.ScopeId => Rule(
                    arm.ScopeKind switch
                    {
                        AccessScopeKind.CavingGroup => AccessDomain.TripLogs,
                        AccessScopeKind.FeatureSet => AccessDomain.Features,
                        AccessScopeKind.Cabinet => AccessDomain.Documents,
                        _ => throw new Xunit.Sdk.XunitException($"No domain to plant a {arm.Name} orphan in"),
                    },
                    arm.ScopeKind,
                    gone),
                AccessReferenceColumn.Subject => new AccessEntry
                {
                    SubjectKind = arm.SubjectKind,
                    SubjectId = gone,
                    Effect = AccessEffect.Allow,
                    Domain = AccessDomain.TripLogs,
                    Actions = AccessAction.Read,
                    ScopeKind = AccessScopeKind.All,
                },
                AccessReferenceColumn.PermissionMember => new PermissionGroupMember
                {
                    PermissionGroupId = ruleset.Id, MemberKind = arm.SubjectKind, MemberId = gone,
                },
                _ => throw new Xunit.Sdk.XunitException($"{arm.Name} is not searched"),
            };
            db.Add(row);
            planted[arm.Name] = (row, gone);
        }

        await db.SaveChangesAsync();
        return planted.ToDictionary(p => p.Key, p =>
        {
            var (row, gone) = ((object, Guid))p.Value;
            return (row is AccessEntry e ? e.Id : ((PermissionGroupMember)row).Id, gone);
        });
    }

    private AccessEntry Rule(AccessDomain domain, AccessScopeKind scope, Guid anchor) => new()
    {
        SubjectKind = AccessSubjectKind.User,
        SubjectId = readerId,
        Effect = AccessEffect.Allow,
        Domain = domain,
        Actions = AccessAction.Read,
        ScopeKind = scope,
        ScopeId = anchor,
    };

    /// <summary>Rules on a binned trip, a binned document, an album and a live trip.</summary>
    private async Task<long[]> KeptRulesAsync()
    {
        var binnedTrip = await CreateTripAsync();
        (await admin.DeleteAsync($"/api/v1/trip-logs/{binnedTrip}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var liveTrip = await CreateTripAsync();

        using var picture = new MagickImage(MagickColors.SlateGray, 64, 48);
        var content = new ByteArrayContent(picture.ToByteArray(MagickFormat.Jpeg));
        content.Headers.ContentType = new("image/jpeg");
        using var form = new MultipartFormDataContent { { content, "file", $"kept-{Guid.NewGuid():N}.jpg" } };
        var file = await CreatedIdAsync(await admin.PostAsync("/api/v1/files/?allowDuplicate=true", form));

        Guid document;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            document = await db.StoredFiles.Where(f => f.Id == file)
                .Join(db.DocumentVersions, f => f.DocumentVersionId, v => v.Id, (_, v) => v.DocumentId)
                .SingleAsync();
        }

        (await admin.PostAsJsonAsync("/api/v1/photos/bulk", new { documentIds = new[] { document }, delete = true }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var album = await CreatedIdAsync(await admin.PostAsJsonAsync("/api/v1/albums", new
        {
            title = $"Kept album {suffix}",
            description = (string?)null,
            visibility = "private",
            cavingGroupId = (Guid?)null,
            subjectEntityType = (string?)null,
            subjectEntityId = (Guid?)null,
        }));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            AccessEntry[] rules =
            [
                Rule(AccessDomain.TripLogs, AccessScopeKind.Object, binnedTrip),
                Rule(AccessDomain.TripLogs, AccessScopeKind.Object, liveTrip),
                Rule(AccessDomain.Documents, AccessScopeKind.Object, document),
                Rule(AccessDomain.Files, AccessScopeKind.Object, file),
                Rule(AccessDomain.Documents, AccessScopeKind.Object, album),
            ];
            db.AccessEntries.AddRange(rules);
            await db.SaveChangesAsync();
            return [.. rules.Select(r => r.Id)];
        }
    }

    private async Task<Guid> CreateTripAsync()
    {
        var caver = await RosterHelper.CaverIdForAsync(factory, adminId);
        return await CreatedIdAsync(await admin.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"Kept trip {suffix} {Guid.NewGuid():N}"[..40],
            tripDate = "2026-07-01",
            caveIds = Array.Empty<Guid>(),
            participants = new[] { new { caverId = caver } },
            visibility = "private",
        }));
    }

    private static async Task<Guid> CreatedIdAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }
}
