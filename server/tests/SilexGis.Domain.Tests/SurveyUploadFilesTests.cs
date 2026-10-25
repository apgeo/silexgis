// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Surveys;

namespace SilexGis.Domain.Tests;

public class SurveyUploadFilesTests
{
    private const long Cap = 1000;

    private static SurveyUploadFileClaim Archive(string name = "inventata.zip", long size = 10) =>
        new(name, size, SurveyUploadFileRole.Archive);

    private static SurveyUploadFileProblem? Check(params SurveyUploadFileClaim[] files) =>
        SurveyUploadFiles.Problem(files, Cap, maxFiles: 5);

    [Fact]
    public void An_archive_with_its_therion_files_and_another_export_is_accepted()
    {
        Check(
                Archive(),
                new("inventata.th", 10, SurveyUploadFileRole.TherionSource),
                new("inventata-1p.th2", 10, SurveyUploadFileRole.TherionSketch),
                new("inventata.svx", 10, SurveyUploadFileRole.Other))
            .ShouldBeNull();
    }

    [Fact]
    public void Exactly_one_archive_is_required()
    {
        SurveyUploadFiles.Problem([], Cap, 5).ShouldNotBeNull();
        SurveyUploadFiles.Problem(null, Cap, 5).ShouldNotBeNull();
        Check(new SurveyUploadFileClaim("inventata.th", 10, SurveyUploadFileRole.TherionSource)).ShouldNotBeNull();
        Check(Archive(), Archive("second.zip")).ShouldNotBeNull();

        // A second ZIP cannot ride along as "another export" either.
        Check(Archive(), new SurveyUploadFileClaim("photos.zip", 10, SurveyUploadFileRole.Other))!
            .File.ShouldBe("photos.zip");
    }

    [Theory]
    [InlineData("../inventata.th")]
    [InlineData("dir/inventata.th")]
    [InlineData("dir\\inventata.th")]
    [InlineData("inventata.th ")]
    [InlineData(" inventata.th")]
    [InlineData("inventata.th.")]
    [InlineData("in\u0001ventata.th")]
    [InlineData("C:inventata.th")]
    [InlineData("")]
    public void A_name_with_a_path_a_control_character_or_a_fragile_end_is_refused(string name)
    {
        Check(Archive(), new SurveyUploadFileClaim(name, 10, SurveyUploadFileRole.TherionSource))
            .ShouldNotBeNull();
    }

    [Fact]
    public void Names_are_unique_whatever_their_case_and_no_longer_than_a_file_name_can_be()
    {
        var problem = Check(Archive(), new SurveyUploadFileClaim("INVENTATA.ZIP", 10, SurveyUploadFileRole.Other));
        problem.ShouldNotBeNull();

        Check(Archive(new string('a', 252) + ".zip")).ShouldNotBeNull();
        Check(Archive(new string('a', 251) + ".zip")).ShouldBeNull();
    }

    [Fact]
    public void A_role_names_the_extension_it_is_and_an_unlisted_extension_is_refused()
    {
        Check(Archive("inventata.th")).ShouldNotBeNull();
        Check(Archive(), new SurveyUploadFileClaim("inventata.th2", 10, SurveyUploadFileRole.TherionSource)).ShouldNotBeNull();
        Check(Archive(), new SurveyUploadFileClaim("inventata.exe", 10, SurveyUploadFileRole.Other))!
            .File.ShouldBe("inventata.exe");
        Check(Archive(), new SurveyUploadFileClaim("README", 10, SurveyUploadFileRole.Other)).ShouldNotBeNull();

        // What a run produces is never something a device declares.
        Check(Archive(), new SurveyUploadFileClaim("inventata.log", 10, SurveyUploadFileRole.Log)).ShouldNotBeNull();
    }

    [Fact]
    public void Every_file_is_between_one_byte_and_the_cap_and_the_count_is_capped()
    {
        Check(Archive(size: 0)).ShouldNotBeNull();
        Check(Archive(size: Cap + 1)).ShouldNotBeNull();
        Check(Archive(size: Cap)).ShouldBeNull();

        var many = Enumerable.Range(0, 5)
            .Select(i => new SurveyUploadFileClaim($"export-{i}.svg", 10, SurveyUploadFileRole.Other))
            .Prepend(Archive())
            .ToArray();
        Check(many).ShouldNotBeNull();
        Check(many[..5]).ShouldBeNull();
    }

    [Fact]
    public void Extensions_are_read_lower_case_and_a_bare_or_leading_dot_has_none()
    {
        SurveyUploadFiles.ExtensionOf("Inventata.TH2").ShouldBe(".th2");
        SurveyUploadFiles.ExtensionOf(".th").ShouldBe("");
        SurveyUploadFiles.ExtensionOf("inventata").ShouldBe("");
        SurveyUploadFiles.ExtensionOf("inventata.").ShouldBe("");
    }

    [Fact]
    public void The_status_page_is_under_its_cave_and_survey()
    {
        var cave = Guid.CreateVersion7();
        var survey = Guid.CreateVersion7();
        var upload = Guid.CreateVersion7();
        SurveyUploadLinks.StatusPage(cave, survey, upload).ShouldBe($"/caves/{cave}/surveys/{survey}/uploads/{upload}");
    }
}
