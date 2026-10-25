// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// A trip report made automatically from a phone recording that a person has to look at: what in
/// the recording could not be settled on its own, raised by the conversion and cleared by hand.
/// </summary>
/// <remarks>
/// <para>
/// One row per trip, keyed by it. The row says <em>why</em> — each reason with how many scans it
/// covers and which ones — and keeps saying it after it is cleared, so a trip somebody looked at
/// still reads as a trip that once needed looking at and the reasons stay visible to whoever looks
/// next. A recording that changes after it was cleared raises the row again rather than adding a
/// second one.
/// </para>
/// <para>
/// The reasons name scans by the phone's identifiers and never by a station or a place's name: a
/// scan's identifier locates nothing, while a station name is a position, and this row is read by
/// surfaces that do not each evaluate the protection that decides who may learn one.
/// </para>
/// </remarks>
public class TripAttention : ITimestamped, IAuditable, IAuditChild
{
    public Guid TripLogId { get; set; }

    public TripLog? TripLog { get; set; }

    /// <summary>
    /// The reasons, as a JSON array of <c>{ code, count, pointIds }</c>; codes are those of
    /// <see cref="SilexGis.Domain.Trips.TripAttentionReasons"/>.
    /// </summary>
    public string Reasons { get; set; } = "[]";

    /// <summary>When the reasons were last raised.</summary>
    public DateTimeOffset RaisedAt { get; set; }

    /// <summary>When somebody said the trip no longer needs looking at; null while it does.</summary>
    public DateTimeOffset? ClearedAt { get; set; }

    public Guid? ClearedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public bool IsRaised => ClearedAt is null;

    public string AuditId => TripLogId.ToString();

    public string? RootEntityType => nameof(TripLog);

    public string? RootEntityId => TripLogId.ToString();
}
