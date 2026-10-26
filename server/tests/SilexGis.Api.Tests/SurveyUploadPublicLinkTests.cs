// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Surveys;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Api.Tests;

/// <summary>
/// The public link to a survey upload's results: made at completion for an unprotected cave, and
/// for a protected one only when the upload asked for it acknowledging the protection; asked for or
/// refused by the upload itself over the account's preference; opening for anybody or only for the
/// installation's signed-in accounts; shown again to every reader of the upload; withdrawn and made
/// again from the web or the device's key; and the page it opens — which carries the results and
/// nothing that places the cave or names its party, and which answers as an invented address the
/// moment an ordinary link's cave is protected or the link withdrawn. Also the notice the uploader
/// gets when the run ends.
/// </summary>
/// <remarks>
/// The survey is genuine TopoDroid output of an invented survey, compiled by a stand-in compiler
/// that writes invented outputs. Nothing here is a real cave.
/// </remarks>
public sealed class SurveyUploadPublicLinkTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string Uploads = "/api/v1/survey-uploads";
    private const string Public = "/api/v1/public/survey-uploads";

    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient owner = null!;
    private Guid ownerId;

    public SurveyUploadPublicLinkTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-supub-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Files:Root"] = filesRoot,
                ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            },
            services =>
            {
                JobWorkers.RemoveFrom(services);
                services.Replace(ServiceDescriptor.Singleton<ISurveyCompiler>(new FakeSurveyCompiler()));
            });
    }

    public async ValueTask InitializeAsync()
    {
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"supub-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"supub-{suffix}@t.local");
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        owner?.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }

    [Fact]
    public async Task An_unprotected_caves_upload_gets_a_link_whose_page_carries_the_results_and_nothing_that_places_the_cave()
    {
        var (id, caveId) = await SendCompiledAsync($"Publicata {suffix}");
        var status = await owner.GetFromJsonAsync<JsonElement>($"{Uploads}/{id}");
        status.GetProperty("publicLinkRefusal").ValueKind.ShouldBe(JsonValueKind.Null);
        var url = status.GetProperty("publicLink").GetProperty("url").GetString()!;
        url.ShouldStartWith("/p/survey/");
        var token = url["/p/survey/".Length..];

        using var anonymous = factory.CreateClient();
        var response = await anonymous.GetAsync($"{Public}/{token}");
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        var page = JsonDocument.Parse(text).RootElement;

        page.GetProperty("survey").GetProperty("title").GetString().ShouldBe($"Publicata {suffix}");
        page.GetProperty("cave").GetProperty("name").GetString().ShouldNotBeNullOrEmpty();
        page.GetProperty("cave").EnumerateObject().Select(p => p.Name).ShouldBe(["name"]);
        page.GetProperty("status").GetString().ShouldBe("succeeded");
        page.GetProperty("stages").EnumerateArray().Select(s => s.GetProperty("state").GetString()).ShouldAllBe(s => s == "done");
        var kinds = page.GetProperty("artefacts").EnumerateArray().Select(a => a.GetProperty("kind").GetString()).ToList();
        kinds.ShouldContain("lox");
        kinds.ShouldContain("plan-pdf");
        kinds.ShouldNotContain(SurveyUploadArtefacts.LogKind);

        // Nothing that names the cave or the upload, the party, the sent files or the compiler's
        // words: the members are the envelope's own, and no identifier of either appears anywhere.
        page.EnumerateObject().Select(p => p.Name).ShouldBe(
            ["survey", "cave", "version", "status", "stage", "stages", "progress", "message", "error", "artefacts", "createdAt", "finishedAt"]);
        text.ShouldNotContain(caveId.ToString(), Case.Insensitive);
        text.ShouldNotContain(id.ToString(), Case.Insensitive);
        text.ShouldNotContain("pestera_inventata.zip");
        text.ShouldNotContain("pestera_inventata.th\"");
        foreach (var member in new[] { "logTail", "team", "files", "latitude", "longitude", "coordinates", "geom", "fix" })
        {
            text.ShouldNotContain($"\"{member}\"", Case.Sensitive, member);
        }

        // Each artefact downloads, without an account, from the address the page handed out.
        var lox = page.GetProperty("artefacts").EnumerateArray().First(a => a.GetProperty("kind").GetString() == "lox");
        (await anonymous.GetAsync(lox.GetProperty("contentUrl").GetString())).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_model_deleted_on_the_web_drops_off_the_public_page_and_the_status_while_the_drawings_stay()
    {
        var (id, _) = await SendCompiledAsync($"Stearsa {suffix}");
        var status = await owner.GetFromJsonAsync<JsonElement>($"{Uploads}/{id}");
        var token = await TokenAsync(id);
        var loxModel = status.GetProperty("artefacts").EnumerateArray()
            .Single(a => a.GetProperty("kind").GetString() == "lox").GetProperty("surveyModelId").GetGuid();

        (await owner.DeleteAsync($"/api/v1/survey-models/{loxModel}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var anonymous = factory.CreateClient();
        var page = await anonymous.GetFromJsonAsync<JsonElement>($"{Public}/{token}");
        var kinds = page.GetProperty("artefacts").EnumerateArray().Select(a => a.GetProperty("kind").GetString()).ToList();
        kinds.ShouldNotContain("lox");
        kinds.ShouldContain("plan-pdf");
        kinds.ShouldContain("3d");

        var after = (await owner.GetFromJsonAsync<JsonElement>($"{Uploads}/{id}"))
            .GetProperty("artefacts").EnumerateArray().Select(a => a.GetProperty("kind").GetString()).ToList();
        after.ShouldNotContain("lox");
        after.ShouldContain("plan-pdf");
    }

    [Fact]
    public async Task A_protected_cave_gets_no_link_and_says_why_while_an_unprotected_one_beside_it_gets_one()
    {
        var (open, _) = await SendCompiledAsync($"Deschisa {suffix}");
        var (shut, _) = await SendCompiledAsync($"Protejata {suffix}", newCave: new { locationProtected = true });

        var opened = await owner.GetFromJsonAsync<JsonElement>($"{Uploads}/{open}");
        opened.GetProperty("publicLink").GetProperty("url").GetString().ShouldStartWith("/p/survey/");

        var refused = await owner.GetFromJsonAsync<JsonElement>($"{Uploads}/{shut}");
        refused.GetProperty("publicLink").ValueKind.ShouldBe(JsonValueKind.Null);
        refused.GetProperty("publicLinkRefusal").GetString().ShouldBe(SurveyUploadPublicLinks.ProtectedCave);
        (await LinkCountAsync(shut)).ShouldBe(0);

        // Nor can one be made by hand without the acknowledgement — an audience alone is not one.
        var minted = await owner.PostAsync($"{Uploads}/{shut}/public-link", null);
        minted.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(minted)).ShouldBe("survey_upload.public_link_refused_protected");
        var withAudience = await owner.PostAsJsonAsync($"{Uploads}/{shut}/public-link", new { audience = "organization", allowProtected = false });
        withAudience.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await LinkCountAsync(shut)).ShouldBe(0);

        // With it, the link is made, says so, and records who acknowledged and when.
        var acknowledged = await owner.PostAsJsonAsync($"{Uploads}/{shut}/public-link", new { allowProtected = true });
        acknowledged.StatusCode.ShouldBe(HttpStatusCode.Created, await acknowledged.Content.ReadAsStringAsync());
        var made = await acknowledged.Content.ReadFromJsonAsync<JsonElement>();
        made.GetProperty("protectedAcknowledged").GetBoolean().ShouldBeTrue();
        var row = await LiveLinkAsync(shut);
        row.ProtectedAcknowledgedBy.ShouldBe(ownerId);
        row.ProtectedAcknowledgedAt.ShouldNotBeNull();
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync($"{Public}/{made.GetProperty("url").GetString()!["/p/survey/".Length..]}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The unprotected cave's link carries no acknowledgement: there was nothing to acknowledge.
        (await LiveLinkAsync(open)).ProtectedAcknowledgedBy.ShouldBeNull();
    }

    [Fact]
    public async Task An_upload_that_acknowledged_its_caves_protection_gets_a_link_that_keeps_opening_while_an_ordinary_one_closes()
    {
        // Both caves protected from the moment they exist; only the first upload acknowledged it.
        var (asked, askedCave) = await SendCompiledAsync(
            $"Protejata cu acord {suffix}", newCave: new { locationProtected = true }, publicLink: new { allowProtected = true });
        var (ordinary, ordinaryCave) = await SendCompiledAsync($"Obisnuita {suffix}");

        var status = await owner.GetFromJsonAsync<JsonElement>($"{Uploads}/{asked}");
        status.GetProperty("publicLinkRefusal").ValueKind.ShouldBe(JsonValueKind.Null);
        status.GetProperty("publicLink").GetProperty("protectedAcknowledged").GetBoolean().ShouldBeTrue();
        var row = await LiveLinkAsync(asked);
        row.ProtectedAcknowledgedBy.ShouldBe(ownerId);
        row.ProtectedAcknowledgedAt.ShouldNotBeNull();

        var askedToken = await TokenAsync(asked);
        var ordinaryToken = await TokenAsync(ordinary);
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync($"{Public}/{askedToken}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await anonymous.GetAsync($"{Public}/{ordinaryToken}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Protecting the ordinary link's cave closes it at once; the acknowledged one stays open
        // while its cave stays protected, re-protected or not.
        await SetProtectedAsync(ordinaryCave, true);
        await SetProtectedAsync(askedCave, true);
        (await anonymous.GetAsync($"{Public}/{ordinaryToken}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await anonymous.GetAsync($"{Public}/{askedToken}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Withdrawing it closes it like any other link.
        (await owner.DeleteAsync($"{Uploads}/{asked}/public-link")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await anonymous.GetAsync($"{Public}/{askedToken}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_acknowledgement_for_an_unprotected_cave_is_not_recorded_and_the_link_then_closes_when_the_cave_is_protected()
    {
        var (id, caveId) = await SendCompiledAsync($"Acord inutil {suffix}", publicLink: new { allowProtected = true });
        (await LiveLinkAsync(id)).ProtectedAcknowledgedBy.ShouldBeNull();
        var token = await TokenAsync(id);
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync($"{Public}/{token}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        await SetProtectedAsync(caveId, true);
        (await anonymous.GetAsync($"{Public}/{token}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_link_made_for_the_organization_asks_an_anonymous_caller_or_a_device_key_to_sign_in_and_opens_for_any_signed_in_account()
    {
        // A cave the upload makes private to its uploader: a viewer has no right on it at all.
        var (id, _) = await SendCompiledAsync(
            $"Organizatie {suffix}", newCave: new { visibility = "private" }, publicLink: new { audience = "organization" });
        var status = await owner.GetFromJsonAsync<JsonElement>($"{Uploads}/{id}");
        status.GetProperty("publicLink").GetProperty("audience").GetString().ShouldBe("organization");
        (await LiveLinkAsync(id)).Audience.ShouldBe(SurveyUploadLinkAudience.Organization);
        var token = await TokenAsync(id);

        using var anonymous = factory.CreateClient();
        var refused = await anonymous.GetAsync($"{Public}/{token}");
        refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CodeAsync(refused)).ShouldBe("survey_upload.public_link_sign_in_required");
        (await refused.Content.ReadAsStringAsync()).ShouldNotContain($"Organizatie {suffix}");

        // A key is not a signed-in account on this route, whatever account it belongs to.
        using var device = AuthHelper.AccessKeyClient(factory, await AuthHelper.CreateAccessKeyAsync(owner, "TopoDroid org phone"));
        var keyed = await device.GetAsync($"{Public}/{token}");
        keyed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CodeAsync(keyed)).ShouldBe("survey_upload.public_link_sign_in_required");

        // A signed-in account with no right on the cave: it cannot read the upload, yet the link
        // opens for it, because holding the address is what the link grants.
        var email = $"supub-org-{suffix}@t.local";
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, email);
        using var member = await AuthHelper.BearerClientAsync(factory, email);
        (await member.GetAsync($"{Uploads}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var page = await member.GetAsync($"{Public}/{token}");
        page.StatusCode.ShouldBe(HttpStatusCode.OK, await page.Content.ReadAsStringAsync());
        (await page.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("survey").GetProperty("title").GetString().ShouldBe($"Organizatie {suffix}");

        // The sign-in question is asked only of a link that opens: an invented or withdrawn one is
        // the one 404 for everybody.
        (await anonymous.GetAsync($"{Public}/{SurveyUploadPublicLinks.NewToken()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await owner.DeleteAsync($"{Uploads}/{id}/public-link")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await anonymous.GetAsync($"{Public}/{token}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await member.GetAsync($"{Public}/{token}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Made again by hand for anybody, it opens without an account.
        var remade = await owner.PostAsJsonAsync($"{Uploads}/{id}/public-link", new { audience = "public" });
        remade.StatusCode.ShouldBe(HttpStatusCode.Created);
        var second = (await remade.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("url").GetString()!["/p/survey/".Length..];
        (await anonymous.GetAsync($"{Public}/{second}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_accounts_default_audience_applies_when_the_upload_does_not_say_and_a_new_link_keeps_the_audience_it_replaces()
    {
        var email = $"supub-aud-{suffix}@t.local";
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, email);
        using var client = await AuthHelper.BearerClientAsync(factory, email);
        (await client.PutAsJsonAsync("/api/v1/me/survey-upload-preferences",
            new { alwaysOutputs = Array.Empty<string>(), publicLinks = true, defaultLinkAudience = "organization" }))
            .IsSuccessStatusCode.ShouldBeTrue();

        var (byDefault, _) = await SendCompiledAsync($"Implicit {suffix}", client: client);
        var (overridden, _) = await SendCompiledAsync($"Pentru toti {suffix}", client: client, publicLink: new { audience = "public" });
        (await LiveLinkAsync(byDefault)).Audience.ShouldBe(SurveyUploadLinkAudience.Organization);
        (await LiveLinkAsync(overridden)).Audience.ShouldBe(SurveyUploadLinkAudience.Public);

        // Made again with no audience named, the link keeps the one it had.
        (await client.PostAsync($"{Uploads}/{byDefault}/public-link", null)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await LiveLinkAsync(byDefault)).Audience.ShouldBe(SurveyUploadLinkAudience.Organization);
    }

    [Fact]
    public async Task The_upload_itself_may_refuse_a_link_the_preference_would_make_or_ask_for_one_it_would_not()
    {
        var (refused, _) = await SendCompiledAsync($"Nedorit {suffix}", publicLink: new { wanted = false });
        var status = await owner.GetFromJsonAsync<JsonElement>($"{Uploads}/{refused}");
        status.GetProperty("publicLink").ValueKind.ShouldBe(JsonValueKind.Null);
        status.GetProperty("publicLinkRefusal").GetString().ShouldBe(SurveyUploadPublicLinks.NotWanted);
        (await LinkCountAsync(refused)).ShouldBe(0);

        var email = $"supub-want-{suffix}@t.local";
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, email);
        using var client = await AuthHelper.BearerClientAsync(factory, email);
        (await client.PutAsJsonAsync("/api/v1/me/survey-upload-preferences", new { alwaysOutputs = Array.Empty<string>(), publicLinks = false }))
            .IsSuccessStatusCode.ShouldBeTrue();
        var (wanted, _) = await SendCompiledAsync($"Dorit {suffix}", client: client, publicLink: new { wanted = true });
        (await client.GetFromJsonAsync<JsonElement>($"{Uploads}/{wanted}"))
            .GetProperty("publicLink").GetProperty("url").GetString().ShouldStartWith("/p/survey/");

        // Asking for a link does not reach past a protected cave: only the acknowledgement does.
        var (shut, _) = await SendCompiledAsync($"Dorit protejat {suffix}", newCave: new { locationProtected = true }, publicLink: new { wanted = true });
        (await owner.GetFromJsonAsync<JsonElement>($"{Uploads}/{shut}"))
            .GetProperty("publicLinkRefusal").GetString().ShouldBe(SurveyUploadPublicLinks.ProtectedCave);
    }

    [Fact]
    public async Task An_audience_outside_the_two_is_refused_on_opening_and_on_making_a_link()
    {
        var opened = await owner.PostAsJsonAsync(Uploads, new
        {
            source = "topodroid",
            surveyTitle = $"Invalid {suffix}",
            files = new[] { new { name = "a.zip", size = 10L, role = "archive" } },
            publicLink = new { audience = 5 },
        });
        opened.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await opened.Content.ReadAsStringAsync());

        var (id, _) = await SendCompiledAsync($"Validata {suffix}");
        (await owner.PostAsJsonAsync($"{Uploads}/{id}/public-link", new { audience = 5 })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await owner.PostAsJsonAsync($"{Uploads}/{id}/public-link", new { audience = "everyone" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_cave_protected_after_its_link_was_made_answers_as_an_invented_token_at_once_and_opens_again_when_unprotected()
    {
        var (id, caveId) = await SendCompiledAsync($"Protejata dupa {suffix}");
        var token = await TokenAsync(id);
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync($"{Public}/{token}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        await SetProtectedAsync(caveId, true);
        var refused = await anonymous.GetAsync($"{Public}/{token}");
        var invented = await anonymous.GetAsync($"{Public}/{SurveyUploadPublicLinks.NewToken()}");
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        invented.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(refused)).ShouldBe(await CodeAsync(invented));

        // The status page shows no address that would not open, and says why.
        var status = await owner.GetFromJsonAsync<JsonElement>($"{Uploads}/{id}");
        status.GetProperty("publicLink").ValueKind.ShouldBe(JsonValueKind.Null);
        status.GetProperty("publicLinkRefusal").GetString().ShouldBe(SurveyUploadPublicLinks.ProtectedCave);

        // The page remembers no verdict of its own: lifting the protection opens the same link.
        await SetProtectedAsync(caveId, false);
        (await anonymous.GetAsync($"{Public}/{token}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_withdrawn_link_answers_as_an_invented_one_and_a_new_one_opens_in_its_place()
    {
        var (id, _) = await SendCompiledAsync($"Retrasa {suffix}");
        var first = await TokenAsync(id);
        using var anonymous = factory.CreateClient();

        (await owner.DeleteAsync($"{Uploads}/{id}/public-link")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var withdrawn = await anonymous.GetAsync($"{Public}/{first}");
        withdrawn.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(withdrawn)).ShouldBe(SurveyUploadEndpointsCodes.NotFound);
        var status = await owner.GetFromJsonAsync<JsonElement>($"{Uploads}/{id}");
        status.GetProperty("publicLink").ValueKind.ShouldBe(JsonValueKind.Null);
        status.GetProperty("publicLinkRefusal").GetString().ShouldBe(SurveyUploadPublicLinks.Revoked);

        var made = await owner.PostAsync($"{Uploads}/{id}/public-link", null);
        made.StatusCode.ShouldBe(HttpStatusCode.Created, await made.Content.ReadAsStringAsync());
        var second = (await made.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("url").GetString()!["/p/survey/".Length..];
        second.ShouldNotBe(first);
        (await anonymous.GetAsync($"{Public}/{second}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await anonymous.GetAsync($"{Public}/{first}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await TokenAsync(id)).ShouldBe(second);

        // Malformed and oversized tokens get the same answer.
        (await anonymous.GetAsync($"{Public}/{new string('A', 200)}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Every_reader_sees_the_address_and_only_a_writer_by_web_or_device_key_makes_or_withdraws_a_link()
    {
        // A cave every signed-in account may read, so a viewer reaches the upload and is refused
        // the act rather than the upload.
        var (id, caveId) = await SendCompiledAsync($"Vazuta {suffix}", newCave: new { visibility = "public" });
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"supub-v-{suffix}@t.local");
        using var viewer = await AuthHelper.BearerClientAsync(factory, $"supub-v-{suffix}@t.local");

        // The address goes to every reader of the upload — the status answer and the listing.
        var url = (await owner.GetFromJsonAsync<JsonElement>($"{Uploads}/{id}")).GetProperty("publicLink").GetProperty("url").GetString();
        url.ShouldStartWith("/p/survey/");
        (await viewer.GetFromJsonAsync<JsonElement>($"{Uploads}/{id}")).GetProperty("publicLink").GetProperty("url").GetString().ShouldBe(url);
        var listed = (await viewer.GetFromJsonAsync<JsonElement>($"{Uploads}?caveId={caveId}"))
            .GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == id);
        listed.GetProperty("publicLink").GetProperty("url").GetString().ShouldBe(url);
        listed.GetProperty("publicLink").GetProperty("audience").GetString().ShouldBe("public");

        (await viewer.PostAsync($"{Uploads}/{id}/public-link", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await viewer.DeleteAsync($"{Uploads}/{id}/public-link")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var anonymous = factory.CreateClient();
        (await anonymous.PostAsync($"{Uploads}/{id}/public-link", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.DeleteAsync($"{Uploads}/{id}/public-link")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // A viewer's device key can do no more than the viewer.
        using var viewerDevice = AuthHelper.AccessKeyClient(factory, await AuthHelper.CreateAccessKeyAsync(viewer, "viewer phone"));
        (await viewerDevice.PostAsync($"{Uploads}/{id}/public-link", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await viewerDevice.DeleteAsync($"{Uploads}/{id}/public-link")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // The writer's device key withdraws the link and makes a new one, as its result screen offers.
        var key = await AuthHelper.CreateAccessKeyAsync(owner, "TopoDroid test phone");
        using var device = AuthHelper.AccessKeyClient(factory, key);
        (await device.DeleteAsync($"{Uploads}/{id}/public-link")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await anonymous.GetAsync($"{Public}/{url!["/p/survey/".Length..]}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var keyed = await device.PostAsJsonAsync($"{Uploads}/{id}/public-link", new { audience = "organization" });
        keyed.StatusCode.ShouldBe(HttpStatusCode.Created, await keyed.Content.ReadAsStringAsync());
        (await keyed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("audience").GetString().ShouldBe("organization");
        (await LiveLinkAsync(id)).CreatedBy.ShouldBe(ownerId);

        (await owner.PostAsync($"{Uploads}/{id}/public-link", null)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task An_account_that_turned_links_off_gets_none_and_is_told_so()
    {
        var email = $"supub-off-{suffix}@t.local";
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, email);
        using var client = await AuthHelper.BearerClientAsync(factory, email);
        (await client.PutAsJsonAsync("/api/v1/me/survey-upload-preferences", new { alwaysOutputs = Array.Empty<string>(), publicLinks = false }))
            .IsSuccessStatusCode.ShouldBeTrue();

        var (id, _) = await SendCompiledAsync($"Fara link {suffix}", client: client);
        var status = await client.GetFromJsonAsync<JsonElement>($"{Uploads}/{id}");
        status.GetProperty("publicLink").ValueKind.ShouldBe(JsonValueKind.Null);
        status.GetProperty("publicLinkRefusal").GetString().ShouldBe(SurveyUploadPublicLinks.AccountOff);
        (await LinkCountAsync(id)).ShouldBe(0);
    }

    [Fact]
    public async Task The_uploader_is_told_the_run_ended_with_the_survey_and_cave_names_and_a_link_to_its_page_and_nothing_else()
    {
        var (id, caveId) = await SendCompiledAsync($"Anuntata {suffix}");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var surveyId = await db.SurveyUploads.Where(u => u.Id == id).Select(u => u.CaveSurveyId).SingleAsync();
        var notice = await db.Notifications.AsNoTracking()
            .Where(n => n.RecipientUserId == ownerId && n.TemplateKey == MessageTemplateCatalog.NotifySurveyUploadSucceeded && n.TargetId == caveId)
            .SingleAsync();
        notice.TargetKind.ShouldBe(NotificationTargetKind.Feature);
        notice.Category.ShouldBe(NotificationCategory.JobCompleted);

        var values = JsonDocument.Parse(notice.Placeholders).RootElement;
        values.EnumerateObject().Select(p => p.Name).Order().ShouldBe(["caveName", "surveyTitle", "url"]);
        values.GetProperty("surveyTitle").GetString().ShouldBe($"Anuntata {suffix}");
        values.GetProperty("url").GetString().ShouldBe(SurveyUploadLinks.StatusPage(caveId, surveyId, id));

        // One notice per run, and the generic job notice is not sent beside it.
        (await db.Notifications.CountAsync(n => n.RecipientUserId == ownerId && n.TargetId == caveId)).ShouldBe(1);
        (await db.Notifications.CountAsync(n => n.RecipientUserId == ownerId && n.TemplateKey == MessageTemplateCatalog.NotifyJobCompleted)).ShouldBe(0);
    }

    [Fact]
    public async Task An_uploader_who_lost_the_caves_exact_position_still_reads_how_their_upload_went_and_nothing_that_places_it()
    {
        // The cave is somebody else's — its owner always places it — and protected; the uploader
        // is given the cave, writing and its exact position. The owner makes the upload a link that
        // acknowledges the protection, so it stays live and every withheld member has something in
        // it while the position is the uploader's.
        var (_, caveId) = await SendCompiledAsync($"Pierduta {suffix}", newCave: new { visibility = "public", locationProtected = true });
        var email = $"supub-up-{suffix}@t.local";
        var uploaderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, email);
        using var uploader = await AuthHelper.BearerClientAsync(factory, email);
        var grant = await owner.PutAsJsonAsync($"/api/v1/objects/feature/{caveId}/access", new
        {
            entries = new[] { new { subjectKind = "user", subjectId = uploaderId, effect = "allow", actions = "read, write, viewExactLocation", scopeKind = "object" } },
        });
        grant.StatusCode.ShouldBe(HttpStatusCode.OK, await grant.Content.ReadAsStringAsync());
        var (id, sameCave) = await SendCompiledAsync($"Pierduta {suffix}", client: uploader, cave: new { id = caveId });
        sameCave.ShouldBe(caveId);
        var made = await owner.PostAsJsonAsync($"{Uploads}/{id}/public-link", new { allowProtected = true });
        made.StatusCode.ShouldBe(HttpStatusCode.Created, await made.Content.ReadAsStringAsync());

        var full = await uploader.GetFromJsonAsync<JsonElement>($"{Uploads}/{id}");
        full.GetProperty("status").GetString().ShouldBe("succeeded");
        full.GetProperty("publicLink").GetProperty("url").GetString().ShouldStartWith("/p/survey/");
        full.GetProperty("artefacts").EnumerateArray().ShouldContain(a => a.GetProperty("contentUrl").ValueKind == JsonValueKind.String);
        full.GetProperty("files").EnumerateArray().ShouldContain(f => f.GetProperty("contentUrl").ValueKind == JsonValueKind.String);
        full.GetProperty("logTail").ValueKind.ShouldBe(JsonValueKind.String);

        // Somebody else who reads the cave but may not place it is not shown the upload at all.
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"supub-lv-{suffix}@t.local");
        using var viewer = await AuthHelper.BearerClientAsync(factory, $"supub-lv-{suffix}@t.local");
        (await viewer.GetAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await viewer.GetAsync($"{Uploads}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The uploader loses the exact position: the run's state stays theirs, nothing that draws,
        // lists or links the survey does.
        await DenyAsync(uploaderId, caveId, AccessAction.ViewExactLocation);
        var own = await uploader.GetAsync($"{Uploads}/{id}");
        var text = await own.Content.ReadAsStringAsync();
        own.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        var withheld = JsonDocument.Parse(text).RootElement;
        withheld.GetProperty("status").GetString().ShouldBe("succeeded");
        withheld.GetProperty("stage").ValueKind.ShouldBe(full.GetProperty("stage").ValueKind);
        withheld.GetProperty("cave").GetProperty("id").GetGuid().ShouldBe(caveId);
        withheld.GetProperty("artefacts").GetArrayLength().ShouldBe(full.GetProperty("artefacts").GetArrayLength());
        withheld.GetProperty("artefacts").EnumerateArray().ShouldAllBe(a => a.GetProperty("contentUrl").ValueKind == JsonValueKind.Null);
        withheld.GetProperty("files").EnumerateArray().ShouldAllBe(f => f.GetProperty("contentUrl").ValueKind == JsonValueKind.Null);
        withheld.GetProperty("logTail").ValueKind.ShouldBe(JsonValueKind.Null);
        withheld.GetProperty("publicLink").ValueKind.ShouldBe(JsonValueKind.Null);

        // Only the status: every act on the upload still answers as if it were not there.
        (await uploader.PostAsync($"{Uploads}/{id}/rerun", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await uploader.PostAsync($"{Uploads}/{id}/public-link", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await uploader.DeleteAsync($"{Uploads}/{id}/public-link")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await uploader.GetFromJsonAsync<JsonElement>($"{Uploads}?caveId={caveId}")).GetProperty("total").GetInt32().ShouldBe(0);

        // Losing the cave itself loses the status too.
        await DenyAsync(uploaderId, caveId, AccessAction.Read);
        (await uploader.GetAsync($"{Uploads}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---- helpers ----

    /// <summary>Error codes this suite expects, named once.</summary>
    private static class SurveyUploadEndpointsCodes
    {
        public const string NotFound = "survey_upload.public_link_not_found";
    }

    /// <summary>Sends a compiled survey of a cave the upload makes, runs it, and answers the upload and its cave.</summary>
    private async Task<(Guid Id, Guid CaveId)> SendCompiledAsync(
        string title, object? newCave = null, HttpClient? client = null, object? publicLink = null, object? cave = null)
    {
        client ??= owner;
        var files = new (string Name, byte[] Bytes)[]
        {
            ("pestera_inventata.zip", Fixture("pestera_inventata.zip")),
            ("pestera_inventata.th", Fixture("pestera_inventata.th")),
            ("pestera_inventata-1p.th2", Fixture("pestera_inventata-1p.th2")),
            ("pestera_inventata-1s.th2", Fixture("pestera_inventata-1s.th2")),
        };
        var opened = await client.PostAsJsonAsync(Uploads, new
        {
            source = "topodroid",
            client = new { name = "TopoDroid", version = "6.5.34" },
            cave,
            newCave,
            surveyTitle = title,
            surveyName = "pestera_inventata",
            publicLink,
            files = files.Select(f => new
            {
                name = f.Name,
                size = f.Bytes.LongLength,
                role = f.Name.EndsWith(".zip", StringComparison.Ordinal) ? "archive"
                    : f.Name.EndsWith(".th2", StringComparison.Ordinal) ? "therionSketch"
                    : "therionSource",
            }),
        });
        opened.StatusCode.ShouldBe(HttpStatusCode.Created, await opened.Content.ReadAsStringAsync());
        var body = await opened.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetGuid();
        var caveId = body.GetProperty("cave").GetProperty("id").GetGuid();

        foreach (var (name, bytes) in files)
        {
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            (await client.PutAsync($"{Uploads}/{id}/files/{Uri.EscapeDataString(name)}", content)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var complete = await client.PostAsync($"{Uploads}/{id}/complete", null);
        complete.StatusCode.ShouldBe(HttpStatusCode.Accepted, await complete.Content.ReadAsStringAsync());
        await RunPipelineAsync(id);
        return (id, caveId);
    }

    private async Task DenyAsync(Guid userId, Guid caveId, AccessAction actions)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Effect = AccessEffect.Deny,
            Domain = AccessDomain.Features,
            Actions = actions,
            ScopeKind = AccessScopeKind.Object,
            ScopeFeatureId = caveId,
        });
        await db.SaveChangesAsync();
    }

    private async Task<string> TokenAsync(Guid id)
    {
        var status = await owner.GetFromJsonAsync<JsonElement>($"{Uploads}/{id}");
        return status.GetProperty("publicLink").GetProperty("url").GetString()!["/p/survey/".Length..];
    }

    private async Task<SurveyUploadPublicLink> LiveLinkAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.SurveyUploadPublicLinks.AsNoTracking().SingleAsync(l => l.SurveyUploadId == id && l.RevokedAt == null);
    }

    private async Task<int> LinkCountAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.SurveyUploadPublicLinks.CountAsync(l => l.SurveyUploadId == id);
    }

    /// <summary>Protects or unprotects the cave itself, both the flag and the derived column a write would set.</summary>
    private async Task SetProtectedAsync(Guid caveId, bool on)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.Features.Where(f => f.Id == caveId)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.LocationProtected, on).SetProperty(f => f.IsProtectedEffective, on));
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static byte[] Fixture(string name) =>
        System.IO.File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "topodroid", name));

    private async Task RunPipelineAsync(Guid uploadId)
    {
        long jobId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var queued = await db.ProcessingJobs.AsNoTracking()
                .Where(j => j.Kind == ProcessingJobKinds.SurveyUploadPipeline && j.Status == ProcessingJobStatus.Queued)
                .ToListAsync();
            jobId = queued.Single(j =>
                JsonSerializer.Deserialize<SurveyUploadPipelinePayload>(j.Payload, JsonSerializerOptions.Web)!.SurveyUploadId == uploadId).Id;
        }

        await QueuedJob.RunAsync(factory.Services, jobId);
    }
}
