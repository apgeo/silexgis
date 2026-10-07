// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using SilexGis.Api.Features.TripTracking;

namespace SilexGis.Api.Tests;

/// <summary>
/// The two list sizes an operator sets for the published trip surface, and the bounds they are
/// held inside whatever is set; and the periods beside them, which refuse the start instead.
/// </summary>
/// <remarks>
/// <para>
/// Both lists are read anonymously, by anybody holding a link somebody put in an article, and the
/// bound on each is what keeps such a link from becoming the cheapest way to read a club's whole
/// register in one request, or to have an unbounded number of parties folded for one. The bound is
/// promised in the settings' own remarks, in the sample environment file and in the route
/// summaries — and it was asserted nowhere: the integration tests only ever narrow a list, so a
/// clamp rewritten as a floor would have passed every one of them.
/// </para>
/// <para>
/// No database, because the bound is a property of the setting and not of any query; these are the
/// ceiling's own tests, with the floor and a value inside the range asserted beside it so that the
/// ceiling cannot pass by the property answering a constant.
/// </para>
/// </remarks>
public class TripTrackingOptionsTests
{
    [Fact]
    public void The_archive_list_size_is_held_to_its_bound_however_high_it_is_set()
    {
        // The number itself is the promise made to operators, so it is asserted as a number and
        // not only through the constant.
        TripPastTrackOptions.MaxListSize.ShouldBe(200);

        new TripPastTrackOptions { ListSize = 10_000 }.EffectiveListSize.ShouldBe(200);
        new TripPastTrackOptions { ListSize = 201 }.EffectiveListSize.ShouldBe(200);
        new TripPastTrackOptions { ListSize = 200 }.EffectiveListSize.ShouldBe(200);
    }

    [Fact]
    public void The_archive_list_size_never_falls_to_nothing_and_is_served_as_asked_in_between()
    {
        // Zero would be a way to turn the feature half off, which is what the enabled switch is
        // for; a negative number is a typo, and a typo serves a sane list.
        new TripPastTrackOptions { ListSize = 0 }.EffectiveListSize.ShouldBe(1);
        new TripPastTrackOptions { ListSize = -3 }.EffectiveListSize.ShouldBe(1);
        new TripPastTrackOptions { ListSize = 1 }.EffectiveListSize.ShouldBe(1);

        new TripPastTrackOptions { ListSize = 25 }.EffectiveListSize.ShouldBe(25);
        new TripPastTrackOptions().EffectiveListSize.ShouldBe(50);
    }

    [Fact]
    public void The_followed_list_size_is_held_to_its_lower_bound_however_high_it_is_set()
    {
        // Lower than the archive's, because a row here is a whole party folded from its own log
        // rather than a title and a headcount.
        TripTrackingOptions.MaxFollowedListSize.ShouldBe(50);
        TripTrackingOptions.MaxFollowedListSize.ShouldBeLessThan(TripPastTrackOptions.MaxListSize);

        new TripTrackingOptions { FollowedListSize = 10_000 }.EffectiveFollowedListSize.ShouldBe(50);
        new TripTrackingOptions { FollowedListSize = 51 }.EffectiveFollowedListSize.ShouldBe(50);
        new TripTrackingOptions { FollowedListSize = 50 }.EffectiveFollowedListSize.ShouldBe(50);
    }

    [Fact]
    public void The_followed_list_size_never_falls_to_nothing_and_is_served_as_asked_in_between()
    {
        new TripTrackingOptions { FollowedListSize = 0 }.EffectiveFollowedListSize.ShouldBe(1);
        new TripTrackingOptions { FollowedListSize = -3 }.EffectiveFollowedListSize.ShouldBe(1);
        new TripTrackingOptions { FollowedListSize = 1 }.EffectiveFollowedListSize.ShouldBe(1);

        new TripTrackingOptions { FollowedListSize = 7 }.EffectiveFollowedListSize.ShouldBe(7);
        new TripTrackingOptions().EffectiveFollowedListSize.ShouldBe(20);
    }

