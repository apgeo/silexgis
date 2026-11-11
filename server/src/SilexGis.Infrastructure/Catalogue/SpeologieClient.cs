// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SilexGis.Domain.Catalogue;

namespace SilexGis.Infrastructure.Catalogue;

/// <summary>What to ask the catalogue for.</summary>
/// <param name="Term">Free text, matched as a substring of the cave's name only. Optional.</param>
/// <param name="County">Two-letter county code, matched exactly. Optional.</param>
/// <param name="Offset">How many rows of the merged answer to skip.</param>
/// <param name="PageSize">How many rows are wanted.</param>
public sealed record SpeologieSearchQuery(string? Term, string? County, long Offset, int PageSize);

/// <summary>
/// One page of catalogue results.
/// </summary>
/// <param name="Items">The caves found, by ascending catalogue id, with no duplicates.</param>
/// <param name="Spellings">The spellings actually searched for, so the caller can say what was asked.</param>
/// <param name="HasMore">
/// Whether rows may follow this page. A page that came back exactly as large as the catalogue
/// serves counts as "perhaps more", because the probe row that would have settled it did not fit.
/// </param>
/// <param name="BeyondReach">
/// The page asked for starts past what this search can serve exactly, so <paramref name="Items"/>
/// is empty although the answer holds more rows. The caller refuses it rather than presenting the
/// empty page as the end of the results.
/// </param>
public sealed record SpeologieSearchPage(
    IReadOnlyList<SpeologieRecord> Items,
    IReadOnlyList<string> Spellings,
    bool HasMore,
    bool BeyondReach = false);

