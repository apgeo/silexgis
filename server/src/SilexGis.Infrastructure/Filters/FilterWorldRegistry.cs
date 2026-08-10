// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Access;
using SilexGis.Domain.Filters;

namespace SilexGis.Infrastructure.Filters;

/// <summary>
/// Every world there is, in the order they are shown, resolved by key.
/// </summary>
/// <remarks>
/// <para>
/// The one place worlds are composed. Everything that needs to know what can be filtered — the
/// vocabulary endpoint, the query endpoint, the selector, the conformance suite — asks here, so
/// adding a world is a single registration rather than an exercise in finding the several lists
/// that would otherwise have to agree.
/// </para>
/// <para>
/// The conformance suite enumerating the same registry is what makes that worth something: a world
/// registered here is a world something proves does not leak, and a world registered here without
/// a fixture to exercise it fails the suite rather than passing it by default.
/// </para>
/// </remarks>
public sealed class FilterWorldRegistry
{
    private readonly Dictionary<string, IFilterWorld> byKey;

    public FilterWorldRegistry(IEnumerable<IFilterWorld> worlds)
    {
        All = [.. worlds];
        byKey = All.ToDictionary(w => w.World, StringComparer.Ordinal);

        if (byKey.Count != All.Count)
        {
            // Two worlds under one key would make which one answers depend on registration order,
            // and a saved filter would silently change meaning when that order changed.
            throw new InvalidOperationException("Two filter worlds share a key.");
        }
    }

    /// <summary>Every registered world, in registration order.</summary>
    public IReadOnlyList<IFilterWorld> All { get; }

    /// <summary>The world a key names, or null when nothing answers to it.</summary>
    public IFilterWorld? Find(string world) =>
        byKey.TryGetValue(world, out var found) ? found : null;

    /// <summary>
    /// Every world's vocabulary for this caller.
    /// </summary>
    public async ValueTask<IReadOnlyList<WorldVocabulary>> VocabulariesAsync(
        AccessContext caller, CancellationToken ct)
    {
        var vocabularies = new List<WorldVocabulary>(All.Count);
        foreach (var world in All)
        {
            vocabularies.Add(await world.VocabularyAsync(caller, ct));
        }

        return vocabularies;
    }
}
