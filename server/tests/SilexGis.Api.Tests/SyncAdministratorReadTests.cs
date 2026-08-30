// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using SilexGis.Api.Features.Sync;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Whether a full administrator may read a sync set belonging to somebody else. It is an
/// installation's decision and it is off until one makes it, so this class runs two hosts against
/// one database: the product exactly as it ships, and the same product with the setting turned on.
/// </summary>
/// <remarks>
/// <para>
/// The negative is asserted against a host given <b>no settings at all</b>, on purpose. A test that
/// injected <c>false</c> and then asserted a refusal would pass just as happily if the product
/// shipped the setting on, which is the failure this shape exists to prevent — what has to be
/// proved is the answer an operator who configured nothing receives.
/// </para>
/// <para>
/// The administrator here is a real one. The account is minted as an administrator and its
/// membership of the protected full-administrators group is then read back out of the database
/// before anything is asserted, because a "negative" proved with an account that was never
/// privileged would be green whatever the setting did.
/// </para>
/// <para>
/// The rest of the class is about how narrow the widening is. Reading is not writing, a listing is
/// not a read of one set, and neither the download nor the upload moves at all: a phone
/// authenticates as one account, so it must never be able to pull another caver's selection onto
/// itself however the account it holds is privileged.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class SyncAdministratorReadTests : IAsyncLifetime, IDisposable
{
    /// <summary>The product as an operator who has configured nothing receives it.</summary>
    private readonly SilexGisApiFactory shipped;

    /// <summary>The same product, on an installation that has allowed the administrator read.</summary>
    private readonly SilexGisApiFactory widened;

    private string suffix = null!;
    private string ownerEmail = null!;
    private string adminEmail = null!;
    private string outsiderEmail = null!;
    private Guid adminId;

    private HttpClient owner = null!;
    private HttpClient shippedAdmin = null!;
    private HttpClient widenedAdmin = null!;
    private HttpClient widenedOutsider = null!;

    private Guid set;

    public SyncAdministratorReadTests(PostgresFixture postgres)
    {
        shipped = new SilexGisApiFactory(postgres.ConnectionString);
        widened = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?> { ["Sync:AllowAdministratorRead"] = "true" });
    }

    public async Task InitializeAsync()
    {
        suffix = Guid.NewGuid().ToString("N")[..8];
        ownerEmail = $"sar-own-{suffix}@t.local";
        adminEmail = $"sar-adm-{suffix}@t.local";
        outsiderEmail = $"sar-out-{suffix}@t.local";

        _ = await AuthHelper.CreateUserAsync(shipped, GlobalRoles.Editor, ownerEmail);
        adminId = await AuthHelper.CreateUserAsync(shipped, GlobalRoles.Admin, adminEmail);

        // A Viewer, deliberately: the seeded Editors group reads past visibility across the whole
        // feature domain, so an Editor outsider would prove less than it looks like it proves.
        _ = await AuthHelper.CreateUserAsync(shipped, GlobalRoles.Viewer, outsiderEmail);

        owner = await AuthHelper.BearerClientAsync(shipped, ownerEmail);
        shippedAdmin = await AuthHelper.BearerClientAsync(shipped, adminEmail);
        widenedAdmin = await AuthHelper.BearerClientAsync(widened, adminEmail);
        widenedOutsider = await AuthHelper.BearerClientAsync(widened, outsiderEmail);

        set = await CreateSetAsync(owner, "Owner's phone");
    }

    /// <summary>
    /// The shipped answer, from a host that was configured with nothing: an administrator reading
    /// somebody else's set is told it does not exist, exactly as anybody else is, and the listing
    /// hands back only their own sets so it cannot be used to count anybody's.
    /// </summary>
    [Fact]
    public async Task An_installation_that_configures_nothing_keeps_a_set_from_its_administrators()
    {
        // The setting the host is actually running on, not the one this test would like it to
        // have: what is being asserted is the product's own default.
        shipped.Services.GetRequiredService<IOptions<SyncOptions>>()
            .Value.AllowAdministratorRead.ShouldBeFalse();

        // And the account really is an administrator, so the refusal below is the setting's doing
        // rather than an unprivileged account's.
        await AssertIsFullAdministratorAsync();

        var refused = await shippedAdmin.GetAsync($"/api/v1/sync/sets/{set}");
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await refused.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("sync.set_not_found");

        // The positive, in the same test against the same host: an administrator's own set is
        // read and listed, so what failed above was the ownership rule and not the route.
        var mine = await CreateSetAsync(shippedAdmin, $"Admin's own phone {suffix}");
        (await shippedAdmin.GetAsync($"/api/v1/sync/sets/{mine}")).StatusCode
            .ShouldBe(HttpStatusCode.OK);

        var listed = await shippedAdmin.GetFromJsonAsync<JsonElement>("/api/v1/sync/sets/");
        var ids = listed.EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        ids.ShouldContain(mine);
        ids.ShouldNotContain(set);
    }

    /// <summary>
    /// With the setting on, the administrator reads the set — and only an administrator does. The
    /// listing does not widen with it: an administrator who cannot name a set's identifier learns
    /// nothing about it, which is what keeps the listing from becoming a census of the members.
    /// </summary>
    [Fact]
    public async Task An_installation_may_let_an_administrator_read_another_accounts_set()
    {
        await AssertIsFullAdministratorAsync();

        var read = await widenedAdmin.GetAsync($"/api/v1/sync/sets/{set}");
        read.StatusCode.ShouldBe(HttpStatusCode.OK, await read.Content.ReadAsStringAsync());
        var body = await read.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetGuid().ShouldBe(set);
        body.GetProperty("name").GetString().ShouldBe("Owner's phone");

        // An ordinary account is not carried along by the setting: it is the administrator the
        // installation allowed, not everybody.
        var outsider = await widenedOutsider.GetAsync($"/api/v1/sync/sets/{set}");
        outsider.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outsider.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("sync.set_not_found");

        // And the listing is still the caller's own, so a set has to be named to be read.
        var listed = await widenedAdmin.GetFromJsonAsync<JsonElement>("/api/v1/sync/sets/");
        listed.EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ShouldNotContain(set);
    }

    /// <summary>
    /// Reading is not writing. The same administrator, on the same host, with the setting on:
    /// the set comes back on a read and does not exist on a replacement or a deletion, because
    /// what an installation may allow is somebody looking at a selection, never editing one.
    /// </summary>
    [Fact]
    public async Task An_administrator_allowed_to_read_a_set_still_cannot_replace_or_delete_it()
    {
        var readable = await widenedAdmin.GetAsync($"/api/v1/sync/sets/{set}");
        readable.StatusCode.ShouldBe(HttpStatusCode.OK);
        var revision = (await readable.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("revision").GetInt64();

        // Sent with the revision the administrator has just read, so the refusal cannot be
        // mistaken for a stale-write conflict.
        var replaced = await widenedAdmin.PutAsJsonAsync(
            $"/api/v1/sync/sets/{set}", Body("Renamed by an administrator", baseRevision: revision));
        replaced.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await replaced.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("sync.set_not_found");

        var deleted = await widenedAdmin.DeleteAsync($"/api/v1/sync/sets/{set}");
        deleted.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await deleted.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("sync.set_not_found");

        // Neither of them touched it: the owner's set is still there, still named what the owner
        // named it, and still at the revision the owner left it at.
        var after = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/sync/sets/{set}");
        after.GetProperty("name").GetString().ShouldBe("Owner's phone");
        after.GetProperty("revision").GetInt64().ShouldBe(revision);
    }

    /// <summary>
    /// The channel a phone talks through does not widen with the setting, and that is the point of
    /// it being a separate resolution: a device authenticates as exactly one account, so another
    /// caver's selection must never be pullable onto it however privileged the account it holds.
    /// The same administrator, on the same host, reads the set through its own address and is told
    /// it does not exist by both transfer routes.
    /// </summary>
    [Fact]
    public async Task The_routes_a_phone_transfers_through_stay_the_owners_however_the_setting_stands()
    {
        (await widenedAdmin.GetAsync($"/api/v1/sync/sets/{set}")).StatusCode
            .ShouldBe(HttpStatusCode.OK);

        var download = await widenedAdmin.GetAsync($"/api/v1/sync/sets/{set}/download");
        download.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await download.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("sync.set_not_found");

        var upload = await widenedAdmin.PostAsJsonAsync($"/api/v1/sync/sets/{set}/upload", new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = SyncEndpoints.ContractVersion,
            rows = new[] { Cave($"Into somebody else's set {suffix}") },
        });
        upload.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await upload.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("sync.set_not_found");

        // The positive against the same two routes on the same host: the administrator's own set
        // transfers, so what was refused above was the ownership of the set rather than the
        // administrator's credential or the routes themselves.
        var mine = await CreateSetAsync(widenedAdmin, $"Admin's transfer phone {suffix}");
        (await widenedAdmin.GetAsync($"/api/v1/sync/sets/{mine}/download")).StatusCode
            .ShouldBe(HttpStatusCode.OK);
        var mineUpload = await widenedAdmin.PostAsJsonAsync($"/api/v1/sync/sets/{mine}/upload", new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = SyncEndpoints.ContractVersion,
            rows = new[] { Cave($"Into my own set {suffix}") },
        });
        mineUpload.StatusCode.ShouldBe(HttpStatusCode.OK, await mineUpload.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Reads the administrator's group membership straight out of the database. Without this the
    /// negative above could be green because the account was never an administrator at all.
    /// </summary>
    private async Task AssertIsFullAdministratorAsync()
    {
        await using var scope = shipped.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var fullAdminsId = await db.PermissionGroups.AsNoTracking()
            .Where(g => g.Slug == SeededPermissionGroups.FullAdministratorsSlug)
            .Select(g => g.Id)
            .SingleAsync();
        (await db.PermissionGroupMembers.AsNoTracking()
            .AnyAsync(m => m.PermissionGroupId == fullAdminsId
                && m.MemberKind == AccessSubjectKind.User
                && m.MemberId == adminId))
            .ShouldBeTrue();
    }

    /// <summary>
    /// One row an upload may legitimately carry. A batch with no rows at all is refused by
    /// validation before the set is ever resolved, so an empty one would prove nothing about who
    /// owns what.
    /// </summary>
    private static object Cave(string name) => new
    {
        id = Guid.CreateVersion7(),
        kind = "cave",
        deleted = false,
        name,
        caveTypeCode = "cave",
        isMain = false,
    };

    private static object Body(string name, long? baseRevision = null) => new
    {
        name,
        cavingGroupId = (Guid?)null,
        uploadVisibility = "private",
        rootFeatureIds = Array.Empty<Guid>(),
        settings = new { },
        baseRevision,
    };

    private static async Task<Guid> CreateSetAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/v1/sync/sets/", Body(name));
        response.StatusCode.ShouldBe(
            HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    public void Dispose()
    {
        owner?.Dispose();
        shippedAdmin?.Dispose();
        widenedAdmin?.Dispose();
        widenedOutsider?.Dispose();
        shipped.Dispose();
        widened.Dispose();
    }

    public Task DisposeAsync() => Task.CompletedTask;
}
