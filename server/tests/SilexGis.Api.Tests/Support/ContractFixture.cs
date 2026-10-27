// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Shouldly;

namespace SilexGis.Api.Tests.Support;

/// <summary>
/// Records, and then guards, the exact bytes of the mobile contract's traffic.
/// </summary>
/// <remarks>
/// <para>
/// A separate application is written against this protocol from its description alone, by people
/// who cannot run this suite. Prose about a payload drifts from the payload silently; a committed
/// file that the suite compares byte for byte cannot. So the recording is the artifact and the
/// comparison is the guard: a normal run asserts against what is committed and fails on any
/// difference, and a run with <c>SILEXGIS_CONTRACT_RECORD=1</c> rewrites the files instead, which
/// is the only way they change. A diff in that rewrite is a contract change and is reviewed as one.
/// </para>
/// <para>
/// Three things in a real response differ on every run and would make a byte comparison useless if
/// left alone, so they are replaced before anything is written or compared — never afterwards, or
/// the recorded files would be the ones that vary:
/// </para>
/// <list type="number">
/// <item>
/// <description>
/// <b>Identifiers.</b> Rows are created the way the application creates them, over its own HTTP
/// surface, which mints the identifier server-side and accepts none from the caller. There is no
/// seam to feed a fixed one through, so each is replaced by the name the test gave it —
/// <c>&lt;cave&gt;</c>, <c>&lt;place&gt;</c> — and an identifier the test did not name becomes
/// <c>&lt;uuid-1&gt;</c> in order of first appearance. That keeps the file readable and still fails
/// if a different row, or a row in a different position, starts appearing.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Times.</b> Every timestamp is stamped by the server from its own clock as the row is written.
/// They all become <c>&lt;timestamp&gt;</c>. Deliberately one placeholder and not a numbered
/// sequence: numbering them would encode which stamps happen to be equal in a given run, and two
/// writes inside one clock tick would then rewrite the file for no reason anybody could act on.
/// What the times actually have to do — order a page, resume a cursor, carry a deletion past a
/// watermark — is asserted by the tests around these recordings, where a wrong answer names itself.
/// A null stays null, which is the distinction that does belong in the file.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>The resume cursor.</b> It encodes a position that moves with the data, and it is opaque by
/// contract — a client may only store one and send it back — so recording its bytes would publish
/// an internal shape as though it were promised, and would rewrite the file whenever the data
/// moved. It becomes a placeholder.
/// </description>
/// </item>
/// </list>
/// <para>
/// Two more follow from the same reasoning. An identifier can sit inside a string — a page
/// address such as <c>/caves/{id}</c> — and is replaced there by the same name it gets standing
/// alone, so a link and the row it points at read as one. And a signed delivery address carries a
/// fresh token every time it is minted; its <c>token=</c> value becomes <c>&lt;token&gt;</c>, the
/// address around it stays, because its shape is what a client follows.
/// </para>
/// <para>
/// Only the response body and the request line are recorded. Headers are not, which is worth
/// saying because the obvious next candidate is a row version tag: an <c>ETag</c> on a sync
/// response would be derived from the database's own transaction counter, would advance with every
/// write anywhere, and nothing here would normalise it. Adding one means giving it a placeholder
/// first, or every unrelated write rewrites these files.
/// </para>
/// <para>
/// Recording writes into the working tree, not the build output the test reads its assemblies
/// from, so the files land where they can be committed.
/// </para>
/// </remarks>
public sealed class ContractFixture
{
    /// <summary>Set to <c>1</c> to rewrite the committed files instead of asserting against them.</summary>
    public const string RecordVariable = "SILEXGIS_CONTRACT_RECORD";

    /// <summary>The field-device sync protocol's directory under <c>contract/</c>.</summary>
    public const string SpeleoLocSurface = "speleoloc-sync";

    /// <summary>The survey-upload protocol's directory under <c>contract/</c>.</summary>
    public const string TopoDroidSurface = "topodroid-sync";

    private const string Version = "v1";

