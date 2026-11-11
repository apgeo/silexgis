// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Surveys;

/// <summary>
/// What a survey model is for, which is the unit the "current" flag is counted over.
/// </summary>
/// <remarks>
/// <para>
/// A cave accumulates survey models — uploads are immutable, so every corrected re-export is a
/// new row beside the old one — and until one of them is marked, nothing on the record says which
/// one the cave is represented by. The mark is held per kind rather than per cave: a line plot
/// and a wall mesh answer different questions (the map and the statistics read line work; the 3D
/// scene draws walls), are exported by different tools at different times, and a cave very often
/// has exactly one of each. One flag across both would mean marking the mesh current silently
/// un-marks the survey the statistics were measured over.
/// </para>
/// <para>
/// The two line-plot formats are one kind, deliberately: they are the same thing written two
/// ways, and a cave that holds its survey in both of them has one survey, not two. The two
/// point-cloud formats are one kind for the same reason — a LAZ file is a LAS file with its
/// points compressed — and a point cloud is a kind of its own because it answers a third question
/// (what the scanner saw), is made by a third family of tools, and would otherwise take the
/// line plot's mark away from the survey the figures are measured over.
/// </para>
/// </remarks>
public enum SurveyModelKind
{
    /// <summary>Stations and legs: a Therion <c>.lox</c> or Survex <c>.3d</c> file.</summary>
    LinePlot = 0,

    /// <summary>A triangle mesh of the passage walls.</summary>
    WallMesh = 1,

    /// <summary>A LiDAR point cloud: a <c>.laz</c> or <c>.las</c> file.</summary>
    PointCloud = 2,
}

public static class SurveyModelKinds
{
    /// <summary>The kind a model of this format is, which decides whose current mark it competes for.</summary>
    public static SurveyModelKind Of(SurveyModelFormat format) => format switch
    {
        SurveyModelFormat.Stl => SurveyModelKind.WallMesh,
        SurveyModelFormat.Laz or SurveyModelFormat.Las => SurveyModelKind.PointCloud,
        _ => SurveyModelKind.LinePlot,
    };

    /// <summary>The formats that make up one kind, for a query that has only the format column to go on.</summary>
    public static IReadOnlyList<SurveyModelFormat> FormatsOf(SurveyModelKind kind) => kind switch
    {
        SurveyModelKind.WallMesh => [SurveyModelFormat.Stl],
        SurveyModelKind.PointCloud => [SurveyModelFormat.Laz, SurveyModelFormat.Las],
        _ => [SurveyModelFormat.Lox, SurveyModelFormat.Survex3d],
    };
}
