// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics.Metrics;
using SilexGis.Api.Diagnostics;
using SilexGis.Domain.Trips;

namespace SilexGis.Api.Features.TripTracking;

/// <summary>
/// Which of the published-trip routes a request reached, as one fixed word.
/// </summary>
/// <remarks>
/// Carried on each of those routes as endpoint metadata and handed to
/// <see cref="PublicTripDiagnostics"/> by its handler. A word rather than the route's pattern or
/// the request's path on purpose: the path of every one of these requests has a live credential in
/// it, and a diagnostic that named a route by its address would be one more place that credential
/// had to be scrubbed out of. A fixed word cannot carry it.
/// </remarks>
public sealed record PublicTripRoute(string Word)
{
    /// <summary>The followed page's own envelope.</summary>
    public static readonly PublicTripRoute Follow = new("follow");

    /// <summary>The parties in the link's cave right now.</summary>
    public static readonly PublicTripRoute Live = new("live");

    /// <summary>The list of the cave's past trips.</summary>
    public static readonly PublicTripRoute Past = new("past");

    /// <summary>One past trip, played back.</summary>
    public static readonly PublicTripRoute PastTrip = new("past_trip");

    /// <summary>
    /// A route under the published-trip window that carries no word of its own — said by the
    /// limiter's count rather than leaving such a route uncounted.
    /// </summary>
    public static readonly PublicTripRoute Unnamed = new("unnamed");
}

/// <summary>
/// What an operator can learn about the published-trip surface without it telling a visitor
/// anything: why a read was refused, and how much the surface is being read.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> Every refusal on these routes is one 404 with one code, deliberately,
/// so that a stranger holding a guess learns nothing from it. The cost of that was paid by the
/// person running the installation: "the club's article shows nothing" had eight possible causes
/// and the server said the same thing for all of them. This says which, in the installation's own
/// log, where only its operator reads.
/// </para>
/// <para>
/// <b>What it must never change.</b> Nothing here reaches a response — no header, no body member,
/// no status. Nothing here reads the database: the reason is worked out from what the route had
/// already read, so a token that was once real still costs exactly what an invented one costs. And
/// nothing here is given the request's path or writes the token: a link is named by the first
/// characters of its stored hash, the same handle the request log writes in place of the token and
/// the administrator's list of published links shows, so the three can be matched to one another
/// and none of them can be used to open the page.
/// </para>
/// <para>
/// <b>The counts are of the whole installation, never of one link.</b> A count per link would be
/// a readership figure for one family's page, which is a different thing to hold and was not
/// asked for; these say how busy the surface is and what it is answering.
/// </para>
/// <para>
/// Public, with its names as constants, because those names are an operator's vocabulary — the
/// counters a monitoring tool is pointed at, the template a log is searched for — and the tests
/// that hold them are in another assembly. What records a read stays internal to this one.
/// </para>
/// </remarks>
public sealed partial class PublicTripDiagnostics
{
    /// <summary>The meter's name — what a tool that watches counters is pointed at.</summary>
    public const string MeterName = "SilexGis.PublicTrips";

    /// <summary>
    /// Reads that reached a route, by <c>route</c> and <c>outcome</c>: <see cref="ServedOutcome"/>,
    /// or the reason word of a refusal.
    /// </summary>
    public const string ReadsCounterName = "silexgis.public_trip.reads";

    /// <summary>Reads the per-address window turned away before they reached a route, by <c>route</c>.</summary>
    public const string LimitedCounterName = "silexgis.public_trip.limited";

    /// <summary>The outcome word of a read that was answered.</summary>
    public const string ServedOutcome = "served";

    /// <summary>The tag both counters carry: the route's fixed word.</summary>
    public const string RouteTag = "route";

    /// <summary>The tag the reads counter carries beside the route.</summary>
    public const string OutcomeTag = "outcome";

    /// <summary>
    /// The refusal event's template. One event per refused read, at Information: it is the
    /// ordinary business of a published link to run out, and none of this is a fault.
    /// </summary>
    public const string RefusedMessage =
        "Published trip read refused: route {Route}, reason {Reason}, link {LinkHandle}";

    private readonly Counter<long> reads;
    private readonly Counter<long> limited;
    private readonly ILogger<PublicTripDiagnostics> logger;

    public PublicTripDiagnostics(IMeterFactory meters, ILogger<PublicTripDiagnostics> logger)
    {
        // Made through the factory rather than as a static, so that the meter belongs to this
        // application and is disposed with it: a process that builds several applications — which
        // is every test run — would otherwise add all of their reads into one instrument.
        var meter = meters.Create(MeterName);
        reads = meter.CreateCounter<long>(
            ReadsCounterName, unit: "{read}",
            description: "Reads of the published-trip routes that reached a route, by route and outcome.");
        limited = meter.CreateCounter<long>(
            LimitedCounterName, unit: "{read}",
            description: "Reads of the published-trip routes turned away by the per-address window, by route.");
        this.logger = logger;
    }

    /// <summary>A read that was answered with its page.</summary>
    internal void Served(PublicTripRoute route) =>
        reads.Add(1, new(RouteTag, route.Word), new(OutcomeTag, ServedOutcome));

    /// <summary>
    /// A read that was answered as an invented token is answered, and why.
    /// </summary>
    /// <param name="token">
    /// The token as the route received it. Used for its handle and for nothing else: it is not
    /// passed to the logger and is not a tag.
    /// </param>
    internal void Refused(PublicTripRoute route, PublishedReadRefusal reason, string token)
    {
        var word = WordOf(reason);
        reads.Add(1, new(RouteTag, route.Word), new(OutcomeTag, word));

        // Asked first so that an installation logging above this level does not hash a token per
        // refused read for a line nobody will write.
        if (!logger.IsEnabled(LogLevel.Information)) return;
        LogRefused(logger, route.Word, word, CredentialUrlScrubber.HandleOf(token));
    }

    /// <summary>A read the per-address window turned away.</summary>
    internal void Limited(PublicTripRoute route) => limited.Add(1, new KeyValuePair<string, object?>(RouteTag, route.Word));

    /// <summary>
    /// The word an operator reads for one reason.
    /// </summary>
    /// <remarks>
    /// Spelled out rather than taken from the member's name, because these words are what an
    /// operator searches a log for and what the installation guide prints: renaming a member in
    /// code must not silently rename them.
    /// </remarks>
    public static string WordOf(PublishedReadRefusal reason) => reason switch
    {
        PublishedReadRefusal.UnknownLink => "unknown_link",
        PublishedReadRefusal.Revoked => "revoked",
        PublishedReadRefusal.Expired => "expired",
        PublishedReadRefusal.WatchOff => "watch_off",
        PublishedReadRefusal.ClosedPastGrace => "closed_past_grace",
        PublishedReadRefusal.PastRetention => "past_retention",
        PublishedReadRefusal.CaveWithheld => "cave_withheld",
        PublishedReadRefusal.ArchiveOff => "archive_off",
        PublishedReadRefusal.TripNotInArchive => "trip_not_in_archive",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "A refusal with no word."),
    };

    [LoggerMessage(
        EventName = "PublishedTripReadRefused", Level = LogLevel.Information, Message = RefusedMessage)]
    private static partial void LogRefused(ILogger logger, string route, string reason, string linkHandle);
}
