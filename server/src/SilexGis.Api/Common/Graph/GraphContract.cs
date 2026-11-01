// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text.Json;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;

namespace SilexGis.Api.Common.Graph;

/// <summary>
/// The kinds of item a relations graph can draw. The first ten are exactly the
/// resource-link target kinds (and carry the same wire names); <see cref="Tag"/>,
/// <see cref="ResLink"/> (a link drawn as a hub) and <see cref="File"/> exist only here.
/// </summary>
public enum GraphNodeType
{
    Feature,
    Document,
    TripLog,
    Caver,
    CavingGroup,
    MapView,
    SurveyModel,
    Geofile,
    Cabinet,
    Expedition,
    Tag,
    ResLink,
    File,
}

/// <summary>The families of relation the graph reads, each from the place that already holds it.</summary>
public enum GraphCategory
{
    ResLink,
    FeatureLink,
    Hierarchy,
    Attachment,
    TripParticipant,
    Expedition,
    Cabinet,
    Tag,
    GroupMembership,
    Survey,
}

/// <summary>
/// Wire names and keys of the graph vocabulary. Names are the camelCase of the enum member,
/// parsed case-insensitively; a number is never accepted, so a client cannot address a kind
/// by an ordinal that a later reordering would silently change.
/// </summary>
public static class GraphWire
{
    public static string Name(GraphNodeType type) => JsonNamingPolicy.CamelCase.ConvertName(type.ToString());

    public static string Name(GraphCategory category) => JsonNamingPolicy.CamelCase.ConvertName(category.ToString());

    public static bool TryParseType(string? value, out GraphNodeType type) => TryParseName(value, out type);

    public static bool TryParseCategory(string? value, out GraphCategory category) => TryParseName(value, out category);

