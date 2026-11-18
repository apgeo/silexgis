// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Api.Features.Import;
using SilexGis.Domain.Import;

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

    private static Dictionary<string, ImportDecision> VectorDecisions(int n) =>
        Enumerable.Range(1, n).ToDictionary(i => i.ToString(), _ => new ImportDecision { Action = ImportDecisionAction.Skip });

    private static List<string> Errors(FluentValidation.Results.ValidationResult result) =>
        [.. result.Errors.Select(e => e.PropertyName)];
}
