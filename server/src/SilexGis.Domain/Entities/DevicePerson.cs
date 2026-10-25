// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>Who made a phone user's link to a caver, which decides who may replace it.</summary>
/// <remarks>
/// Values are stored as numbers and only ever appended. The order is the order of authority:
/// see <see cref="DevicePersonLinks.MayReplace"/>.
/// </remarks>
public enum DevicePersonLinkSource
{
    /// <summary>The phone chose it, from the directory it was given.</summary>
    Device = 0,

    /// <summary>Somebody who keeps the roster chose it in the browser.</summary>
    Administrator = 1,

    /// <summary>Somebody importing a phone's archive chose it for the trips in that archive.</summary>
    ArchiveImport = 2,
}

/// <summary>
/// One user of the SpeleoLoc application on some phone, as that phone last described them, and
/// the caver they are — the one place this installation answers "who is that phone user".
/// </summary>
/// <remarks>
/// <para>
/// A phone has users of its own (the people sharing it on a trip), identified by an identifier
/// the phone minted; they are not accounts and never become accounts or cavers by being seen.
/// Everything that needs to say who on a phone did something — a trip's participants, a photo's
/// photographer, an archive's points — resolves the phone's identifier through this row, so a
/// link made once (by the phone, by somebody keeping the roster, or while importing an archive)
/// holds for every later upload and import.
/// </para>
/// <para>
/// One caver may have several of these — the same person on two phones — while one phone user is
/// at most one caver. Audited like the roster it points into, so a link can be traced and an
/// upload that made one can be taken back from the history.
/// </para>
/// </remarks>
public class DevicePerson : ITimestamped, IAuditable
{
    /// <summary>The phone's own identifier for this user, adopted verbatim.</summary>
    public Guid DeviceUserId { get; set; }

    /// <summary>
    /// The phone's user name, as the phone last sent it; null for a phone user only ever met in an
    /// imported archive, which names its users by identifier alone.
    /// </summary>
    public string? Username { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    /// <summary>The caver this phone user is; null while nobody has said.</summary>
    public Guid? CaverId { get; set; }

    /// <summary>Who made the current link. Meaningless while <see cref="CaverId"/> is null.</summary>
    public DevicePersonLinkSource LinkSource { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>The account whose upload or import last described this phone user.</summary>
    public Guid? UploadedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => DeviceUserId.ToString();
}

/// <summary>The rule for replacing a phone user's link to a caver, stated once.</summary>
public static class DevicePersonLinks
{
    /// <summary>
    /// Whether a link made by <paramref name="incoming"/> may replace the link a phone user has.
    /// </summary>
    /// <remarks>
    /// A phone user without a link takes any. Otherwise a link is replaced only by a source of at
    /// least the same authority: a phone's choice by anybody, an archive import's choice by another
    /// import or by the roster's keeper, and the roster keeper's choice only by the roster's keeper.
    /// A phone picking a name from a list is the weakest statement here and somebody who keeps the
    /// roster deciding in the browser the strongest, so a phone that keeps sending an old choice
    /// cannot undo a correction made deliberately — the phone learns of the correction instead,
    /// from the links the directory hands back.
    /// </remarks>
    public static bool MayReplace(Guid? currentCaverId, DevicePersonLinkSource current, DevicePersonLinkSource incoming) =>
        currentCaverId is null || Rank(incoming) >= Rank(current);

    private static int Rank(DevicePersonLinkSource source) => source switch
    {
        DevicePersonLinkSource.Device => 0,
        DevicePersonLinkSource.ArchiveImport => 1,
        DevicePersonLinkSource.Administrator => 2,
        _ => 0,
    };
}
