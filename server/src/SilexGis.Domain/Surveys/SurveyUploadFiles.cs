// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Surveys;

/// <summary>One file an upload says it will send: its name, its size in bytes, and what it is.</summary>
public sealed record SurveyUploadFileClaim(string? Name, long Size, SurveyUploadFileRole Role);

/// <summary>What is wrong with a declared file list, and which file, when it is one file.</summary>
public sealed record SurveyUploadFileProblem(string Detail, string? File = null);

/// <summary>
/// The rules a device's list of files must keep before anything of it is accepted: which names,
/// which kinds, how large, how many, and exactly one archive.
/// </summary>
/// <remarks>
/// <para>
/// Every file is checked when the upload is opened, not when its bytes arrive, so a device learns
/// that its fifth file will be refused before it has spent minutes of a cave-entrance signal on the
/// first four.
/// </para>
/// <para>
/// A name is addressed as the last segment of an address and stored as a document's original name,
/// so it holds no path, no control character, and nothing that only some systems tolerate as a
/// file name's end. Two names that differ only in case are one name: a server on Windows would
/// store them as one file.
/// </para>
/// </remarks>
public static class SurveyUploadFiles
{
    /// <summary>The refusal for a file list that breaks any of these rules.</summary>
    public const string InvalidCode = "survey_upload.files_invalid";

    /// <summary>The longest file name kept, as a document's original name holds it.</summary>
    public const int MaxNameLength = 255;

    /// <summary>
    /// Every extension an upload may carry: the survey tool's archive, the Therion files, and the
    /// exports a survey tool commonly writes beside them. Lower-case, with the dot.
    /// </summary>
    public static readonly IReadOnlyList<string> AcceptedExtensions =
    [
        ".zip", ".th", ".th2", ".thconfig", ".svx", ".log", ".svg", ".pdf", ".dxf", ".kml", ".gpx",
        ".csv", ".dat", ".tro", ".trox", ".srv", ".plt", ".shz", ".csx", ".cave", ".cav", ".sur",
        ".trb", ".txt",
    ];

    /// <summary>The roles a device may declare; the rest are what a processing run produces.</summary>
    public static readonly IReadOnlyList<SurveyUploadFileRole> DeclarableRoles =
    [
        SurveyUploadFileRole.Archive,
        SurveyUploadFileRole.TherionSource,
        SurveyUploadFileRole.TherionSketch,
        SurveyUploadFileRole.Other,
    ];

    // Characters a name may not hold: path separators, and what Windows refuses in a file name.
    private const string Forbidden = "/\\:*?\"<>|";

    /// <summary>A name's extension, lower-case and with its dot, or empty when it has none.</summary>
    public static string ExtensionOf(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot <= 0 || dot == name.Length - 1 ? "" : name[dot..].ToLowerInvariant();
    }

    /// <summary>Whether a single name may be a file of an upload.</summary>
    public static bool IsValidName(string? name) =>
        !string.IsNullOrEmpty(name)
        && name.Length <= MaxNameLength
        && name == name.Trim()
        && !name.EndsWith('.')
        && name != "." && name != ".."
        && !name.Any(c => char.IsControl(c) || Forbidden.Contains(c));

    /// <summary>Which extension a declared role requires, or null when any accepted one will do.</summary>
    public static string? ExtensionFor(SurveyUploadFileRole role) => role switch
    {
        SurveyUploadFileRole.Archive => ".zip",
        SurveyUploadFileRole.TherionSource => ".th",
        SurveyUploadFileRole.TherionSketch => ".th2",
        _ => null,
    };

    /// <summary>The first thing wrong with a declared file list, or null when it may be accepted.</summary>
    /// <param name="files">The files as declared.</param>
    /// <param name="maxFileBytes">The largest one file may be.</param>
    /// <param name="maxFiles">The most files one upload may declare.</param>
    public static SurveyUploadFileProblem? Problem(
        IReadOnlyList<SurveyUploadFileClaim>? files, long maxFileBytes, int maxFiles)
    {
        if (files is null || files.Count == 0)
        {
            return new SurveyUploadFileProblem("An upload declares its files, the survey archive among them.");
        }

        if (files.Count > maxFiles)
        {
            return new SurveyUploadFileProblem($"An upload may declare at most {maxFiles} files.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            if (!IsValidName(file.Name))
            {
                return new SurveyUploadFileProblem(
                    $"A file name must be 1 to {MaxNameLength} characters with no path, no control "
                    + "characters and none of / \\ : * ? \" < > |, and must not end with a dot or a space.",
                    file.Name);
            }

            var name = file.Name!;
            if (!seen.Add(name))
            {
                return new SurveyUploadFileProblem("Two files of one upload have the same name.", name);
            }

            var extension = ExtensionOf(name);
            if (!AcceptedExtensions.Contains(extension))
            {
                return new SurveyUploadFileProblem(
                    $"This kind of file is not accepted. Accepted: {string.Join(", ", AcceptedExtensions)}.", name);
            }

            if (!DeclarableRoles.Contains(file.Role))
            {
                return new SurveyUploadFileProblem(
                    "A file sent is the archive, a Therion source, a Therion sketch or another export.", name);
            }

            if (ExtensionFor(file.Role) is { } required && extension != required)
            {
                return new SurveyUploadFileProblem($"A file declared as this role is a {required} file.", name);
            }

            // The archive is the one file the processing run reads as the survey's own record, so
            // a second ZIP cannot ride along as "another export" and leave which one is meant to
            // a guess.
            if (file.Role == SurveyUploadFileRole.Other && extension == ".zip")
            {
                return new SurveyUploadFileProblem("Only the survey archive may be a .zip file.", name);
            }

            if (file.Size <= 0 || file.Size > maxFileBytes)
            {
                return new SurveyUploadFileProblem(
                    $"Each file is between 1 byte and {maxFileBytes} bytes.", name);
            }
        }

        var archives = files.Count(f => f.Role == SurveyUploadFileRole.Archive);
        return archives == 1
            ? null
            : new SurveyUploadFileProblem("An upload carries exactly one survey archive.");
    }
}

/// <summary>What a processing run of an upload is asked to do, kept on the upload so a rerun can repeat it.</summary>
/// <param name="Compile">Whether to compile the survey's sources at all.</param>
/// <param name="Outputs">The outputs to produce, catalogue names, already combined with the account's and the installation's.</param>
/// <param name="MapScale">The scale the maps are drawn at, as 1:<c>MapScale</c>.</param>
public sealed record SurveyUploadRunOptions(bool Compile, IReadOnlyList<string> Outputs, int MapScale)
{
    /// <summary>The scale maps are drawn at when the upload names none.</summary>
    public const int DefaultMapScale = 500;

    /// <summary>The most detailed scale accepted.</summary>
    public const int MinMapScale = 50;

    /// <summary>The least detailed scale accepted.</summary>
    public const int MaxMapScale = 100_000;
}

/// <summary>The addresses an upload is reached at on the web, relative to the installation's public address.</summary>
public static class SurveyUploadLinks
{
    /// <summary>
    /// The signed-in page a run is watched on. Every link to an upload — the device's result
    /// dialog, the notification — points here, so it is built in one place.
    /// </summary>
    public static string StatusPage(Guid caveId, Guid caveSurveyId, Guid uploadId) =>
        $"/caves/{caveId}/surveys/{caveSurveyId}/uploads/{uploadId}";

    /// <summary>The public page of an upload's results, opened by its link's token without an account.</summary>
    public static string PublicPage(string token) => $"/p/survey/{token}";
}