    private static bool TryParseName<T>(string? value, out T parsed)
        where T : struct, Enum
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        foreach (var candidate in Enum.GetValues<T>())
        {
            if (string.Equals(JsonNamingPolicy.CamelCase.ConvertName(candidate.ToString()), trimmed, StringComparison.OrdinalIgnoreCase))
            {
                parsed = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a node of this type can be the item a neighbourhood is asked around.</summary>
    public static bool IsSeedType(GraphNodeType type) => type != GraphNodeType.File;

    /// <summary>
    /// The resource-link world of a node type: null for the feature world, the entity type for
    /// the other linkable kinds. False for the graph-only types.
    /// </summary>
    public static bool TryTargetWorld(GraphNodeType type, out AttachedEntityType? world)
    {
        world = null;
        switch (type)
        {
            case GraphNodeType.Feature: return true;
            case GraphNodeType.Document: world = AttachedEntityType.Document; return true;
            case GraphNodeType.TripLog: world = AttachedEntityType.TripLog; return true;
            case GraphNodeType.Caver: world = AttachedEntityType.Caver; return true;
            case GraphNodeType.CavingGroup: world = AttachedEntityType.CavingGroup; return true;
            case GraphNodeType.MapView: world = AttachedEntityType.MapView; return true;
            case GraphNodeType.SurveyModel: world = AttachedEntityType.SurveyModel; return true;
            case GraphNodeType.Geofile: world = AttachedEntityType.Geofile; return true;
            case GraphNodeType.Cabinet: world = AttachedEntityType.Cabinet; return true;
            case GraphNodeType.Expedition: world = AttachedEntityType.Expedition; return true;
            default: return false;
        }
    }

    /// <summary>Inverse of <see cref="TryTargetWorld"/>, for a stored member row.</summary>
    public static GraphNodeType FromTargetWorld(AttachedEntityType? world) => world switch
    {
        null => GraphNodeType.Feature,
        AttachedEntityType.Document => GraphNodeType.Document,
        AttachedEntityType.TripLog => GraphNodeType.TripLog,
        AttachedEntityType.Caver => GraphNodeType.Caver,
        AttachedEntityType.CavingGroup => GraphNodeType.CavingGroup,
        AttachedEntityType.MapView => GraphNodeType.MapView,
        AttachedEntityType.SurveyModel => GraphNodeType.SurveyModel,
        AttachedEntityType.Geofile => GraphNodeType.Geofile,
        AttachedEntityType.Cabinet => GraphNodeType.Cabinet,
        AttachedEntityType.Expedition => GraphNodeType.Expedition,
        _ => throw new ArgumentOutOfRangeException(nameof(world), world, "Not a graph node type."),
    };

    /// <summary>
    /// <see cref="FromTargetWorld"/> for a stored polymorphic row that may name a kind the graph
    /// does not draw (a georeferenced map, a bare stored file, a calendar event): false then.
    /// </summary>
    public static bool TryFromTargetWorld(AttachedEntityType? world, out GraphNodeType type)
    {
        type = GraphNodeType.Feature;
        if (world is null)
        {
            return true;
        }

        foreach (var candidate in Enum.GetValues<GraphNodeType>())
        {
            if (TryTargetWorld(candidate, out var w) && w == world)
            {
                type = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>"&lt;type&gt;:&lt;id&gt;" — one node per item, however many relations reach it.</summary>
    public static string Key(GraphNodeType type, string id) => $"{Name(type)}:{id}";

    public static string Key(GraphNodeType type, Guid id) => Key(type, id.ToString());

    /// <summary>
    /// A member the viewer may not read is keyed by its member row, never by what it names, so
    /// two links naming the same unreadable item meet in no common node — the graph then shows
    /// no path through something the viewer cannot see.
    /// </summary>
    public static string RestrictedKey(Guid memberId) => $"restricted:{memberId}";

    /// <summary>The order every category pages in: far-end title, case-insensitively, then key.</summary>
    public static int CompareTitles(string? a, string? b) =>
        string.Compare(a ?? string.Empty, b ?? string.Empty, CultureInfo.InvariantCulture, CompareOptions.IgnoreCase);
}

/// <summary>The item a neighbourhood is asked around. <see cref="Id"/> is the item's id as text.</summary>
public sealed record GraphSeed(GraphNodeType Type, string Id)
{
    public string Key => GraphWire.Key(Type, Id);

    /// <summary>The id as a Guid — every type but <see cref="GraphNodeType.Tag"/> is keyed by one.</summary>
    public Guid Guid => System.Guid.Parse(Id);
}

/// <summary>One category's window: how many top-level items to skip and take, in the category's order.</summary>
public sealed record GraphPage(int Offset, int Limit);

/// <summary>
/// A node as a source reports it. Only a placeholder or a link hub carries its own display;
/// every other node is resolved for display by the orchestrator, which drops it when the
/// resolution does not admit it.
/// </summary>
public sealed record GraphNodeRef(
    string Key,
    GraphNodeType Type,
    string? Id,
    bool Restricted = false,
    string? ShortCode = null,
    int? MemberCount = null);

/// <summary>An edge as a source reports it; source and target are node keys.</summary>
public sealed record GraphEdgeRef(
    string Key,
    GraphCategory Category,
    string Relation,
    string Source,
    string Target,
    bool Directed,
    string? RelationName = null,
    string? InverseName = null,
    bool? IsMain = null,
    string? AnchorKind = null);

/// <summary>
/// A source's page: the nodes and edges of its window of top-level items, and how many
/// top-level items there are in all for this viewer — counted after every filter. A top-level
/// item is one link (its hub and every member shown) for resource links, one edge otherwise.
/// </summary>
public sealed record GraphSourceAnswer(IReadOnlyList<GraphNodeRef> Nodes, IReadOnlyList<GraphEdgeRef> Edges, int Total)
{
    public static readonly GraphSourceAnswer Empty = new([], [], 0);
}

/// <summary>
/// One relation family's neighbours of one item. Implemented inside the slice that owns the
/// relation's existing read route, calling that route's own read function, so the graph
/// answers exactly what the route answers for the same viewer and no second copy of the
/// rule exists to drift.
/// </summary>
public interface IGraphEdgeSource
{
    GraphCategory Category { get; }

    bool AcceptsSeed(GraphNodeType seedType);

    /// <summary>The seed is already known to be readable by <paramref name="ctx"/>.</summary>
    Task<GraphSourceAnswer> NeighboursAsync(GraphSeed seed, AccessContext ctx, GraphPage page, CancellationToken ct);
}

/// <summary>What the orchestrator draws a resolved node with.</summary>
public sealed record GraphNodeDisplay(string? Title, string? Subtitle, string? Route, string? Kind = null);

/// <summary>
/// Display data for the nodes of some types, for readable ids only: an id missing from the
/// answer is unreadable (or gone) and its node is not drawn. The same question decides
/// whether an item may be a seed.
/// </summary>
public interface IGraphNodeResolver
{
    IReadOnlyCollection<GraphNodeType> Types { get; }

    Task<IReadOnlyDictionary<string, GraphNodeDisplay>> ResolveAsync(
        AccessContext ctx, GraphNodeType type, IReadOnlyCollection<string> ids, CancellationToken ct);
}
