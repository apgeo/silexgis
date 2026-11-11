// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Shouldly;
using SilexGis.Domain.Scans;

namespace SilexGis.Domain.Tests;

/// <summary>The session rules on invented sessions laid out as HDMapping's step 1 writes them.</summary>
public class HdmappingSessionRulesTests
{
    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("scan_lio_0.laz")]
    [InlineData("sub/scan_lio_0.laz")]
    [InlineData("sub\\poses.reg")]
    [InlineData("a..b.laz")]
    public void A_relative_name_inside_the_folder_is_contained(string name) =>
        HdmappingSessionRules.IsContainedName(name).ShouldBeTrue();

    [Theory]
    [InlineData("/data/scans/jobs/other-hdmapping-export/pointCloudLaz.laz")]
    [InlineData("\\\\server\\share\\cloud.laz")]
    [InlineData("C:\\scans\\cloud.laz")]
    [InlineData("../other/pointCloudLaz.laz")]
    [InlineData("sub/../../other/pointCloudLaz.laz")]
    [InlineData("sub\\..\\..\\cloud.laz")]
    [InlineData("cloud\0.laz")]
    public void A_name_that_leaves_the_folder_is_not(string name) =>
        HdmappingSessionRules.IsContainedName(name).ShouldBeFalse();

    [Fact]
    public void A_session_of_relative_names_passes()
    {
        using var session = JsonDocument.Parse("""
            {"Session Settings": {"folder_name": ".", "out_folder_name": "", "poses_file_name": "poses.reg",
                                  "initial_poses_file_name": "", "out_poses_file_name": "out.reg", "lidar_odometry_version": "0.1"},
             "laz_file_names": [{"file_name": "scan_lio_0.laz"}, {"file_name": "scan_lio_1.laz"}]}
            """);
        HdmappingSessionRules.SessionProblem(session.RootElement).ShouldBeNull();
    }

    [Theory]
    [InlineData("""{"laz_file_names": [{"file_name": "../7f-hdmapping-export/pointCloudLaz.laz"}]}""", "file_name")]
    [InlineData("""{"Session Settings": {"poses_file_name": "/etc/passwd"}}""", "poses_file_name")]
    [InlineData("""{"Session Settings": {"folder_name": "/data/scans/jobs"}}""", "folder_name")]
    [InlineData("""{"Session Settings": {"out_folder_name": ".."}}""", "out_folder_name")]
    [InlineData("""{"Session Settings": {"initial_poses_file_name": "x/../../y"}}""", "initial_poses_file_name")]
    [InlineData("""{"Session Settings": {"out_poses_file_name": "C:/out.reg"}}""", "out_poses_file_name")]
    public void A_session_naming_a_file_outside_its_folder_is_refused_naming_the_key(string json, string key)
    {
        using var session = JsonDocument.Parse(json);
        HdmappingSessionRules.SessionProblem(session.RootElement).ShouldNotBeNull().ShouldContain($"\"{key}\"");
    }

    [Fact]
    public void A_session_that_is_not_an_object_is_refused()
    {
        using var session = JsonDocument.Parse("[]");
        HdmappingSessionRules.SessionProblem(session.RootElement).ShouldNotBeNull();
    }

    [Fact]
    public void An_archive_may_unpack_to_a_few_times_its_size_and_no_more()
    {
        const long gib = 1024L * 1024 * 1024;
        HdmappingSessionRules.UnpackedFits(gib, gib).ShouldBeTrue();
        HdmappingSessionRules.UnpackedFits(gib, 4 * gib).ShouldBeTrue();
        HdmappingSessionRules.UnpackedFits(gib, 5 * gib).ShouldBeFalse();
        // A tiny session of text compresses well and is still taken.
        HdmappingSessionRules.UnpackedFits(300, 200_000).ShouldBeTrue();
        HdmappingSessionRules.UnpackedFits(300, 2 * 1024 * 1024).ShouldBeFalse();
        HdmappingSessionRules.UnpackedFits(long.MaxValue, long.MaxValue).ShouldBeTrue();
        HdmappingSessionRules.UnpackedFits(10, -1).ShouldBeFalse();
    }
}
