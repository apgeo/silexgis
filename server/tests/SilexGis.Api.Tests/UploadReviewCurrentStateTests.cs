// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The "now" side of a held edit of a feature row, and the names of the existing features held
/// lines name, as the batch view draws them on the server: whole for a reviewer, the current
/// position included, whatever the reviewer may read elsewhere; by the uploader's own rights for an
/// uploader reading their own batch.
/// </summary>
/// <remarks>Every cave, name and position here is invented.</remarks>
public sealed class UploadReviewCurrentStateTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private HttpClient uploader = null!;
    private HttpClient admin = null!;
    private Guid uploaderId;
    private Guid adminId;
    private Guid set;
    private string marker = null!;

    public UploadReviewCurrentStateTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async ValueTask InitializeAsync()
    {
        marker = Guid.NewGuid().ToString("N")[..8];
        uploaderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"rcs-up-{marker}@t.local");
        uploader = await AuthHelper.BearerClientAsync(factory, $"rcs-up-{marker}@t.local");
        adminId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"rcs-adm-{marker}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"rcs-adm-{marker}@t.local");

        var response = await uploader.PostAsJsonAsync("/api/v1/sync/sets/", new
        {
            name = $"Phone {marker}",
            uploadVisibility = "private",
            rootFeatureIds = Array.Empty<Guid>(),
            settings = new { },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        set = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task A_reviewer_who_may_not_place_a_protected_row_is_shown_where_it_is_now_and_its_names_and_outside_review_still_is_not()
    {
        var (caveId, entranceId, batchId, reviewId) = await HeldEntranceMoveAsync("Pestera Paza", "Intrarea veche", protect: true);

        // A reviewer who may read the rows but not place them.
        var readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"rcs-pos-{marker}@t.local");
        using var reader = await AuthHelper.BearerClientAsync(factory, $"rcs-pos-{marker}@t.local");
        await GrantAsync(readerId, AccessEffect.Allow, AccessDomain.UploadReview);
        await GrantAsync(readerId, AccessEffect.Allow, AccessDomain.Features);

        // Fixture proof: outside review the reader is shown the entrance snapped.
        await ShouldBeSnappedAsync(reader, entranceId, 22.5111);

        var detail = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/review/batches/{batchId}");
        var fields = FieldsOf(LineOf(detail, reviewId));
        fields["name"].GetProperty("current").GetString().ShouldBe("Intrarea veche");
        fields["name"].GetProperty("proposed").GetString().ShouldBe("Intrarea noua");
        fields["name"].GetProperty("differs").GetBoolean().ShouldBeTrue();

        var position = fields["position"];
        position.GetProperty("currentWithheld").GetBoolean().ShouldBeFalse();
        position.GetProperty("differs").GetBoolean().ShouldBeTrue();
        var now = position.GetProperty("current");
        Coordinates(now).ShouldBe([22.5111, 45.6111]);
        now.GetProperty("altitude").GetDouble().ShouldBe(640.0);
        now.GetProperty("isMain").GetBoolean().ShouldBeTrue();
        Coordinates(position.GetProperty("proposed")).ShouldBe([22.6222, 45.6333]);

        var names = NamesOf(detail);
        names[caveId].ShouldBe(("Pestera Paza", "cave"));
        names[entranceId].ShouldBe(("Intrarea veche", "caveEntrance"));

        // Seeing it in review lifts nothing anywhere else.
        await ShouldBeSnappedAsync(reader, entranceId, 22.5111);
    }

    [Fact]
    public async Task A_reviewer_who_may_not_read_a_private_row_is_shown_it_now_and_named_and_outside_review_still_may_not_read_it()
    {
        var (caveId, entranceId, batchId, reviewId) = await HeldEntranceMoveAsync("Pestera Inchisa", "Intrarea privata", protect: false);

        // A reviewer who may read nothing but the queue; the rows are private to the uploader.
        var readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"rcs-priv-{marker}@t.local");
        using var reader = await AuthHelper.BearerClientAsync(factory, $"rcs-priv-{marker}@t.local");
        await GrantAsync(readerId, AccessEffect.Allow, AccessDomain.UploadReview);
        (await reader.GetAsync($"/api/v1/features/{entranceId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await reader.GetAsync($"/api/v1/features/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var detail = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/review/batches/{batchId}");
        var fields = FieldsOf(LineOf(detail, reviewId));
        fields["name"].GetProperty("current").GetString().ShouldBe("Intrarea privata");
        Coordinates(fields["position"].GetProperty("current")).ShouldBe([22.5111, 45.6111]);
        var names = NamesOf(detail);
        names[caveId].ShouldBe(("Pestera Inchisa", "cave"));
        names[entranceId].ShouldBe(("Intrarea privata", "caveEntrance"));

        (await reader.GetAsync($"/api/v1/features/{entranceId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await reader.GetAsync($"/api/v1/features/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // An account without the review right is still refused the queue and not shown the batch.
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"rcs-out-{marker}@t.local");
        using var outsider = await AuthHelper.BearerClientAsync(factory, $"rcs-out-{marker}@t.local");
        (await outsider.GetAsync("/api/v1/review/batches")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await outsider.GetAsync($"/api/v1/review/batches/{batchId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_uploader_reading_their_own_batch_is_shown_neither_the_now_side_nor_the_name_of_a_row_they_may_no_longer_read()
    {
        var (caveId, entranceId, batchId, reviewId) = await HeldEntranceMoveAsync("Pestera Luata", "Intrarea luata", protect: false);

        // Fixture proof: before the rows are taken away the uploader is shown their own edit whole.
        var before = await uploader.GetFromJsonAsync<JsonElement>($"/api/v1/review/batches/{batchId}");
        FieldsOf(LineOf(before, reviewId))["name"].GetProperty("current").GetString().ShouldBe("Intrarea luata");
        NamesOf(before).Keys.ShouldBe([caveId, entranceId], ignoreOrder: true);

        // The rows go to somebody else, renamed, and the uploader may read neither any more.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.Features.Where(f => f.Id == caveId || f.Id == entranceId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(f => f.OwnerUserId, adminId)
                    .SetProperty(f => f.Name, f => f.Name + " (luata)"));
        }

        await GrantAsync(uploaderId, AccessEffect.Deny, AccessDomain.Features, caveId);
        await GrantAsync(uploaderId, AccessEffect.Deny, AccessDomain.Features, entranceId);
        (await uploader.GetAsync($"/api/v1/features/{entranceId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await uploader.GetAsync($"/api/v1/features/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var raw = await uploader.GetStringAsync($"/api/v1/review/batches/{batchId}");
        raw.ShouldNotContain("(luata)");
        raw.ShouldNotContain("22.5111");
        raw.ShouldNotContain(caveId.ToString());
        var detail = JsonDocument.Parse(raw).RootElement;
        var line = LineOf(detail, reviewId);
        line.GetProperty("proposal").ValueKind.ShouldBe(JsonValueKind.Null);
        (line.TryGetProperty("fields", out var fields) ? fields.ValueKind : JsonValueKind.Null).ShouldBe(JsonValueKind.Null);
        detail.GetProperty("features").GetArrayLength().ShouldBe(0);

        // A reviewer is still shown it, under its new name.
        var reviewed = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/review/batches/{batchId}");
        NamesOf(reviewed)[entranceId].Name.ShouldBe("Intrarea luata (luata)");
    }

    [Fact]
    public async Task An_uploader_who_may_read_but_not_place_a_row_is_shown_its_other_fields_and_its_position_as_withheld()
    {
        var (caveId, entranceId, batchId, reviewId) = await HeldEntranceMoveAsync("Pestera Cedata", "Intrarea cedata", protect: true);

        // The rows go to somebody else, still readable by all: the protection now keeps their
        // position from the uploader, who is no longer their owner.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.Features.Where(f => f.Id == caveId || f.Id == entranceId)
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.OwnerUserId, adminId).SetProperty(f => f.Visibility, Visibility.Public));
        }

        await ShouldBeSnappedAsync(uploader, entranceId, 22.5111);

        var raw = await uploader.GetStringAsync($"/api/v1/review/batches/{batchId}");
        raw.ShouldNotContain("22.5111");
        raw.ShouldNotContain("45.6111");
        raw.ShouldNotContain("22.6222");
        var detail = JsonDocument.Parse(raw).RootElement;
        var fields = FieldsOf(LineOf(detail, reviewId));
        fields["name"].GetProperty("current").GetString().ShouldBe("Intrarea cedata");
        var position = fields["position"];
        position.GetProperty("currentWithheld").GetBoolean().ShouldBeTrue();
        position.GetProperty("current").ValueKind.ShouldBe(JsonValueKind.Null);
        position.GetProperty("proposed").ValueKind.ShouldBe(JsonValueKind.Null);
        NamesOf(detail)[entranceId].Name.ShouldBe("Intrarea cedata");
    }

    /// <summary>
    /// A cave and its main entrance the uploader made unheld, then — held — an edit of the
    /// entrance renaming it and moving it from its true point.
    /// </summary>
    private async Task<(Guid Cave, Guid Entrance, Guid Batch, Guid ReviewId)> HeldEntranceMoveAsync(
        string caveName, string entranceName, bool protect)
    {
        var caveId = Guid.CreateVersion7();
        var entranceId = Guid.CreateVersion7();
        await UploadAsync(held: false, Cave(caveId, caveName), Entrance(entranceId, caveId, null, [22.5111, 45.6111], entranceName));
        DateTimeOffset revision;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            if (protect)
            {
                await db.Features.Where(f => f.Id == caveId || f.Id == entranceId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(f => f.LocationProtected, f => f.Id == caveId)
                        .SetProperty(f => f.IsProtectedEffective, true));
            }

            revision = (await db.Features.AsNoTracking().SingleAsync(f => f.Id == entranceId)).UpdatedAt;
        }

        await MarkAsync(true);
        var held = await UploadAsync(held: true, Entrance(entranceId, caveId, revision, [22.6222, 45.6333], "Intrarea noua"));
        var row = held.GetProperty("rows")[0];
        row.GetProperty("status").GetString().ShouldBe("held");
        return (caveId, entranceId, held.GetProperty("importBatchId").GetGuid(), row.GetProperty("reviewId").GetGuid());
    }

    private static JsonElement LineOf(JsonElement detail, Guid reviewId) =>
        detail.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("reviewId").GetGuid() == reviewId);

    private static Dictionary<string, JsonElement> FieldsOf(JsonElement line) =>
        line.GetProperty("fields").EnumerateArray().ToDictionary(f => f.GetProperty("field").GetString()!);

    private static Dictionary<Guid, (string? Name, string? Kind)> NamesOf(JsonElement detail) =>
        detail.GetProperty("features").EnumerateArray().ToDictionary(
            f => f.GetProperty("id").GetGuid(),
            f => (f.GetProperty("name").GetString(), f.GetProperty("kind").GetString()));

    private static double[] Coordinates(JsonElement position) =>
        [.. position.GetProperty("geometry").GetProperty("coordinates").EnumerateArray().Select(c => c.GetDouble())];

    /// <summary>Read through the shared feature route, the row is placed approximately, not at its true point.</summary>
    private static async Task ShouldBeSnappedAsync(HttpClient client, Guid featureId, double trueLongitude)
    {
        var envelope = await client.GetFromJsonAsync<JsonElement>($"/api/v1/features/{featureId}");
        var feature = envelope.GetProperty("feature");
        feature.GetProperty("approximateLocation").GetBoolean().ShouldBeTrue();
        if (feature.TryGetProperty("geometry", out var geometry) && geometry.ValueKind == JsonValueKind.Object)
        {
            Math.Abs(geometry.GetProperty("coordinates")[0].GetDouble() - trueLongitude).ShouldBeGreaterThan(1e-9);
        }
    }

    private async Task GrantAsync(Guid userId, AccessEffect effect, AccessDomain domain, Guid? only = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Effect = effect,
            Domain = domain,
            Actions = AccessAction.Read,
            ScopeKind = only is null ? AccessScopeKind.All : AccessScopeKind.Object,
            ScopeFeatureId = only,
        });
        await db.SaveChangesAsync();
    }

    private async Task MarkAsync(bool needsReview)
    {
        var response = await admin.PutAsJsonAsync(
            $"/api/v1/users/{uploaderId}/upload-review", new { uploadsNeedReview = needsReview });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task<JsonElement> UploadAsync(bool held, params object[] rows)
    {
        var response = await uploader.PostAsJsonAsync($"/api/v1/sync/sets/{set}/upload", new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            clientFeatures = held ? new[] { "held" } : Array.Empty<string>(),
            rows,
        });
        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, raw);
        return JsonDocument.Parse(raw).RootElement.Clone();
    }

    private static object Cave(Guid id, string name) => new
    {
        id,
        kind = "cave",
        baseRevision = (DateTimeOffset?)null,
        deleted = false,
        name,
        caveTypeCode = "cave",
        isMain = false,
    };

    private static object Entrance(Guid id, Guid caveId, DateTimeOffset? revision, double[] at, string name) => new
    {
        id,
        kind = "caveEntrance",
        parentId = caveId,
        baseRevision = revision,
        deleted = false,
        name,
        entranceTypeCode = "natural",
        isMain = true,
        geometry = new { type = "Point", coordinates = at },
        altitude = 640.0,
        positionQuality = "Unknown",
    };

    public void Dispose()
    {
        uploader?.Dispose();
        admin?.Dispose();
        factory.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
