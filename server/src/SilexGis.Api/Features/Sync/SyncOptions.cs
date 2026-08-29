// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Import;

namespace SilexGis.Api.Features.Sync;

/// <summary>
/// The limits a mobile client is told about before it starts, and the only ones it is
/// entitled to assume. A device that has never talked to this installation asks once and
/// then sizes its own pages and batches to the answer, which is why these are announced
/// rather than left to be discovered by a rejected request in a cave with no signal.
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
