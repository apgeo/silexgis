// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Api.Features.Import;
using SilexGis.Domain.Import;
using SilexGis.Domain.Import.TripCsv;

namespace SilexGis.Api.Tests;

/// <summary>
/// A review of a vector file or of a trip sheet is stored whole and read back whole on every save,
/// so the decisions it carries and the rows a confirmation chooses are bounded, and each decision's
/// action has to be one of the defined ones: an enum binds any number of its underlying type, and an
/// undefined action would be read one way by the preview and another by the confirmation.
/// </summary>
public sealed class ImportReviewBoundsTests
{
    private const int Cap = 200_000;

    [Fact]
    public void A_vector_review_of_the_most_decisions_is_taken_and_one_more_is_refused()
    {
        var validator = new ImportSessionWriteRequestValidator();

        Errors(validator.Validate(new ImportSessionWriteRequest(new ImportOptions(), VectorDecisions(Cap))))
            .ShouldNotContain(p => p.StartsWith("Decisions"));
        Errors(validator.Validate(new ImportSessionWriteRequest(new ImportOptions(), VectorDecisions(Cap + 1))))
            .ShouldContain("Decisions");
    }

    [Fact]
    public void A_vector_confirmation_choosing_more_than_the_most_candidates_is_refused()
    {
        var validator = new ImportCommitRequestValidator();
        static ImportCommitRequest Choosing(int n) =>
            new(new ImportOptions(), [.. Enumerable.Range(1, n).Select(i => (long)i)], VectorDecisions(0), false);

        Errors(validator.Validate(Choosing(Cap))).ShouldNotContain("Selection");
        Errors(validator.Validate(Choosing(Cap + 1))).ShouldContain("Selection");
    }

    [Fact]
    public void A_vector_decision_with_an_undefined_action_or_kind_is_refused()
    {
        var validator = new ImportSessionWriteRequestValidator();
        var undefined = new Dictionary<string, ImportDecision>
        {
            ["1"] = new() { Action = (ImportDecisionAction)99 },
            ["2"] = new() { Kind = (ImportTargetKind)99 },
            ["3"] = new() { Name = new string('n', 256) },
        };

        var errors = Errors(validator.Validate(new ImportSessionWriteRequest(new ImportOptions(), undefined)));
        errors.ShouldContain(p => p.EndsWith("Action"));
        errors.ShouldContain(p => p.EndsWith("Kind"));
        errors.ShouldContain(p => p.EndsWith("Name"));
    }

    [Fact]
    public void A_trip_review_of_the_most_decisions_is_taken_and_one_more_or_an_undefined_action_is_refused()
    {
        var validator = new TripImportSessionWriteRequestValidator();
        static TripImportSessionWriteRequest Of(int n) => new(
            new TripImportOptions(),
            Enumerable.Range(1, n).ToDictionary(i => i.ToString(), _ => new TripImportDecision { Action = TripImportRowAction.Skip }));

        Errors(validator.Validate(Of(Cap))).ShouldNotContain(p => p.StartsWith("Decisions"));
        Errors(validator.Validate(Of(Cap + 1))).ShouldContain("Decisions");

        var undefined = new TripImportSessionWriteRequest(
            new TripImportOptions(),
            new Dictionary<string, TripImportDecision> { ["4"] = new() { Action = (TripImportRowAction)7 } });
        Errors(validator.Validate(undefined)).ShouldContain(p => p.EndsWith("Action"));
    }

    [Fact]
    public void A_trip_confirmation_choosing_more_than_the_most_rows_is_refused()
    {
        var validator = new TripImportCommitRequestValidator();
        static TripImportCommitRequest Choosing(int n) => new(new TripImportOptions(), [.. Enumerable.Range(1, n)], null);

        Errors(validator.Validate(Choosing(Cap))).ShouldNotContain("Lines");
        Errors(validator.Validate(Choosing(Cap + 1))).ShouldContain("Lines");
    }

    [Fact]
    public void A_photograph_review_is_bounded_in_decisions_and_in_places_chosen()
    {
        const int pictures = PhotoImportOptions.MaxFiles;
        const int decisions = 10 * pictures;
        static Dictionary<string, PhotoDecision> Decided(int n) =>
            Enumerable.Range(0, n).ToDictionary(_ => Guid.NewGuid().ToString(), _ => new PhotoDecision());
        static List<Guid> Chosen(int n) => [.. Enumerable.Range(0, n).Select(_ => Guid.NewGuid())];

        var session = new PhotoSessionWriteRequestValidator();
        Errors(session.Validate(new PhotoSessionWriteRequest([], new PhotoImportOptions(), Decided(decisions))))
            .ShouldNotContain(p => p.StartsWith("Decisions"));
        Errors(session.Validate(new PhotoSessionWriteRequest([], new PhotoImportOptions(), Decided(decisions + 1))))
            .ShouldContain("Decisions");

        var preview = new PhotoPreviewRequestValidator();
        var previewErrors = Errors(preview.Validate(new PhotoPreviewRequest(
            new PhotoImportOptions(), [], Decided(decisions + 1), null, null, null, new string('s', 201))));
        previewErrors.ShouldContain("Decisions");
        previewErrors.ShouldContain("Search");

        var commit = new PhotoCommitRequestValidator();
        Errors(commit.Validate(new PhotoCommitRequest(new PhotoImportOptions(), [], Chosen(pictures), Decided(0))))
            .ShouldNotContain("Selection");
        Errors(commit.Validate(new PhotoCommitRequest(new PhotoImportOptions(), [], Chosen(pictures + 1), Decided(0))))
            .ShouldContain("Selection");

        var undefined = new Dictionary<string, PhotoDecision>
        {
            [Guid.NewGuid().ToString()] = new() { Action = (ImportDecisionAction)99 },
            [new string('k', 41)] = new() { Name = new string('n', 256) },
        };
        var errors = Errors(session.Validate(new PhotoSessionWriteRequest([], new PhotoImportOptions(), undefined)));
        errors.ShouldContain(p => p.EndsWith("Action"));
        errors.ShouldContain(p => p.EndsWith("Key"));
        errors.ShouldContain(p => p.EndsWith("Name"));
    }

