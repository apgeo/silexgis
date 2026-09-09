// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.PhotoLibraries;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.PhotoLibraries;

namespace SilexGis.Api.Features.PhotoLibraries;

/// <summary>
/// The one place that decides whether this installation will talk to a neighbouring photo library,
/// asked by every route in this slice before anything leaves the machine.
/// </summary>
/// <remarks>
/// <para>
/// Two facts decide it and they are not the same fact. <b>Whether the installation knows about a
/// library at all</b> comes from the deployment: an address and a credential were supplied, or they
/// were not, and nothing a person can do from a screen changes that. <b>Whether it is being used
/// right now</b> is a stored decision an administrator takes while the system is running, and it
/// only ever goes one way from where the deployment left it — a library that exists can be stopped
/// and started again, and a library that does not exist cannot be conjured into being by switching
/// something on. That asymmetry is the design; a symmetric enable/disable toggle would be a
/// different feature, and a worse one, because "enabled" said about a container nobody deployed is
/// a state nobody can act on.
/// </para>
/// <para>
/// It is one object rather than a check repeated per route because the check is easy to leave out
/// and impossible to notice missing: a route that skips it goes on working perfectly against a
/// library somebody thought they had stopped, and the only symptom is traffic at a neighbour's
/// container that nobody is looking for. So every route asks here, and a suspended or unconfigured
/// library is simply not handed back — there is no library object to call.
/// </para>
/// <para>
/// That is the first of two answers rather than the whole of it, and the difference is worth
/// knowing before adding a route. Asking here is something a handler <em>does</em>, so it is
/// something a handler can be written without; what makes "no socket is opened" a property rather
/// than a promise is that each library client asks the same question for itself, in the one method
/// its outgoing calls already go through. A handler that never came here is refused there. This
/// gate exists so that the refusal is a shaped answer — absent, exactly as for a library the
/// deployment never supplied — instead of an exception from underneath.
/// </para>
/// </remarks>
public sealed class PhotoLibraryGate(IEnumerable<IPhotoLibrary> libraries, IAppSettingsService settings)
{
    /// <summary>
    /// The library to ask, or null when this installation will ask it nothing — because it runs no
    /// such product, because nobody supplied an address for it, or because it has been suspended.
    /// </summary>
    /// <remarks>
    /// One answer for all three on purpose. Every route in this slice already treats a library
    /// nobody configured as absent rather than broken, and a suspended one is to behave exactly the
    /// same way: the whole point of the brake is that the application looks like an installation
    /// that never had the library, rather than like one whose library is failing.
    /// </remarks>
    public async ValueTask<IPhotoLibrary?> UsableAsync(PhotoLibrarySource source, CancellationToken ct)
    {
        var library = libraries.FirstOrDefault(l => l.Source == source && l.IsConfigured);
        if (library is null)
        {
            return null;
        }

        var suspension = await settings.GetPhotoLibrarySuspensionAsync(ct);
        if (!suspension.IsSuspended(source))
        {
            return library;
        }

        // Stopped means stopped using and forgetting what it said, not merely declining to ask
        // again. Done here as well as on the status survey so that the first request to arrive
        // after the brake goes on drops the reading, whether or not anybody is watching a status
        // line. Nothing is asked of the library to do it.
        library.Forget();
        return null;
    }

    /// <summary>
    /// Whether this build knows the product at all, separately from whether it will be used.
    /// </summary>
    /// <remarks>
    /// The map route needs the difference: a product this installation does not run is not found,
    /// while one it runs and is not using answers with an empty collection, which is what the
    /// overlay already draws for a library that holds nothing here.
    /// </remarks>
    public bool Runs(PhotoLibrarySource source) => libraries.Any(l => l.Source == source);

    /// <summary>
    /// Every product this build can read, what this installation has decided about each, and what
    /// each one said when it was last asked.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The libraries are asked together rather than one after another: they are separate
    /// installations with separate uptime, and asking them in turn would make one stopped container
    /// cost the whole answer its own timeout before the other was reached.
    /// </para>
    /// <para>
    /// Only a library that is configured and not suspended is asked anything. A suspended one is
    /// reported as suspended and nothing is claimed about whether it would have answered — saying
    /// "not answering" about a library nobody asked would be inventing a failure, and asking it in
    /// order to have something to say would be the one thing suspension exists to stop.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<PhotoLibraryReport>> SurveyAsync(CancellationToken ct)
    {
        var suspension = await settings.GetPhotoLibrarySuspensionAsync(ct);
        var all = libraries.ToList();

        var asked = all
            .Where(library => library.IsConfigured && !suspension.IsSuspended(library.Source))
            .ToList();

        // Everything stopped forgets what it was holding, and this is the path that reaches it:
        // the settings screen re-asks for this survey the moment a brake goes on, so a library
        // taken out of use drops its reading then rather than the next time something happens to
        // want it. It opens no socket, and it is safe to repeat.
        foreach (var stopped in all.Where(library => library.IsConfigured && suspension.IsSuspended(library.Source)))
        {
            stopped.Forget();
        }

        var health = await Task.WhenAll(asked.Select(library => library.ProbeAsync(ct)));

        // Built by hand rather than with ToDictionary, which throws on a repeated key. Two
        // registrations of one product is a deployment mistake, and the status route is where an
        // operator would go to see it; taking that route down with an exception would hide it.
        var answers = new Dictionary<PhotoLibrarySource, LibraryHealth>();
        for (var index = 0; index < asked.Count; index++)
        {
            answers[asked[index].Source] = health[index];
        }

        return
        [
            .. all.Select(library =>
            {
                var suspended = library.IsConfigured && suspension.IsSuspended(library.Source);
                return new PhotoLibraryReport(
                    library.Source,
                    library.IsConfigured,
                    suspended,
                    library.SearchMatching,
                    // A library nothing may be asked of is a library no picture may be fetched
                    // from either, whatever its own byte gate last recorded.
                    PicturesAvailable: !suspended && library.IsConfigured && library.PicturesAvailable,
                    answers.TryGetValue(library.Source, out var answer) ? answer : LibraryHealth.NotAsked);
            }),
        ];
    }
}

/// <summary>
/// One neighbouring photo library as the status surface sees it: what this installation has been
/// given, what it has decided, and what the library said.
/// </summary>
/// <param name="Source">Which product it is.</param>
/// <param name="Configured">
/// An address and a credential were supplied by the deployment. Nothing an administrator does from
/// a screen can make this true.
/// </param>
/// <param name="Suspended">
/// This installation is not using it for now, by an administrator's decision. Only ever true for a
/// library that is configured — there is nothing to stop using otherwise.
/// </param>
/// <param name="Matching">What the product does with words; a fact about the product, known without asking it.</param>
/// <param name="PicturesAvailable">Whether image bytes may currently be fetched from it.</param>
/// <param name="Health">
/// What it said when it was last asked, or that it was not asked — which is the answer for a
/// library that is unconfigured and for one that is suspended, and in both cases no socket was
/// opened to produce it.
/// </param>
public sealed record PhotoLibraryReport(
    PhotoLibrarySource Source,
    bool Configured,
    bool Suspended,
    LibrarySearchMatching Matching,
    bool PicturesAvailable,
    LibraryHealth Health);
