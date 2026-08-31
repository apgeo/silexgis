// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Api.Features.QrLanding;

/// <summary>
/// Whether a cave's printed codes resolve for a visitor who is not signed in, and the record
/// of the decision that last governed it.
/// </summary>
/// <remarks>
/// Not published is a state, not a missing record, so it is answered rather than refused: every
/// cave that has ever existed starts here and most stay here, and a caller allowed to ask the
/// question is entitled to the answer "no". When a decision has been taken the fields describe
/// the most recent one, live or withdrawn — the manager showing this can therefore say when the
/// codes stopped resolving, which is the fact somebody asks for after a label turns up
/// somewhere it should not have.
/// </remarks>
/// <param name="Published">
/// True only while a decision stands. This is the server's own reading of its rows and not a
/// derivation left to the caller: whether a code resolves is one rule with one home.
/// </param>
/// <param name="PublishedAt">When the most recent decision was taken; null if there never was one.</param>
/// <param name="RevokedAt">When it was withdrawn; null while it stands, or if there never was one.</param>
public sealed record CaveQrPublicationDto(
    bool Published,
    Guid? PublicationId,
    Guid? PublishedBy,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? RevokedAt);

/// <summary>
/// Everything a visitor who is not signed in is told when a printed code resolves.
/// </summary>
/// <remarks>
/// <para>
/// A type of its own with one field, and the fields it does not have are the whole point of it.
/// There is no position here and no name here — not blanked, not obfuscated, not filtered:
/// absent. A filter is a thing somebody later improves, and the rule that a printed code never
/// discloses where a cave is or what it is called is too important to be carried by one. It is
/// carried by there being no field to put either in.
/// </para>
/// <para>
/// The trap this shape exists to close: the batch protection helper answers "may this caller see
/// exact coordinates", and for an unprotected feature it answers yes to everyone, signed in or
/// not, because that is the correct answer to the question it was asked. A version of this route
/// that emitted a position "run through protection" would therefore publish full-precision
/// coordinates for every cave nobody had flipped protection on — which is most of them — while
/// passing any test that only exercised a protected one.
/// </para>
/// <para>
/// The address is a code printed on a label, short enough to transcribe and reproducible outside
/// this server from the data it names, so the whole space of it is enumerable and no rate limit
/// changes that. What makes the enumeration pointless is that there is nothing here worth
/// enumerating for: a sweep of every possible code learns how many codes an installation has
/// published and nothing whatever that ties any one of them to a cave, a place, a position or a
/// person. That is a property of this record's field list, and it lasts exactly as long as the
/// field list does.
/// </para>
/// </remarks>
/// <param name="InstanceName">
/// What this installation calls itself — the same name it already tells anyone who asks it
/// what it is, and the only thing a resolving code says.
/// </param>
public sealed record PublicQrDto(string InstanceName);
