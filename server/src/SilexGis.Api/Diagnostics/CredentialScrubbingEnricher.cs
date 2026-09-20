// SPDX-License-Identifier: AGPL-3.0-or-later
using Serilog.Core;
using Serilog.Events;

namespace SilexGis.Api.Diagnostics;

/// <summary>
/// Rewrites the request address on every log event on its way to every sink.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the net, not the fix.</b> The one event that carries a live credential by design is
/// the request-completion line, and that one is scrubbed where it is built — the request-logging
/// options hand the whole property list to a callback in <c>Program</c>, which replaces
/// <c>RequestPath</c> before the event exists. This enricher catches everything else: a warning
/// from the framework, the context on an unhandled exception, anything written inside a request
/// that names the address. Each of those would otherwise have needed its own fix, and none of them
/// is anybody's first thought.
/// </para>
/// <para>
/// <b>An enricher rather than a message template, for both.</b> The obvious-looking fix is a
/// template that does not mention <c>{RequestPath}</c> — but the property is attached to the event
/// whether or not a template renders it, so a console sink would look clean while a JSON sink went
/// on writing the token in a field, and an installation that added a structured sink would
/// reintroduce the leak by editing configuration with nothing anywhere to say so. Replacing the
/// property scrubs the rendered line <em>and</em> the structured payload, in every sink.
/// </para>
/// <para>
/// <b>It has to be attached to both loggers.</b> The host keeps the bootstrap logger as
/// <c>Log.Logger</c> (<c>preserveStaticLogger</c>), and some of what this application writes goes
/// out through that one rather than through the configured pipeline — so an enricher added only to
/// the configured logger would silently cover half of what it looks like it covers. Both are
/// enriched in <c>Program</c>, and this paragraph is the reason not to tidy one of them away.
/// </para>
/// <para>
/// The property names are a short list rather than "anything that looks like a URL", because
/// rewriting an arbitrary string because it resembles an address is how a log quietly starts lying
/// about data that was never a credential. These are the names ASP.NET Core and Serilog actually
/// use for the address of the request in hand; the scrubber itself is a no-op on anything that is
/// not one of the shapes it knows, so a <c>Path</c> that happens to be a file path passes through
/// untouched.
/// </para>
/// <para>
/// <c>QueryString</c> is on the list and is not an afterthought. The framework's own "Request
/// starting" and "Request finished" lines split the address across
/// <c>{Path}{QueryString}</c> — so a signed delivery URL, which is how a followed page fetches the
/// survey drawing and every photograph on it, puts its whole credential in the second half and
/// none of it in the first. Those two lines are quiet at this application's shipped level and
/// loud at the level a development instance runs, which is precisely the kind of leak that is
/// found by somebody reading a log rather than by anything failing.
/// </para>
/// </remarks>
public sealed class CredentialScrubbingEnricher : ILogEventEnricher
{
    private static readonly string[] AddressProperties =
        ["RequestPath", "RequestUri", "Path", "QueryString"];

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var name in AddressProperties)
        {
            if (!logEvent.Properties.TryGetValue(name, out var value)
                || value is not ScalarValue { Value: string address })
            {
                continue;
            }

            var scrubbed = CredentialUrlScrubber.Scrub(address);
            // Compared rather than written unconditionally: the overwhelming majority of requests
            // carry no credential, and an event whose properties were not touched is one less
            // allocation on the hot path of every logged request.
            if (!ReferenceEquals(scrubbed, address))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(name, new ScalarValue(scrubbed)));
            }
        }
    }
}