    [Fact]
    public void A_trip_sheet_s_choices_are_bounded_by_the_sheet()
    {
        var validator = new TripImportOptionsValidator();
        static Dictionary<string, Guid> Chosen(int n, int keyLength = 8) =>
            Enumerable.Range(0, n).ToDictionary(i => i.ToString().PadLeft(keyLength, 'k'), _ => Guid.NewGuid());

        Errors(validator.Validate(new TripImportOptions { CaverChoices = Chosen(Cap) }))
            .ShouldNotContain("CaverChoices");
        Errors(validator.Validate(new TripImportOptions { CaverChoices = Chosen(Cap + 1) }))
            .ShouldContain("CaverChoices");
        Errors(validator.Validate(new TripImportOptions { FeatureChoices = Chosen(1, 501) }))
            .ShouldContain("FeatureChoices");
        Errors(validator.Validate(new TripImportOptions { FeatureChoices = Chosen(1, 500) }))
            .ShouldNotContain("FeatureChoices");
        Errors(validator.Validate(new TripImportOptions { SlashSeparatedFields = [(TripCsvField)999] }))
            .ShouldContain("SlashSeparatedFields");
        Errors(validator.Validate(new TripImportOptions
        {
            Columns = new Dictionary<TripCsvField, string> { [(TripCsvField)999] = "x" },
        })).ShouldContain("Columns");
        Errors(validator.Validate(new TripImportOptions())).ShouldBeEmpty();
    }

    [Fact]
    public void A_vector_import_s_tags_and_languages_and_a_trip_sheet_s_search_are_bounded()
    {
        var options = new ImportOptionsValidator();
        static List<long> Tags(int n) => [.. Enumerable.Range(1, n).Select(i => (long)i)];
        static List<string> Languages(int n, int length = 2) =>
            [.. Enumerable.Range(0, n).Select(i => i.ToString().PadLeft(length, 'l'))];

        Errors(options.Validate(new ImportOptions { TagIds = Tags(ImportOptionsValidator.MaxTags) }))
            .ShouldNotContain("TagIds");
        Errors(options.Validate(new ImportOptions { TagIds = Tags(ImportOptionsValidator.MaxTags + 1) }))
            .ShouldContain("TagIds");
        Errors(options.Validate(new ImportOptions { Languages = Languages(ImportOptionsValidator.MaxLanguages) }))
            .ShouldNotContain(p => p.StartsWith("Languages"));
        Errors(options.Validate(new ImportOptions { Languages = Languages(ImportOptionsValidator.MaxLanguages + 1) }))
            .ShouldContain("Languages");
        Errors(options.Validate(new ImportOptions { Languages = Languages(1, ImportOptionsValidator.MaxLanguageTagLength) }))
            .ShouldNotContain(p => p.StartsWith("Languages"));
        Errors(options.Validate(new ImportOptions { Languages = Languages(1, ImportOptionsValidator.MaxLanguageTagLength + 1) }))
            .ShouldContain("Languages[0]");
        Errors(options.Validate(new ImportOptions { Languages = [" "] })).ShouldContain("Languages[0]");

        var preview = new TripImportPreviewRequestValidator();
        static TripImportPreviewRequest Searching(int length) =>
            new(new TripImportOptions(), null, null, new string('s', length));
        Errors(preview.Validate(Searching(TripImportPreviewRequestValidator.MaxSearchLength))).ShouldNotContain("Search");
        Errors(preview.Validate(Searching(TripImportPreviewRequestValidator.MaxSearchLength + 1))).ShouldContain("Search");
    }

    private static Dictionary<string, ImportDecision> VectorDecisions(int n) =>
        Enumerable.Range(1, n).ToDictionary(i => i.ToString(), _ => new ImportDecision { Action = ImportDecisionAction.Skip });

    private static List<string> Errors(FluentValidation.Results.ValidationResult result) =>
        [.. result.Errors.Select(e => e.PropertyName)];
}
