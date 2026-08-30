// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Import;

namespace SilexGis.Api.Features.Sync;

/// <summary>
/// The limits a mobile client is told about before it starts, and the only ones it is
/// entitled to assume. A device that has never talked to this installation asks once and
/// then sizes its own pages and batches to the answer, which is why these are announced
/// rather than left to be discovered by a rejected request in a cave with no signal.
/// Not every member here is announced, and <see cref="SyncCapabilitiesDto"/> is the enumeration of
/// the ones that are. The page a request that named no size receives is announced nowhere, because
/// a device that needs to know its page size sends one, and what it sizes that choice against is
/// the ceiling rather than somebody else's default. Whether an administrator may read another
/// account's selection is announced nowhere either: it is decided by the account rather than by the
/// client — a device signed in as a full administrator reaches the widened read, and every other
/// device sees no difference at all — so telling every device about it would be a statement about
/// the people running this server rather than about the protocol. Nor is the duplicate radius: it
/// shapes an answer rather than bounding a request, so a device reads the duplicates an upload
/// actually reported instead of sizing anything against the distance behind them.
/// </summary>
/// <remarks>
/// The salt behind the printed-code derivation is deliberately <b>not</b> here. It is a
/// compatibility constant rather than an installation's choice — it is fixed so that two datasets
/// which never met derive the same printed code from the same place code — and nothing on this
/// server derives a code in any case: a scanned label is answered by looking the stored value up.
/// It is compiled in beside the derivation it belongs to. A setting for it belongs to whatever
/// change first derives a code here and needs to agree with a differently-built device.
/// </remarks>
public sealed class SyncOptions
{
    public const string SectionName = "Sync";

    /// <summary>
    /// The largest page of rows a download may hand back in one response. Sized so a page
    /// stays comfortably inside a mobile connection's patience rather than to any storage
    /// limit; a device that wants fewer asks for fewer.
    /// </summary>
    public int PageSizeMax { get; set; } = 500;

    /// <summary>
    /// The page a device gets when it asks for one without naming a size. Well under the ceiling
    /// by default: a first sync happens on whatever connection a car park has, and a page that has
    /// to be retried whole is cheaper to retry small. A setting rather than a constant because how
    /// much a request can carry before it is worth abandoning is a property of where an
    /// installation's members go, not of the software.
    /// </summary>
    /// <remarks>
    /// A page is a count of rows and not a budget of bytes, and the two are not close: a selection
    /// carries everything contained in its roots, so a synced cave brings its surveyed centerline,
    /// whose geometry is the whole survey as a multi-line string and can be larger on its own than
    /// the other ninety-nine rows together. Nothing here bounds that, which is why the size is a
    /// request a device is expected to lower rather than a promise that a page is small.
    /// </remarks>
    public int DefaultPageSize { get; set; } = 100;

    /// <summary>
    /// Whether a full administrator may read a sync set belonging to another account. Off unless
    /// an installation turns it on, because a sync set is a statement about where a person goes:
    /// it names, in the clear, which caves that caver's phone carries and which club those rows
    /// will be created for. Support ("which caves does this phone hold?") is the case that wants
    /// it, and an installation whose members expect that support is the one entitled to allow it.
    /// </summary>
    /// <remarks>
    /// It widens <b>reading one set by its identifier</b> and nothing else. Replacing and deleting
    /// stay the owner's alone whatever this says, the listing keeps answering with the caller's own
    /// sets so it can never be used to count anybody else's, and the download and upload routes go
    /// on resolving a set by its owner — a device authenticates as one account and must never be
    /// able to pull another account's selection onto itself, however the account it holds is
    /// privileged. What it does newly disclose is real and deliberate: an administrator who already
    /// knows a set's identifier learns its name, its club, the visibility its rows will be created
    /// at, its code-generation settings, when it was last edited, and the whole list of caves it
    /// carries.
    /// </remarks>
    public bool AllowAdministratorRead { get; set; }

    /// <summary>
    /// The most rows one upload batch may carry. A batch is applied as a unit, so this bounds
    /// how much work a single failed or replayed request costs, not how much a device may
    /// eventually send.
    /// </summary>
    public int UploadRowsMax { get; set; } = 500;

    /// <summary>
    /// How far from an uploaded row this server looks for something that might already be it.
    /// The same default a file import uses, so the two channels answer the same question the same
    /// way, and a setting rather than a constant because how close two entrances can be before
    /// they are probably one is a property of the karst, not of the software.
    /// </summary>
    public double DuplicateRadiusMeters { get; set; } = ImportOptions.DefaultDuplicateRadiusMeters;

    /// <summary>Announced page ceiling, brought inside a range a client can actually use.</summary>
    public int ResolvedPageSizeMax => Math.Clamp(PageSizeMax, 1, 5000);

    /// <summary>
    /// The default page, held inside the ceiling it is a default for. An installation that sets a
    /// default above its own maximum gets the maximum rather than a refusal: the request this
    /// applies to is one that named no size at all, so there is no caller to tell that the server
    /// is misconfigured.
    /// </summary>
    public int ResolvedDefaultPageSize => Math.Clamp(DefaultPageSize, 1, ResolvedPageSizeMax);

    /// <summary>Announced batch ceiling, clamped for the same reason.</summary>
    public int ResolvedUploadRowsMax => Math.Clamp(UploadRowsMax, 1, 5000);

    /// <summary>
    /// The duplicate radius, held inside the same bound the file importer keeps. A radius of zero
    /// turns the report off; the ceiling is what stops one badly-placed row sweeping in every
    /// feature the installation holds.
    /// </summary>
    public double ResolvedDuplicateRadiusMeters =>
        Math.Clamp(DuplicateRadiusMeters, 0, ImportOptions.MaxDuplicateRadiusMeters);
}
