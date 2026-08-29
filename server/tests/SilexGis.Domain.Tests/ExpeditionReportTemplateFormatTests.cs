// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Trips;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The same little language, writing up a camp instead of a trip.
/// </summary>
/// <remarks>
/// One grammar so a club edits one kind of file, two vocabularies because a fortnight has a
/// day-by-day shape and a set of teams and an afternoon underground has neither. What these tests
/// hold down is the boundary between the two: a name that means something under one kind and
/// nothing under the other must be refused where the person editing the file can still see it,
/// rather than printing an empty line six weeks later.
/// </remarks>
public class ExpeditionReportTemplateFormatTests
{
    [Fact]
    public void The_camp_layout_the_system_hands_out_reads_back_as_the_document_it_describes()
    {
        var read = ReportTemplateFormat.Parse(
            ReportTemplateFormat.ExpeditionDefault, ReportTemplateKind.Expedition);

        read.Ok.ShouldBeTrue(string.Join(" ", read.Errors));
        read.Parts.ShouldContain(p => p.Directive == ReportTemplateDirective.Title);
        read.Parts.ShouldContain(p => p.Directive == ReportTemplateDirective.Days);
        read.Parts.ShouldContain(p => p.Directive == ReportTemplateDirective.Trips);
        read.Parts.ShouldContain(p => p.Directive == ReportTemplateDirective.Teams);
        read.Parts.ShouldContain(p => p.Directive == ReportTemplateDirective.Roster);
        read.Parts.ShouldContain(p => p.Directive == ReportTemplateDirective.Photographs);
    }

    /// <summary>
    /// The day-by-day and team lines belong to a camp alone, and a trip's form belongs to a trip
    /// alone — each refused under the other kind, with the line named.
    /// </summary>
    [Fact]
    public void A_word_that_belongs_to_one_kind_is_refused_under_the_other()
    {
        var campWordsOnATrip = ReportTemplateFormat.Parse(
            "title: {title}\ndays\nteams\ntrips", ReportTemplateKind.Trip);
        campWordsOnATrip.Ok.ShouldBeFalse();
        campWordsOnATrip.Errors.Count.ShouldBe(3);
        campWordsOnATrip.Errors.ShouldContain(e => e.Contains("Line 2", StringComparison.Ordinal));

        // And the same words are ordinary under a camp — the positive case, in the same test, so
        // the refusal above cannot be passing because the whole parse is broken.
        var campWordsOnACamp = ReportTemplateFormat.Parse(
            "title: {title}\ndays\nteams\ntrips", ReportTemplateKind.Expedition);
        campWordsOnACamp.Ok.ShouldBeTrue(string.Join(" ", campWordsOnACamp.Errors));

        var formOnACamp = ReportTemplateFormat.Parse(
            "title: {title}\nsection: safety", ReportTemplateKind.Expedition);
        formOnACamp.Ok.ShouldBeFalse();
        formOnACamp.Errors.ShouldContain(e => e.Contains("Line 2", StringComparison.Ordinal));

        var formOnATrip = ReportTemplateFormat.Parse(
            "title: {title}\nsection: safety", ReportTemplateKind.Trip);
        formOnATrip.Ok.ShouldBeTrue(string.Join(" ", formOnATrip.Errors));
    }

    /// <summary>
    /// A name out of the other kind's vocabulary is refused rather than quietly coming back empty,
    /// because a layout that prints nothing where the club expected a figure is a fault nobody is
    /// told about.
    /// </summary>
    [Fact]
    public void A_name_from_the_other_kinds_vocabulary_is_refused_and_the_camps_own_names_are_not()
    {
        var tripName = ReportTemplateFormat.Parse(
            "field: Where = {sketch}", ReportTemplateKind.Expedition);
        tripName.Ok.ShouldBeFalse();
        tripName.Errors.ShouldContain(e => e.Contains("a camp", StringComparison.Ordinal));
        tripName.Errors.ShouldContain(e => e.Contains("sketch", StringComparison.Ordinal));

        // An answer out of a trip's form is a reach a camp does not have either: a camp fills in
        // no form, so naming one of its answers is a mistake and not an empty line.
        ReportTemplateFormat
            .Parse("field: Note = {safety.rope}", ReportTemplateKind.Expedition)
            .Ok.ShouldBeFalse();

        var campNames = ReportTemplateFormat.Parse(
            "field: Where = {area}\nfield: How long = {days}\nfield: Trips = {trips}",
            ReportTemplateKind.Expedition);
        campNames.Ok.ShouldBeTrue(string.Join(" ", campNames.Errors));
    }

    /// <summary>
    /// The two vocabularies overlap only where the word means the same thing. Where a camp shares
    /// a name with a trip it is because a camp answers it too — never because the list was copied.
    /// </summary>
    [Fact]
    public void The_camps_vocabulary_is_its_own_and_shares_only_what_a_camp_can_answer()
    {
        ReportTemplateFormat.PlaceholdersFor(ReportTemplateKind.Expedition)
            .ShouldBe(ReportTemplateFormat.ExpeditionPlaceholders);
        ReportTemplateFormat.PlaceholdersFor(ReportTemplateKind.Trip)
            .ShouldBe(ReportTemplateFormat.Placeholders);

        ReportTemplateFormat.ExpeditionPlaceholders.ShouldContain("days");
        ReportTemplateFormat.ExpeditionPlaceholders.ShouldContain("trips");
        ReportTemplateFormat.ExpeditionPlaceholders.ShouldContain("area");
        ReportTemplateFormat.ExpeditionPlaceholders.ShouldNotContain("sketch");
        ReportTemplateFormat.ExpeditionPlaceholders.ShouldNotContain("incident");
        ReportTemplateFormat.ExpeditionPlaceholders.ShouldNotContain("weather");
    }

    /// <summary>
    /// Nothing about the language itself changed: a stand-alone word still takes nothing after it,
    /// and every fault still names its line.
    /// </summary>
    [Fact]
    public void A_camps_stand_alone_words_take_nothing_after_them()
    {
        var read = ReportTemplateFormat.Parse(
            "title: {title}\ndays: every one of them", ReportTemplateKind.Expedition);

        read.Ok.ShouldBeFalse();
        read.Errors.ShouldContain(e => e.Contains("Line 2", StringComparison.Ordinal));
        read.Errors.ShouldContain(e => e.Contains("stands on its own", StringComparison.Ordinal));
    }
}
