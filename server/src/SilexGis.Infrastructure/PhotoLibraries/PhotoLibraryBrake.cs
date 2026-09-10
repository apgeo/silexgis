// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Extensions.DependencyInjection;
using SilexGis.Domain.PhotoLibraries;
using SilexGis.Domain.Settings;

namespace SilexGis.Infrastructure.PhotoLibraries;

/// <summary>
/// Whether this installation is still using a neighbouring photo library, asked at the point a
/// request would otherwise leave the machine.
/// </summary>
/// <remarks>
/// <para>
/// This exists because the guarantee it protects has to be structural rather than remembered. A
/// surface that decides for itself whether a library may be talked to is a decision every future
/// route has to repeat, and the failure when one forgets is silent: requests keep going to a
/// library somebody stopped, which looks from the inside exactly like a library that is working,
/// and the only symptom is traffic at a neighbour's container nobody is watching. Asked here — in
/// the one method each client already calls before it opens anything — a route that forgets is
/// refused by the client itself rather than by the route's own good manners.
/// </para>
/// <para>
/// It is a separate object from the stored settings, and not the settings themselves, because the
/// clients outlive any one request while the settings reader belongs to one. Answers come from the
/// same short in-memory window every other section resolves through, so asking on every call costs
/// a lookup rather than a query.
/// </para>
/// </remarks>
public interface IPhotoLibraryBrake
{
    /// <summary>Whether an administrator has stopped this installation using one library.</summary>
    ValueTask<bool> IsSuspendedAsync(PhotoLibrarySource source, CancellationToken ct);
}

/// <summary>
/// Reads the brake from the installation's stored settings.
/// </summary>
/// <remarks>
/// A scope per question, because the settings reader is per-request and the photo-library clients
/// are not: they are long-lived on purpose, holding a credential and — for one of the products — a
/// whole library's worth of positions that can only be read in one answer. The scope is cheap
/// beside what it guards, which is a request to another machine, and the answer it fetches is
/// served from a process-wide cache on all but one call in each short window.
/// </remarks>
public sealed class StoredPhotoLibraryBrake(IServiceScopeFactory scopes) : IPhotoLibraryBrake
{
    public async ValueTask<bool> IsSuspendedAsync(PhotoLibrarySource source, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IAppSettingsService>();
        var suspension = await settings.GetPhotoLibrarySuspensionAsync(ct);
        return suspension.IsSuspended(source);
    }
}
