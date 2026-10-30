// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Api.Features.Users;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Infrastructure.Identity;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Deleting an account that still owns records is refused with a coded conflict that says what is
/// in the way, instead of reaching the database's foreign-key refusal and answering 500.
/// </summary>
/// <remarks>
/// <para>
/// The rows are inserted straight into the tables with foreign-key enforcement switched off for the
/// inserting connection only: what is under test is the account's own check, and building a valid
/// graph of every owned kind (a feature needs a layer, a share needs a feature, and so on) would test
/// nineteen creation endpoints instead. The deleting request runs on an ordinary connection and meets
/// every constraint the database has.
/// </para>
/// </remarks>
public sealed class UserDeleteBlockedTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string tag = Guid.NewGuid().ToString("N")[..8];

    private HttpClient admin = null!;

    public UserDeleteBlockedTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async ValueTask InitializeAsync()
    {
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"udb-adm-{tag}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"udb-adm-{tag}@t.local");
    }

    public static IEnumerable<TheoryDataRow<string>> Kinds() =>
        UserDeleteBlockers.Entries.Select(e => new TheoryDataRow<string>(e.Kind));

    /// <summary>
    /// The client words every kind the server can name, in both languages: a kind without a label
    /// would show its snake_case identifier inside a translated sentence.
    /// </summary>
    [Theory]
    [InlineData("en")]
    [InlineData("ro")]
    public void Every_blocking_kind_has_a_label_in_the_client(string locale)
    {
        var path = Path.Combine(RepositoryRoot(), "client", "src", "i18n", "locales", $"{locale}.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var labels = document.RootElement.GetProperty("admin").GetProperty("users")
            .GetProperty("problems").GetProperty("deleteBlockedKinds")
            .EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        var kinds = UserDeleteBlockers.Entries.Select(e => e.Kind).ToHashSet(StringComparer.Ordinal);
        kinds.Except(labels).ShouldBeEmpty($"{locale}.json lacks a label for these kinds");
        labels.Except(kinds).ShouldBeEmpty($"{locale}.json labels kinds the server never names");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "client", "src", "i18n", "locales"))
                && Directory.Exists(Path.Combine(directory.FullName, "server", "src")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("The repository root was not found above the test assembly.");
    }

    /// <summary>
    /// Every kind refuses on its own, counts what it holds, names nothing else, leaves the account
    /// alone — and stops refusing once the rows are gone.
    /// </summary>
    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task An_account_owning_one_kind_is_refused_with_its_kind_and_count_then_deleted_once_it_owns_nothing(
        string kind)
    {
        var entry = UserDeleteBlockers.Entries.Single(e => e.Kind == kind);
        var userId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"udb-{kind}-{tag}@t.local");

        await InsertOwnedRowsAsync(entry, userId, 2);

        var refused = await admin.DeleteAsync($"/api/v1/users/{userId}");

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var body = JsonDocument.Parse(await refused.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("code").GetString().ShouldBe("user.delete_blocked");
        var blocking = body.GetProperty("blocking").EnumerateArray().ToList();
        blocking.Count.ShouldBe(1, "only the kind that holds the account is named");
        blocking[0].GetProperty("kind").GetString().ShouldBe(kind);
        blocking[0].GetProperty("count").GetInt32().ShouldBe(2);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.Users.AnyAsync(u => u.Id == userId)).ShouldBeTrue("a refused deletion removes nothing");
        }

        await RemoveOwnedRowsAsync(entry, userId);

        (await admin.DeleteAsync($"/api/v1/users/{userId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// The list in the endpoint is complete: it names every foreign key to the account table that
    /// restricts deletion, and no column that is not one.
    /// </summary>
    /// <remarks>
    /// This is what turns "somebody added an owned kind and forgot the check" from a 500 in front of
    /// an administrator into a failing test.
    /// </remarks>
    [Fact]
    public async Task Every_restricting_foreign_key_to_the_account_table_is_counted()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var restricting = db.Model.GetEntityTypes()
            .SelectMany(t => t.GetDeclaredForeignKeys())
            .Where(fk => fk.PrincipalEntityType.ClrType == typeof(SilexGisUser)
                         && fk.DeleteBehavior is DeleteBehavior.Restrict or DeleteBehavior.NoAction
                             or DeleteBehavior.ClientNoAction)
            .SelectMany(fk => fk.Properties.Select(p => $"{fk.DeclaringEntityType.ClrType.Name}.{p.Name}"))
            .Order()
            .ToList();

        var counted = UserDeleteBlockers.Entries
            .SelectMany(e => e.Columns.Select(c => $"{e.Entity.Name}.{c}"))
            .Order()
            .ToList();

        counted.ShouldBe(restricting);
        UserDeleteBlockers.Entries.Select(e => e.Kind).Distinct().Count()
            .ShouldBe(UserDeleteBlockers.Entries.Count, "a kind is named once");
    }

    /// <summary>
    /// An account that owns nothing is deleted as before, and an account that owns rows of several
    /// kinds hears about all of them, in the list's order.
    /// </summary>
    [Fact]
    public async Task Several_kinds_are_all_named_and_an_account_owning_nothing_is_deleted()
    {
        var free = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"udb-free-{tag}@t.local");
        (await admin.DeleteAsync($"/api/v1/users/{free}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var busy = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"udb-busy-{tag}@t.local");
        var trips = UserDeleteBlockers.Entries.Single(e => e.Kind == "trips");
        var albums = UserDeleteBlockers.Entries.Single(e => e.Kind == "albums");
        await InsertOwnedRowsAsync(trips, busy, 3);
        await InsertOwnedRowsAsync(albums, busy, 1);

        var refused = await admin.DeleteAsync($"/api/v1/users/{busy}");

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var body = JsonDocument.Parse(await refused.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("blocking").EnumerateArray()
            .Select(b => $"{b.GetProperty("kind").GetString()}={b.GetProperty("count").GetInt32()}")
            .ShouldBe(["albums=1", "trips=3"]);
        body.GetRawText().ShouldNotContain(busy.ToString(), Case.Insensitive, "the answer holds no identifiers of the rows");
    }

    /// <summary>A caller without the right to delete accounts is told no before anything is counted.</summary>
    [Fact]
    public async Task A_caller_without_the_users_domain_is_refused_not_told_what_the_account_owns()
    {
        var editorEmail = $"udb-ed-{tag}@t.local";
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, editorEmail);
        using var editor = await AuthHelper.BearerClientAsync(factory, editorEmail);
        var owner = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"udb-own-{tag}@t.local");
        await InsertOwnedRowsAsync(UserDeleteBlockers.Entries.Single(e => e.Kind == "trips"), owner, 1);

        var forbidden = await editor.DeleteAsync($"/api/v1/users/{owner}");
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await forbidden.Content.ReadAsStringAsync()).ShouldNotContain("blocking");

        (await admin.DeleteAsync($"/api/v1/users/{owner}")).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        using var anonymous = factory.CreateClient();
        (await anonymous.DeleteAsync($"/api/v1/users/{owner}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>A row in the trash still holds the account's key and is counted.</summary>
    [Fact]
    public async Task A_soft_deleted_trip_still_blocks_the_account()
    {
        var owner = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"udb-trash-{tag}@t.local");
        var trips = UserDeleteBlockers.Entries.Single(e => e.Kind == "trips");
        await InsertOwnedRowsAsync(trips, owner, 1);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.TripLogs.Where(t => t.OwnerUserId == owner)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.DeletedAt, DateTimeOffset.UtcNow)))
                .ShouldBe(1);
            (await db.TripLogs.CountAsync(t => t.OwnerUserId == owner)).ShouldBe(0, "the trash is hidden from ordinary queries");
        }

        var refused = await admin.DeleteAsync($"/api/v1/users/{owner}");
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await refused.Content.ReadAsStringAsync()).ShouldContain("trips");
    }

    // ---------------------------------------------------------------- row construction

    /// <summary>
    /// Inserts <paramref name="count"/> rows of one entity whose owner columns name the account and
    /// whose every other required column holds an arbitrary value.
    /// </summary>
    private async Task InsertOwnedRowsAsync(UserDeleteBlockers.Entry entry, Guid userId, int count)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.Database.OpenConnectionAsync();
        // Only this connection, only until it closes: foreign keys are enforced by triggers that
        // this setting skips.
        await db.Database.ExecuteSqlRawAsync("SET session_replication_role = replica");

        var entityType = db.Model.FindEntityType(entry.Entity)!;
        for (var i = 0; i < count; i++)
        {
            var row = Activator.CreateInstance(entry.Entity)!;
            var tracked = db.Entry(row);
            foreach (var property in entityType.GetProperties())
            {
                if (property.IsShadowProperty() && property.IsForeignKey() is false)
                {
                    continue;
                }

                if (entry.Columns.Contains(property.Name, StringComparer.Ordinal))
                {
                    tracked.Property(property.Name).CurrentValue = userId;
                }
                else if (!property.IsNullable && !property.IsPrimaryKey() && property.ValueGenerated == ValueGenerated.Never)
                {
                    tracked.Property(property.Name).CurrentValue = Arbitrary(property);
                }
                else if (property.IsPrimaryKey() && property.ClrType == typeof(Guid))
                {
                    tracked.Property(property.Name).CurrentValue = Guid.NewGuid();
                }
            }

            // Two rows' worth of table constraints that a blank value cannot meet.
            switch (row)
            {
                case SilexGis.Domain.Entities.SurveyUploadPublicLink link:
                    // An acknowledgement is a person and a moment, never one without the other.
                    link.ProtectedAcknowledgedAt = DateTimeOffset.UtcNow;
                    break;
                case SilexGis.Domain.Entities.Feature feature:
                    // A cave (kind 0) must name its type.
                    feature.FeatureTypeId = 1;
                    break;
            }

            db.Add(row);
        }

        await db.SaveChangesAsync();
    }

    private async Task RemoveOwnedRowsAsync(UserDeleteBlockers.Entry entry, Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var table = db.Model.FindEntityType(entry.Entity)!;
        var name = table.GetTableName()!;
        var schema = table.GetSchema() ?? "public";
        // Names come from the model, the value is a parameter.
        var where = string.Join(" OR ", entry.Columns.Select(c => "\"" + table.FindProperty(c)!.GetColumnName() + "\" = {0}"));
#pragma warning disable EF1003 // table and column names come from the model; the value is a parameter
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM \"" + schema + "\".\"" + name + "\" WHERE " + where, userId);
#pragma warning restore EF1003
    }

    private static object Arbitrary(IProperty property)
    {
        var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
        if (type == typeof(string))
        {
            var text = Guid.NewGuid().ToString("N");
            var max = property.GetMaxLength();
            return max is { } m and < 32 ? text[..m] : text;
        }

        if (type == typeof(Guid)) return Guid.NewGuid();
        if (type == typeof(DateTimeOffset)) return DateTimeOffset.UtcNow;
        if (type == typeof(DateTime)) return DateTime.UtcNow;
        if (type == typeof(DateOnly)) return DateOnly.FromDateTime(DateTime.UtcNow);
        if (type == typeof(byte[])) return new byte[] { 1 };
        if (typeof(Point).IsAssignableFrom(type)) return new Point(25, 45) { SRID = 4326 };
        if (typeof(Geometry).IsAssignableFrom(type))
        {
            return new GeometryFactory(new PrecisionModel(), 4326).CreatePoint(new Coordinate(25, 45));
        }

        if (type == typeof(JsonDocument)) return JsonDocument.Parse("{}");
        return type.IsValueType ? Activator.CreateInstance(type)! : throw new NotSupportedException(
            $"No arbitrary value for {property.DeclaringType.DisplayName()}.{property.Name} ({type.Name})");
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        admin?.Dispose();
        factory.Dispose();
        GC.SuppressFinalize(this);
    }
}
