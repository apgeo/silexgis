// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Scans;
using SilexGis.Domain.Surveys;

namespace SilexGis.Domain.Tests;

public class ScanExternalStepsTests
{
    [Fact]
    public void A_worker_step_switched_elsewhere_runs_elsewhere_and_the_rest_do_not()
    {
        var recipe = Recording();
        recipe.RunsElsewhere(HdmappingOdometryStep.StepId).ShouldBeFalse();

        var switched = PointCloudRecipes.WithExternal(recipe, HdmappingOdometryStep.StepId).ShouldNotBeNull();
        switched.RunsElsewhere(HdmappingOdometryStep.StepId).ShouldBeTrue();
        switched.RunsElsewhere(HdmappingExportStep.StepId).ShouldBeFalse();
        // The steps and their hashes are untouched: a product made elsewhere matches as a worker's would.
        switched.Steps.Select(s => s.ParamsHash).ShouldBe(recipe.Steps.Select(s => s.ParamsHash));

        // Switching it again changes nothing.
        PointCloudRecipes.WithExternal(switched, HdmappingOdometryStep.StepId)!.External.ShouldBe([HdmappingOdometryStep.StepId]);
    }

    [Fact]
    public void A_step_the_api_does_or_one_the_recipe_lacks_is_not_switched()
    {
        var recipe = Recording();
        PointCloudRecipes.WithExternal(recipe, PointCloudInspectStep.StepId).ShouldBeNull();
        PointCloudRecipes.WithExternal(recipe, PointCloudDownsampleStep.StepId).ShouldBeNull();
        PointCloudRecipes.WithExternal(recipe, "no.such.step").ShouldBeNull();
        // A step whose products have no name to be sent back under would wait for nothing.
        PointCloudRecipes.WithExternal(recipe, "py3dtiles.tile").ShouldBeNull();
    }

    [Fact]
    public void The_steps_done_elsewhere_and_the_wait_survive_the_options_document()
    {
        var inputs = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var recipe = PointCloudRecipes.WithExternal(Recording(), HdmappingOdometryStep.StepId)! with
        {
            Waiting = new ScanExternalWait(HdmappingOdometryStep.StepId, inputs),
        };

        var read = ScanRecipes.FromOptionsJson(ScanRecipes.ToOptionsJson(recipe)).ShouldNotBeNull();
        read.External.ShouldBe([HdmappingOdometryStep.StepId]);
        read.Waiting.ShouldNotBeNull().StepId.ShouldBe(HdmappingOdometryStep.StepId);
        read.Waiting.InputArtifactIds.ShouldBe(inputs);

        // A recipe stored before either existed still reads, waiting for nothing and doing everything here.
        var plain = ScanRecipes.FromOptionsJson(ScanRecipes.ToOptionsJson(Recording())).ShouldNotBeNull();
        plain.External.ShouldBeNull();
        plain.Waiting.ShouldBeNull();
        ScanRecipes.ToOptionsJson(Recording()).ShouldNotContain("external", Case.Insensitive);
    }

    [Fact]
    public void A_product_is_taken_back_only_under_a_name_the_waiting_step_makes()
    {
        var odometry = new HdmappingOdometryStep();
        ScanExternalOutputs.KindOf(odometry, "session.zip").ShouldBe(ScanArtifactKind.HdmSession);
        ScanExternalOutputs.KindOf(odometry, "SESSION.ZIP").ShouldBe(ScanArtifactKind.HdmSession);
        ScanExternalOutputs.KindOf(odometry, "cloud.laz").ShouldBeNull();
        ScanExternalOutputs.KindOf(odometry, "session.tar").ShouldBeNull();

        var export = new HdmappingExportStep();
        ScanExternalOutputs.KindOf(export, "cloud.las").ShouldBe(ScanArtifactKind.PointCloudLaz);
        ScanExternalOutputs.KindOf(export, "trajectory.csv").ShouldBe(ScanArtifactKind.Trajectory);
        ScanExternalOutputs.FormatOf("Cloud.LAS").ShouldBe("las");
    }

    [Theory]
    [InlineData("session.mjs", true)]
    [InlineData("lio/scan_lio_0.laz", true)]
    [InlineData("../outside.laz", false)]
    [InlineData("lio/../../outside.laz", false)]
    [InlineData("..\\outside.laz", false)]
    [InlineData("/etc/passwd", false)]
    [InlineData("\\server\\share", false)]
    [InlineData("C:/Windows/x", false)]
    [InlineData("", false)]
    public void A_member_of_a_sent_archive_must_stay_inside_it(string name, bool contained) =>
        ScanExternalOutputs.IsContainedMember(name).ShouldBe(contained);

