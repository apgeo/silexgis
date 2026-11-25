// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net.Sockets;

namespace SilexGis.Infrastructure.Outbound;

/// <summary>
/// Reading the body of an answer from another server: the time limit that covers it, and which
/// failures while it arrives mean "that server is not answering properly" rather than a defect here.
/// </summary>
/// <remarks>
/// <para>
/// Every client here sends with the headers read and the body still open, so that a large answer
/// is streamed rather than buffered twice. That has one consequence that is easy to miss:
/// <see cref="HttpClient.Timeout"/> stops counting once the headers have arrived. A server, or a
/// proxy in front of one, that sends its headers and then stops sending without closing the
/// connection would otherwise hold whatever is reading — a request, a background refresh holding a
/// permit, a job on a one-at-a-time lane — until the process restarts. The body therefore gets a
/// deadline of its own, linked to the caller's token so that the caller can still stop it.
/// </para>
/// <para>
/// A connection dropped part-way through the body surfaces as an I/O error, not as a request error,
/// so a client that only catches request errors around its send reports it as something unexpected
/// rather than as the far side being unavailable.
/// </para>
/// </remarks>
public static class OutboundBody
{
    /// <summary>
    /// A token that ends when the caller's does or when <paramref name="limit"/> has passed,
    /// whichever comes first. The caller disposes it.
    /// </summary>
    public static CancellationTokenSource Deadline(TimeSpan limit, CancellationToken ct)
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(limit);
        return deadline;
    }

    /// <summary>
    /// Whether <paramref name="e"/>, raised while an answer's body was read, means the far side
    /// stopped answering: the body's own deadline, a dropped connection, or a compressed body that
    /// ended in the middle of a block. Never true once the caller has asked to stop, so a closed
    /// tab or a host shutdown still arrives as the cancellation it is.
    /// </summary>
    /// <remarks>
    /// A plain <see cref="IOException"/> is deliberately not matched: a full or failing local disk
    /// raises that same type when a body is being copied to a file, and must not be blamed on the
    /// server at the other end.
    /// </remarks>
    public static bool IsLost(Exception e, CancellationToken ct) =>
        !ct.IsCancellationRequested
        && (e is OperationCanceledException or HttpRequestException or HttpIOException or InvalidDataException
            || e is IOException { InnerException: SocketException });

    /// <summary>
    /// Reads the whole body into memory, refusing to hold more than <paramref name="maxBytes"/>:
    /// <c>null</c> when the answer is larger than that. Bytes are counted as they arrive, after any
    /// transfer compression has been undone, so a small compressed answer that expands without end
    /// is stopped too.
    /// </summary>
    public static async Task<MemoryStream?> ReadBoundedAsync(HttpContent content, long maxBytes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (content.Headers.ContentLength is { } declared && declared > maxBytes)
        {
            return null;
        }

        await using var body = await content.ReadAsStreamAsync(ct);
        var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                await buffer.DisposeAsync();
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        buffer.Position = 0;
        return buffer;
    }
}