/// <summary>
/// This installation's only way of talking to speologie.org.
///
/// <para>
/// Three things about it are load-bearing and none of them are incidental.
/// </para>
/// <para>
/// <b>It is the throttle.</b> Every call the application makes to the catalogue passes through
/// one gate held by this object: one call at a time, process-wide, with a minimum gap between
/// them. That is why it is a singleton. The catalogue is a volunteer-run register whose own
/// documentation asks not to be hammered, and a limit that lives in each caller is a limit that
/// the next caller forgets. Putting it here means a screen cannot be written that goes faster,
/// because there is nowhere else to go.
/// </para>
/// <para>
/// <b>It clamps the page size itself.</b> The catalogue caps a page at 100 and clamps silently
/// rather than refusing, so asking for a thousand would look like it worked and quietly return a
/// hundred. This clamps before asking, so what was requested and what was meant are the same
/// thing, and so the ceiling survives the far end changing its mind about enforcing it.
/// </para>
/// <para>
/// <b>It searches for more than one spelling of the term.</b> Romanian is written with two
/// different diacritic conventions and a good deal of the catalogue with none, and the
/// catalogue's search folds none of them together — so one literal query returns part of the
/// answer and looks like the whole of it. The spellings are asked for as aliased fields of a
/// single request, which costs the far end exactly one call.
/// </para>
/// </summary>
public sealed class SpeologieClient(
    IHttpClientFactory httpClientFactory,
    IOptions<SpeologieOptions> options,
    ILogger<SpeologieClient> logger)
{
    /// <summary>Name of the client; registered in <c>DependencyInjection</c>.</summary>
    public const string HttpClientName = "speologie";

    /// <summary>
    /// How many caves are read in one detail request. The catalogue answers aliased fields, so a
    /// selection is fetched in a handful of calls rather than one per cave; the chunk is small
    /// because each of these carries a description and descriptions are occasionally enormous.
    /// </summary>
    private const int DetailChunkSize = 10;

    /// <summary>
    /// Fields read when listing. Deliberately without <c>descriere</c>: measured over the
    /// catalogue, a hundred rows without it are a couple of hundred kilobytes and a hundred rows
    /// with it are megabytes, because a single record's description can exceed one megabyte on
    /// its own. A list never needs it.
    /// </summary>
    private const string ListFields =
        "id title slug judet localitate munte lungime denivelare denNegativa altitudine " +
        "nrHidro bazinHidroId roca scufundabila clasificare stiinta disparuta codAp";

    /// <summary>Everything, for the one cave a person is about to import.</summary>
    private const string DetailFields = ListFields + " descriere";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// The gate every outbound call passes through. One permit, so calls are serialised; the
    /// spacing is applied while the permit is held, so a burst of callers queues rather than
    /// arriving together after each has waited its own interval.
    /// </summary>
    private readonly SemaphoreSlim gate = new(1, 1);

    private readonly Stopwatch sinceStart = Stopwatch.StartNew();
    private long lastCallCompletedMs = long.MinValue / 2;

    private SpeologieOptions Options => options.Value;

    /// <summary>Whether this installation has been given a key at all.</summary>
    public bool IsConfigured => Options.IsConfigured;

    /// <summary>The largest page this installation will ask for, after its own clamp.</summary>
    public int MaxPageSize => Options.EffectiveMaxPageSize;

    /// <summary>How many caves one import may take.</summary>
    public int MaxSelection => Math.Max(1, Options.MaxSelection);

    /// <summary>
    /// Searches the catalogue. Every spelling of the term is asked for in the same request and
    /// the answers are merged by catalogue id.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A term with one spelling, or a search by county alone, is one result set, and the catalogue
    /// pages it by offset at any depth.
    /// </para>
    /// <para>
    /// A term with several spellings is several result sets, each paged by its own row position,
    /// merged here and cut by catalogue id. Handing every spelling the same offset would skip rows:
    /// a row fetched for one page and cut by id falls behind the next page's offset in its own
    /// spelling and is never asked for again. So each spelling is read from its start, as deep as
    /// the page wanted, and the merged list is paged here. That is exact — the first N rows of the
    /// merged answer are each within the first N rows of their own spelling, since every row
    /// ahead of them in their spelling is also ahead of them in the merge — and it costs the same
    /// single request per page. Its limit is the catalogue's page ceiling: past one page's depth
    /// per spelling the merge cannot be completed in one request, and the page is reported as
    /// beyond reach instead of being served short.
    /// </para>
    /// </remarks>
    public async Task<SpeologieSearchPage> SearchAsync(SpeologieSearchQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        EnsureConfigured();

        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var offset = Math.Max(0L, query.Offset);

        var spellings = RomanianText.SearchSpellings(query.Term);
        var county = Normalise(query.County);

        if (spellings.Count == 0 && county is null)
        {
            // No term and no county is a request to walk the whole catalogue one page at a time.
            // The catalogue would answer it, and this application declines to ask: the feature is
            // for finding caves to import, and a browse-everything surface is how a fragile
            // register gets crawled by accident.
            return new SpeologieSearchPage([], [], HasMore: false);
        }

        // One more than wanted, so "is there another page" is answered by what came back rather
        // than by a count the catalogue does not publish.
        var merging = spellings.Count > 1;
        var wanted = offset + pageSize + 1;
        var limit = (int)Math.Min(merging ? wanted : pageSize + 1, SpeologieOptions.RemotePageCeiling);
        var remoteOffset = merging ? 0L : offset;

        if (remoteOffset > int.MaxValue)
        {
            // The catalogue's integers stop here, and no register holds this many caves.
            return new SpeologieSearchPage([], spellings, HasMore: false, BeyondReach: true);
        }

        var variables = new Dictionary<string, object?>
        {
            ["limit"] = limit,
            ["offset"] = (int)remoteOffset,
        };

        if (county is not null)
        {
            variables["judet"] = county;
        }

        var fields = new List<string>();
        var aliases = new List<string>();

        if (spellings.Count == 0)
        {
            aliases.Add("s0");
            fields.Add($"s0: pesteri({Args(term: null, county)}) {{ {ListFields} }}");
        }
        else
        {
            for (var i = 0; i < spellings.Count; i++)
            {
                var alias = $"s{i}";
                var variable = $"q{i}";
                variables[variable] = spellings[i];
                aliases.Add(alias);
                fields.Add($"{alias}: pesteri({Args(variable, county)}) {{ {ListFields} }}");
            }
        }

        var declarations = string.Join(", ", variables.Keys.Select(Declare));
        var document = $"query Search({declarations}) {{ {string.Join(" ", fields)} }}";

        var data = await SendAsync(document, variables, ct);

        // Merged by id, ordered by id. The catalogue orders each spelling's rows by id already,
        // so this is the order a caller would have seen from any single spelling — the merge does
        // not reshuffle a result set somebody was reading.
        var merged = new SortedDictionary<int, SpeologieRecord>();

        // A spelling that filled its limit may hold more rows than were read. Every row of the
        // merge up to the smallest last id such a spelling returned is known to be complete;
        // past it, a row of that spelling could still sort in between.
        var saturated = false;
        var exactThrough = int.MaxValue;

        foreach (var alias in aliases)
        {
            var (rows, served) = ReadList(data, alias);

            foreach (var row in rows)
            {
                merged.TryAdd(row.Id, row);
            }

            if (served >= limit)
            {
                saturated = true;
                if (rows.Count > 0)
                {
                    exactThrough = Math.Min(exactThrough, rows.Max(r => r.Id));
                }
            }
        }

        if (!merging)
        {
            // One result set, already positioned by the catalogue at the offset asked for.
            return new SpeologieSearchPage([.. merged.Values.Take(pageSize)], spellings, saturated);
        }

        var exact = merged.Values.Where(r => r.Id <= exactThrough).ToArray();

        if (saturated && offset >= exact.Length)
        {
            return new SpeologieSearchPage([], spellings, HasMore: true, BeyondReach: true);
        }

        var items = exact.Skip((int)Math.Min(offset, exact.Length)).Take(pageSize).ToArray();
        var hasMore = saturated || exact.Length > offset + pageSize;

        return new SpeologieSearchPage(items, spellings, hasMore);
    }

    /// <summary>Reads one cave in full, by the catalogue's id. Null when the catalogue has no such cave.</summary>
    public async Task<SpeologieRecord?> GetAsync(int id, CancellationToken ct = default)
    {
        var found = await GetManyAsync([id], ct);
        return found.Count == 0 ? null : found[0];
    }

    /// <summary>
    /// Reads several caves in full. Asked for as aliased fields so a selection costs a handful of
    /// calls rather than one per cave; ids the catalogue does not know are simply absent from the
    /// answer, which is how the catalogue reports them and is not an error.
    /// </summary>
    public async Task<IReadOnlyList<SpeologieRecord>> GetManyAsync(
        IReadOnlyCollection<int> ids, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        EnsureConfigured();

        var wanted = ids.Distinct().OrderBy(x => x).ToArray();
        if (wanted.Length == 0)
        {
            return [];
        }

        var found = new List<SpeologieRecord>(wanted.Length);

        foreach (var chunk in wanted.Chunk(DetailChunkSize))
        {
            var variables = new Dictionary<string, object?>();
            var fields = new List<string>(chunk.Length);

            for (var i = 0; i < chunk.Length; i++)
            {
                variables[$"id{i}"] = chunk[i];
                fields.Add($"c{i}: pestera(id: $id{i}) {{ {DetailFields} }}");
            }

            var declarations = string.Join(", ", variables.Keys.Select(k => $"${k}: Int!"));
            var document = $"query Detail({declarations}) {{ {string.Join(" ", fields)} }}";

            var data = await SendAsync(document, variables, ct);

            for (var i = 0; i < chunk.Length; i++)
            {
                if (ReadOne(data, $"c{i}") is { } record)
                {
                    found.Add(record);
                }
            }
        }

        return found;
    }

    /// <summary>Reads one cave in full, by the slug that is also its public page's address.</summary>
    public async Task<SpeologieRecord?> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        var variables = new Dictionary<string, object?> { ["slug"] = slug.Trim() };
        var document = $"query Detail($slug: String!) {{ c0: pestera(slug: $slug) {{ {DetailFields} }} }}";

        var data = await SendAsync(document, variables, ct);
        return ReadOne(data, "c0");
    }

    private static string Args(string? term, string? county)
    {
        var parts = new List<string>(4);

        if (term is not null)
        {
            parts.Add($"q: ${term}");
        }

        if (county is not null)
        {
            parts.Add("judet: $judet");
        }

        parts.Add("limit: $limit");
        parts.Add("offset: $offset");

        return string.Join(", ", parts);
    }

    private static string Declare(string name) => name switch
    {
        "limit" or "offset" => $"${name}: Int!",
        _ => $"${name}: String!",
    };

    private static string? Normalise(string? county) =>
        string.IsNullOrWhiteSpace(county) ? null : county.Trim().ToUpperInvariant();

    private void EnsureConfigured()
    {
        if (!IsConfigured)
        {
            throw new SpeologieException(SpeologieException.NotConfiguredCode);
        }
    }

    /// <summary>
    /// Sends one GraphQL document and returns its <c>data</c> object.
    ///
    /// <para>
    /// The catalogue reports failure in three different shapes and they need telling apart: a 401
    /// for a key it will not accept, a 400 for a document it cannot parse, and a 200 carrying an
    /// <c>errors</c> array for a request it understood and would not serve. Only the first is
    /// something an administrator can fix, and only the third leaves usable partial data — which
    /// is deliberately not used here, because a page half-answered is a page nobody can tell is
    /// half-answered.
    /// </para>
    /// </summary>
    private async Task<JsonElement> SendAsync(
        string document, IReadOnlyDictionary<string, object?> variables, CancellationToken ct)
    {
        var payload = new { query = document, variables };
        var attempts = Math.Max(0, Options.MaxRetries) + 1;

        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                response = await SendThrottledAsync(payload, ct);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                if (attempt >= attempts)
                {
                    throw new SpeologieException(
                        SpeologieException.UnavailableCode,
                        "The cave catalogue could not be reached.", e);
                }

                logger.LogWarning(e, "speologie.org did not answer (attempt {Attempt} of {Attempts}).", attempt, attempts);
                continue;
            }

            using (response)
            {
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    throw new SpeologieException(
                        SpeologieException.UnauthorizedCode,
                        "The cave catalogue did not accept this installation's API key.");
                }

                // Too many requests, or their side is unwell. Both are worth one more try after a
                // pause — and the pause is theirs to choose if they said so.
                if (response.StatusCode is HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError)
                {
                    if (attempt >= attempts)
                    {
                        throw new SpeologieException(
                            SpeologieException.UnavailableCode,
                            $"The cave catalogue answered {(int)response.StatusCode} and did not recover.");
                    }

                    var wait = RetryAfter(response) ?? TimeSpan.FromMilliseconds(500 * attempt);
                    logger.LogWarning(
                        "speologie.org answered {Status}; waiting {Delay} before attempt {Next}.",
                        (int)response.StatusCode, wait, attempt + 1);
                    await Task.Delay(wait, ct);
                    continue;
                }

                JsonElement body;
                try
                {
                    body = await response.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
                }
                catch (JsonException)
                {
                    // Not JSON at all: a proxy's error page, a maintenance page, an empty body.
                    // Behind an error status it is that status's refusal like any other; behind a
                    // success it is not the catalogue answering, which is their side being unwell
                    // rather than this application asking for something wrong.
                    throw response.IsSuccessStatusCode
                        ? new SpeologieException(
                            SpeologieException.UnavailableCode,
                            "The cave catalogue answered with something that is not a catalogue answer.")
                        : new SpeologieException(
                            SpeologieException.RejectedCode,
                            $"The cave catalogue answered {(int)response.StatusCode}.");
                }

                if (body.ValueKind == JsonValueKind.Object
                    && body.TryGetProperty("errors", out var errors)
                    && errors.ValueKind == JsonValueKind.Array
                    && errors.GetArrayLength() > 0)
                {
                    throw new SpeologieException(SpeologieException.RejectedCode, FirstMessage(errors));
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new SpeologieException(
                        SpeologieException.RejectedCode,
                        $"The cave catalogue answered {(int)response.StatusCode}.");
                }

                // An object or nothing: every reader of the answer looks members up in it by name,
                // and "data": null with no errors to explain it is not an answer to read.
                if (body.ValueKind != JsonValueKind.Object
                    || !body.TryGetProperty("data", out var data)
                    || data.ValueKind != JsonValueKind.Object)
                {
                    throw new SpeologieException(
                        SpeologieException.RejectedCode, "The cave catalogue answered without any data.");
                }

                return data.Clone();
            }
        }
    }

    /// <summary>
    /// Holds the gate, waits out whatever is left of the minimum interval, sends, and stamps the
    /// clock when the call is done.
    /// </summary>
    /// <remarks>
    /// The stamp goes at the end rather than the start on purpose: it makes the configured
    /// interval a gap between calls rather than a period they are started on, so a slow answer
    /// does not earn the next request the right to leave immediately.
    /// </remarks>
    private async Task<HttpResponseMessage> SendThrottledAsync(object payload, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        client.Timeout = TimeSpan.FromSeconds(Math.Clamp(Options.TimeoutSeconds, 5, 120));

        await gate.WaitAsync(ct);
        try
        {
            var interval = Math.Max(0, Options.MinRequestIntervalMs);
            var waited = sinceStart.ElapsedMilliseconds - lastCallCompletedMs;
            if (waited < interval)
            {
                await Task.Delay((int)(interval - waited), ct);
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, Options.Endpoint)
            {
                Content = JsonContent.Create(payload, options: Json),
            };

            request.Headers.TryAddWithoutValidation("X-API-Key", Options.ApiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            if (!string.IsNullOrWhiteSpace(Options.UserAgent))
            {
                request.Headers.TryAddWithoutValidation("User-Agent", Options.UserAgent);
            }

            return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        finally
        {
            lastCallCompletedMs = sinceStart.ElapsedMilliseconds;
            gate.Release();
        }
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var after = response.Headers.RetryAfter;

        if (after?.Delta is { } delta)
        {
            return Clamp(delta);
        }

        if (after?.Date is { } date)
        {
            return Clamp(date - DateTimeOffset.UtcNow);
        }

        return null;

        // However long they ask for, this application is not going to hold a request open for
        // minutes; past the clamp it is better to fail and let the person try again.
        static TimeSpan Clamp(TimeSpan value) =>
            value <= TimeSpan.Zero ? TimeSpan.Zero
            : value > TimeSpan.FromSeconds(10) ? TimeSpan.FromSeconds(10)
            : value;
    }

    private static string? FirstMessage(JsonElement errors)
    {
        foreach (var error in errors.EnumerateArray())
        {
            if (error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String)
            {
                return message.GetString();
            }
        }

        return null;
    }

    /// <summary>
    /// The rows one aliased listing answered, and how many it served — which is more than the rows
    /// when an element could not be read, and is what says whether the listing filled its limit.
    /// </summary>
    private static (IReadOnlyList<SpeologieRecord> Rows, int Served) ReadList(JsonElement data, string alias)
    {
        if (!data.TryGetProperty(alias, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return ([], 0);
        }

        var rows = new List<SpeologieRecord>(array.GetArrayLength());
        foreach (var element in array.EnumerateArray())
        {
            if (ReadRecord(element) is { } record)
            {
                rows.Add(record);
            }
        }

        return (rows, array.GetArrayLength());
    }

    private static SpeologieRecord? ReadOne(JsonElement data, string alias) =>
        data.TryGetProperty(alias, out var element) ? ReadRecord(element) : null;

    /// <summary>
    /// Reads one row defensively. The catalogue declares its numbers as floats and returns them
    /// as integers, declares a boolean flag as a string of "0" or "1", and can add a field at any
    /// time; a reader that insisted on the declared shapes would break on data that is perfectly
    /// usable.
    /// </summary>
    private static SpeologieRecord? ReadRecord(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object
            || !e.TryGetProperty("id", out var id)
            || !id.TryGetInt32(out var idValue))
        {
            return null;
        }

        return new SpeologieRecord(
            Id: idValue,
            Title: Str(e, "title") ?? string.Empty,
            Slug: Str(e, "slug"),
            Descriere: Str(e, "descriere"),
            Judet: Str(e, "judet"),
            Localitate: Str(e, "localitate"),
            Munte: Str(e, "munte"),
            Lungime: Num(e, "lungime"),
            Denivelare: Num(e, "denivelare"),
            DenNegativa: Num(e, "denNegativa"),
            Altitudine: Num(e, "altitudine"),
            NrHidro: Str(e, "nrHidro"),
            BazinHidroId: Int(e, "bazinHidroId"),
            Roca: Str(e, "roca"),
            Scufundabila: Str(e, "scufundabila"),
            Clasificare: Str(e, "clasificare"),
            Stiinta: Str(e, "stiinta"),
            Disparuta: Bool(e, "disparuta"),
            CodAp: Str(e, "codAp"));

        static string? Str(JsonElement o, string name) =>
            o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        static double? Num(JsonElement o, string name) =>
            o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)
                ? d
                : null;

        static int? Int(JsonElement o, string name) =>
            o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)
                ? i
                : null;

        static bool? Bool(JsonElement o, string name) => o.TryGetProperty(name, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => v.GetString() switch { "1" => true, "0" => false, _ => (bool?)null },
                JsonValueKind.Number => v.TryGetInt32(out var n) ? n != 0 : null,
                _ => null,
            }
            : null;
    }
}
