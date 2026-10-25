// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text;
using SilexGis.Domain.Surveys;
using Therion.Workspace.Import;

namespace SilexGis.Infrastructure.Surveys;

/// <summary>An uploaded Therion file, by its bare name, with its text.</summary>
public sealed record UploadedTherionFile(string Name, string Text);

/// <summary>What a compile run is built from.</summary>
/// <param name="Survey">The uploaded survey file.</param>
/// <param name="Sketches">The sketch files uploaded with it.</param>
/// <param name="Outputs">The outputs to produce, by catalogue name.</param>
/// <param name="MapScale">The drawings' scale, as 1 to this.</param>
/// <param name="Title">The survey's title, for the drawings.</param>
/// <param name="Fix">A fixed station with a position read from the device's archive, used only when the survey file fixes none itself.</param>
public sealed record SurveyScaffoldInput(
    UploadedTherionFile Survey,
    IReadOnlyList<UploadedTherionFile> Sketches,
    IReadOnlyList<string> Outputs,
    int MapScale,
    string Title,
    TopoDroidFixedPoint? Fix);

/// <summary>One output a compile was asked for: its catalogue name and where in the project the compiler writes it.</summary>
public sealed record SurveyExpectedOutput(string Name, string RelativePath);

/// <summary>A project written out, ready to compile.</summary>
/// <param name="ConfigFileName">The compiler configuration, in the project directory.</param>
/// <param name="ExpectedOutputs">What the configuration asks the compiler to write, in order.</param>
/// <param name="SketchesDrawn">The uploaded sketches the compiled survey reads.</param>
/// <param name="Georeferenced">Whether the compiled survey is fixed to a position, by its own fix or by the archive's.</param>
/// <param name="GeneratedFiles">The files written around the survey (the configuration and the wrapper survey), relative to the project.</param>
public sealed record SurveyScaffold(
    string ConfigFileName,
    IReadOnlyList<SurveyExpectedOutput> ExpectedOutputs,
    IReadOnlyList<string> SketchesDrawn,
    bool Georeferenced,
    IReadOnlyList<string> GeneratedFiles);

/// <summary>
/// Builds the Therion project an uploaded survey is compiled in: the compile copies of the survey
/// and its sketches, a wrapper survey around them and the configuration that names the outputs.
/// </summary>
/// <remarks>
/// The wrapper and the configuration are planned by the survey library's project scaffold, which
/// writes nothing itself; this writes what it planned. The copies are what the compiler reads, so
/// they are where the upload is made compilable and checked: see <see cref="TherionCompileCopy"/>.
/// When the survey file fixes no station but the device's archive does, the wrapper fixes that
/// station, in longitude and latitude as the archive stores them.
/// </remarks>
public static class SurveyUploadScaffold
{
    /// <summary>The name of the wrapper survey that contains the uploaded one.</summary>
    public const string ProjectName = "upload";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Writes the project for <paramref name="input"/> into <paramref name="projectDirectory"/>, which
    /// must exist and be empty. Throws <see cref="SurveyPipelineException"/> when the upload reads a
    /// file it did not bring, or cannot be built into a project.
    /// </summary>
    public static SurveyScaffold Write(string projectDirectory, SurveyScaffoldInput input)
    {
        var sketchNames = input.Sketches.Select(s => s.Name).ToList();
        var copy = TherionCompileCopy.ForCompile(input.Survey.Text, sketchNames);
        var sketches = input.Sketches.Select(s => s with { Text = TherionCompileCopy.Normalise(s.Text) }).ToList();

        var uploaded = sketchNames.Append(input.Survey.Name).ToList();
        var refused = TherionCompileCopy.UnsafeReferences(input.Survey.Name, copy.Text, uploaded)
            .Concat(sketches.SelectMany(s => TherionCompileCopy.UnsafeReferences(s.Name, s.Text, uploaded)))
            .ToList();
        if (refused.Count > 0)
        {
            var first = refused[0];
            throw new SurveyPipelineException(
                SurveyPipelineFailures.SourceInvalid,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{first.File}, line {first.Line}: '{first.Command}' names a file that is not part of this upload. A survey may read only the files sent with it, by their bare names."));
        }

        var header = TopodroidSurveyHeaderReader.Read(copy.Text);
        var exports = new List<ExportItem>();
        var names = new List<string>();
        foreach (var name in input.Outputs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (ScaffoldExportPresets.Catalogue.TryGetValue(name, out var items))
            {
                exports.AddRange(items);
                names.AddRange(items.Select(_ => name));
            }
        }

        var archiveFix = !header.HasFix && input.Fix is { } fix && fix.Station.Length > 0
            ? (Station: fix.Station, fix.Longitude, fix.Latitude, Height: fix.GeoidHeight ?? fix.EllipsoidHeight ?? 0)
            : ((string Station, double Longitude, double Latitude, double Height)?)null;

        var options = new ScaffoldOptions
        {
            ProjectName = ProjectName,
            InnerSurveyName = header.SurveyName,
            SourceFileName = input.Survey.Name,
            Title = TherionCompileCopy.Normalise(input.Title.Replace('\n', ' ').Replace('\r', ' ')),
            Scale = input.MapScale,
            Exports = exports,
            SketchFileNames = sketchNames,
            SurveyHasFix = header.HasFix,
            CoordinateSystem = "long-lat",
            EntranceStation = archiveFix?.Station ?? "",
            FixC1 = archiveFix is { } a1 ? a1.Longitude.ToString("R", CultureInfo.InvariantCulture) : "",
            FixC2 = archiveFix is { } a2 ? a2.Latitude.ToString("R", CultureInfo.InvariantCulture) : "",
            FixC3 = archiveFix is { } a3 ? a3.Height.ToString("R", CultureInfo.InvariantCulture) : "0",
        };

        ScaffoldPlan plan;
        try
        {
            plan = TopodroidProjectScaffold.BuildPlan(options);
        }
        catch (ArgumentException)
        {
            // The library's message quotes the value it refused, which can be a coordinate; the
            // row says only what kind of thing went wrong.
            throw new SurveyPipelineException(
                SurveyPipelineFailures.CompileFailed,
                "The survey could not be built into a Therion project: its survey name, a station name or "
                + "its title holds characters Therion does not accept there.");
        }

        foreach (var directory in plan.Directories)
        {
            Directory.CreateDirectory(Under(projectDirectory, directory));
        }

        foreach (var file in plan.Files)
        {
            File.WriteAllText(Under(projectDirectory, file.RelativePath), file.Content, Utf8);
        }

        var texts = sketches.ToDictionary(s => s.Name, s => s.Text, StringComparer.Ordinal);
        texts[input.Survey.Name] = copy.Text;
        foreach (var source in plan.SourceCopies)
        {
            var path = Under(projectDirectory, source.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, texts[source.SourceFileName], Utf8);
        }

        return new SurveyScaffold(
            plan.ThconfigRelativePath,
            plan.ExpectedOutputs.Select((e, i) => new SurveyExpectedOutput(names[i], e.RelativePath)).ToList(),
            copy.SketchesDrawn,
            header.HasFix || archiveFix is not null,
            plan.Files.Select(f => f.RelativePath).ToList());
    }

    // The plan's paths are relative with forward slashes and checked by the library not to climb
    // out; they are joined segment by segment so the result is right on every platform.
    private static string Under(string root, string relative) =>
        Path.Combine([root, .. relative.Split('/', StringSplitOptions.RemoveEmptyEntries)]);
}
