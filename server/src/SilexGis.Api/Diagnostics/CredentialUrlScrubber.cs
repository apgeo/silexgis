// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace SilexGis.Api.Diagnostics;

/// <summary>
/// Takes the credential out of a URL before anything writes it down.
/// </summary>
/// <remarks>
/// <para>
/// <b>Some of this application's addresses <em>are</em> the password.</b> A follow link for a
/// published trip, a feature or album share, a signed delivery URL — each carries its whole claim
/// in the address, because the caller is a browser loading a page or an image and can attach no
/// header. That is a deliberate design and it is fine until something writes the address in a
/// file: a request log is then a list of live credentials, and it is the one artefact that gets
/// copied around casually. An operator reads it, a support bundle ships it, a crash report quotes
/// it — and whoever holds that file can open every published page on the installation for as long
/// as the links live.
/// </para>
/// <para>
/// So the rule is applied where the writing happens rather than trusted to each call site, and it
/// is one table rather than one regular expression per log line. The same knowledge is stated a
/// second time in the web server's configuration, because the proxy in front writes its own access
/// log and never asks this code anything; the two are kept honest by a test over the configuration
/// files, and the wording below says what each of them can and cannot do.
/// </para>
/// <para>
/// <b>What a scrubbed line still tells a debugger, which is the whole reason this is not a
/// blanket "[redacted]".</b> The method, the route shape, the status and the timing all survive
/// untouched, so every question a request log is normally asked — which endpoint is slow, what is
/// 500ing, how often the published page is polled — is answered exactly as before. And the
/// credential is replaced by a <em>handle</em> rather than by a constant: the first eight
/// characters of the same hash the database stores. That means two lines about the same link still
/// look like the same link (so "one follower is polling" and "this article has forty readers" stay
/// distinguishable), and an operator with a real question can go the other way and find the row —
/// <c>select id, trip_log_id, created_at, revoked_at, expires_at from trip_tracking_shares where
/// token_hash like '<em>handle</em>%'</c> — without ever holding a token. The handle is a prefix
/// of a SHA-256 over 32 random bytes, so nothing can be walked back from it: it names which link
/// was used and says nothing about what the link is.
/// </para>
/// <para>
/// <b>What this cannot reach, stated rather than left to be discovered.</b> Only the paths this
/// table names are scrubbed, so a credential-bearing address added elsewhere keeps being written
/// in full until it is added here — which is why the table is a table and why the shapes are
/// listed with their reasons. And it governs Serilog's view of a request only: a proxy's error log
/// (as opposed to its access log) quotes the request line it failed on and is written by the proxy
/// itself, so an upstream timeout can still put a token in an error file. That residue is the
/// reason the links now expire at all rather than something this class can fix.
/// </para>
/// </remarks>
public static class CredentialUrlScrubber
{
    /// <summary>
    /// The path prefixes whose next segment is a credential and not an identifier.
    /// </summary>
    /// <remarks>
    /// Each of these is a capability somebody chose to hand out, and the segment after the prefix
    /// is the whole of the holder's claim. The first two are the same published page seen from its
    /// two sides — the address a follower's browser is on, and the address the page's own script
    /// calls — and both are written down by the proxy, so leaving either would leave the link
    /// readable. The other three are the sibling share surfaces, which mint and store their tokens
    /// in exactly this form; they are listed because the knowledge here is "a URL segment that is a
    /// credential" rather than "the trip page", and a table that scrubbed one of four identical
    /// shapes would be an invitation to assume the other three were considered and found safe.
    /// </remarks>
    private static readonly string[] CredentialPathPrefixes =
    [
        // The follower's own address, served by the web server as the single-page application.
        "/shared/trips/",
        // The envelope that page reads, and the one the framed viewer reads.
        "/api/v1/public/trips/",
        "/api/v1/shared/features/",
        "/api/v1/shared/views/",
        "/api/v1/public/albums/",
    ];

    /// <summary>
    /// The query parameter every signed delivery URL carries its capability in — the survey model
    /// and each published photograph on a followed page among them.
    /// </summary>
    private const string CredentialQueryParameter = "token";

    /// <summary>What replaces a credential when there is nothing useful to say about it.</summary>
    private const string Redacted = "[redacted]";

    /// <summary>How a replaced path segment opens, so an already-scrubbed one can be recognised.</summary>
    private const string HandlePrefix = "[token:";

    /// <summary>
    /// How many characters of the stored hash stand in for the credential. Eight is enough to tell
    /// one live link from another in a log and to prefix-match a row, and short enough that the
    /// handle is plainly a handle rather than something to be mistaken for a token.
    /// </summary>
    private const int HandleLength = 8;

