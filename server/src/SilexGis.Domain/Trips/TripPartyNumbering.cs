// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Trips;

/// <summary>One person's place in a trip's party: who, and the number they are shown under.</summary>
public readonly record struct TripPartyPlace(Guid CaverId, int Number);

/// <summary>
/// How a trip's party is numbered and ordered: by the number each person was given when the trip
/// first named them.
/// </summary>
/// <remarks>
/// <para>
/// <b>One rule for every surface that lists a party</b> — the page a follower reads, the playback
/// of a past trip and the table a coordinator works from. Two copies of it is how "Caver 3" comes
/// to mean two different people on two screens about the same trip.
/// </para>
/// <para>
/// <b>A person on the roster with no stored number is still listed.</b> Every path that writes a
/// roster is meant to give its people numbers, and a path that forgot must cost a moving number
/// rather than a missing person: such a person comes after everybody who has a number, in the
/// order the roster first wrote them down, and is numbered onward from the highest number the
/// trip has ever given. That is exactly how the whole party was numbered before numbers were
/// stored, so a forgotten writer degrades to the old behaviour for the people it forgot and for
/// nobody else.
/// </para>
/// </remarks>
public static class TripPartyNumbering
{
    /// <summary>The first number of a party; numbers count from here.</summary>
    public const int First = 1;

    /// <summary>
    /// The number the next newly named person takes: one past the highest ever given on the trip,
    /// whether or not its holder is still on the roster. Never a gap somebody left.
    /// </summary>
    public static int Next(IEnumerable<int> given)
    {
        var highest = First - 1;
        foreach (var number in given)
        {
            if (number > highest) highest = number;
        }
        return highest + 1;
    }

    /// <summary>
    /// The party in the order it is listed, each person with their number.
    /// </summary>
    /// <param name="roster">
    /// Everybody the trip names now, each with the lowest id among their roster rows — the order
    /// the roster first wrote them down, used only for people who hold no number.
    /// </param>
    /// <param name="stored">The numbers the trip has given, by person.</param>
    /// <param name="highestGiven">
    /// The highest number the trip has ever given, counting numbers whose holders have left; zero
    /// when it has given none.
    /// </param>
    public static List<TripPartyPlace> Order(
        IEnumerable<(Guid CaverId, long FirstRowId)> roster,
        IReadOnlyDictionary<Guid, int> stored,
        int highestGiven)
    {
        var numbered = new List<TripPartyPlace>();
        var unnumbered = new List<(Guid CaverId, long FirstRowId)>();
        foreach (var member in roster)
        {
            if (stored.TryGetValue(member.CaverId, out var number))
            {
                numbered.Add(new TripPartyPlace(member.CaverId, number));
            }
            else
            {
                unnumbered.Add(member);
            }
        }

        numbered.Sort((a, b) => a.Number.CompareTo(b.Number));
        var next = Math.Max(highestGiven, numbered.Count > 0 ? numbered[^1].Number : First - 1) + 1;
        foreach (var member in unnumbered.OrderBy(m => m.FirstRowId))
        {
            numbered.Add(new TripPartyPlace(member.CaverId, next++));
        }
        return numbered;
    }
}
