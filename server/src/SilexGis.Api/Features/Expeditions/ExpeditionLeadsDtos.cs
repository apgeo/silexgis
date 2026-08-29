// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Api.Features.Expeditions;

/// <summary>
/// One open way on that a camp's trips turned up, as the board shows it.
/// </summary>
/// <remarks>
/// <para>
/// Named properties rather than positional, for the reason the camp's own reading gives: a member
/// inserted between two of the same type would be absorbed by the neighbour it displaces, and
/// nothing would fail until somebody read one field as another.
/// </para>
/// <para>
/// No coordinate travels here, and that is not an omission to be corrected later. A lead is a
/// place somebody has not been through yet; a list of them ranked by how promising they look is
/// the same disclosure as any single position and rather more inviting. The rows a caller may not
/// place exactly are left out of the board entirely, so what is on it needs no second rule.
/// </para>
/// </remarks>
public sealed record ExpeditionLeadDto
{
    public required Guid Id { get; init; }

    public string? Name { get; init; }

    /// <summary>
    /// How promising it looked, on the register's own scale, or absent because nobody graded it.
    /// The scale is the one the place itself carries — the board neither invents nor translates it.
    /// </summary>
    public string? Grade { get; init; }

    /// <summary>What is left to do there, as whoever found it wrote it down.</summary>
    public string? Note { get; init; }
}

/// <summary>
/// The leads that share one state, in the order a season would be planned from them.
/// </summary>
/// <remarks>
/// <see cref="State"/> is the value stored on the place itself, and absent when nobody recorded
/// one — which is legal and common, since a lead is written down the evening it is found and
/// answered years later. It is served as it is stored rather than mapped onto anything: the
/// vocabulary has exactly one home, on the kind of place this is, and a second copy here would be
/// the thing that later disagrees with it.
/// </remarks>
public sealed record ExpeditionLeadGroupDto
{
    public string? State { get; init; }

    public required IReadOnlyList<ExpeditionLeadDto> Leads { get; init; }
}

/// <summary>
/// Everything a camp's trips left open, gathered by state.
/// </summary>
/// <remarks>
/// <para>
/// A view over what the trips already name and never a list of its own: a lead is on this board
/// because a member trip named a place of that kind, so recording it once records it everywhere.
/// A place two of the camp's trips both named is one lead, not two.
/// </para>
/// <para>
/// Answered as this caller may see it, like every other reading of a camp: the trips are the
/// member trips they may read, and a lead they may not place exactly is not on the board at all.
/// Two people opening the same camp therefore see different boards and both are right — the page
/// says so in words rather than leaving the numbers to imply otherwise.
/// </para>
/// <para>
/// Unpaged and unfiltered, deliberately. The board is read whole, and a field narrowing it by a
/// stored property would be the first of a general mechanism that belongs with the filtering work
/// rather than here.
/// </para>
/// </remarks>
public sealed record ExpeditionLeadsDto
{
    public required Guid ExpeditionId { get; init; }

    public required IReadOnlyList<ExpeditionLeadGroupDto> Groups { get; init; }

    /// <summary>How many leads the groups hold between them.</summary>
    public required int Leads { get; init; }

    /// <summary>
    /// Whether the camp has more leads this caller may read than the board carries. It says so
    /// rather than stopping quietly, because a board silently short of its last rows is read as a
    /// camp that has run out of places to go.
    /// </summary>
    public required bool Truncated { get; init; }
}
