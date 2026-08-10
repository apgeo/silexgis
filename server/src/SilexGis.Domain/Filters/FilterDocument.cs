// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SilexGis.Domain.Filters;

/// <summary>
/// Which worlds a request is about, and what is asked of each.
/// </summary>
/// <param name="World">The world key — <c>feature</c>, <c>document</c>, <c>tripLog</c>, and so on.</param>
/// <param name="Where">
/// The condition tree for this world, or null for "everything this world holds". Null rather than
/// an empty AllOf so that "no conditions" is one shape rather than two that must be kept in step.
/// </param>
public sealed record WorldScope(string World, FilterNode? Where = null);

/// <summary>
/// A filter, as it travels: in a URL, in a saved row, in a reference from a view.
/// </summary>
/// <remarks>
/// <para>
/// The document holds only what a person authored. It carries no viewport, and that omission is
/// load-bearing rather than tidy: the map's extent is a parameter of one request, and a saved or
/// shared filter that carried an extent would be a stored, negatable, shareable statement about
/// where somebody was looking.
/// </para>
/// <para>
/// It also carries no free text field: text is a condition on a named field like everything else.
/// A document with a "search" alongside its conditions would have two ways to say one thing and
/// two places to enforce the rules about what may be matched on.
/// </para>
/// </remarks>
public sealed record FilterDocument
{
    /// <summary>
    /// Bumped only when documents written before it stop being readable. A saved filter outlives
    /// the code that wrote it, so a document that cannot be read must say so rather than being
    /// silently reinterpreted as whatever the current shape deserialises to.
    /// </summary>
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    /// <summary>
    /// The worlds in play, in the order they should be shown. One entry per world: a scope naming
    /// the same world twice would have two answers to "what is asked of it", and nothing sensible
    /// to do with the second.
    /// </summary>
    public IReadOnlyList<WorldScope> Scope { get; init; } = [];

    public SortKey Sort { get; init; } = SortKey.Updated;

    public bool Descending { get; init; } = true;

    /// <summary>
    /// What proximity is measured from, when the sort is proximity. Null means the caller supplies
    /// an anchor with the request — the selected object, or the middle of the view.
    /// </summary>
    public FilterAnchor? Anchor { get; init; }

    /// <summary>
    /// Whether this narrows anything. Never written: it is derived from the scope that is already
    /// in the document, and a stored copy would be one more thing that can disagree with it.
    /// </summary>
    [JsonIgnore]
    public bool IsEmpty => Scope.Count == 0 || Scope.All(s => s.Where is null);

    public static JsonSerializerOptions SerializerOptions { get; } = Build();

    public static string Serialize(FilterDocument document) =>
        JsonSerializer.Serialize(document, SerializerOptions);

    /// <summary>
    /// The document a stored string holds, or null when it holds something this version cannot
    /// read. Null rather than an exception: a saved filter that has become unreadable must not stop
    /// the page that lists it from rendering.
    /// </summary>
    public static FilterDocument? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var document = JsonSerializer.Deserialize<FilterDocument>(json, SerializerOptions);
            return document?.Version > CurrentVersion ? null : document;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            // A node or value that does not say which shape it is. The reader raises this rather
            // than a JsonException, and catching only the latter is how a filter saved by a version
            // that spelled a discriminator differently would take down the page listing it — which
            // is the one thing this method promises cannot happen.
            return null;
        }
    }

    private static JsonSerializerOptions Build()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

/// <summary>
/// What a spatial condition or a proximity sort is measured from.
/// </summary>
/// <remarks>
/// An anchor is a coordinate somebody authored, so it is the one part of a document that is itself
/// disclosive: a filter shared by a person who may place a protected cave would otherwise hand its
/// position to everyone who opens the filter. Which is why a saved document never stores one — the
/// anchor travels with the request that runs the filter, and the saved row keeps only the shape of
/// the question.
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "anchor")]
[JsonDerivedType(typeof(PointAnchor), "point")]
[JsonDerivedType(typeof(ObjectAnchor), "object")]
public abstract record FilterAnchor;

/// <summary>A place on the map: what the middle of the view gives, or a point somebody dropped.</summary>
public sealed record PointAnchor(double Longitude, double Latitude) : FilterAnchor;

/// <summary>
/// An object, which is resolved to its position before anything is compiled.
/// <para>
/// Resolution is where the rule bites: an object the caller may read but may not place exactly is
/// refused rather than resolved, because using it as an anchor would measure everything else
/// against a position they were not given.
/// </para>
/// </summary>
public sealed record ObjectAnchor(string World, Guid Id) : FilterAnchor;
