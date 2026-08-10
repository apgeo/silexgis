// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net.Http.Json;

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
}
