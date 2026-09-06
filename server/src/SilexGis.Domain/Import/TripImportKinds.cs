// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Import;

/// <summary>
/// The kinds of thing a trip spreadsheet's place columns are allowed to become, and the kinds
/// they are allowed to be taken for.
///
/// <para>
/// The two lists are one list on purpose. An importer that will only ever create a massif or a
/// karst area but will happily <em>match</em> anything shaped like an area binds a club's trips
/// to rows it would never have written itself — a work area, which is a club's declaration that
/// it works somewhere, is not something a column of somebody's spreadsheet is saying, and a cave
/// invented under one would join that declaration without anybody deciding so.
/// </para>
/// </summary>
public static class TripImportKinds
{
    /// <summary>The kind a massif column becomes: a stretch of country, with no geometry yet.</summary>
    public const string Massif = "massif";

    /// <summary>
    /// The kind a sub-area column becomes. A valley or a sector inside a massif is a karst area
    /// rather than a massif of its own.
    /// </summary>
    public const string SubArea = "karst_area";

    /// <summary>A cave system, which a sheet's place columns may name even though none is created.</summary>
    public const string CaveSystem = "cave_system";

    /// <summary>The cave kind a name with nothing else known about it becomes.</summary>
    public const string Cave = "cave";

    /// <summary>
    /// The area kinds a massif or sub-area column may be taken for. Narrower than "anything
    /// filed under the area category", which is what makes a work area unreachable from here.
    /// </summary>
    public static readonly string[] Areas = [Massif, SubArea, CaveSystem];
}
