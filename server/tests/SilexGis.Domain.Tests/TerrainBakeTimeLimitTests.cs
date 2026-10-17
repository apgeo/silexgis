// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Terrain;

namespace SilexGis.Domain.Tests;

/// <summary>
/// A bake stopped at the time limit says how long it ran, the limit and the setting, and the screen
/// reads those numbers back out of what was stored.
/// </summary>
public class TerrainBakeTimeLimitTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_sentence_names_the_time_the_limit_and_the_setting_and_reads_back(bool confirmed)
    {
        var stop = new TerrainBakeTimeLimit(43_217, 43_200);
        var sentence = stop.Sentence(confirmed);

        sentence.ShouldStartWith("Time limit reached");
        sentence.ShouldContain("12 h");
        sentence.ShouldContain("SILEXGIS__Terrain__BakeTimeoutSeconds=43200");

        // A build's message holds five hundred characters; past that the numbers would be cut off
        // before they could be read back.
        sentence.Length.ShouldBeLessThan(500);

        TerrainBakeTimeLimit.TryRead(sentence).ShouldBe(stop);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("The bake was withdrawn because the build was called off.")]
    [InlineData("Time limit reached: something else entirely.")]
    public void Any_other_message_carries_no_numbers(string? message) =>
        TerrainBakeTimeLimit.TryRead(message).ShouldBeNull();

    [Theory]
    [InlineData(0, 60)]
    [InlineData(59, 60)]
    [InlineData(43_200, 43_200)]
    [InlineData(int.MaxValue, 604_800)]
    public void The_setting_is_brought_inside_a_minute_to_seven_days(int configured, int seconds) =>
        TerrainBakeTimeLimit.Limit(configured).ShouldBe(TimeSpan.FromSeconds(seconds));
}
