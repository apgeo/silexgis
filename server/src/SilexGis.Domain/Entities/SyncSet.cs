// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// What one account has chosen to carry on a mobile device: the caves (with everything
/// contained in them) it wants offline, the visibility rows uploaded from that device are
/// created with, and an opaque settings document the device owns.
/// </summary>
/// <remarks>
/// <para>
/// A sync set is per-account configuration, not shared content. It is deliberately outside
/// the access model — no domain, no rulesets, no caving-group reads: only its owner can see
/// it or change it, and that includes administrators. Nothing is gained by letting one caver
/// read another's device list, and the list is a statement about a person's habits and where
/// they go, which is exactly the kind of thing this application protects elsewhere.
/// </para>
/// <para>
/// The set names roots, not rows. There is no registry of what has been synced: a feature row
/// already carries its own identity, its update time and its tombstone, so membership is the
/// small durable fact and everything else is derived at read time from the containment
/// ancestry. Adding a row under a member root therefore puts it in the set with no bookkeeping.
/// </para>
/// <para>
/// Deliberately not audited. The audit trail is a shared read: it is gated on a right over the
/// audit domain and not on the row the entry describes, so an audited create would publish this
/// set's name, its group binding and its whole settings document to every account holding that
/// right — the very thing the paragraph above says cannot happen. There is nothing to reconcile
/// here: an audit entry and "the owner is the only reader" cannot both be true, and the second
/// is the promise the endpoints are written to keep.
/// </para>
/// </remarks>
public class SyncSet : ITimestamped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>What the caver calls this device or this selection.</summary>
    public required string Name { get; set; }

    /// <summary>The account this set belongs to. The only reader and the only writer.</summary>
    public Guid OwnerUserId { get; set; }

    /// <summary>
    /// The caving group rows uploaded through this set are bound to, when the caver chose one.
    /// Null is a complete configuration and the default; a group is offered, never assumed.
    /// </summary>
    public Guid? CavingGroupId { get; set; }

    /// <summary>
    /// The baseline audience a row uploaded from this device is created with. Private by
    /// default whether or not a group is bound: widening is a deliberate act taken on the
    /// server, where the person doing it can see what they are widening.
    /// </summary>
    public Visibility UploadVisibility { get; set; } = Visibility.Private;

    /// <summary>
    /// The device's own settings document (jsonb), stored verbatim and never interpreted here.
    /// It carries the code-generation settings that keep two devices producing compatible
    /// codes; the server has no opinion about their meaning and must not acquire one, because
    /// validating them would reject settings that are legal on the device.
    /// </summary>
    public string Settings { get; set; } = "{}";

    /// <summary>
    /// Monotonic counter, bumped once per change that actually altered the stored set. It is
    /// what a device compares against to find out whether the copy it holds is still current,
    /// and what a later replacement of the settings document is arbitrated on. Deliberately
    /// not a timestamp: two edits inside one clock tick must still be distinguishable, and a
    /// device's own clock must never enter the comparison.
    /// </summary>
    public long Revision { get; set; } = 1;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// One root a sync set carries: the named feature and everything contained in it. A cave is
/// the ordinary case; an area works the same way, because containment is what the download
/// walks.
/// </summary>
public class SyncSetMember
{
    public long Id { get; set; }

    public Guid SyncSetId { get; set; }

    /// <summary>
    /// The root feature. Membership records the caver's choice, not a right: what actually
    /// leaves the server is decided at read time against the caller's own visibility, so a
    /// root that stops being readable simply stops producing rows.
    /// </summary>
    public Guid RootFeatureId { get; set; }
}
