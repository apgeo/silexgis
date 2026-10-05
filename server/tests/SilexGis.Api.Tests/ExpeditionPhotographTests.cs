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
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The gallery narrowed to one camp: the pictures filed against the camp itself and the ones on
/// the trips it gathers — read through the caller's own visibility over the camp and over each
/// trip, so the filter shows a reader exactly what the camp's own page would let them reach.
/// </summary>
/// <remarks>
/// The assertions that matter are the negative ones. A camp the reader may not open answers
/// empty rather than refusing, even when a trip inside it is readable — otherwise the camp's id
/// would be a key for listing which trips belong to it. And a picture on a member trip the reader
/// may not open is absent, while the same reader is shown a picture on a member trip they may
/// open without holding any right on the picture's own document: the trip is what reaches it.
/// </remarks>
public sealed class ExpeditionPhotographTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;

    private HttpClient organiser = null!;

    // A plain reader, holding nothing anywhere: what reaches them reaches them through the
    // camp and its trips, or not at all.
    private HttpClient reader = null!;

    public ExpeditionPhotographTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"xph-org-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"xph-rdr-{suffix}@t.local");
        organiser = await AuthHelper.BearerClientAsync(factory, $"xph-org-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"xph-rdr-{suffix}@t.local");
    }

    [Fact]
    public async Task A_camps_pictures_are_the_ones_on_the_camp_and_on_the_trips_it_gathers()
    {
        var camp = await CreateCampAsync("authenticated");
        var trip = await CreateTripAsync("authenticated");
        var otherTrip = await CreateTripAsync("authenticated");
        await JoinAsync(camp, trip);

        var onCamp = await UploadAndAttachAsync("on-camp.png", "expedition", camp);
        var onTrip = await UploadAndAttachAsync("on-trip.png", "tripLog", trip);
        var elsewhere = await UploadAndAttachAsync("elsewhere.png", "tripLog", otherTrip);

        var pictures = await GalleryAsync(organiser, camp);
        pictures.ShouldContain(onCamp);
        pictures.ShouldContain(onTrip);
        pictures.ShouldNotContain(elsewhere);
    }

    [Fact]
    public async Task A_reader_reaches_a_picture_through_a_member_trip_they_may_open_and_no_other()
    {
        var camp = await CreateCampAsync("authenticated");
        var openTrip = await CreateTripAsync("authenticated");
        var closedTrip = await CreateTripAsync("private");
        await JoinAsync(camp, openTrip);
        await JoinAsync(camp, closedTrip);

        // Both pictures are the organiser's own private documents; the reader holds no right on
        // either. What reaches them is the trip the picture hangs on, and only the open one is
        // theirs to read.
        var reachable = await UploadAndAttachAsync("reachable.png", "tripLog", openTrip);
        var withheld = await UploadAndAttachAsync("withheld.png", "tripLog", closedTrip);

        var pictures = await GalleryAsync(reader, camp);
        pictures.ShouldContain(reachable);
        pictures.ShouldNotContain(withheld);
    }

    [Fact]
    public async Task A_camp_the_reader_may_not_open_answers_empty_even_when_a_trip_in_it_is_readable()
    {
        var camp = await CreateCampAsync("private");
        var openTrip = await CreateTripAsync("authenticated");
        await JoinAsync(camp, openTrip);
        var picture = await UploadAndAttachAsync("on-open-trip.png", "tripLog", openTrip);

        // Through the trip's own filter the reader sees it — the trip is theirs to read.
        (await GalleryAsync(reader, $"tripLogId={openTrip}")).ShouldContain(picture);

        // Through the camp's filter they see nothing, and get no refusal either: a camp they may
        // not open answers as one that is not there, so its id is not a key for listing the trips
        // it holds. The organiser, who may open it, sees the picture through the same filter.
        var response = await reader.GetAsync($"/api/v1/photos?pageSize=100&expeditionId={camp}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GalleryAsync(reader, camp)).ShouldBeEmpty();
        (await GalleryAsync(organiser, camp)).ShouldContain(picture);
    }

    [Fact]
    public async Task A_visitor_without_an_account_is_refused_and_an_unknown_camp_answers_empty()
    {
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync($"/api/v1/photos?expeditionId={Guid.NewGuid()}"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        (await GalleryAsync(organiser, Guid.NewGuid())).ShouldBeEmpty();
    }

    private async Task<Guid> CreateCampAsync(string visibility)
    {
        var response = await organiser.PostAsJsonAsync("/api/v1/expeditions/", new
        {
            name = $"Picture camp {Guid.NewGuid():N}"[..24],
            description = (string?)null,
            startDate = "2026-07-01",
            endDate = (string?)null,
            geom = (object?)null,
            cavingGroupId = (Guid?)null,
            visibility,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateTripAsync(string visibility)
    {
        var response = await organiser.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"Camp trip {Guid.NewGuid():N}"[..20],
            tripDate = "2026-07-02",
            participants = Array.Empty<object>(),
            visibility,
            hadIncident = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task JoinAsync(Guid campId, Guid tripId)
    {
        var response = await organiser.PostAsJsonAsync(
            $"/api/v1/expeditions/{campId}/trips", new { tripLogId = tripId });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    /// <summary>A picture of the organiser's, private as uploaded, hung on one object.</summary>
    private async Task<Guid> UploadAndAttachAsync(string name, string entityType, Guid entityId)
    {
        // Unique bytes: the store warns about content it already holds, and these suites share
        // one database.
        using var image = new MagickImage(MagickColors.SlateGray, 64, 64);
        image.Comment = $"{name} {Guid.NewGuid()}";
        var content = new ByteArrayContent(image.ToByteArray(MagickFormat.Png));
        content.Headers.ContentType = new("image/png");
        using var form = new MultipartFormDataContent { { content, "file", name } };
        var uploaded = await organiser.PostAsync("/api/v1/files/", form);
        var uploadedPayload = await uploaded.Content.ReadAsStringAsync();
        uploaded.StatusCode.ShouldBe(HttpStatusCode.Created, uploadedPayload);
        var fileId = JsonDocument.Parse(uploadedPayload).RootElement.GetProperty("id").GetGuid();

        var attached = await organiser.PostAsJsonAsync("/api/v1/attachments/", new
        {
            fileId,
            entityType,
            entityId,
            role = "photoSurface",
            caption = (string?)null,
            sortOrder = 0,
        });
        attached.StatusCode.ShouldBe(HttpStatusCode.Created, await attached.Content.ReadAsStringAsync());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var file = await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == fileId);
        var version = await db.DocumentVersions.AsNoTracking().FirstAsync(v => v.Id == file.DocumentVersionId);
        return version.DocumentId;
    }

    private static Task<List<Guid>> GalleryAsync(HttpClient client, Guid expeditionId) =>
        GalleryAsync(client, $"expeditionId={expeditionId}");

    private static async Task<List<Guid>> GalleryAsync(HttpClient client, string query)
    {
        var page = await client.GetFromJsonAsync<JsonElement>($"/api/v1/photos?pageSize=100&{query}");
        return [.. page.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("documentId").GetGuid())];
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        organiser?.Dispose();
        reader?.Dispose();
        factory.Dispose();
    }
}
