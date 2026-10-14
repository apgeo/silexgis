// SPDX-License-Identifier: AGPL-3.0-or-later
using OSGeo.GDAL;
using Shouldly;
using SilexGis.Infrastructure.Geodata;

namespace SilexGis.Api.Tests;

/// <summary>
/// The page renderer quiets the raster library around one call with no lock, on a service that
/// answers requests concurrently, beside terrain work that reads the library's error messages
/// in the same process. That is safe only because the library keeps its error handlers per
/// thread. This pins it: a library upgrade that made the stack process-wide would let one
/// request silence — or capture — another's errors, and would show up here first.
/// </summary>
public sealed class ErrorHandlerScopeTests
{
    [Fact]
    public void A_handler_pushed_on_one_thread_never_sees_another_threads_errors()
    {
        GdalRuntime.Configure();
        var seen = new List<string>();
        Gdal.GDALErrorHandlerDelegate record = (_, _, message) =>
        {
            lock (seen)
            {
                seen.Add(System.Runtime.InteropServices.Marshal.PtrToStringUTF8(message) ?? string.Empty);
            }
        };

        using var pushed = new ManualResetEventSlim();
        using var otherDone = new ManualResetEventSlim();
        var marker = Guid.NewGuid().ToString("N");

        // Waits are recorded and asserted after the join: an assertion thrown on a bare thread
        // would take the whole test host down instead of failing this test.
        bool holderWaited = false, otherWaited = false;

        var holder = new Thread(() =>
        {
            Gdal.PushErrorHandler(record);
            try
            {
                pushed.Set();
                holderWaited = otherDone.Wait(TimeSpan.FromSeconds(30));

                // The positive half: the handler does catch what its own thread raises, so an
                // empty list below means "not delivered here", not "never delivered".
                Gdal.Error(CPLErr.CE_Warning, 1, $"own {marker}");
            }
            finally
            {
                Gdal.PopErrorHandler();
            }
        });

        var other = new Thread(() =>
        {
            otherWaited = pushed.Wait(TimeSpan.FromSeconds(30));
            Gdal.Error(CPLErr.CE_Warning, 1, $"other {marker}");
            otherDone.Set();
        });

        holder.Start();
        other.Start();
        other.Join();
        holder.Join();
        GC.KeepAlive(record);

        holderWaited.ShouldBeTrue();
        otherWaited.ShouldBeTrue();

        seen.ShouldContain($"own {marker}");
        seen.ShouldNotContain($"other {marker}");
    }
}