    // The periods, unlike the sizes above, refuse the start. A size that is wrong still serves a
    // list; a period that is wrong stops pages answering, on routes that answer every refusal
    // alike, so it would be found by a reader of an empty page rather than by whoever typed it.

    [Fact]
    public void The_shipped_settings_start()
    {
        // If a default ever stopped passing its own check, every installation that never set the
        // value would fail to start.
        new TripTrackingOptionsValidator().Validate(null, new TripTrackingOptions())
            .Succeeded.ShouldBeTrue();
        new TripPastTrackOptionsValidator().Validate(null, new TripPastTrackOptions())
            .Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("-00:00:01")]
    [InlineData("-14.00:00:00")]
    public void A_link_lifetime_of_nothing_or_less_refuses_to_start(string lifetime)
    {
        var result = new TripTrackingOptionsValidator().Validate(
            null, new TripTrackingOptions { ShareLifetime = TimeSpan.Parse(lifetime) });

        result.Failed.ShouldBeTrue();
        // Named the way it is typed, because the reader is looking at a container that will not
        // come up, and told what to write instead of what was probably meant.
        result.FailureMessage.ShouldContain("SILEXGIS__TripTracking__ShareLifetime");
        result.FailureMessage.ShouldContain("3650.00:00:00");
        result.FailureMessage.ShouldContain("00:00:01");
        result.FailureMessage.ShouldContain("revoking");
        result.FailureMessage.ShouldNotContain("ShareGraceAfterClose");
    }

