// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;

namespace SilexGis.Infrastructure.TestData;

/// <summary>
/// The notes a run carries, as stable codes rather than sentences: <c>code</c>, or
/// <c>code:count</c> where the note is about a number of rows. The administrators' page words
/// each code in the reader's language; a sentence stored here would reach a Romanian page in
/// English. A note the page does not know is shown as it is stored.
/// </summary>
public static class TestDataNotes
{
    /// <summary>Elevations come from an invented surface, not from a terrain build.</summary>
    public const string InventedElevations = "inventedElevations";

    /// <summary>Club-only visibility was written as signed-in-only: the run made no caving group to bind caves to.</summary>
    public const string ClubOnlyAsSignedIn = "clubOnlyAsSignedIn";

    /// <summary>Generated caves or places kept on removal because people have used them.</summary>
    public const string KeptInUse = "keptInUse";

    /// <summary>Generated areas or caves kept on removal because data no run made lies beneath them.</summary>
    public const string KeptBeneath = "keptBeneath";

    /// <summary>Generated caves written without their planned code, because a cave here already carries it.</summary>
    public const string CodesTaken = "codesTaken";

    /// <summary>Generated accounts given a tagged address, because an account here already has the planned one.</summary>
    public const string EmailsTaken = "emailsTaken";

    /// <summary>Generated accounts share the password given when the run was started.</summary>
    public const string AccountsSharePassword = "accountsSharePassword";

    /// <summary>Generated accounts have no password: no password was given when the run was started.</summary>
    public const string AccountsWithoutPassword = "accountsWithoutPassword";

    /// <summary>Generated people kept on removal because data no run made refers to them.</summary>
    public const string PeopleKeptInUse = "peopleKeptInUse";

    public static string WithCount(string code, int count) =>
        $"{code}:{count.ToString(CultureInfo.InvariantCulture)}";
}
