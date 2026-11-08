// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>Who a processing profile is kept for. Stored as smallint; append-only.</summary>
public enum ProcessingProfileScope : short
{
    /// <summary>Every account of the installation may use it.</summary>
    Installation = 0,

    /// <summary>The members of one caving group may use it.</summary>
    CavingGroup = 1,

    /// <summary>Its author alone may see and use it.</summary>
    Personal = 2,
}

/// <summary>
/// A named set of parameters for one scan processing step — HDMapping's odometry, tiling,
/// thinning — that a person picks when a scan is processed instead of typing the values each time.
/// </summary>
/// <remarks>
/// <para>
/// The values are held in exactly the shape the step's parameters take in a recipe, so applying a
/// profile is a copy and never a translation. A recipe never refers to a profile, though: an upload
/// snapshots the values it resolved, so editing or deleting a profile later never changes what a
/// past run means.
/// </para>
/// <para>
/// The tool version is the version of the tool the values were written for, as the worker
/// announced it when the profile was saved. A profile is still offered after the tool is upgraded;
/// the version is what lets a reader see that it was tuned for another one.
/// </para>
/// <para>
/// A built-in profile has no owner and is never edited in place: whoever wants to change one saves
/// a copy of their own.
/// </para>
/// </remarks>
public class ProcessingProfile : ITimestamped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The registered step whose parameters these are.</summary>
    public required string StepId { get; set; }

    public string? ToolVersion { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public ProcessingProfileScope Scope { get; set; } = ProcessingProfileScope.Personal;

    /// <summary>The author. Null only for a built-in profile, which nobody authored here.</summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>The caving group a group profile is kept for; null for every other scope.</summary>
    public Guid? CavingGroupId { get; set; }

    /// <summary>The step's parameters, as one JSON object in the shape a recipe stores them.</summary>
    public string Values { get; set; } = "{}";

    /// <summary>
    /// Where the values came from: <see cref="ProcessingProfileOrigins.BuiltIn"/>,
    /// <see cref="ProcessingProfileOrigins.Edited"/>, or <c>imported:</c> followed by the name of
    /// the file they were read from.
    /// </summary>
    public string Origin { get; set; } = ProcessingProfileOrigins.Edited;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>The words a profile's origin is written in.</summary>
public static class ProcessingProfileOrigins
{
    public const string BuiltIn = "builtIn";

    public const string Edited = "edited";

    public const string ImportedPrefix = "imported:";

    /// <summary>The longest file name an imported profile records; longer ones are cut.</summary>
    public const int MaxFileNameLength = 255;

    public static string Imported(string fileName)
    {
        var name = Path.GetFileName(fileName ?? string.Empty).Trim();
        if (name.Length > MaxFileNameLength)
        {
            name = name[..MaxFileNameLength];
        }

        return ImportedPrefix + name;
    }

    public static bool IsBuiltIn(string origin) => origin == BuiltIn;
}
