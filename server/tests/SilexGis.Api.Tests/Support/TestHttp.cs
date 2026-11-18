// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;

namespace SilexGis.Api.Tests.Support;

/// <summary>Test HTTP helpers.</summary>
public static class TestHttp
{
    /// <summary>
    /// PUT with an If-Match header (wildcard by default). Editing caves, surface features and
    /// trip logs requires the precondition at the API freeze; tests that assert permissions or
    /// business rules rather than concurrency just present the wildcard.
    /// </summary>
    public static Task<HttpResponseMessage> PutWithIfMatchAsync(
        this HttpClient client, string url, object body, string ifMatch = "*")
    {
        var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return client.SendAsync(request);
    }

    /// <summary>
    /// POST with an If-Match header and no body — the shape a state transition takes. The
    /// precondition is required on those the same way it is on a full edit, so a test that is
    /// about the transition and not about concurrency presents the wildcard.
    /// </summary>
    public static Task<HttpResponseMessage> PostWithIfMatchAsync(
        this HttpClient client, string url, string ifMatch = "*")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return client.SendAsync(request);
    }

    /// <summary>
    /// The same, with a body — the shape a transition takes when the state to move to is named
    /// in the request rather than in the route, which is how an activity with more than a couple
    /// of moves asks for one.
    /// </summary>
    public static Task<HttpResponseMessage> PostWithIfMatchAsync(
        this HttpClient client, string url, object body, string ifMatch = "*")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return client.SendAsync(request);
    }

    /// <summary>
    /// The fields a validation refusal names, once it is shown to be one: a 400 with the generic
    /// validation code. A test that pins a limit compares these to the one field it is about, so a
    /// request refused for any other reason cannot pass for the rule.
    /// </summary>
    public static async Task<string[]> RefusedFieldsAsync(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body);
        var problem = JsonDocument.Parse(body).RootElement;
        problem.GetProperty("code").GetString().ShouldBe("validation.failed", body);
        return [.. problem.GetProperty("errors").EnumerateObject().Select(e => e.Name)];
    }
}
