// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Who may replace a phone user's link to a caver: anybody while there is none, otherwise only a
/// source of at least the authority of the one that made it — so a phone cannot undo a choice made
/// in the browser, and an import cannot undo the roster keeper's.
/// </summary>
public class DevicePersonLinksTests
{
    private static readonly Guid Linked = Guid.CreateVersion7();

    [Theory]
    [InlineData(DevicePersonLinkSource.Device)]
    [InlineData(DevicePersonLinkSource.ArchiveImport)]
    [InlineData(DevicePersonLinkSource.Administrator)]
    public void A_phone_user_with_no_link_takes_one_from_anybody(DevicePersonLinkSource incoming)
    {
        // The source recorded beside a missing link means nothing, whatever it says.
        DevicePersonLinks.MayReplace(null, DevicePersonLinkSource.Administrator, incoming).ShouldBeTrue();
    }

    [Theory]
    [InlineData(DevicePersonLinkSource.Device, DevicePersonLinkSource.Device, true)]
    [InlineData(DevicePersonLinkSource.Device, DevicePersonLinkSource.ArchiveImport, true)]
    [InlineData(DevicePersonLinkSource.Device, DevicePersonLinkSource.Administrator, true)]
    [InlineData(DevicePersonLinkSource.ArchiveImport, DevicePersonLinkSource.Device, false)]
    [InlineData(DevicePersonLinkSource.ArchiveImport, DevicePersonLinkSource.ArchiveImport, true)]
    [InlineData(DevicePersonLinkSource.ArchiveImport, DevicePersonLinkSource.Administrator, true)]
    [InlineData(DevicePersonLinkSource.Administrator, DevicePersonLinkSource.Device, false)]
    [InlineData(DevicePersonLinkSource.Administrator, DevicePersonLinkSource.ArchiveImport, false)]
    [InlineData(DevicePersonLinkSource.Administrator, DevicePersonLinkSource.Administrator, true)]
    public void A_link_is_replaced_only_by_a_source_of_at_least_its_authority(
        DevicePersonLinkSource current, DevicePersonLinkSource incoming, bool replaced)
    {
        DevicePersonLinks.MayReplace(Linked, current, incoming).ShouldBe(replaced);
    }
}