    /// <summary>
    /// The same value with every credential in it replaced, or the value unchanged when it holds
    /// none. Returns <see langword="null"/> for null, so a caller can hand it whatever it has.
    /// </summary>
    /// <remarks>
    /// Deliberately total and allocation-free on the ordinary path: this runs on every request that
    /// is logged, and the overwhelming majority of addresses carry no credential at all and must
    /// cost nothing but a handful of prefix comparisons.
    /// </remarks>
    public static string? Scrub(string? url)
    {
        if (string.IsNullOrEmpty(url)) return url;

        var scrubbed = ScrubQuery(url);
        return ScrubPath(scrubbed);
    }

    /// <summary>
    /// Replaces the credential segment that follows one of the known prefixes, keeping whatever
    /// follows it.
    /// </summary>
    /// <remarks>
    /// The tail is kept because it is route shape rather than secret — <c>/embed</c> after a follow
    /// token is the difference between somebody reading the page and somebody's article framing it,
    /// which is exactly the kind of thing a log is read to find out.
    /// </remarks>
    private static string ScrubPath(string url)
    {
        foreach (var prefix in CredentialPathPrefixes)
        {
            var at = url.IndexOf(prefix, StringComparison.Ordinal);
            if (at < 0) continue;

            var start = at + prefix.Length;
            var end = start;
            while (end < url.Length && url[end] is not ('/' or '?' or '#'))
            {
                end++;
            }

            if (end == start) continue;

            // Already done, and this is load-bearing rather than defensive. Two things scrub this
            // application's addresses — the request-logging callback, where the completion line is
            // built, and the enricher that catches every other event — and a line that goes through
            // both would otherwise be scrubbed twice, with the second pass taking the hash of the
            // marker the first one left. Nothing leaks either way; what is destroyed is the one
            // property that makes a handle worth having, because a hash of "[token:…]" prefix-
            // matches no row in any table. Silently: the line still reads as a correctly scrubbed
            // line, and the only way to notice is to try to look a handle up and find nothing.
            var segment = url[start..end];
            if (segment.StartsWith(HandlePrefix, StringComparison.Ordinal)
                || string.Equals(segment, Redacted, StringComparison.Ordinal))
            {
                return url;
            }

            return string.Concat(
                url.AsSpan(0, start),
                Handle(segment),
                url.AsSpan(end));
        }

        return url;
    }

    /// <summary>
    /// Replaces the value of the delivery-token parameter wherever it appears, keeping every other
    /// parameter — the width a thumbnail was asked for, a page number — exactly as it was.
    /// </summary>
    /// <remarks>
    /// No handle here, and that is the honest answer rather than an omission. A delivery token is
    /// stateless: it is signed and carries its own reach and lifetime, and there is no row anywhere
    /// for a handle to lead to. A tag that matched nothing would read like one that did.
    /// </remarks>
    private static string ScrubQuery(string url)
    {
        var query = url.IndexOf('?');
        if (query < 0) return url;

        // Nothing to do, and said before any allocation: most addresses that carry a query carry
        // no credential in it, and this runs on every logged request.
        if (url.IndexOf(CredentialQueryParameter + "=", query, StringComparison.Ordinal) < 0)
        {
            return url;
        }

        var builder = new StringBuilder(url.Length);
        builder.Append(url, 0, query + 1);

        var first = true;
        var replaced = false;
        foreach (var pair in url[(query + 1)..].Split('&'))
        {
            if (!first) builder.Append('&');
            first = false;

            var equals = pair.IndexOf('=', StringComparison.Ordinal);
            if (equals >= 0
                && string.Equals(pair[..equals], CredentialQueryParameter, StringComparison.Ordinal)
                // Same reason as the path above: a value that is already the marker is left as it
                // is, so an address that passes through both scrubbing points comes out identical
                // to one that passed through either.
                && !string.Equals(pair[(equals + 1)..], Redacted, StringComparison.Ordinal))
            {
                builder.Append(CredentialQueryParameter).Append('=').Append(Redacted);
                replaced = true;
            }
            else
            {
                builder.Append(pair);
            }
        }

        // The original instance when nothing was taken out — `token=` can appear inside the name of
        // a parameter that is not it (`csrf_token=`), and an address that was not changed must come
        // back unchanged so the enricher can leave the log event alone.
        return replaced ? builder.ToString() : url;
    }

    /// <summary>
    /// The prefix of the stored hash that stands in for one token.
    /// </summary>
    /// <remarks>
    /// Computed the same way the share rows compute what they store — SHA-256 of the token's UTF-8
    /// bytes, base64url — so the handle in a log line is a literal prefix of the
    /// <c>token_hash</c> column and can be matched against it with <c>like</c>. Written here rather
    /// than borrowed from the share slice on purpose: this must go on working for a token shape it
    /// has never seen, including one minted by a surface that does not exist yet, so it hashes the
    /// segment it was given and asks nothing about where it came from.
    /// </remarks>
    private static string Handle(string credential)
    {
        var hash = Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(credential)));
        return $"{HandlePrefix}{hash[..HandleLength]}]";
    }
}
