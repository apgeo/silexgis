// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Filters;

/// <summary>
/// What a cabinet can be filtered by.
/// </summary>
/// <remarks>
/// <para>
/// A cabinet is a shelf in the club's filing scheme, and the whole scheme is shown to every
/// signed-in account: it has no owner, no caving group and no visibility of its own, so none of
/// those common fields is offered and there is no sort by owner.
/// </para>
/// <para>
/// <b>How many documents a cabinet holds is not here.</b> That number is counted per reader — it is
/// the documents on the shelf this reader may open — so a condition on it would be a per-caller
/// fact, and "cabinets holding at least one document" would read the documents' visibility off the
/// cabinet list.
/// </para>
/// </remarks>
public static class CabinetFilterFields
{
    public const string World = "cabinet";

    public static IReadOnlyList<FieldDescriptor> Fields { get; } =
    [
        .. CommonFilterFields.Descriptors.Where(f => f.Key is CommonFilterFields.Name
            or CommonFilterFields.CreatedAt or CommonFilterFields.UpdatedAt),
    ];

    public static WorldVocabulary Vocabulary { get; } = new(
        World,
        $"filters.worlds.{World}",
        Fields,
        [SortKey.Created, SortKey.Updated, SortKey.Title]);
}