    [Fact]
    public void A_refined_session_runs_a_recordings_recipe_without_the_odometry()
    {
        var refined = PointCloudRecipes.ForRefinedSession(new AcceptAll(), choice: null, rerunFrom: null, out _).ShouldNotBeNull();
        refined.Steps.Select(s => s.StepId).ShouldBe(
            Recording().Steps.Select(s => s.StepId).Where(id => id != HdmappingOdometryStep.StepId));
        refined.Steps[1].StepId.ShouldBe(HdmappingExportStep.StepId);

        PointCloudRecipes.StartsFromSession(refined).ShouldBeTrue();
        PointCloudRecipes.StartsFromSession(Recording()).ShouldBeFalse();
        PointCloudRecipes.StartsFromSession(PointCloudRecipes.For(new AcceptAll(), null, null, out _)).ShouldBeFalse();
        PointCloudRecipes.StartsFromSession(null).ShouldBeFalse();

        // There is no odometry to move to another computer.
        PointCloudRecipes.WithExternal(refined, HdmappingOdometryStep.StepId).ShouldBeNull();
    }

    [Fact]
    public void A_refined_session_is_declared_alone_as_the_archive()
    {
        const long cap = 1000;
        Problem(("session.zip", 10, SurveyUploadFileRole.Archive)).ShouldBeNull();
        Problem(("Session.ZIP", 10, SurveyUploadFileRole.Archive)).ShouldBeNull();
        MandeyeRecording.IsRefinedSession(["SESSION.zip"]).ShouldBeTrue();
        MandeyeRecording.IsRefinedSession(["session.zip", "imu0000.csv"]).ShouldBeFalse();
        MandeyeRecording.IsRefinedSession(["lidar0000.laz"]).ShouldBeFalse();

        Problem(("session.zip", 10, SurveyUploadFileRole.Archive), ("lidar0000.laz", 10, SurveyUploadFileRole.Archive))
            .ShouldNotBeNull().File.ShouldBe("lidar0000.laz");
        Problem(("session.zip", 10, SurveyUploadFileRole.Other)).ShouldNotBeNull();
        Problem(("session.zip", cap + 1, SurveyUploadFileRole.Archive)).ShouldNotBeNull();
        Problem(("session.zip", 0, SurveyUploadFileRole.Archive)).ShouldNotBeNull();

        static SurveyUploadFileProblem? Problem(params (string Name, long Size, SurveyUploadFileRole Role)[] files) =>
            MandeyeRecording.Problem([.. files.Select(f => new SurveyUploadFileClaim(f.Name, f.Size, f.Role))], cap, cap * 10, 100);
    }

    [Fact]
    public void A_refined_session_is_archived_as_an_hdmapping_session_that_must_be_a_zip()
    {
        SurveySourceFormats.ClaimedKind("session.zip", SurveyOrigin.Mandeye).ShouldBe(SurveySourceKind.HdmSession);
        SurveySourceFormats.ClaimedKind("lidar0000.laz", SurveyOrigin.Mandeye).ShouldBe(SurveySourceKind.LidarRawSession);
        SurveySourceFormats.ClaimedKind("other.zip", SurveyOrigin.Mandeye).ShouldBeNull();
        using (var bytes = new MemoryStream())
        {
            using (var zip = new System.IO.Compression.ZipArchive(bytes, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
            {
                using var entry = zip.CreateEntry("session.mjs").Open();
                entry.Write("{}"u8);
            }

            SurveySourceFormats.ContentMatches(SurveySourceKind.HdmSession, bytes.ToArray()).ShouldBeTrue();
        }

        SurveySourceFormats.ContentMatches(SurveySourceKind.HdmSession, "{\"not\": \"a zip\"}"u8).ShouldBeFalse();
        // Never offered on the web's archive route: a session holds the scanner's path.
        SurveySourceFormats.ArchivableOnTheWeb(SurveySourceKind.HdmSession).ShouldBeFalse();
    }

    private static ScanRecipe Recording() =>
        PointCloudRecipes.ForRecording(new AcceptAll(), choice: null, rerunFrom: null, odometry: null, out _).ShouldNotBeNull();

    private sealed class AcceptAll : ITypedPropertiesValidator
    {
        public IReadOnlyList<string> Validate(string schemaJson, string propertiesJson) => [];

        public IReadOnlyList<string> ValidateSchema(string schemaJson) => [];
    }
}
