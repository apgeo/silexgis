// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Linq.Expressions;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Filters;

namespace SilexGis.Infrastructure.Filters;

/// <summary>
/// The cave list's two free-text narrowings — region and search — as one rule for every route that
/// takes them: the list itself, its vector export and its KarstLink export.
/// </summary>
/// <remarks>
/// <para>
/// A file asked for from the list has to hold the caves the list shows. When each route spelled
/// "region" on its own, the list matched a fragment of the region while the vector export matched
/// only the whole text, so a region typed as "Bihor" listed the caves of "Munții Bihor" and then
/// exported none of them — the file looked complete and was not.
/// </para>
/// <para>
/// Both go through the filter document's own text leaf with its "contains" operator, so the bare
/// query parameter and a region condition in a filter document are the same question, compared the
/// same way: a fragment anywhere in the text, without regard to case or diacritics (a handheld
/// writes "Pestera", a person "Peștera").
/// </para>
/// </remarks>
public static class CaveListText
{
    /// <summary>The cave's region holds <paramref name="text"/>.</summary>
    public static Expression<Func<Feature, bool>> RegionContains(string text) =>
        FilterLeaves.Text<Feature>(Contains(FeatureFilterFields.Region, text), f => f.Cave!.Region);

    /// <summary>
    /// The cave's name or one of its other toponyms holds <paramref name="text"/> — the list's
    /// search box, which matches what a person would call the cave, not its code.
    /// </summary>
    public static Expression<Func<Feature, bool>> NameOrToponymContains(string text) =>
        FilterLeaves.Text<Feature>(Contains(FeatureFilterFields.Name, text), f => f.Name)
            .Or(FilterLeaves.Text<Feature>(
                Contains(FeatureFilterFields.OtherToponyms, text), f => f.Cave!.OtherToponyms));

    private static ConditionNode Contains(string field, string text) =>
        new(field, FilterOp.Contains, [new TextValue(text)]);
}