    /// <summary>
    /// An identifier anywhere inside a string. Only applied after the whole-string checks, so a
    /// value that is nothing but an identifier is still told apart from one that merely holds one.
    /// </summary>
    private static readonly Regex EmbeddedUuid = new(
        @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The token of a signed delivery address, which is minted afresh on every answer.</summary>
    private static readonly Regex DeliveryToken = new(
        @"(?<=[?&]token=)[^&#\s]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string surface;

    /// <summary>
    /// Times as this API writes them: an ISO-8601 instant with an offset or a trailing Z. Anchored
    /// at both ends so an identifier or a name that merely contains digits is left alone.
    /// </summary>
    private static readonly Regex Timestamp = new(
        @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Uuid = new(
        @"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly Dictionary<string, string> named = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> discovered = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string Value, string Placeholder)> literals = [];

    /// <summary>A fixture writing under one protocol's directory of <c>contract/</c>.</summary>
    /// <param name="surface">The protocol's directory name; the field-device sync protocol's when omitted.</param>
    public ContractFixture(string surface = SpeleoLocSurface) => this.surface = surface;

    public static bool Recording =>
        Environment.GetEnvironmentVariable(RecordVariable) is "1" or "true";

    /// <summary>Gives one identifier a name, so it reads as itself in the recorded file.</summary>
    public ContractFixture Name(Guid id, string label)
    {
        named[id.ToString()] = $"<{label}>";
        return this;
    }

    /// <summary>
    /// Replaces a run-specific literal wherever it appears inside a string — the per-run suffix
    /// every test puts in the names it creates, so two suites can share one database.
    /// </summary>
    public ContractFixture Literal(string value, string label)
    {
        literals.Add((value, $"<{label}>"));
        return this;
    }

    /// <summary>
    /// Writes or checks one exchange. <paramref name="caseName"/> is the directory it lands in.
    /// </summary>
    public Task AssertAsync(string caseName, string requestLine, HttpResponseMessage response) =>
        AssertAsync(caseName, requestLine, requestBody: null, response);

    /// <summary>
    /// Writes or checks one exchange that carried a body up as well as down.
    /// </summary>
    /// <remarks>
    /// A read is fully described by its request line, and the first recordings here were all
    /// reads. A write is not: what a device sends is half of what has to be got right, and it is
    /// the half the other application has to construct rather than merely parse. So a case with a
    /// request body records it too, scrubbed by the same rules as the answer — the identifiers a
    /// device mints for its own rows appear on both sides of the exchange, and a recording that
    /// pinned them on one side and named them on the other would read as two different rows.
    /// </remarks>
    public Task AssertAsync(
        string caseName, string requestLine, string? requestBody, HttpResponseMessage response) =>
        WriteExchangeAsync(caseName, requestLine, requestBody, response, status: null);

    /// <summary>
    /// Reads one address, records the exchange as it happened, and hands the answer back for the
    /// caller's own assertions — so the recording and the assertions are about the same bytes.
    /// </summary>
    public async Task<JsonElement> RecordGetAsync(string caseName, HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        var payload = await response.Content.ReadAsStringAsync();
        ((int)response.StatusCode).ShouldBe(200, payload);
        await AssertAsync(caseName, $"GET {url}", response);
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Sends one JSON body, records what went up and what came back, and hands the answer back.
    /// The body is serialised here rather than by <c>PostAsJsonAsync</c>, so the recorded request
    /// is the bytes that were sent rather than a second rendering of the same object.
    /// </summary>
    public async Task<JsonElement> RecordPostAsync(string caseName, HttpClient client, string url, object body)
    {
        var json = JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var response = await client.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));
        var payload = await response.Content.ReadAsStringAsync();
        ((int)response.StatusCode).ShouldBe(200, payload);
        await AssertAsync(caseName, $"POST {url}", json, response);
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Sends one JSON body the server is expected to refuse, and records the refusal with its status.
    /// </summary>
    public async Task<JsonElement> RecordRefusedPostAsync(string caseName, HttpClient client, string url, object body)
    {
        var json = JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var response = await client.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));
        var payload = await response.Content.ReadAsStringAsync();
        await AssertRefusalAsync(caseName, $"POST {url}", json, response);
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Reads one address the server is expected to refuse, and records the refusal with its status.
    /// </summary>
    public async Task<JsonElement> RecordRefusedGetAsync(string caseName, HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        var payload = await response.Content.ReadAsStringAsync();
        await AssertRefusalAsync(caseName, $"GET {url}", response);
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Writes or checks one exchange the server refused.
    /// </summary>
    /// <remarks>
    /// A refusal is not described by its body alone. The status is what a client branches on
    /// before it has looked at a single field, and it travels in the status line rather than in
    /// the payload, so it lands in a <c>status.txt</c> beside the answer. A successful exchange
    /// gets no such file: its status is 200 by construction, and a file saying so on every case
    /// would be noise that hid the ones where the value is the point.
    /// </remarks>
    public Task AssertRefusalAsync(
        string caseName, string requestLine, HttpResponseMessage response) =>
        AssertRefusalAsync(caseName, requestLine, requestBody: null, response);

    /// <summary>
    /// Writes or checks one successful exchange whose status is not a plain 200 — a 201 for
    /// something made, a 202 for work queued — with the status beside it, because a client
    /// branches on it as surely as on a refusal's.
    /// </summary>
    public Task AssertWithStatusAsync(
        string caseName, string requestLine, string? requestBody, HttpResponseMessage response)
    {
        ((int)response.StatusCode).ShouldBeLessThan(
            400, $"{caseName} is recorded as a success, so the server refusing it is what has changed.");

        return WriteExchangeAsync(
            caseName,
            requestLine,
            requestBody,
            response,
            status: $"{(int)response.StatusCode} {response.StatusCode}\n");
    }

    /// <inheritdoc cref="AssertRefusalAsync(string, string, HttpResponseMessage)"/>
    public Task AssertRefusalAsync(
        string caseName, string requestLine, string? requestBody, HttpResponseMessage response)
    {
        ((int)response.StatusCode).ShouldBeGreaterThanOrEqualTo(
            400,
            $"{caseName} is recorded as a refusal, so the server answering it successfully is the "
            + "thing that has changed, not the recorded bytes.");

        return WriteExchangeAsync(
            caseName,
            requestLine,
            requestBody,
            response,
            status: $"{(int)response.StatusCode} {response.StatusCode}\n");
    }

    private async Task WriteExchangeAsync(
        string caseName,
        string requestLine,
        string? requestBody,
        HttpResponseMessage response,
        string? status)
    {
        var raw = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(raw);

        // The request first, so that an identifier neither side named is numbered in the order a
        // reader meets it: sent, then answered.
        var sent = requestBody is null ? null : Render(requestBody);
        var body = Render(document.RootElement);
        var request = Scrub(requestLine) + "\n";

        var directory = Path.Combine(ContractRoot(surface), caseName);
        await CompareOrWriteAsync(Path.Combine(directory, "request.txt"), request);
        if (sent is not null)
        {
            await CompareOrWriteAsync(Path.Combine(directory, "request.json"), sent);
        }

        if (status is not null)
        {
            await CompareOrWriteAsync(Path.Combine(directory, "status.txt"), status);
        }

        await CompareOrWriteAsync(Path.Combine(directory, "response.json"), body);
    }

    private string Render(string json)
    {
        using var document = JsonDocument.Parse(json);
        return Render(document.RootElement);
    }

    /// <summary>
    /// One payload as it lands in a file: re-serialised indented rather than copied from the wire,
    /// so a person can read it and a difference in it points at the field that moved. The relaxed
    /// encoder is what keeps the placeholders legible; it means the file is a rendering of the
    /// payload and not its literal bytes, which is the right trade for a document somebody has to
    /// read. Written as LF and read back as LF — the repository normalises text on the way in, so
    /// a recording made with the platform's line ending would pass here and fail for the next
    /// person to check the file out.
    /// </summary>
    private string Render(JsonElement root)
    {
        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(
            buffer,
            new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            Write(writer, root);
        }

        return Encoding.UTF8.GetString(buffer.ToArray()).ReplaceLineEndings("\n") + "\n";
    }

    /// <summary>
    /// Holds one file in the contract directory against what is committed, or rewrites it when
    /// the suite is recording. Shared so that everything in that directory changes by one act and
    /// obeys one rule about line endings, placeholders and encoding.
    /// </summary>
    internal static Task CompareOrWriteAsync(string path, string content) =>
        CompareOrWriteAsync(path, content, Recording);

    /// <summary>
    /// The same, with the decision to write made by the caller rather than read from the
    /// recording switch. The manifest needs it: it is generated from a walk of the directory
    /// these files live in, so it is taken in a pass of its own rather than in the pass that is
    /// rewriting them, and that pass is not a recording run.
    /// </summary>
    internal static async Task CompareOrWriteAsync(
        string path, string content, bool write, string? remedy = null)
    {
        if (write)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, content, new UTF8Encoding(false));
            return;
        }

        var howToRewrite = remedy
            ?? $"re-run with {RecordVariable}=1 and review the rewritten file as a contract change "
            + "— the application on the other side of this protocol is written against these bytes";

        File.Exists(path).ShouldBeTrue(
            $"No recorded contract file at {path}. To write it, {howToRewrite}.");

        var recorded = (await File.ReadAllTextAsync(path)).ReplaceLineEndings("\n");
        recorded.ShouldBe(
            content,
            $"{path} no longer matches what the server sends. If the change is intended, "
            + $"{howToRewrite}.");
    }

    /// <summary>
    /// The working tree's contract directory, found by walking up from the assembly rather than
    /// hard-coding a depth. The assembly runs from a build output directory, and writing the
    /// recording there would leave it where nothing can commit it.
    /// </summary>
    internal static string ContractRoot(string surface = SpeleoLocSurface)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "server", "SilexGis.slnx")))
        {
            directory = directory.Parent;
        }

        directory.ShouldNotBeNull("Could not find the working tree above the test assembly.");
        return Path.Combine(directory.FullName, "contract", surface, Version);
    }

    private void Write(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);

                    // A cursor is opaque by contract: a device stores it and hands it back, and
                    // nothing else may read it. Recording its bytes would publish a shape the
                    // server has not promised and would rewrite this file whenever the data moves.
                    if (property.Name is "nextCursor" && property.Value.ValueKind is JsonValueKind.String)
                    {
                        writer.WriteStringValue("<cursor>");
                        continue;
                    }

                    // The correlation identifier the framework attaches to every problem
                    // document. It is a fresh trace on every request by definition, and it is
                    // for reading a server log with, not for a client to act on.
                    if (property.Name is "traceId" or "requestId"
                        && property.Value.ValueKind is JsonValueKind.String)
                    {
                        writer.WriteStringValue("<trace>");
                        continue;
                    }

                    Write(writer, property.Value);
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    Write(writer, item);
                }

                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                writer.WriteStringValue(Scrub(element.GetString()!));
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }

    private string Scrub(string value)
    {
        if (Timestamp.IsMatch(value))
        {
            return "<timestamp>";
        }

        if (Uuid.IsMatch(value))
        {
            return Placeholder(value);
        }

        foreach (var (literal, placeholder) in literals)
        {
            value = value.Replace(literal, placeholder, StringComparison.Ordinal);
        }

        foreach (var (id, label) in named)
        {
            value = value.Replace(id, label, StringComparison.OrdinalIgnoreCase);
        }

        value = DeliveryToken.Replace(value, "<token>");
        return EmbeddedUuid.Replace(value, match => Placeholder(match.Value));
    }

    private string Placeholder(string uuid)
    {
        if (named.TryGetValue(uuid, out var label))
        {
            return label;
        }

        if (!discovered.TryGetValue(uuid, out var placeholder))
        {
            placeholder = $"<uuid-{discovered.Count + 1}>";
            discovered[uuid] = placeholder;
        }

        return placeholder;
    }
}
