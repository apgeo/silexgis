// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Import;

/// <summary>What a source row says about itself, once the mapping has been applied.</summary>
public sealed record SourceAttributes(string? Name, string? Description, string? Code, double? Elevation);

/// <summary>
/// Picks the attributes out of a source row: the columns the importer named, or the ones the
/// tools in this field actually write.
///
/// <para>
/// Detection is by folded key, so <c>Name</c>, <c>NAME</c> and <c>nume</c> are found without a
/// mapping — a GPX writes <c>name</c> and <c>desc</c>, a shapefile is limited to ten-character
/// column names and abbreviates, and a spreadsheet is whatever somebody typed.
/// </para>
/// </summary>
public static class SourceAttributeReader
{
    public static SourceAttributes Read(
        IReadOnlyDictionary<string, string?> properties, ImportAttributeMapping mapping)
    {
        var name = Pick(properties, mapping.NameField, ImportAttributeMapping.NameCandidates);
        var description = Pick(properties, mapping.DescriptionField, ImportAttributeMapping.DescriptionCandidates);
        var code = Pick(properties, mapping.CodeField, ImportAttributeMapping.CodeCandidates);
        var elevation = Pick(properties, mapping.ElevationField, ImportAttributeMapping.ElevationCandidates);

        // A description identical to the name is what GPX writers produce when a waypoint has
        // only a label; keeping both would put the same sentence in two fields of every
        // imported object.
        if (description is not null && string.Equals(description, name, StringComparison.Ordinal))
        {
            description = null;
        }

        return new SourceAttributes(name, description, code, CoordinateText.ParseNumber(elevation));
    }

    /// <summary>
    /// The value of the named field, or of the first candidate the row carries. An explicitly
    /// named field that the row does not have yields nothing rather than falling back — a
    /// mapping the importer chose is an instruction, and quietly substituting a different
    /// column for it is how a whole import ends up named after the wrong thing.
    /// </summary>
    private static string? Pick(
        IReadOnlyDictionary<string, string?> properties, string? chosen, IReadOnlyList<string> candidates)
    {
        if (!string.IsNullOrWhiteSpace(chosen))
        {
            return Lookup(properties, chosen);
        }

        foreach (var candidate in candidates)
        {
            if (Lookup(properties, candidate) is { } value)
            {
                return value;
            }
        }

        return null;
    }

    private static string? Lookup(IReadOnlyDictionary<string, string?> properties, string key)
    {
        if (properties.TryGetValue(key, out var exact))
        {
            return Blank(exact);
        }

        var folded = FoldedText.Of(key).Value;
        foreach (var (candidateKey, value) in properties)
        {
            if (FoldedText.Of(candidateKey).Value == folded)
            {
                return Blank(value);
            }
        }

        return null;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
