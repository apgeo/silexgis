// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Documents;

/// <summary>
/// What may be done with a photograph, as a small closed vocabulary.
///
/// <para>
/// Closed rather than free text, because the field exists to be acted on: "may this go in the
/// bulletin" has to be answerable by looking, and a column holding "ask Ana" or "CC-BY I think"
/// answers nothing. The codes are the standard ones so they mean the same thing outside this
/// installation as inside it — a picture leaves an archive far more often than a licence
/// changes.
/// </para>
/// <para>
/// Stored as the code and never as a label. The label is translated on the client like every
/// other string, and a stored label would read in whichever language the person who set it
/// happened to be using.
/// </para>
/// </summary>
public static class PhotoLicences
{
    /// <summary>Kept by the photographer; reuse needs asking.</summary>
    public const string AllRightsReserved = "all-rights-reserved";

    /// <summary>Public domain dedication.</summary>
    public const string Cc0 = "cc0";

    public const string CcBy = "cc-by";

    public const string CcBySa = "cc-by-sa";

    public const string CcByNc = "cc-by-nc";

    public const string CcByNcSa = "cc-by-nc-sa";

    public const string CcByNd = "cc-by-nd";

    public const string CcByNcNd = "cc-by-nc-nd";

    /// <summary>
    /// Every code, in the order a chooser should offer them: the most permissive first, since
    /// that is the order somebody deciding reads them in.
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Cc0,
        CcBy,
        CcBySa,
        CcByNc,
        CcByNcSa,
        CcByNd,
        CcByNcNd,
        AllRightsReserved,
    ];

    /// <summary>
    /// Whether a value may be stored: one of the codes, or nothing at all.
    /// </summary>
    /// <remarks>
    /// Absent is deliberately valid and deliberately not the same as
    /// <see cref="AllRightsReserved"/>. "Nobody has said" is the state most of an archive is in,
    /// and recording it as a licence would claim somebody had decided something they had not.
    /// </remarks>
    public static bool IsKnown(string? code) =>
        string.IsNullOrWhiteSpace(code) || All.Contains(code, StringComparer.Ordinal);

    /// <summary>
    /// Whether this licence lets a club reuse the picture without asking first — which is the
    /// one question a bulletin editor is actually asking.
    /// </summary>
    /// <remarks>
    /// A picture nobody has recorded a licence for answers false, because the honest answer to
    /// "may I use this" when nothing was decided is "go and ask".
    /// </remarks>
    public static bool AllowsReuse(string? code) =>
        code is Cc0 or CcBy or CcBySa or CcByNc or CcByNcSa or CcByNd or CcByNcNd;

    /// <summary>Whether the licence obliges the club to name the photographer.</summary>
    public static bool RequiresAttribution(string? code) =>
        AllowsReuse(code) && code != Cc0;
}
