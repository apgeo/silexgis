// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Documents;

/// <summary>
/// What the bytes of an archived survey source have to look like for the file to be what its
/// name claims.
/// </summary>
public enum SurveySourceEvidence
{
    /// <summary>Characters — the source languages and the compiler's log are all plain text.</summary>
    Text = 0,

    /// <summary>A ZIP container — an exported project bundle.</summary>
    ZipArchive = 1,
}

/// <summary>
/// Which raw survey sources may be archived, and how a file claiming to be one is checked.
///
/// <para>
/// A compiled survey is a derived artifact: it is what a source project produced on the day it was
/// compiled, with the compiler of that day. An installation that keeps only the compiled form
/// cannot re-compile when the toolchain moves on, so the sources are archived beside it. Nothing is
/// parsed here: what this type decides is which formats may be archived and whether a file is the
/// format its name claims, never what any of them says.
/// </para>
///
/// <para>
/// The name decides which format is <em>claimed</em>; the bytes decide whether the claim holds. A
/// list of accepted extensions on its own accepts an executable renamed to a source extension,
/// which is why every accepted kind names the evidence its content must show and
/// <see cref="ContentMatches"/> is asked before anything is recorded. The check is deliberately
/// coarse — it establishes the broad class the bytes belong to, not that the text is a valid
/// survey — because judging the contents is exactly what this archive does not do.
/// </para>
/// </summary>
public static class SurveySourceFormats
{
    /// <summary>The name claims a format nothing here accepts.</summary>
    public const string UnsupportedCode = "survey_source.format_unsupported";

    /// <summary>The name claims one format and the bytes are something else.</summary>
    public const string MismatchCode = "survey_source.content_mismatch";

    private static readonly (string Extension, SurveySourceKind Kind)[] Extensions =
    [
        // Therion's own languages: the survey data and the drawings that go with it.
        (".th", SurveySourceKind.TherionSource),
        (".th2", SurveySourceKind.TherionSource),
        (".thconfig", SurveySourceKind.TherionConfig),

        (".svx", SurveySourceKind.SurvexSource),

        // Kept because it is the only complete record of what the compilation actually did, and it
        // is lost for ever if it is not captured at upload. It is also the only archived kind whose
        // contents are read afterwards, for the loop-error table it carries.
        (".log", SurveySourceKind.TherionLog),

        // A survey app's export bundle, which is a ZIP whatever it holds inside.
        (".zip", SurveySourceKind.TopoDroidArchive),
    ];

    /// <summary>Every extension this archive accepts, lower-cased and dotted.</summary>
    public static IReadOnlyList<string> AcceptedExtensions { get; } =
        [.. Extensions.Select(e => e.Extension).Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// The kind the file name claims, or null when nothing here accepts that name. The extension
    /// is read from the last dot, so a name with no extension claims nothing.
    /// </summary>
    public static SurveySourceKind? ClaimedKind(string? fileName)
    {
        var extension = ExtensionOf(fileName);
        if (extension is null)
        {
            return null;
        }

        foreach (var (candidate, kind) in Extensions)
        {
            if (string.Equals(candidate, extension, StringComparison.Ordinal))
            {
                return kind;
            }
        }

        return null;
    }

    /// <summary>What a file of this kind must be, for the refusal to say what was expected.</summary>
    public static SurveySourceEvidence EvidenceFor(SurveySourceKind kind) => kind switch
    {
        SurveySourceKind.TopoDroidArchive => SurveySourceEvidence.ZipArchive,
        _ => SurveySourceEvidence.Text,
    };

    /// <summary>
    /// Whether the first bytes of the file are consistent with the kind its name claims.
    ///
    /// <para>
    /// Text is accepted only when no binary signature claimed the bytes first: a format that is
    /// recognisably something else is that thing, whatever its name says, and every such format is
    /// already named by the shared signature table. The declared media type takes no part — it is
    /// supplied by whoever is uploading, exactly like the name, so letting it vouch for the bytes
    /// would leave the same hole this check exists to close.
    /// </para>
    /// </summary>
    /// <param name="header">
    /// The first bytes of the file, up to <see cref="FileFormats.HeaderBytes"/>.
    /// </param>
    public static bool ContentMatches(SurveySourceKind kind, ReadOnlySpan<byte> header)
    {
        var signature = FileFormats.FromHeader(header);
        return EvidenceFor(kind) switch
        {
            SurveySourceEvidence.ZipArchive =>
                signature is not null
                && string.Equals(
                    signature.MimeType, FileFormats.ZipContainerMimeType, StringComparison.Ordinal),
            _ => signature is null && FileFormats.LooksLikeText(header),
        };
    }

    /// <summary>
    /// The media type an archived source of this kind is recorded under.
    ///
    /// <para>
    /// Survey-specific types rather than <c>text/plain</c>, because these files are survey data
    /// kept to be re-compiled, not prose kept to be read: recording them as text would enrol every
    /// one of them in the text-extraction path, which would index a survey's station names as
    /// searchable prose. The compilation log is read for the figures it reports, and that reading
    /// is a different path with a different answer to who may see the result.
    /// </para>
    /// </summary>
    public static string MediaTypeFor(SurveySourceKind kind) => kind switch
    {
        SurveySourceKind.TherionSource => "application/x-therion",
        SurveySourceKind.TherionConfig => "application/x-therion-config",
        SurveySourceKind.TherionLog => "application/x-therion-log",
        SurveySourceKind.SurvexSource => "application/x-survex",
        SurveySourceKind.TopoDroidArchive => FileFormats.ZipContainerMimeType,
        _ => FileFormats.UnknownMimeType,
    };

    /// <summary>
    /// What the file was expected to contain, in words worth putting in front of whoever
    /// uploaded it.
    /// </summary>
    public static string ExpectedContent(SurveySourceKind kind) =>
        EvidenceFor(kind) == SurveySourceEvidence.ZipArchive
            ? "a ZIP archive"
            : "a text file";

    private static string? ExtensionOf(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var name = Path.GetFileName(fileName);
        var dot = name.LastIndexOf('.');
        return dot <= 0 || dot == name.Length - 1 ? null : name[dot..].ToLowerInvariant();
    }
}
