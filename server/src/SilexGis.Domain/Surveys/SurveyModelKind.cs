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
/// Two kinds and not three, deliberately: the two line-plot formats are the same thing written two
/// ways, and a cave that holds its survey in both of them has one survey, not two.
/// </para>
/// </remarks>
public enum SurveyModelKind
{
    /// <summary>Stations and legs: a Therion <c>.lox</c> or Survex <c>.3d</c> file.</summary>
    LinePlot = 0,

    /// <summary>A triangle mesh of the passage walls.</summary>
    WallMesh = 1,
}

public static class SurveyModelKinds
{
    /// <summary>The kind a model of this format is, which decides whose current mark it competes for.</summary>
    public static SurveyModelKind Of(SurveyModelFormat format) =>
        format == SurveyModelFormat.Stl ? SurveyModelKind.WallMesh : SurveyModelKind.LinePlot;

    /// <summary>The formats that make up one kind, for a query that has only the format column to go on.</summary>
    public static IReadOnlyList<SurveyModelFormat> FormatsOf(SurveyModelKind kind) =>
        kind == SurveyModelKind.WallMesh
            ? [SurveyModelFormat.Stl]
            : [SurveyModelFormat.Lox, SurveyModelFormat.Survex3d];
}
