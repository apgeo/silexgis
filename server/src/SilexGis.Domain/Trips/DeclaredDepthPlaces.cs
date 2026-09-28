// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Trips;

/// <summary>
/// What a cave has said its depths mean, and how that answer relates to measuring one.
/// </summary>
/// <remarks>
/// <para>
/// <b>A declaration outranks a measurement, and only where one exists.</b> Measuring — the nearest
/// station to the datum under the trip's filter — is the general answer and stays the answer for
/// every depth nobody has declared. Where a cave has declared a depth, that is the answer and no
/// measuring is attempted: the declaration was written by somebody who knows the cave, and the
/// reason it exists at all is that the arithmetic disagrees with the cave's shape — parallel shafts
/// share a depth, and the nearest station to a round number may be somewhere nobody goes.
/// </para>
/// <para>
/// <b>Exact on the depth, not nearest.</b> A declaration is a statement about one depth, and
/// reading it as "the nearest declared depth" would quietly re-introduce the very guessing it was
/// written to replace — a party at −402 would be filed at a declared −400 without anybody saying
/// so. A depth nobody declared falls through to measurement, which is honest about being
/// approximate and says how far off it was.
/// </para>
/// <para>
/// Nothing here reads a database or a survey: it is given the declarations and decides only what a
/// depth means. Whether the station it names exists on the survey in force is the caller's
/// question, and a declaration naming a station a re-exported model no longer has is a visible,
/// fixable fault rather than a silent one.
/// </para>
/// </remarks>
public static class DeclaredDepthPlaces
{
    /// <summary>One depth a cave has declared, reduced to what resolution needs.</summary>
    /// <param name="DepthM">Metres below the entrance datum, positive downwards.</param>
    /// <param name="ViewerStationName">The station it means, in the survey viewer's spelling.</param>
    /// <param name="PlaceLabel">What people call it, or null where there is no such word.</param>
    public readonly record struct Declared(decimal DepthM, string ViewerStationName, string? PlaceLabel);

    /// <summary>
    /// The station a cave has declared for this depth, or null when it has declared none.
    /// </summary>
    /// <remarks>
    /// Null is the ordinary answer: most caves declare nothing, and a cave that declares its
    /// bivouac and its sump has said nothing about the other four hundred metres.
    /// </remarks>
    public static Declared? For(IReadOnlyCollection<Declared> declarations, decimal depthM)
    {
        ArgumentNullException.ThrowIfNull(declarations);

        foreach (var declaration in declarations)
        {
            if (declaration.DepthM == depthM)
            {
                return declaration;
            }
        }

        return null;
    }

    /// <summary>
    /// The declaration people call by this name, or null when none is called that.
    /// </summary>
    /// <remarks>
    /// <para>
    /// How a sheet or a screen names a place instead of a depth or a station. Matched through the
    /// project's one name folder, so a label typed without diacritics or with a doubled space is
    /// the same place — the same reason a caver's name is matched that way.
    /// </para>
    /// <para>
    /// A name two declarations answer to is <b>no answer</b> rather than the first of them: two
    /// places a club calls the same thing is a thing to fix in the declarations, and picking one
    /// would file a report at whichever happened to be written first.
    /// </para>
    /// </remarks>
    public static Declared? ByLabel(IReadOnlyCollection<Declared> declarations, string? label)
    {
        ArgumentNullException.ThrowIfNull(declarations);

        var asked = Import.TripImportNames.Key(label);
        if (asked.Length == 0)
        {
            return null;
        }

        Declared? found = null;
        foreach (var declaration in declarations)
        {
            if (Import.TripImportNames.Key(declaration.PlaceLabel) != asked)
            {
                continue;
            }

            if (found is not null)
            {
                return null;
            }

            found = declaration;
        }

        return found;
    }
}
