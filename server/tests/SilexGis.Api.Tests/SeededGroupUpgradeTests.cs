// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// What happens to an installation that was seeded before a resource domain existed.
///
/// Every other test here starts from an empty database, so the well-known permission groups
/// are always created by today's seeder with today's domain list — which is exactly the case
/// that cannot go wrong. The case that can is the opposite one: the groups already exist, the
/// seeder is create-only by slug and will not touch them again, and a right that has just
/// moved to a new domain therefore reaches nobody. This class builds that database
/// deliberately and asserts the upgrade carries the right across.
///
/// It runs on a database of its own, because it rewinds migration state and empties a
/// domain's entries — neither of which a database shared with other tests could survive.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SeededGroupUpgradeTests : IAsyncLifetime, IDisposable
{
    /// <summary>The migration that carries the upload right onto already-seeded groups.
    /// Matched by name, not by timestamp, so it survives being renumbered.</summary>
    private const string BackfillMigrationSuffix = "_BackfillDocumentAccessEntries";

    private readonly string adminConnectionString;
    private readonly string databaseName = $"silexgis_upgrade_{Guid.NewGuid():N}";
    private readonly string filesRoot =
        Path.Combine(Path.GetTempPath(), $"silexgis-test-upgrade-{Guid.NewGuid():N}");

    private SilexGisApiFactory factory = null!;
    private HttpClient editor = null!;

    public SeededGroupUpgradeTests(PostgresFixture postgres) =>
        adminConnectionString = postgres.ConnectionString;

    public async Task InitializeAsync()
    {
        await using (var admin = new NpgsqlConnection(adminConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        var connectionString =
            new NpgsqlConnectionStringBuilder(adminConnectionString) { Database = databaseName }
                .ConnectionString;
        factory = new SilexGisApiFactory(connectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });

        // Touching the factory boots the application, which migrates and seeds — so from
        // here the database is a fresh install made by today's code.
        var email = $"upgrade-{Guid.NewGuid().ToString("N")[..8]}@t.local";
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, email);
        editor = await AuthHelper.BearerClientAsync(factory, email);
    }

    [Fact]
    public async Task An_installation_seeded_before_documents_were_a_domain_keeps_its_upload()
    {
        // A fresh install is the easy half, and it is asserted first so the rest cannot pass
        // by accident: the seeded editors ruleset carries documents, and the upload works.
        (await DomainActionsAsync(SeededPermissionGroups.EditorsSlug, AccessDomain.Documents))
            .ShouldNotBeNull();
        (await UploadAsync("fresh-install.txt")).Status.ShouldBe(HttpStatusCode.Created);

        // Now make it the database an older installation actually has. The groups exist and
        // hold everything they always held, but nothing was ever written against documents,
        // because the domain did not exist when they were created.
        await RewindToPreDocumentsStateAsync();

        // Which is precisely the regression: the person who could always upload cannot. The
        // refusal is the real one the upload path produces, not a stand-in for it.
        var refused = await UploadAsync("before-upgrade.txt");
        refused.Status.ShouldBe(HttpStatusCode.Forbidden);
        refused.Code.ShouldBe(CreateRules.ForbiddenCode);

        // Starting the application migrates, which is where the carry-over happens.
        await MigrateAsync();

        // Each well-known group ends up holding over documents exactly what it holds over
        // the file store — the rights that decided uploads until the move — so an upgraded
        // installation and a fresh one land on the same rules.
        string[] seeded =
        [
            SeededPermissionGroups.EditorsSlug,
            SeededPermissionGroups.ReviewersSlug,
            SeededPermissionGroups.AdministratorsSlug,
        ];
        foreach (var slug in seeded)
        {
            var fileStore = await DomainActionsAsync(slug, AccessDomain.Files);
            fileStore.ShouldNotBeNull($"{slug} should hold file-store rights to carry over");
            (await DomainActionsAsync(slug, AccessDomain.Documents)).ShouldBe(fileStore);
        }

        // A group the seeder does not range over is left alone: it never asked for the new
        // domain, and inventing rights for it would be the opposite mistake.
        (await DomainActionsAsync(SeededPermissionGroups.AllUsersSlug, AccessDomain.Documents))
            .ShouldBeNull();

        // And the same upload, by the same person, is accepted again.
        (await UploadAsync("after-upgrade.txt")).Status.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task The_carry_over_leaves_an_answer_that_was_already_given_alone()
    {
        await RewindToPreDocumentsStateAsync();

        // An installation that has already decided about documents — here, by allowing the
        // editors a read and nothing else — must not have that decision overwritten by a
        // step whose whole justification is that no decision existed yet.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var editors = await db.PermissionGroups
                .FirstAsync(g => g.Slug == SeededPermissionGroups.EditorsSlug);
            db.AccessEntries.Add(new()
            {
                PermissionGroupId = editors.Id,
                Effect = AccessEffect.Allow,
                Domain = AccessDomain.Documents,
                Actions = AccessAction.Read,
                ScopeKind = AccessScopeKind.All,
            });
            await db.SaveChangesAsync();
        }

        await MigrateAsync();

        (await DomainActionsAsync(SeededPermissionGroups.EditorsSlug, AccessDomain.Documents))
            .ShouldBe(AccessAction.Read);

        // The decision holds all the way through: a read without a create is still no upload.
        var refused = await UploadAsync("still-refused.txt");
        refused.Status.ShouldBe(HttpStatusCode.Forbidden);
        refused.Code.ShouldBe(CreateRules.ForbiddenCode);

        // Proving that refusal is the rule and not a fixture that quietly stopped working:
        // the reviewers group, which had said nothing about documents, did get its rights.
        (await DomainActionsAsync(SeededPermissionGroups.ReviewersSlug, AccessDomain.Documents))
            .ShouldBe(await DomainActionsAsync(SeededPermissionGroups.ReviewersSlug, AccessDomain.Files));
    }

    // ---- helpers ----

    /// <summary>
    /// Turns the database into one seeded before documents were a domain: every entry
    /// against them removed, and the carry-over step marked unapplied so the next startup
    /// runs it the way it would on a real upgrade.
    /// </summary>
    private async Task RewindToPreDocumentsStateAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.AccessEntries.Where(e => e.Domain == AccessDomain.Documents).ExecuteDeleteAsync();

        var migrationId = db.Database.GetMigrations()
            .SingleOrDefault(m => m.EndsWith(BackfillMigrationSuffix, StringComparison.Ordinal));
        migrationId.ShouldNotBeNull(
            "the carry-over migration is gone — the upgrade path it covers would be unguarded");
        await db.Database.ExecuteSqlAsync(
            $"DELETE FROM \"__EFMigrationsHistory\" WHERE migration_id = {migrationId}");
    }

    private async Task MigrateAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.Database.MigrateAsync();
    }

    /// <summary>The actions a seeded group holds domain-wide, or null when it holds none.</summary>
    private async Task<AccessAction?> DomainActionsAsync(string slug, AccessDomain domain)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var actions = await (
            from entry in db.AccessEntries.AsNoTracking()
            from home in db.PermissionGroups
            where home.Id == entry.PermissionGroupId && home.Slug == slug
                && entry.Domain == domain && entry.ScopeKind == AccessScopeKind.All
            select entry.Actions).ToListAsync();
        return actions.Count == 0 ? null : actions.Aggregate((left, right) => left | right);
    }

    private async Task<(HttpStatusCode Status, string? Code)> UploadAsync(string fileName)
    {
        var content = new ByteArrayContent("body"u8.ToArray());
        content.Headers.ContentType = new("text/plain");
        using var form = new MultipartFormDataContent { { content, "file", fileName } };
        var response = await editor.PostAsync("/api/v1/files/", form);
        var payload = await response.Content.ReadAsStringAsync();
        if (response.IsSuccessStatusCode)
        {
            return (response.StatusCode, null);
        }

        var problem = JsonDocument.Parse(payload).RootElement;
        return (response.StatusCode,
            problem.TryGetProperty("code", out var code) ? code.GetString() : null);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        editor?.Dispose();
        factory?.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }

        // The container goes away with the collection, but the database would otherwise sit
        // there for the rest of the run.
        using var admin = new NpgsqlConnection(adminConnectionString);
        admin.Open();
        using var drop = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", admin);
        drop.ExecuteNonQuery();
    }
}
