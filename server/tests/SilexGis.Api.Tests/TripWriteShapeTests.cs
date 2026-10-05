// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Reflection;
using Shouldly;
using SilexGis.Api.Features.TripLogs;

namespace SilexGis.Api.Tests;

/// <summary>
/// A write on a trip replaces every column it carries, and more than one client surface saves a
/// trip whole — the form, and the sections card that echoes the rest of the trip back around the
/// one section it edits. A column added to the write and produced by only one of them is dropped
/// by the other on its next save, silently. What keeps that from happening is the compiler: every
/// member of the write request is required on the wire, so the generated client type requires it,
/// so a surface that builds the body as a typed literal fails to build until it produces the new
/// member. This pins the first link of that chain; the client pins the rest.
/// </summary>
/// <remarks>
/// The alternative — reading an absent member as "leave the stored value alone" — is the contract
/// for exactly the members that say so (the audience pair, the cave list, the three sections), and
/// is not available for the rest: an absent nullable member and a null one are the same bytes to
/// the serializer, and a presence-tracking wrapper on every member would reshape the whole
/// contract and the generated client with it, to solve a problem the compiler already catches as
/// long as no member is optional. So no member may carry a default, because a defaulted parameter
/// is published as optional and the chain breaks at its first link.
/// </remarks>
public sealed class TripWriteShapeTests
{
    [Fact]
    public void Every_member_of_the_trip_write_is_required_on_the_wire()
    {
        var constructor = typeof(TripLogWriteRequest).GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderByDescending(c => c.GetParameters().Length)
            .First();

        var optional = constructor.GetParameters()
            .Where(p => p.HasDefaultValue || p.IsOptional)
            .Select(p => p.Name)
            .ToList();

        optional.ShouldBeEmpty(
            "a defaulted member is published as optional, and the surfaces that echo a trip whole would no longer be made to produce it");
    }
}
