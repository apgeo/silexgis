// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;

namespace SilexGis.Api.Features.TripTracking;

/// <summary>
/// The validator of one published-trip answer: a short value that is the same for two answers
/// exactly when a reader holding the first has no use for the second.
/// </summary>
/// <remarks>
/// <para>
/// <b>Worked out from what is about to be sent, not from what was read to build it.</b> A stamp
/// taken from the tables — the newest change, a row count — has to name every input of the answer,
/// and these answers have a dozen: the link, the trip, the watch, the teams, the captions, the
/// roster, the names, the reports, the protection of every cave a report is anchored to, the camp,
/// the survey and its pictures and map sheets. One input forgotten is a family looking at a page
/// that is quietly out of date, and every later addition to the answer is one more thing to
/// remember. A value derived from the answer itself cannot forget anything: whatever changes what
/// is sent changes the validator. What it does not save is the reading — the answer is built in
/// full before it can be compared — so this spares the reader's connection and the page's redraw
/// and nothing of the database's work.
/// </para>
/// <para>
/// <b>What is left out, and what replaces it.</b> An answer may carry signed, short-lived
/// addresses — the survey to draw, a picture, a map sheet — and each is signed afresh on every
/// read, so two answers that say the same thing differ in those values and in nothing else. The
/// signature is therefore left out of the comparison. Left out altogether, a reader could go on
/// being told "unchanged" about addresses that had since stopped working; so an answer that
/// carries any is also marked with the stretch of time it was made in
/// (<see cref="SignedAddressBucket"/>), and once that stretch is over the same answer has a new
/// validator and is sent whole, with fresh addresses. An answer with no such address — the two
/// lists — has nothing that runs out, and its validator moves only with its content.
/// </para>
/// <para>
/// <b>Found by walking the answer, not by naming members.</b> Every text in the answer is looked
/// at, wherever it sits, so an address added to these answers later is covered without anybody
/// remembering this file. A text somebody typed that happens to look like a signed address is
/// treated as one: that answer gets a new validator each stretch, and a change to the part of the
/// text read as a signature reaches a reader one stretch late at worst.
/// </para>
/// </remarks>
public static class PublicTripValidator
{
    /// <summary>
    /// The query parameter the file delivery routes read their signature from.
    /// </summary>
    private const string SignatureParameter = "token=";

    /// <summary>
    /// How long an answer carrying a signed address keeps one validator: half the life of the
    /// address.
    /// </summary>
    /// <remarks>
    /// A reader's copy is only ever confirmed inside the stretch it was made in, so a copy that
    /// has just been confirmed is younger than one stretch and its addresses have more than half
    /// their life left — time enough to fetch what they name. A longer stretch would confirm
    /// copies whose addresses are about to stop; a much shorter one would send the whole answer
    /// to a page that asks once a minute nearly as often as before.
    /// </remarks>
    public static TimeSpan SignedAddressBucket => FileAccessTokenService.FetchLifetime / 2;

    /// <summary>
    /// The validator of <paramref name="answer"/> as it would be sent at <paramref name="now"/>.
    /// </summary>
    /// <param name="answer">The value the route is about to serialise.</param>
    /// <param name="options">
    /// The options the response is serialised with, so that what is compared is what is sent.
    /// </param>
    /// <param name="now">Read only when the answer carries a signed address.</param>
    /// <returns>
    /// Lower-case hexadecimal — an alphabet with no comma, no quote and no hyphen, so the value
    /// survives a list of tags and can never end in a suffix a compressing proxy appends.
    /// </returns>
    public static string Of(object answer, JsonSerializerOptions options, DateTimeOffset now)
    {
        var sent = JsonSerializer.SerializeToNode(answer, answer.GetType(), options);
        var signed = false;
        sent = WithoutSignatures(sent, ref signed);

        var compared = new StringBuilder(sent?.ToJsonString() ?? "null");
        if (signed)
        {
            // On a line of its own: the compact form above holds no line break, so the stretch
            // cannot be mistaken for, or forged by, anything inside the answer.
            compared.Append('\n').Append(
                (now.ToUnixTimeSeconds() / (long)SignedAddressBucket.TotalSeconds)
                .ToString(CultureInfo.InvariantCulture));
        }

        // Half of the digest: this tells one answer from the next for one reader, and is not a
        // secret or a proof of anything.
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(compared.ToString()));
        return Convert.ToHexStringLower(digest.AsSpan(0, 16));
    }

    private static JsonNode? WithoutSignatures(JsonNode? node, ref bool signed)
    {
        switch (node)
        {
            case JsonObject members:
                foreach (var name in members.Select(member => member.Key).ToList())
                {
                    var member = members[name];
                    var replaced = WithoutSignatures(member, ref signed);
                    if (!ReferenceEquals(replaced, member)) members[name] = replaced;
                }

                return members;

            case JsonArray items:
                for (var i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    var replaced = WithoutSignatures(item, ref signed);
                    if (!ReferenceEquals(replaced, item)) items[i] = replaced;
                }

                return items;

            case JsonValue value when value.GetValueKind() == JsonValueKind.String:
                var text = value.GetValue<string>();
                var unsigned = WithoutSignature(text);
                if (ReferenceEquals(unsigned, text)) return node;
                signed = true;
                return JsonValue.Create(unsigned);

            default:
                return node;
        }
    }

    /// <summary>
    /// An address with the value of its signature removed, or the very same text when it carries
    /// none.
    /// </summary>
    private static string WithoutSignature(string text)
    {
        StringBuilder? kept = null;
        var from = 0;
        while (true)
        {
            var at = text.IndexOf(SignatureParameter, from, StringComparison.Ordinal);
            if (at < 0) break;

            var valueStart = at + SignatureParameter.Length;
            if (at == 0 || text[at - 1] is not ('?' or '&'))
            {
                from = valueStart;
                continue;
            }

            var valueEnd = text.AsSpan(valueStart).IndexOfAny('&', '#');
            valueEnd = valueEnd < 0 ? text.Length : valueStart + valueEnd;

            kept ??= new StringBuilder(text.Length);
            kept.Append(text, 0, valueStart);
            text = text[valueEnd..];
            from = 0;
        }

        return kept is null ? text : kept.Append(text).ToString();
    }
}

