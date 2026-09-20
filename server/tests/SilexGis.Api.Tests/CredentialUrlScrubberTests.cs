// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Serilog.Events;
using Shouldly;
using SilexGis.Api.Diagnostics;

namespace SilexGis.Api.Tests;

/// <summary>
/// Taking the credential out of an address before anything writes it down.
/// </summary>
/// <remarks>
/// <para>
/// Every assertion that something is gone is paired with one that says what is left, because a
/// scrubber that returned the empty string would pass every "the token is not in there" check ever
/// written and would make the log useless. What is left has to be enough to debug with, and the
/// tests below say exactly how much.
/// </para>
/// <para>
/// The token used throughout is forty-three characters of base64url, which is the shape this
/// application actually mints (32 random bytes), so nothing here passes because of a short or
/// oddly-shaped string.
/// </para>
/// </remarks>
public class CredentialUrlScrubberTests
{
    /// <summary>A token of the shape and length the publication endpoint really mints.</summary>
    private const string Token = "Zs7Kq2mX9pLrT4vN8wYhB1cD6eF3gJ0kM5nQ7sU2aI0";

    /// <summary>The handle a debugger sees — the first eight characters of the stored hash.</summary>
    private static string Handle(string token) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(token)))[..8];

    [Fact]
    public void A_follow_token_leaves_the_path_and_the_route_shape_stays()
    {
        var scrubbed = CredentialUrlScrubber.Scrub($"/api/v1/public/trips/{Token}")!;

        scrubbed.ShouldNotContain(Token);
        // The positive half, and the one that makes this worth doing rather than dropping the line:
        // the route is still identifiable, so "the published page is being polled" is still readable
        // off the log.
        scrubbed.ShouldStartWith("/api/v1/public/trips/");
        scrubbed.ShouldBe($"/api/v1/public/trips/[token:{Handle(Token)}]");
    }

    [Fact]
    public void The_handle_is_a_prefix_of_what_the_share_row_stores()
    {
        // The whole reason the credential is replaced by a handle rather than by a constant. An
        // operator reading a log line can find the row it belongs to without ever holding a token:
        //   select ... from trip_tracking_shares where token_hash like '<handle>%'
        // Asserted against the hash computed exactly as the mint computes what it stores.
        var stored = Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(Token)));

        var scrubbed = CredentialUrlScrubber.Scrub($"/api/v1/public/trips/{Token}")!;
        var handle = scrubbed["/api/v1/public/trips/[token:".Length..^1];

        stored.ShouldStartWith(handle);
        handle.Length.ShouldBe(8);
    }

    [Fact]
    public void Two_reads_of_one_link_carry_the_same_handle_and_two_links_do_not()
    {
        // What the handle buys beyond hiding the token: a log can still tell one follower polling
        // every minute from an article with two links in it. Without this the scrubbed lines would
        // be indistinguishable and the log would answer a question it used to answer.
        const string other = "Ab3Cd4Ef5Gh6Ij7Kl8Mn9Op0Qr1St2Uv3Wx4Yz5Aa6";

        var first = CredentialUrlScrubber.Scrub($"/api/v1/public/trips/{Token}");
        var again = CredentialUrlScrubber.Scrub($"/api/v1/public/trips/{Token}");
        var second = CredentialUrlScrubber.Scrub($"/api/v1/public/trips/{other}");

        first.ShouldBe(again);
        first.ShouldNotBe(second);
    }

    [Fact]
    public void The_followers_own_page_is_scrubbed_and_the_embed_suffix_survives()
    {
        // Both sides of the same published page. The suffix is kept on purpose: it is the
        // difference between somebody reading the page and somebody's article framing it.
        var page = CredentialUrlScrubber.Scrub($"/shared/trips/{Token}")!;
        var embed = CredentialUrlScrubber.Scrub($"/shared/trips/{Token}/embed")!;

        page.ShouldNotContain(Token);
        embed.ShouldNotContain(Token);
        page.ShouldBe($"/shared/trips/[token:{Handle(Token)}]");
        embed.ShouldBe($"/shared/trips/[token:{Handle(Token)}]/embed");
        embed.ShouldEndWith("/embed");
    }

    [Fact]
    public void A_delivery_token_leaves_the_query_and_every_other_parameter_stays()
    {
        // How a followed page fetches the survey model and each published photograph.
        var scrubbed = CredentialUrlScrubber.Scrub(
            $"/api/v1/files/0195a0d0-0000-7000-8000-000000000001/thumbnail?size=480&token={Token}")!;

        scrubbed.ShouldNotContain(Token);
        scrubbed.ShouldBe(
            "/api/v1/files/0195a0d0-0000-7000-8000-000000000001/thumbnail?size=480&token=[redacted]");
        // The positive half: the width is route shape, not secret, and a log that lost it could no
        // longer say which rendering was being asked for.
        scrubbed.ShouldContain("size=480");
    }

    [Fact]
    public void The_sibling_share_surfaces_are_scrubbed_too()
    {
        // These mint and store their tokens in exactly the same form. Scrubbing one of four
        // identical shapes would read as a decision that the other three were safe.
        foreach (var prefix in new[]
                 {
                     "/api/v1/shared/features/",
                     "/api/v1/shared/views/",
                     "/api/v1/public/albums/",
                 })
        {
            var scrubbed = CredentialUrlScrubber.Scrub($"{prefix}{Token}")!;
            scrubbed.ShouldNotContain(Token);
            scrubbed.ShouldBe($"{prefix}[token:{Handle(Token)}]");
        }
    }

    [Fact]
    public void Scrubbing_an_already_scrubbed_address_changes_nothing()
    {
        // Two things scrub an address here — the request-logging callback where the completion line
        // is built, and the enricher that catches every other event — and an event that goes
        // through both was, before this, scrubbed twice: the second pass took the hash of the
        // marker the first one left. Nothing leaked, and the line still read as a correctly
        // scrubbed line; what was destroyed was the one property that makes a handle worth having,
        // because a hash of "[token:…]" prefix-matches no row in any table. It was found by looking
        // at a running server's log and seeing two different handles for one request, which is the
        // only way it could have been found.
        var once = CredentialUrlScrubber.Scrub($"/api/v1/public/trips/{Token}")!;
        var twice = CredentialUrlScrubber.Scrub(once)!;

        twice.ShouldBe(once);
        // And the handle still leads back to the row, which is the thing double-scrubbing broke.
        Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(Token)))
            .ShouldStartWith(twice["/api/v1/public/trips/[token:".Length..^1]);

        var query = CredentialUrlScrubber.Scrub($"/api/v1/files/x/content?token={Token}")!;
        CredentialUrlScrubber.Scrub(query).ShouldBe(query);
    }

    [Fact]
    public void An_ordinary_address_is_handed_back_untouched()
    {
        // The other direction, and the one that keeps this from being a blunt instrument: an
        // address carrying no credential must come out exactly as it went in — same value, and the
        // same instance, because the enricher uses that to leave the event alone.
        const string ordinary = "/api/v1/trip-logs/0195a0d0-0000-7000-8000-000000000001/tracking";
        const string withQuery = "/api/v1/caves?page=3&pageSize=50";

        CredentialUrlScrubber.Scrub(ordinary).ShouldBeSameAs(ordinary);
        CredentialUrlScrubber.Scrub(withQuery).ShouldBeSameAs(withQuery);
        // A parameter whose name merely ends in the one that matters is not the one that matters.
        CredentialUrlScrubber.Scrub("/api/v1/caves?csrf_token=abc").ShouldBeSameAs(
            "/api/v1/caves?csrf_token=abc");
    }

    [Fact]
    public void Nothing_and_the_empty_string_are_answers_rather_than_exceptions() =>
        // This runs inside the logging pipeline, where throwing would turn a scrubbed line into no
        // line at all — which is a worse failure than the one it exists to prevent.
        Should.NotThrow(() =>
        {
            CredentialUrlScrubber.Scrub(null).ShouldBeNull();
            CredentialUrlScrubber.Scrub(string.Empty).ShouldBe(string.Empty);
            CredentialUrlScrubber.Scrub("/api/v1/public/trips/").ShouldBe("/api/v1/public/trips/");
        });

    [Fact]
    public void The_enricher_replaces_the_property_and_not_only_the_rendered_line()
    {
        // The reason this is an enricher at all. Serilog's request logging attaches `RequestPath`
        // to the event whether or not a template renders it, so a message template that simply did
        // not mention the path would look clean in a console sink while a JSON sink went on writing
        // the token in a field. Asserted against the property, which is what a structured sink
        // serialises.
        var evt = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            exception: null,
            MessageTemplate(),
            [new LogEventProperty("RequestPath", new ScalarValue($"/api/v1/public/trips/{Token}"))]);

        new CredentialScrubbingEnricher().Enrich(evt, new NullPropertyFactory());

        var written = ((ScalarValue)evt.Properties["RequestPath"]).Value as string;
        written.ShouldNotBeNull();
        written.ShouldNotContain(Token);
        written.ShouldBe($"/api/v1/public/trips/[token:{Handle(Token)}]");
    }

    [Fact]
    public void The_enricher_leaves_an_ordinary_request_alone()
    {
        // The positive twin: without it the test above would pass against an enricher that blanked
        // every path it was ever handed.
        const string ordinary = "/api/v1/trip-logs";
        var evt = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            exception: null,
            MessageTemplate(),
            [new LogEventProperty("RequestPath", new ScalarValue(ordinary))]);

        new CredentialScrubbingEnricher().Enrich(evt, new NullPropertyFactory());

        ((ScalarValue)evt.Properties["RequestPath"]).Value.ShouldBe(ordinary);
    }

    private static Serilog.Events.MessageTemplate MessageTemplate() =>
        new Serilog.Parsing.MessageTemplateParser().Parse("HTTP {RequestPath}");

    /// <summary>
    /// The factory Serilog hands an enricher. This one never creates a property, which is correct
    /// for the enricher under test: it replaces a property that already exists rather than adding
    /// one, and a factory that returned something would hide a call that should not happen.
    /// </summary>
    private sealed class NullPropertyFactory : Serilog.Core.ILogEventPropertyFactory
    {
        public LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false) =>
            new(name, new ScalarValue(value));
    }
}
