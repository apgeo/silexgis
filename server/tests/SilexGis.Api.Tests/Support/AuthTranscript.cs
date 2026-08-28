// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using System.Text.Json;

namespace SilexGis.Api.Tests.Support;

/// <summary>
/// Records the requests and responses a flow actually made, and writes them beside the test
/// assembly so that client-facing documentation can be transcribed from a run rather than written
/// from memory. Documentation of a protocol is worth what it costs to keep true, and prose about
/// what a server was believed to do is how a second implementation ends up debugging the wrong end.
/// </summary>
public sealed class AuthTranscript(string caption)
{
    /// <summary>
    /// Long opaque values are cut short. A transcript exists to show the shape of an exchange; a
    /// pasted JSON web token is several hundred characters that say nothing a reader can use, and
    /// leaving whole credentials in a checked-in document is a habit worth not forming.
    /// </summary>
    private const int MaxValueLength = 96;

    private readonly StringBuilder text = new();

    private int step;

    public async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpRequestMessage request, string note)
    {
        var method = request.Method;
        var uri = request.RequestUri?.ToString() ?? "/";
        var contentType = request.Content?.Headers.ContentType?.ToString();
        var requestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync();

        var response = await client.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        step++;
        text.Append("### ").Append(step).Append(". ").Append(note).AppendLine().AppendLine();

        text.Append(method).Append(' ').Append(uri).AppendLine();
        if (contentType is not null)
        {
            text.Append("Content-Type: ").Append(contentType).AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(requestBody))
        {
            text.AppendLine().Append(Abbreviate(requestBody)).AppendLine();
        }

        text.AppendLine()
            .Append("--> ").Append((int)response.StatusCode).Append(' ').Append(response.StatusCode)
            .AppendLine();

        if (response.Headers.Location is { } location)
        {
            text.Append("Location: ").Append(location).AppendLine();
        }

        if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            foreach (var cookie in cookies)
            {
                text.Append("Set-Cookie: ").Append(Abbreviate(cookie)).AppendLine();
            }
        }

        if (!string.IsNullOrWhiteSpace(responseBody))
        {
            text.AppendLine().Append(Abbreviate(responseBody)).AppendLine();
        }

        text.AppendLine();
        return response;
    }

    public async Task WriteAsync(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, fileName);
        var body = new StringBuilder()
            .AppendLine(caption)
            .AppendLine()
            .Append(text)
            .ToString();
        await File.WriteAllTextAsync(path, body);
    }

    /// <summary>
    /// Shortens the opaque parts of a body — token values, cookie values, code parameters — while
    /// leaving the structure that a reader needs intact.
    /// </summary>
    private static string Abbreviate(string body)
    {
        if (body.StartsWith('{'))
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    var shortened = document.RootElement.EnumerateObject()
                        .Select(p => $"  \"{p.Name}\": {ShortenJsonValue(p.Value)}");
                    return "{\n" + string.Join(",\n", shortened) + "\n}";
                }
            }
            catch (JsonException)
            {
                // Not JSON after all; fall through and shorten it as plain text.
            }
        }

        return string.Join('&', body.Split('&').Select(ShortenPair));
    }

    private static string ShortenJsonValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => "\"" + Cut(value.GetString()!) + "\"",
        JsonValueKind.Array => "[" + string.Join(", ", value.EnumerateArray().Select(ShortenJsonValue)) + "]",

        // Written out rather than left to ToString(), which renders these three as "True", "False"
        // and the empty string — none of which is what went over the wire.
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => "null",
        _ => value.ToString(),
    };

    private static string ShortenPair(string pair)
    {
        var separator = pair.IndexOf('=', StringComparison.Ordinal);
        return separator < 0 ? Cut(pair) : pair[..(separator + 1)] + Cut(pair[(separator + 1)..]);
    }

    private static string Cut(string value) =>
        value.Length <= MaxValueLength ? value : value[..MaxValueLength] + "…";
}