/// <summary>
/// Lets a reader of a published-trip route keep its copy of the answer and be told, on asking
/// again, that nothing changed.
/// </summary>
/// <remarks>
/// <para>
/// <b>After the route has decided, never before.</b> The route runs first and in full: the link is
/// looked up, its windows are read against the clock, the cave's protection is asked again. Only
/// an answer it chose to give is compared with what the reader says it holds. A link taken back,
/// or a cave that has since been protected, is therefore refused exactly as it is to a reader who
/// holds nothing — a validator is a claim about a copy, not a credential, and "unchanged" said to
/// a reader who may no longer read would be a way of going on reading.
/// </para>
/// <para>
/// <b>What a reader and anything between may keep.</b> An answer is the reader's own and must be
/// asked about every time it is used: a browser keeps it and confirms it, and nothing shared
/// between readers may keep it at all — the address it was read at is a credential, and an answer
/// stored under it would outlive the link being taken back. A refusal is kept by nobody, so a
/// link that starts answering again — a watch started again, an archive switched back on — is not
/// hidden behind a remembered "not found".
/// </para>
/// <para>
/// <b>The refusal stays one answer.</b> Every refusal gets the same header and no validator,
/// whatever its reason, as it gets the same status and the same body.
/// </para>
/// </remarks>
internal sealed class PublicTripValidatorFilter(TimeProvider clock, IOptions<JsonOptions> json) : IEndpointFilter
{
    /// <summary>The reader may keep its copy and must ask before using it; nobody else may keep one.</summary>
    internal const string Revalidated = "private, no-cache";

    /// <summary>Kept by nobody.</summary>
    internal const string NotKept = "no-store";

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context);
        var http = context.HttpContext;

        // A route declares the answers it can give as one union; what it chose is inside.
        var decided = result is INestedHttpResult union ? union.Result : result;
        if (decided is not IStatusCodeHttpResult { StatusCode: StatusCodes.Status200OK }
            || decided is not IValueHttpResult { Value: { } answer })
        {
            http.Response.Headers.CacheControl = NotKept;
            return result;
        }

        var tag = Concurrency.WeakETag(
            PublicTripValidator.Of(answer, json.Value.SerializerOptions, clock.GetUtcNow()));
        http.Response.Headers.CacheControl = Revalidated;
        http.Response.Headers.ETag = tag;

        // The headers above go out either way: a reader told "unchanged" is told again what it
        // holds and on what terms it may keep it.
        return Concurrency.MatchesIfNoneMatch(http, tag)
            ? TypedResults.StatusCode(StatusCodes.Status304NotModified)
            : result;
    }
}

internal static class PublicTripValidatorExtensions
{
    /// <summary>
    /// Puts a published-trip route behind <see cref="PublicTripValidatorFilter"/> and says so in
    /// the contract.
    /// </summary>
    internal static RouteHandlerBuilder WithPublicTripValidator(this RouteHandlerBuilder route) =>
        route.AddEndpointFilter<PublicTripValidatorFilter>()
            .Produces(StatusCodes.Status304NotModified);
}
