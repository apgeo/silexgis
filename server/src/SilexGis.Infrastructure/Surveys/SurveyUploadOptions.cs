// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Surveys;

namespace SilexGis.Infrastructure.Surveys;

/// <summary>
/// How surveys sent from a field device are received: how large and how many files, whether a
/// survey that names no cave may make one, and what a processing run produces.
/// </summary>
public sealed class SurveyUploadOptions
{
    public const string SectionName = "SurveyUploads";

    /// <summary>
    /// The largest single file of an upload, in bytes. A phone's survey archive carries its
    /// sketches and photos, so this is set well above any survey source; the reverse proxy in
    /// front of the application has to let a body of this size through as well.
    /// </summary>
    public long MaxFileBytes { get; set; } = 250L * 1024 * 1024;

    /// <summary>The most files one upload may declare.</summary>
    public int MaxFilesPerUpload { get; set; } = 64;

    /// <summary>
    /// Whether an upload that names no cave may make one, named after the survey. Off, such an
    /// upload is refused and the device has to name a cave the account can write to.
    /// </summary>
    public bool CaveCreation { get; set; } = true;

    /// <summary>
    /// The cave type a cave made from an upload gets when the upload names none. Empty means the
    /// installation's ordinary cave type.
    /// </summary>
    public long? DefaultCaveTypeId { get; set; }

    /// <summary>
    /// The outputs a run produces when the upload asks for none, as a comma-separated list of
    /// catalogue names.
    /// </summary>
    public string DefaultOutputs { get; set; } = string.Join(",", SurveyOutputs.Defaults);

    /// <summary>
    /// Outputs every run on this installation produces, whatever the upload and the account ask
    /// for, as a comma-separated list of catalogue names. Empty by default.
    /// </summary>
    public string AlwaysOutputs { get; set; } = "";

    /// <summary>
    /// How many hours an opened upload waits for its files before it is abandoned. The files that
    /// did arrive stay archived.
    /// </summary>
    public int UploadSessionHours { get; set; } = 24;

    /// <summary>
    /// The most a survey archive may unpack to, in bytes, counting every entry it declares — the
    /// photos it carries included. An archive declaring more is refused unread; an entry that
    /// expands past what it declares is refused as it is read.
    /// </summary>
    public long MaxUnpackedBytes { get; set; } = 2L * 1024 * 1024 * 1024;

    /// <summary>The unpacking cap, never below one byte.</summary>
    public long UnpackedCap => Math.Max(1, MaxUnpackedBytes);

    /// <summary>The session lifetime, held to between one hour and a week.</summary>
    public TimeSpan UploadSession => TimeSpan.FromHours(Math.Clamp(UploadSessionHours, 1, 24 * 7));

    /// <summary>The per-file cap, never below one byte.</summary>
    public long FileCap => Math.Max(1, MaxFileBytes);

    /// <summary>The file-count cap, never below one: an upload needs at least its archive.</summary>
    public int FileCountCap => Math.Max(1, MaxFilesPerUpload);

    /// <summary>The defaults as a list, falling back to the catalogue's own when the setting names nothing known.</summary>
    public IReadOnlyList<string> DefaultOutputList()
    {
        var parsed = SurveyOutputs.Parse(DefaultOutputs).Where(SurveyOutputs.IsKnown).ToList();
        return parsed.Count > 0 ? parsed : SurveyOutputs.Defaults;
    }

    /// <summary>The installation's always-produced outputs as a list.</summary>
    public IReadOnlyList<string> AlwaysOutputList() => SurveyOutputs.Parse(AlwaysOutputs);
}
