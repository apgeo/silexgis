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
}
