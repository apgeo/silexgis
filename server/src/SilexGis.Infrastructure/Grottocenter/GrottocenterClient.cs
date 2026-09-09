// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SilexGis.Infrastructure.Grottocenter;

/// <summary>One cave the far end offered for a name that was asked about.</summary>
/// <param name="ExternalId">Its identifier there, exactly as written.</param>
/// <param name="Name">What it is called there, so a person can tell two candidates apart.</param>
/// <param name="Country">Where it is, when the answer says — the cheapest way to spot a wrong continent.</param>
/// <param name="Url">The page a person can open to check before accepting the match.</param>
public sealed record GrottocenterCandidate(string ExternalId, string? Name, string? Country, string? Url);

/// <summary>Why a lookup could not be answered. Carries a stable code for the endpoint to pass on.</summary>
public sealed class GrottocenterException(string code, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public string Code { get; } = code;
}

/// <summary>
/// This installation's only way of talking to grottocenter.org.
///
/// <para>
/// <b>It is the throttle</b>, for the same reason its neighbour for the Romanian catalogue is:
/// one gate, held here, process-wide, so a screen cannot be written that goes faster because
/// there is nowhere else to go. That is why it is a singleton.
/// </para>
/// <para>
/// <b>It answers nothing when the integration is off</b>, and being off is the shipped state.
/// The check is here rather than only at the endpoint so that no future caller can reach the
/// network by forgetting it.
/// </para>
/// <para>
/// <b>It reads the answer tolerantly and keeps almost none of it.</b> The far end's response
/// shape is not this application's to pin down, so the parse looks for an array in any of the
/// places one has been seen and takes only an identifier and enough text for a person to
/// recognise the cave. An answer it cannot make sense of yields no candidates rather than an
/// exception: "we found nothing" is the honest outcome of not understanding a reply, and a
/// stack trace would be about this end when the surprise is at the other.
/// </para>
/// </summary>
public sealed class GrottocenterClient(
    IHttpClientFactory httpClientFactory,
    IOptions<GrottocenterOptions> options,
    ILogger<GrottocenterClient> logger)
{
    /// <summary>Name of the client; registered in <c>DependencyInjection</c>.</summary>
    public const string HttpClientName = "grottocenter";

    /// <summary>The integration is switched off on this installation.</summary>
    public const string DisabledCode = "grottocenter.disabled";

    /// <summary>The far end could not be reached, or answered with a failure.</summary>
    public const string UnreachableCode = "grottocenter.unreachable";

    /// <summary>Keys an identifier has been seen under, in the order they are tried.</summary>
    private static readonly string[] IdKeys = ["id", "_id", "entranceId", "caveId"];

    /// <summary>Keys a display name has been seen under.</summary>
    private static readonly string[] NameKeys = ["name", "title", "caveName"];

    /// <summary>Properties an array of results has been seen under, plus the bare array itself.</summary>
    private static readonly string[] ResultKeys = ["results", "caves", "entrances", "documents", "hits"];

    /// <summary>
    /// The gate every outbound call passes through. One permit, so calls are serialised; the
    /// spacing is applied while the permit is held, so a burst of callers queues rather than
    /// arriving together after each has waited its own interval.
    /// </summary>
    private readonly SemaphoreSlim gate = new(1, 1);

    private readonly Stopwatch sinceStart = Stopwatch.StartNew();
    private long lastCallCompletedMs = long.MinValue / 2;

    private GrottocenterOptions Options => options.Value;

    /// <summary>Whether this installation may call the far end at all.</summary>
    public bool IsConfigured => Options.IsConfigured;

    /// <summary>The page a stored identifier stands for.</summary>
    public string? EntryUrl(string externalId) => Options.EntryUrl(externalId);

    /// <summary>
    /// Caves the far end offers for a name.
    /// </summary>
    /// <remarks>
    /// Only the name is sent. Not the position — that is the one thing a lookup must not
    /// disclose, and it would be disclosed to a service outside this installation for every
    /// cave anybody ever pressed the button on, including the ones whose position this
    /// installation exists to protect. A name is enough to search by and is what a person
    /// would have typed into the same search themselves.
    /// </remarks>
    public async Task<IReadOnlyList<GrottocenterCandidate>> SearchAsync(string name, CancellationToken ct = default)
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(name))
        {
            return [];
        }

        var url = $"{Options.Endpoint.TrimEnd('/')}/search?resourceType=entrances&complete=false"
            + $"&query={Uri.EscapeDataString(name.Trim())}";

        using var document = await GetAsync(url, ct);
        return document is null ? [] : Candidates(document.RootElement);
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
        {
            throw new GrottocenterException(
                DisabledCode,
                "This installation does not look caves up in grottocenter.org. An administrator "
                + "turns the integration on before it can be used.");
        }
    }

    private async Task<JsonDocument?> GetAsync(string url, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var wait = lastCallCompletedMs + Math.Max(0, Options.MinRequestIntervalMs) - sinceStart.ElapsedMilliseconds;
            if (wait > 0)
            {
                await Task.Delay((int)wait, ct);
            }

            var client = httpClientFactory.CreateClient(HttpClientName);
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(Options.TimeoutSeconds, 1, 120));

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (!string.IsNullOrWhiteSpace(Options.UserAgent))
            {
                request.Headers.UserAgent.ParseAdd(Options.UserAgent);
            }

            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new GrottocenterException(
                    UnreachableCode,
                    $"grottocenter.org answered {(int)response.StatusCode}.");
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            try
            {
                return JsonDocument.Parse(body);
            }
            catch (JsonException error)
            {
                // Not an error of this application's making, and not worth failing a screen
                // over: the caller is told there were no candidates.
                logger.LogWarning(error, "grottocenter.org answered something that is not JSON");
                return null;
            }
        }
        catch (HttpRequestException error)
        {
            throw new GrottocenterException(UnreachableCode, "grottocenter.org could not be reached.", error);
        }
        catch (TaskCanceledException error) when (!ct.IsCancellationRequested)
        {
            throw new GrottocenterException(UnreachableCode, "grottocenter.org did not answer in time.", error);
        }
        finally
        {
            lastCallCompletedMs = sinceStart.ElapsedMilliseconds;
            gate.Release();
        }
    }

    private IReadOnlyList<GrottocenterCandidate> Candidates(JsonElement root)
    {
        var array = Array(root);
        if (array is not { } items)
        {
            return [];
        }

        var candidates = new List<GrottocenterCandidate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var limit = Math.Clamp(Options.MaxCandidates, 1, 100);

        foreach (var item in items.EnumerateArray())
        {
            if (candidates.Count >= limit)
            {
                break;
            }

            if (item.ValueKind != JsonValueKind.Object || Text(item, IdKeys) is not { } id || !seen.Add(id))
            {
                continue;
            }

            candidates.Add(new GrottocenterCandidate(id, Text(item, NameKeys), Text(item, ["country"]), EntryUrl(id)));
        }

        return candidates;
    }

    private static JsonElement? Array(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var key in ResultKeys)
        {
            if (root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Array)
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>The first of the given properties carrying something printable, as text.</summary>
    private static string? Text(JsonElement item, IReadOnlyList<string> keys)
    {
        foreach (var key in keys)
        {
            if (!item.TryGetProperty(key, out var value))
            {
                continue;
            }

            var text = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null,
            };

            if (!string.IsNullOrWhiteSpace(text))
            {
                return text.Trim();
            }
        }

        return null;
    }
}
