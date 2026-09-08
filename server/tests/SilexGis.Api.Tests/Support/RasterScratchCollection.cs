// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Api.Tests.Support;

/// <summary>
/// Tests that move the raster library's scratch directory, run one at a time.
/// </summary>
/// <remarks>
/// <para>
/// The setting that says where the library may do its working writes is one value for the whole
/// process — the library has no notion of one caller's temporary directory — so anything that
/// redirects it redirects it for every other piece of work running at that moment. That is
/// deliberate and harmless in the application, where the redirection points somewhere writable and
/// every writer invents its own file name inside it. It is not harmless between tests: one of these
/// proves the redirection by pointing the setting at a directory that cannot be written to at all,
/// and another running beside it then fails for a reason that has nothing to do with what it is
/// checking. Naming a collection is what makes them take turns.
/// </para>
/// <para>
/// Membership is required of anything that <em>writes</em> a cloud-optimised raster, not only of
/// anything that moves the setting: the writer works through a scratch file for the whole length of
/// the write, so it is the side that gets hurt. That includes work driven through the database, so
/// this collection carries the container as well — a class that needs both a database and the
/// raster library belongs here rather than beside the other database tests, where it would run
/// beside the very tests that make the setting unusable.
/// </para>
/// </remarks>
[CollectionDefinition(Name)]
public sealed class RasterScratchCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "raster-scratch";
}
