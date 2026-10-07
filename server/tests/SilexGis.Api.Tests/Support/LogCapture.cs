// SPDX-License-Identifier: AGPL-3.0-or-later
using Serilog.Core;
using Serilog.Events;

namespace SilexGis.Api.Tests.Support;

/// <summary>
/// Everything the application logged during a test. Registered as a sink on the real logging
/// pipeline — the host reads its sinks from the service collection — so a warning asserted here
/// is one the code path under test emitted through the logger the installation actually uses.
/// </summary>
/// <remarks>
/// <para>
/// Given to a host through its own <c>configureServices</c>:
/// <c>services.AddSingleton&lt;ILogEventSink&gt;(logs)</c>.
/// </para>
/// <para>
/// <b>What it does not see.</b> Only what is written through the application's injected loggers.
/// The per-request line is written through the process-wide logger that exists before the
/// application is built, and so are the lines of start-up itself; neither arrives here. An
/// assertion that something is absent from these events says nothing about those.
/// </para>
/// </remarks>
internal sealed class LogCapture : ILogEventSink
{
    private readonly List<LogEvent> events = [];

    /// <summary>Each event as its level and its rendered message.</summary>
    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (events)
            {
                return [.. events.Select(e => $"{e.Level} {e.RenderMessage()}")];
            }
        }
    }

    /// <summary>
    /// The events themselves, for a test about what an event carries beside its message: a
    /// property is written by a structured sink whether or not the message renders it.
    /// </summary>
    public IReadOnlyList<LogEvent> Events
    {
        get
        {
            lock (events)
            {
                return [.. events];
            }
        }
    }

    public void Clear()
    {
        lock (events)
        {
            events.Clear();
        }
    }

    public void Emit(LogEvent logEvent)
    {
        lock (events)
        {
            events.Add(logEvent);
        }
    }
}
