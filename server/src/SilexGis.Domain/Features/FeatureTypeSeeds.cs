// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Features;

/// <summary>
/// Codes of shipped feature types that something other than the seeder depends on by name.
/// </summary>
/// <remarks>
/// Not the whole palette: the full list of shipped kinds lives with the seeder that writes it, and
/// copying it here would be two lists to keep in step for no reader. What belongs here is the codes
/// a behaviour is built on — a query that resolves a kind by code and quietly answers with nothing
/// when the row is missing. Held as a constant, a rename fails the build; held as a literal in two
/// places, it turns into a feature that returns an empty, entirely plausible answer forever.
/// <para>
/// Add a code here the moment a second place needs it, and have the seeder emit the same constant.
/// </para>
/// </remarks>
public static class FeatureTypeSeeds
{
    /// <summary>
    /// An open way on: the place a club records as "somebody has to go back and push this". It
    /// carries the state of the way on and how promising it looked, and the leads a camp turned up
    /// are read off places of this kind.
    /// </summary>
    public const string Continuation = "continuation";

    /// <summary>
    /// A stretch of country a club works: a massif, a karst zone, a valley system. It is the kind
    /// a work area is <em>declared</em> by rather than a label put on one, so that what is a work
    /// area is a fact about the row and not about whichever list somebody happened to add it to.
    /// <para>
    /// Its own kind rather than a tag on the areas that already exist, and that is the whole of
    /// the decision. A tag is installation-wide free text: renaming or deleting the one that means
    /// "we work here" would empty the board that lists them, silently and permanently, and nothing
    /// would fail. A kind is resolved by this constant, and the board that cannot find it answers
    /// empty — so the code is held in one place where a rename fails the build.
    /// </para>
    /// <para>
    /// The levels below one — a valley inside a massif, a sector inside a valley — are not a
    /// second kind. They are rows of this kind sitting under another through the containment
    /// hierarchy every feature already has, which is why a sub-area is a work area in its own
    /// right the moment somebody opens it.
    /// </para>
    /// </summary>
    public const string WorkArea = "work_area";

    /// <summary>
    /// A closed surface depression — a doline or swallow hole. It is resolved by this code wherever
    /// depressions are counted, measured or asked about as a class rather than drawn, so a rename of
    /// the shipped kind fails the build instead of turning those answers into empty ones that look
    /// perfectly reasonable.
    /// </summary>
    public const string Sinkhole = "sinkhole";

    /// <summary>
    /// A karst area: an outline a registry files caves under. With <see cref="Massif"/> it is the
    /// kind the karstification index is published for in a map window, so a rename fails the build
    /// rather than leaving that layer answering, plausibly, with no areas anywhere.
    /// </summary>
    public const string KarstArea = "karst_area";

    /// <summary>A massif or mountain: the wider outline karst areas sit in. See <see cref="KarstArea"/>.</summary>
    public const string Massif = "massif";

    /// <summary>
    /// A mapped fracture or fault trace: the line somebody drew where the rock is broken. It is
    /// resolved by this code when a cave's passage trends are compared against the structure around
    /// it, so a rename of the shipped kind fails the build rather than turning that comparison into
    /// one that finds no faults anywhere and says so plausibly.
    /// </summary>
    public const string FractureLine = "fracture_line";

    /// <summary>
    /// The one shipped type bound to the cave kind rather than to generic rows. Every cave carries
    /// it, which is what lets "type is cave" and a club's own cave fields work in the same filter
    /// and the same typed-properties schema as every other kind. Resolved by this code by the write
    /// service that stamps it on every new cave and by the migration that gave it to existing ones.
    /// </summary>
    public const string Cave = "cave";

    /// <summary>
    /// A hill: an outline on the surface, an area kind exactly like <see cref="Massif"/> and with no
    /// relation to caves beyond what any surface feature has. Listed beside the massif wherever the
    /// area kinds are resolved by code, so the two answer alike.
    /// </summary>
    public const string Hill = "hill";
}
