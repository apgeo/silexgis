// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Api.Tests.Support;

/// <summary>
/// A clock a test can set.
/// </summary>
/// <remarks>
/// Three lines rather than a test-only package, because that is all this needs. Note what it can
/// and cannot do: it moves the times the application <em>stamps</em>, so an assertion on the next
/// attempt after a failed send can be an equality against the backoff ladder rather than a range.
/// It cannot make a row due — whether a delivery is claimable is decided by the database's own
/// <c>now()</c>, so backdating the row is still the only lever for that.
/// </remarks>
public sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
