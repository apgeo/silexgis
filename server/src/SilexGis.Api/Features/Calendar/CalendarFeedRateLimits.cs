// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Api.Features.Calendar;

/// <summary>
/// The bound on how often one subscription address may be polled.
/// </summary>
/// <remarks>
/// <para>
/// <b>Partitioned on the token, not on the caller's address</b>, which is the opposite of every
/// other window this application keeps. The sign-in, printed-code and published-trip windows are
/// keyed on the address because their traffic comes from people at keyboards and the question is
/// "how much is this connection asking". A feed is polled by calendar services — a phone, a
/// desktop, a hosted calendar's own fetcher farm — legitimately for ever and from many addresses
/// at once, and the same hosted fetcher serves thousands of subscribers. Keyed on the address, one
/// popular calendar service would spend one budget for every member of the club; keyed on the
/// token, each address is bounded on its own and a reader cannot be throttled by a stranger.
/// </para>
/// <para>
/// It is cost control and not a confidentiality control. The token is 32 random bytes and
/// unguessable, so there is no space for a window to protect; what keeps the surface safe is what
/// a row declines to carry, and the one 404 every failure answers with. The window exists because
/// every poll resolves an account's whole grant set and walks three tables, and a misconfigured
/// client polling every second would do that work every second.
/// </para>
/// <para>
/// <b>Sized against how calendars actually poll.</b> A phone asks every fifteen minutes to a few
/// hours; a hosted calendar every few hours. One token is normally polled by one device, so the
/// default is far above anything a working subscriber does and far below unbounded. A poll over
/// the window is answered with a plain 429 and tried again on the subscriber's next schedule.
/// </para>
/// </remarks>
internal static class CalendarFeedRateLimits
{
    /// <summary>The policy name, registered in the application's rate-limiter setup.</summary>
    internal const string PolicyName = "calendar-feed";

    /// <summary>Polls a minute from one token.</summary>
    internal const int DefaultPerMinute = 20;

    /// <summary>Where an installation overrides it.</summary>
    internal const string ConfigurationKey = "CalendarFeed:RateLimitPerMinute";

    /// <summary>The route value the token travels in, which the partition reads.</summary>
    internal const string TokenRouteValue = "token";
}