    [Theory]
    [InlineData("00:00:01")]
    [InlineData("00:06:00")]
    [InlineData("3650.00:00:00")]
    public void Any_link_lifetime_longer_than_nothing_starts(string lifetime)
    {
        new TripTrackingOptionsValidator().Validate(
                null, new TripTrackingOptions { ShareLifetime = TimeSpan.Parse(lifetime) })
            .Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void A_closing_grace_of_nothing_starts_and_a_negative_one_does_not()
    {
        // Zero is a real choice — the page stops answering the moment the watch is closed — and
        // it is what a rehearsal of the archive is run with, so it must not be refused along with
        // the values that mean nothing.
        new TripTrackingOptionsValidator().Validate(
                null, new TripTrackingOptions { ShareGraceAfterClose = TimeSpan.Zero })
            .Succeeded.ShouldBeTrue();

        var result = new TripTrackingOptionsValidator().Validate(
            null, new TripTrackingOptions { ShareGraceAfterClose = TimeSpan.FromSeconds(-1) });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("SILEXGIS__TripTracking__ShareGraceAfterClose");
        result.FailureMessage.ShouldContain("00:00:00 is allowed");
        result.FailureMessage.ShouldNotContain("ShareLifetime");
    }

    [Fact]
    public void Two_wrong_periods_are_both_named_in_one_refusal()
    {
        // One start per mistake is a slow way to fix two of them.
        var result = new TripTrackingOptionsValidator().Validate(
            null,
            new TripTrackingOptions
            {
                ShareLifetime = TimeSpan.Zero,
                ShareGraceAfterClose = TimeSpan.FromDays(-2),
            });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("SILEXGIS__TripTracking__ShareLifetime");
        result.FailureMessage.ShouldContain("SILEXGIS__TripTracking__ShareGraceAfterClose");
    }

    [Fact]
    public void The_list_size_never_refuses_a_start_whatever_it_is_set_to()
    {
        // Held between its bounds where it is read, as asserted above; the check at start must
        // not quietly become a second, stricter rule about the same number.
        foreach (var size in new[] { int.MinValue, -3, 0, 1, 10_000 })
        {
            new TripTrackingOptionsValidator().Validate(null, new TripTrackingOptions { FollowedListSize = size })
                .Succeeded.ShouldBeTrue();
            new TripPastTrackOptionsValidator().Validate(null, new TripPastTrackOptions { ListSize = size })
                .Succeeded.ShouldBeTrue();
        }
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("-30.00:00:00")]
    public void A_retention_of_nothing_or_less_refuses_to_start_and_says_how_to_say_what_was_meant(string retention)
    {
        var result = new TripPastTrackOptionsValidator().Validate(
            null, new TripPastTrackOptions { Retention = TimeSpan.Parse(retention) });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("SILEXGIS__TripPastTracks__Retention");
        // The two things somebody writing zero can have meant, each with its own way of being said.
        result.FailureMessage.ShouldContain("leave it unset");
        result.FailureMessage.ShouldContain("SILEXGIS__TripPastTracks__Enabled=false");
    }

    [Fact]
    public void No_retention_and_any_retention_longer_than_nothing_start()
    {
        new TripPastTrackOptionsValidator().Validate(null, new TripPastTrackOptions { Retention = null })
            .Succeeded.ShouldBeTrue();
        new TripPastTrackOptionsValidator().Validate(
                null, new TripPastTrackOptions { Retention = TimeSpan.FromSeconds(1) })
            .Succeeded.ShouldBeTrue();
        new TripPastTrackOptionsValidator().Validate(
                null, new TripPastTrackOptions { Retention = TimeSpan.FromDays(3650) })
            .Succeeded.ShouldBeTrue();
        // Switched off, the retention is not read at all, but a wrong one is still a wrong one.
        new TripPastTrackOptionsValidator().Validate(
                null, new TripPastTrackOptions { Enabled = false, Retention = TimeSpan.FromDays(30) })
            .Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void The_quiet_threshold_is_three_hours_and_zero_or_less_switches_the_mark_off()
    {
        new TripTrackingOptions().QuietAfter.ShouldBe(TimeSpan.FromHours(3));
        new TripTrackingOptions().EffectiveQuietAfter.ShouldBe(TimeSpan.FromHours(3));

        new TripTrackingOptions { QuietAfter = TimeSpan.FromMinutes(45) }.EffectiveQuietAfter
            .ShouldBe(TimeSpan.FromMinutes(45));
        new TripTrackingOptions { QuietAfter = TimeSpan.Zero }.EffectiveQuietAfter.ShouldBe(TimeSpan.Zero);
        // A negative duration is a typo; read as "off", because the other reading marks everybody.
        new TripTrackingOptions { QuietAfter = TimeSpan.FromHours(-3) }.EffectiveQuietAfter.ShouldBe(TimeSpan.Zero);
    }

    /// <summary>
    /// What an operator's spelling of the quiet threshold turns into, as the install guide and the
    /// sample environment file describe it.
    /// </summary>
    /// <remarks>
    /// Three sentences of documentation rest on this and nothing else asserted them: the form is
    /// <c>[d.]hh:mm:ss</c>, a bare number is a number of days, and a value that is no duration is
    /// refused when the options are read — every time they are read, since a failed read is not
    /// kept. The last is the one an operator needs told, because the options are read by the
    /// published-trip routes too. If the binding is ever made forgiving, this is the test that
    /// says the documentation has to change with it.
    /// </remarks>
    [Fact]
    public void The_quiet_threshold_is_read_from_configuration_as_the_documentation_says()
    {
        Read("03:00:00").Value.QuietAfter.ShouldBe(TimeSpan.FromHours(3));
        Read("1.12:00:00").Value.QuietAfter.ShouldBe(TimeSpan.FromHours(36));
        Read("3").Value.QuietAfter.ShouldBe(TimeSpan.FromDays(3));

        var unreadable = Read("3h");
        Should.Throw<InvalidOperationException>(() => unreadable.Value);
        Should.Throw<InvalidOperationException>(() => unreadable.Value);
    }

    private static IOptions<TripTrackingOptions> Read(string quietAfter)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{TripTrackingOptions.SectionName}:QuietAfter"] = quietAfter,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddOptions<TripTrackingOptions>().BindConfiguration(TripTrackingOptions.SectionName);
        return services.BuildServiceProvider().GetRequiredService<IOptions<TripTrackingOptions>>();
    }
}
