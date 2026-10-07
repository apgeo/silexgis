// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Persistence;

/// <summary>
/// Who a typed name means: the one rule that turns a name written on a record into a person in
/// the directory.
/// </summary>
/// <remarks>
/// <para>
/// A record may name somebody by a bare name instead of by their entry — it is how a trip
/// records the people who never sign in, and how a camp's roster records its cook. Each such
/// name has to become an entry, so the person can be counted and found again; and the same name
/// has to become the <em>same</em> entry every time it is written, or one person is scattered
/// across several. So a name somebody is already recorded under means that person, and only a
/// name nobody holds makes a new entry.
/// </para>
/// <para>
/// <b>One body, and every record that names people this way calls it.</b> This is a rule about
/// identity, and a second copy of a rule about identity is how a club comes to hold two entries
/// for one person: two copies agree until one of them is changed — to compare names more
/// loosely, say — and from then on the same typed name means one person on a trip and another
/// on a camp, with nothing failing anywhere. Merging the two afterwards is possible, and is
/// exactly the work this exists not to cause.
/// </para>
/// <para>
/// Two people can already share a name — that is the state the directory's merge exists to
/// resolve — so a name is matched to the <b>oldest</b> entry holding it, with the identifier as
/// the tie-break. Which of them it picks matters less than that it picks the same one every
/// time, rather than whichever row the database happened to return first.
/// </para>
/// <para>
/// A name is compared exactly as written, once the spaces around it are dropped. Nothing here
/// decides whether the caller may write the record doing the naming, and no right over the
/// directory is asked: naming somebody on a record one may write is part of writing it. Nothing
/// here saves, either — a new entry is added to the unit of work and is committed with the
/// record that named it, so a write that fails leaves nobody behind in the directory.
/// </para>
/// </remarks>
public static class CaverNames
{
    /// <summary>
    /// Resolves every name one write carries, in a single read of the directory.
    /// </summary>
    /// <param name="db">The unit of work the naming record is being written in.</param>
    /// <param name="typedNames">
    /// The names as they were written, in the order they were written. A name written twice is
    /// one person. That a name is not blank is for the shape of the request to have settled
    /// before this is asked.
    /// </param>
    /// <param name="ct">Cancels the read.</param>
    public static async Task<ResolvedCaverNames> ResolveAsync(
        SilexGisDbContext db, IEnumerable<string> typedNames, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(typedNames);

        var names = typedNames.Select(name => name.Trim()).ToList();
        var named = names.Where(name => name.Length > 0).ToList();

        // Grouped before it is keyed. Keying the query straight by name would fault on two
        // entries sharing one, and lose the whole write over a coincidence of spelling.
        var matchedRows = named.Count == 0
            ? []
            : await db.Cavers.Where(c => named.Contains(c.FullName))
                .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
                .ToListAsync(ct);
        var ids = matchedRows
            .GroupBy(c => c.FullName)
            .ToDictionary(g => g.Key, g => g.First().Id);

        // In the order written, so the entries one write adds are added in the order it named
        // them. Remembered as it goes: a name nobody holds, written twice in one request, is one
        // new person and not two.
        foreach (var name in names)
        {
            if (ids.ContainsKey(name))
            {
                continue;
            }

            var created = new Caver { FullName = name };
            db.Cavers.Add(created);
            ids[name] = created.Id;
        }

        return new ResolvedCaverNames(ids);
    }
}

/// <summary>
/// What the names on one write turned out to mean: for each, the person in the directory — one
/// who was already there, or one this write is adding.
/// </summary>
public sealed class ResolvedCaverNames
{
    private readonly Dictionary<string, Guid> ids;

    internal ResolvedCaverNames(Dictionary<string, Guid> ids) => this.ids = ids;

    /// <summary>
    /// The person a name was resolved to. Asked with the name as it was written: the spaces
    /// around it are dropped here exactly as they were when it was resolved, so a caller cannot
    /// miss its own name by a trailing space.
    /// </summary>
    public Guid IdOf(string typedName) => ids[typedName.Trim()];
}
