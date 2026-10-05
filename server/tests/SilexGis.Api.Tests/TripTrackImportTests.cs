// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Infrastructure.Identity;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// A recorded track written onto a trip as the trip's own sketch, from a GPX file uploaded in
/// one request: that the line lands on the trip and nowhere else, that a file with nothing
/// linear in it or nothing readable in it is refused and the trip keeps what it had, that the
/// ceiling on points refuses rather than thins, and that the write is guarded as a trip write.
/// </summary>
public sealed class TripTrackImportTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string TwoSegmentTrack = """
        <?xml version="1.0" encoding="UTF-8"?>
        <gpx version="1.1" creator="test" xmlns="http://www.topografix.com/GPX/1/1">
          <wpt lat="45.51" lon="25.41"><name>Spring</name><ele>812</ele></wpt>
          <trk><name>Approach</name>
            <trkseg>
              <trkpt lat="45.500" lon="25.400"><ele>700</ele></trkpt>
              <trkpt lat="45.505" lon="25.405"><ele>720</ele></trkpt>
              <trkpt lat="45.510" lon="25.410"><ele>740</ele></trkpt>
            </trkseg>
            <trkseg>
              <trkpt lat="45.510" lon="25.410"><ele>740</ele></trkpt>
              <trkpt lat="45.515" lon="25.415"><ele>760</ele></trkpt>
              <trkpt lat="45.520" lon="25.420"><ele>780</ele></trkpt>
            </trkseg>
          </trk>
        </gpx>
        """;

    private const string WaypointsOnly = """
        <?xml version="1.0" encoding="UTF-8"?>
        <gpx version="1.1" creator="test" xmlns="http://www.topografix.com/GPX/1/1">
          <wpt lat="45.51" lon="25.41"><name>Spring</name></wpt>
          <wpt lat="45.52" lon="25.42"><name>Sink</name></wpt>
        </gpx>
        """;

    private readonly SilexGisApiFactory factory;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient organiser = null!;
    private HttpClient reader = null!;

    public TripTrackImportTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            // Low enough for a six-point track to cross it, so the ceiling is exercised by a
            // file a test can read, rather than by one nobody would want in a repository.
            ["Import:MaxTripTrackPoints"] = "4",
        });

    public async Task InitializeAsync()
    {
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tt-org-{suffix}@t.local");
        // A Viewer, deliberately: the seeded Editors group writes every trip at the widest
        // scope, so "an editor was refused" would say nothing about the trip's own guard.
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tt-read-{suffix}@t.local");
        organiser = await AuthHelper.BearerClientAsync(factory, $"tt-org-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"tt-read-{suffix}@t.local");
    }

    [Fact]
    public async Task A_track_in_an_uploaded_gpx_becomes_the_trips_own_line_and_nothing_else_is_kept()
    {
        var tripId = await CreateTripAsync("Walked in", "authenticated");
        var geofilesBefore = await CountGeofilesAsync();

        // The ceiling is four points for this class; a track joined down to four crosses nothing.
        var response = await organiser.PostAsync(
            $"/api/v1/trip-logs/{tripId}/geometry/gpx", Form("approach.gpx", FourPointTrack()));
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);

        // The two segments meet at one point, which is written once: a line, not a line with a
        // stutter in it, and the elevations the points carried are not part of the sketch.
        var geom = JsonDocument.Parse(payload).RootElement.GetProperty("geom");
        geom.GetProperty("type").GetString().ShouldBe("LineString");
        var coordinates = geom.GetProperty("coordinates");
        coordinates.GetArrayLength().ShouldBe(4);
        coordinates[0].GetArrayLength().ShouldBe(2);
        coordinates[0][0].GetDouble().ShouldBe(25.400, 1e-9);
        coordinates[0][1].GetDouble().ShouldBe(45.500, 1e-9);

        // Read back from the trip, by somebody who may read it: the sketch is the trip's.
        var read = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/trip-logs/{tripId}");
        read.GetProperty("geom").GetProperty("coordinates").GetArrayLength().ShouldBe(4);

        // No registry row for what was one edit of one trip.
        (await CountGeofilesAsync()).ShouldBe(geofilesBefore);
    }

    [Fact]
    public async Task A_file_with_nothing_linear_in_it_is_refused_and_the_trip_keeps_its_sketch()
    {
        var tripId = await CreateTripAsync("Nothing to walk", "authenticated");
        var first = await organiser.PostAsync(
            $"/api/v1/trip-logs/{tripId}/geometry/gpx", Form("approach.gpx", FourPointTrack()));
        first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());

        var refused = await organiser.PostAsync(
            $"/api/v1/trip-logs/{tripId}/geometry/gpx", Form("springs.gpx", Encoding.UTF8.GetBytes(WaypointsOnly)));
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOfAsync(refused)).ShouldBe("trip_track.no_track");

        var read = await organiser.GetFromJsonAsync<JsonElement>($"/api/v1/trip-logs/{tripId}");
        read.GetProperty("geom").GetProperty("coordinates").GetArrayLength().ShouldBe(4);
    }

    [Fact]
    public async Task A_file_that_is_not_a_readable_gpx_is_refused_by_name_and_by_content()
    {
        var tripId = await CreateTripAsync("Wrong file", "authenticated");

        var wrongName = await organiser.PostAsync(
            $"/api/v1/trip-logs/{tripId}/geometry/gpx", Form("approach.kml", Encoding.UTF8.GetBytes(TwoSegmentTrack)));
        wrongName.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOfAsync(wrongName)).ShouldBe("trip_track.format_unsupported");

        var garbage = await organiser.PostAsync(
            $"/api/v1/trip-logs/{tripId}/geometry/gpx", Form("approach.gpx", Encoding.UTF8.GetBytes("not a track at all")));
        garbage.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOfAsync(garbage)).ShouldBe("trip_track.file_unreadable");

        var empty = await organiser.PostAsync(
            $"/api/v1/trip-logs/{tripId}/geometry/gpx", Form("approach.gpx", []));
        empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOfAsync(empty)).ShouldBe("trip_track.file_empty");
    }

    /// <summary>
    /// Over the ceiling the track is refused, never thinned: a line the server simplified would
    /// be a line nobody recorded, handed exactly to everybody who may read the trip.
    /// </summary>
    [Fact]
    public async Task A_track_over_the_point_ceiling_is_refused_rather_than_thinned()
    {
        var tripId = await CreateTripAsync("Too long a walk", "authenticated");

        var refused = await organiser.PostAsync(
            $"/api/v1/trip-logs/{tripId}/geometry/gpx", Form("approach.gpx", Encoding.UTF8.GetBytes(TwoSegmentTrack)));
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOfAsync(refused)).ShouldBe("trip_track.too_many_points");

        var read = await organiser.GetFromJsonAsync<JsonElement>($"/api/v1/trip-logs/{tripId}");
        read.GetProperty("geom").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Writing_a_track_is_a_trip_write_refused_to_a_reader_and_hidden_from_a_stranger()
    {
        var readable = await CreateTripAsync("Readable but not mine", "authenticated");
        var hidden = await CreateTripAsync("Not shown", "private");

        var refused = await reader.PostAsync(
            $"/api/v1/trip-logs/{readable}/geometry/gpx", Form("approach.gpx", FourPointTrack()));
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var notFound = await reader.PostAsync(
            $"/api/v1/trip-logs/{hidden}/geometry/gpx", Form("approach.gpx", FourPointTrack()));
        notFound.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var anonymous = factory.CreateClient();
        (await anonymous.PostAsync($"/api/v1/trip-logs/{readable}/geometry/gpx", Form("approach.gpx", FourPointTrack())))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>The two-segment track with its second segment cut to the joint and one more point.</summary>
    private static byte[] FourPointTrack() => Encoding.UTF8.GetBytes("""
        <?xml version="1.0" encoding="UTF-8"?>
        <gpx version="1.1" creator="test" xmlns="http://www.topografix.com/GPX/1/1">
          <trk><name>Approach</name>
            <trkseg>
              <trkpt lat="45.500" lon="25.400"><ele>700</ele></trkpt>
              <trkpt lat="45.505" lon="25.405"><ele>720</ele></trkpt>
              <trkpt lat="45.510" lon="25.410"><ele>740</ele></trkpt>
            </trkseg>
            <trkseg>
              <trkpt lat="45.510" lon="25.410"><ele>740</ele></trkpt>
              <trkpt lat="45.515" lon="25.415"><ele>760</ele></trkpt>
            </trkseg>
          </trk>
        </gpx>
        """);

    private static MultipartFormDataContent Form(string fileName, byte[] bytes) =>
        new() { { new ByteArrayContent(bytes), "file", fileName } };

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString();

    private async Task<Guid> CreateTripAsync(string title, string visibility)
    {
        var response = await organiser.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"{title} {suffix}",
            tripDate = "2026-09-12",
            caveIds = Array.Empty<Guid>(),
            participants = Array.Empty<object>(),
            visibility,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<int> CountGeofilesAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.Geofiles.CountAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        organiser?.Dispose();
        reader?.Dispose();
        factory.Dispose();
    }
}
