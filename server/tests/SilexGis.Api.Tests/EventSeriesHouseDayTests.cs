// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;

namespace SilexGis.Api.Tests;

/// <summary>
/// Which day is "today" when the rest of a repeating event is called off: the installation's own
/// calendar day, not the day at Greenwich. For a club east of UTC the two differ every night
/// between local midnight and the offset, and in that hour last evening's occurrence has already
/// happened where the club is while it is still "today" in UTC.
/// </summary>
public sealed class EventSeriesHouseDayTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];
    private readonly TestTimeProvider clock = new(DateTimeOffset.UtcNow);

    private HttpClient owner = null!;

    public EventSeriesHouseDayTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?> { ["Notifications:TimeZone"] = "Europe/Bucharest" },
            services => services.AddSingleton<TimeProvider>(clock));

    public async ValueTask InitializeAsync()
    {
        var email = $"evd-own-{suffix}@t.local";
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, email);
        owner = await AuthHelper.BearerClientAsync(factory, email);
    }

    /// <summary>
    /// Half past one in the morning of the ninth in Bucharest (summer time, UTC+3) is half past
    /// ten in the evening of the eighth in UTC. The evening of the eighth has been and gone where
    /// the club is, so calling off "the eighth and the rest" keeps it and removes only the ninth
    /// and the tenth.
    /// </summary>
    [Fact]
    public async Task Calling_off_the_rest_after_local_midnight_keeps_last_evening()
    {
        var first = new DateOnly(2030, 6, 8);
        var created = await PostAsync(new
        {
            title = $"Evening {suffix}",
            kind = "clubMeeting",
            startDate = first.ToString("yyyy-MM-dd"),
            visibility = "private",
            recurrence = new { frequency = "daily", count = 3, rule = "Every day" },
        });
        var seriesId = created.GetProperty("seriesId").GetGuid();
        var rows = await BySeriesAsync(seriesId);
        rows.Count.ShouldBe(3);
        var eighth = rows.Single(x => x.GetProperty("startDate").GetString() == "2030-06-08")
            .GetProperty("id").GetGuid();

        clock.Now = new DateTimeOffset(2030, 6, 8, 22, 30, 0, TimeSpan.Zero);
        var response = await owner.DeleteAsync($"/api/v1/events/{eighth}/series/following");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        var result = JsonDocument.Parse(payload).RootElement;
        result.GetProperty("deleted").GetInt32().ShouldBe(2);
        result.GetProperty("kept").GetInt32().ShouldBe(1);

        (await BySeriesAsync(seriesId)).Select(x => x.GetProperty("startDate").GetString())
            .ShouldBe(["2030-06-08"]);
    }

    private async Task<JsonElement> PostAsync(object body)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/events", body);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private async Task<List<JsonElement>> BySeriesAsync(Guid seriesId)
    {
        var response = await owner.GetAsync($"/api/v1/events?pageSize=200&search={suffix}&seriesId={seriesId}");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return [.. JsonDocument.Parse(payload).RootElement.Clone().GetProperty("items").EnumerateArray()];
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
    }
}
