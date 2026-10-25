// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Settings;

namespace SilexGis.Api.Features.Sync;

/// <summary>
/// The optional parts of the contract, announced by name in the capabilities answer. A device
/// that meets a server serving only some of them takes the parts that are there instead of
/// failing at the first transfer — which is why reading rows is announced separately from writing
/// them, and why a build that can only be read from says so.
/// </summary>
/// <remarks>
/// The list is worked out for each request rather than fixed for the build, because some parts
/// are an installation's choice: a part an installation has switched off is not announced, and a
/// device that does not see it announced does not ask for it. Adding a name is how something new
/// reaches a device without the contract version moving — a device ignores names it does not
/// recognise.
/// </remarks>
public static class SyncFeatures
{
    public const string Download = "download";
    public const string Upload = "upload";
    public const string Documents = "documents";

    /// <summary>
    /// Photos and documents from the phone: bytes staged through the resumable transfer routes, and
    /// the upload's <c>documents</c> and <c>documentLinks</c> sections that name them.
    /// </summary>
    public const string DocumentUpload = "documentUpload";

    /// <summary>
    /// The parts every installation running this build serves, whatever it has switched — the
    /// baseline the recorded exchanges are pinned to.
    /// </summary>
    public static readonly IReadOnlyList<string> Unconditional = [Download, Upload, Documents, DocumentUpload];

    /// <summary>
    /// The people directory and the phone-user links (<c>GET /sync/people</c> and the upload's
    /// <c>people</c> section). Switchable by the installation.
    /// </summary>
    public const string People = "people";

    /// <summary>
    /// The parts answered to one request: the unconditional ones, in their fixed order, followed by
    /// each switchable part the installation has switched on and this caller may use.
    /// </summary>
    /// <remarks>
    /// The settings are read for every call and never cached beside the list, so switching a part
    /// off takes it out of the next capabilities answer.
    /// </remarks>
    public static IReadOnlyList<string> Answered(SyncSettings settings, AccessContext ctx)
    {
        List<string> parts = [.. Unconditional];
        if (settings.PeopleDirectoryEnabled && CaverDirectory.MayReadPeople(ctx))
        {
            parts.Add(People);
        }

        return parts;
    }
}
