// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Domain.Settings;

namespace SilexGis.Api.Features.Sync;

/// <summary>What the first step decided to write for one phone user.</summary>
/// <param name="Person">What the phone sent.</param>
/// <param name="Known">Whether this installation had the phone user already.</param>
/// <param name="Changes">
/// Whether anything the history records changes — a name, or the link. A phone user sent again as
/// they stand is only marked as seen, which leaves no line and no history row behind.
/// </param>
/// <param name="Relink">Whether the phone's link replaces the one that stands.</param>
internal sealed record SyncPersonProposal(SyncUploadPersonDto Person, bool Known, bool Changes, bool Relink);

/// <summary>
/// The upload's <c>people</c> section: the phone's own users, as the phone describes them, and
/// which caver each one is.
/// </summary>
/// <remarks>
/// <para>
/// A phone user is recorded whatever the directory's switch says: that a phone has a user called
/// so-and-so is what lets anything the phone sends later name its author. The link is what the
/// directory governs — a phone may only say a user is somebody the directory lists for this
/// account, and while the directory is off it lists nobody, so a link is refused for being one the
/// installation does not take rather than for naming a stranger.
/// </para>
/// <para>
/// A link stands until somebody with at least the same authority replaces it, by the one rule
/// <see cref="DevicePersonLinks.MayReplace"/> states. A phone's different choice beside a link
/// somebody made in the browser is not an error: the names are still taken, and the directory's
/// links tell the phone what stands.
/// </para>
/// </remarks>
internal sealed class SyncPeopleSection(SyncSettings settings) : SyncUploadSection<SyncUploadPersonDto, SyncPersonProposal>
{
    private HashSet<Guid> listed = [];

    public override string Name => SyncUploadSections.People;

    protected override IReadOnlyList<SyncUploadPersonDto>? ItemsOf(SyncUploadRequest request) => request.People;

    protected override async Task PrepareAsync(
        SyncUploadRun run, IReadOnlyList<SyncUploadPersonDto> items, CancellationToken ct)
    {
        var named = items.Where(p => p.CaverId is not null).Select(p => p.CaverId!.Value).Distinct().ToList();
        listed = await SyncPeopleDirectory.ListedAmongAsync(run.Db, run.Access, settings, named, ct);
    }

    public override async Task<SyncResolution<SyncPersonProposal>> ResolveAsync(
        SyncUploadRun run, SyncUploadPersonDto person, CancellationToken ct)
    {
        if (person.CaverId is { } caverId)
        {
            if (!settings.PeopleDirectoryEnabled)
            {
                return SyncResolution<SyncPersonProposal>.Answer(Refuse(
                    person.DeviceUserId,
                    "sync.feature_disabled",
                    "This installation does not take links from phone users to cavers."));
            }

            if (!listed.Contains(caverId))
            {
                return SyncResolution<SyncPersonProposal>.Answer(Refuse(
                    person.DeviceUserId,
                    "sync.person_not_listed",
                    "That caver is not in the people directory this account is given."));
            }
        }

        var existing = await run.Db.DevicePersons.AsNoTracking()
            .FirstOrDefaultAsync(p => p.DeviceUserId == person.DeviceUserId, ct);
        if (existing is null)
        {
            return SyncResolution<SyncPersonProposal>.Write(
                new SyncPersonProposal(person, Known: false, Changes: true, Relink: person.CaverId is not null));
        }

        var relink = person.CaverId is { } wanted
            && existing.CaverId != wanted
            && DevicePersonLinks.MayReplace(existing.CaverId, existing.LinkSource, DevicePersonLinkSource.Device);
        var renamed = existing.Username != person.Username
            || existing.FirstName != person.FirstName
            || existing.LastName != person.LastName;
        return SyncResolution<SyncPersonProposal>.Write(
            new SyncPersonProposal(person, Known: true, Changes: relink || renamed, Relink: relink));
    }

    public override async Task<SyncApplied> ApplyAsync(
        SyncUploadRun run, SyncPersonProposal proposal, CancellationToken ct)
    {
        var person = proposal.Person;
        var now = DateTimeOffset.UtcNow;
        var uploader = run.Access.UserId;

        if (!proposal.Known)
        {
            run.Db.DevicePersons.Add(new DevicePerson
            {
                DeviceUserId = person.DeviceUserId,
                Username = person.Username,
                FirstName = person.FirstName,
                LastName = person.LastName,
                CaverId = person.CaverId,
                LinkSource = DevicePersonLinkSource.Device,
                FirstSeenAt = now,
                LastSeenAt = now,
                UploadedByUserId = uploader,
            });
            return new SyncApplied(Ok(person.DeviceUserId, SyncRowStatus.Created), Line(person.DeviceUserId, ImportDecisionAction.Create));
        }

        if (!proposal.Changes)
        {
            // Seen again, nothing said differently: noted straight in the row, outside the history,
            // because a phone that sends its users with every upload would otherwise write a
            // history row per upload that says nothing.
            await run.Db.DevicePersons
                .Where(p => p.DeviceUserId == person.DeviceUserId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.LastSeenAt, now)
                    .SetProperty(p => p.UploadedByUserId, uploader), ct);
            return new SyncApplied(Ok(person.DeviceUserId, SyncRowStatus.Unchanged), null);
        }

        var row = await run.Db.DevicePersons.FirstAsync(p => p.DeviceUserId == person.DeviceUserId, ct);
        row.Username = person.Username;
        row.FirstName = person.FirstName;
        row.LastName = person.LastName;
        if (proposal.Relink)
        {
            row.CaverId = person.CaverId;
            row.LinkSource = DevicePersonLinkSource.Device;
        }

        row.LastSeenAt = now;
        row.UploadedByUserId = uploader;
        return new SyncApplied(Ok(person.DeviceUserId, SyncRowStatus.Updated), Line(person.DeviceUserId, ImportDecisionAction.Attach));
    }

    private static ImportBatchItem Line(Guid deviceUserId, ImportDecisionAction action) => new()
    {
        DevicePersonId = deviceUserId,
        Action = action,
    };

    private static SyncUploadRowResultDto Ok(Guid id, SyncRowStatus status) => new(id, status, null, null, null);

    private static SyncUploadRowResultDto Refuse(Guid id, string code, string detail) =>
        new(id, SyncRowStatus.Rejected, null, code, detail);
}
