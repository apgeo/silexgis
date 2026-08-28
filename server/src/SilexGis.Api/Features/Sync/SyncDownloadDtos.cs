// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Entities;

namespace SilexGis.Api.Features.Sync;

/// <summary>
/// One containment edge above a downloaded row. Containment is the only structure the server
/// derives anything from — protection and visibility are both inherited along it — so a device
/// that drops these edges rebuilds a tree that means something different from the stored one.
/// Only parents the caller may read are named; a bare identifier for a row somebody cannot see
/// would be a disclosure with nothing to attach it to. That filtering means the list is not
/// necessarily a complete ancestry, which is why nothing a device needs to know about a row is
/// left for it to derive from these edges.
/// </summary>
public sealed record SyncParentDto(Guid ParentId, bool IsPrimary);

/// <summary>
/// One live feature as a device receives it.
/// </summary>
/// <remarks>
/// Geometry here is always exact or absent, never approximate: a row whose position this caller
/// may not see exactly does not appear in the payload at all. That is deliberate and specific to
/// this channel — a download is bulk, is kept in cleartext for as long as the app is installed,
/// and is re-shared by the device to people this server never authenticated, so an approximate
/// coordinate would be a permanent copy of roughly where a protected cave is rather than a
/// momentary view of it.
/// </remarks>
/// <param name="FeatureTypeCode">
/// The kind's stable code, never its numeric id: ids are assigned by whichever installation
/// seeded the table and are not comparable between two servers.
/// </param>
/// <param name="LocationProtected">
/// Whether this row is itself a protection root — a property of the row alone, and not the answer
/// to "is this position guarded".
/// </param>
/// <param name="ProtectedEffective">
/// Whether the position this row carries is guarded at all, by this row or by anything containing
/// it. Carried rather than left for the device to work out, because the device cannot work it out:
/// <see cref="SyncParentDto"/> edges are filtered to parents this caller may read, so a row can sit
/// inside a protected cave the caller cannot see and arrive with no parents at all. A caller that
/// owns such a row is entitled to its exact position and receives it — but a device that wrote it
/// to cleartext storage and re-shared it as unguarded would be handing out a guarded position with
/// nothing anywhere in the payload saying so.
/// </param>
/// <param name="UpdatedAt">
/// Server-stamped, and the value the device sends back as its base revision when it later writes
/// this row. It is this row's revision on this server and the only value arbitration compares; a
/// device's own clock never enters that comparison.
/// </param>
/// <param name="ClientUpdatedAt">
/// The moment a device believed it last wrote this row, by that device's own clock, or null for
/// the rows this server's own interface created. Provenance only — it is offered so two devices
/// editing the same row while both are offline can compare their versions with each other, and it
/// is never what decides which write this server keeps.
/// </param>
/// <remarks>
/// The list stops where it stops on purpose. Kind-specific columns are not folded in here as they
/// are elsewhere, and one of them is the reason to say so: a cave's altitude is emitted today with
/// no protection check on it at all, beside neighbouring fields that do get one. That is existing
/// behaviour on existing routes and is not this channel's to fix — but it is emphatically not
/// something to copy into a payload that is written to a phone in cleartext, so this record does
/// not carry it and a later widening should not add it without deciding that question first.
/// </remarks>
public sealed record SyncFeatureDto(
    Guid Id,
    FeatureKind Kind,
    string? FeatureTypeCode,
    FeatureCategory Category,
    string? Name,
    string? Description,
    GeoJsonGeometry? Geometry,
    JsonElement Properties,
    int? PropertiesSchemaVersion,
    bool LocationProtected,
    bool ProtectedEffective,
    Visibility Visibility,
    IReadOnlyList<SyncParentDto> Parents,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ClientUpdatedAt);

/// <summary>
/// A row that is gone, carrying its identifier and the moment it went and nothing else.
/// </summary>
/// <remarks>
/// The stub is this short on purpose. Everything else a feature row holds says something: a name
/// is half of what protection hides, the property document is emitted verbatim with no protection
/// filter anywhere on its path, and the ancestry would publish which cave a deleted place hung
/// under. A device needs neither — it already holds the row it is being told to drop, and matches
/// it by identifier.
/// </remarks>
public sealed record SyncTombstoneDto(Guid Id, DateTimeOffset DeletedAt);

/// <summary>
/// One page of a device's download: the rows that changed, the rows that went, and the settings
/// document that governs how the device generates codes.
/// </summary>
/// <param name="SetRevision">
/// The sync set's revision at the moment this page was read. It is what the device compares to
/// find out whether the settings document it holds is still current, and what a later replacement
/// of that document is arbitrated against.
/// </param>
/// <param name="Settings">
/// The device's own settings document, stored and returned verbatim. The server has no opinion
/// about its meaning and must not acquire one: validating it here would reject settings that are
/// legal on the device.
/// </param>
/// <param name="NextCursor">
/// Where to resume. Present whenever the page emitted anything, so a device that has caught up
/// keeps a watermark to come back with; null only when there was nothing at or after the cursor
/// it asked from. Opaque — its contents are the server's business and its shape may change.
/// </param>
/// <param name="HasMore">
/// Whether more changes were waiting behind this page. False means the device is level with the
/// server as of this read, not that it should stop asking.
/// </param>
public sealed record SyncDownloadPageDto(
    long SetRevision,
    JsonElement Settings,
    IReadOnlyList<SyncFeatureDto> Features,
    IReadOnlyList<SyncTombstoneDto> Tombstones,
    string? NextCursor,
    bool HasMore);
