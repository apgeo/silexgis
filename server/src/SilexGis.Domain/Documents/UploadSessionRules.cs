// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Documents;

/// <summary>What to do with a piece of a resumable upload that has just arrived.</summary>
public enum ChunkDisposition
{
    /// <summary>It continues exactly where the last one stopped. Append it.</summary>
    Append,

    /// <summary>
    /// Every byte of it is already stored. Answer as though it had just been written and say
    /// how far the file has got.
    /// </summary>
    AlreadyHeld,

    /// <summary>
    /// It does not start where the file currently ends. Refuse it and tell the client the
    /// offset to resume from.
    /// </summary>
    OutOfOrder,

    /// <summary>Accepting it would make the file larger than it was declared to be.</summary>
    Overflow,
}

/// <summary>
/// The arithmetic of a resumable upload: which piece may be appended, when a transfer is
/// finished, and when a session has been abandoned.
///
/// <para>
/// Pure and separate from the endpoint, because the interesting cases are the ones that are
/// awkward to provoke over HTTP — a piece that arrives twice because the response to the
/// first copy was lost, a client resuming from a stale idea of where it got to, a declared
/// size that turns out to be a lie. Each is a line of arithmetic and a test, rather than a
/// scenario somebody has to reproduce against a running server.
/// </para>
/// </summary>
public static class UploadSessionRules
{
    /// <summary>
    /// How large a piece the server asks clients to send. Small enough that losing one to a
    /// dropped connection costs little, large enough that a 500 MB file is not four thousand
    /// requests. Advice, not a rule — the endpoint accepts whatever arrives, so a client on a
    /// better link may send more.
    /// </summary>
    public const int SuggestedChunkBytes = 8 * 1024 * 1024;

    /// <summary>
    /// How long an untouched session survives before its bytes are swept up. Generous,
    /// because the case it serves is somebody who lost signal and comes back to it — but not
    /// unbounded, because the partial blob is otherwise permanent and nothing points at it.
    /// </summary>
    public static readonly TimeSpan IdleLifetime = TimeSpan.FromHours(24);

    /// <summary>Refusal: the piece does not continue the file.</summary>
    public const string OutOfOrderCode = "upload.chunk_out_of_order";

    /// <summary>Refusal: the pieces add up to more than the file was said to be.</summary>
    public const string OverflowCode = "upload.size_mismatch";

    /// <summary>Refusal: fewer bytes arrived than the file was said to hold.</summary>
    public const string IncompleteCode = "upload.incomplete";

    /// <summary>Refusal: the session has expired, or never existed.</summary>
    public const string SessionNotFoundCode = "upload.session_not_found";

    /// <summary>
    /// What to do with a piece claiming to start at <paramref name="offset"/>.
    /// </summary>
    /// <remarks>
    /// The replay case is the one worth being careful about. A client that sent a piece,
    /// never saw the answer, and sent it again must get the same answer the second time — if
    /// a replay were refused as out of order, every lost response would end the upload. So a
    /// piece lying entirely inside what is already stored is accepted as a no-op. A piece
    /// that <em>overlaps</em> the boundary is not: the stored bytes and the resent ones would
    /// have to be assumed identical, and an upload is not the place to assume that. The
    /// client is told where the file ends and sends from there.
    /// </remarks>
    public static ChunkDisposition Decide(long receivedBytes, long offset, long chunkLength, long declaredSizeBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(receivedBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(chunkLength);

        if (offset < 0)
        {
            return ChunkDisposition.OutOfOrder;
        }

        if (offset + chunkLength <= receivedBytes)
        {
            return chunkLength == 0 && offset != receivedBytes
                ? ChunkDisposition.OutOfOrder
                : ChunkDisposition.AlreadyHeld;
        }

        if (offset != receivedBytes)
        {
            return ChunkDisposition.OutOfOrder;
        }

        return offset + chunkLength > declaredSizeBytes ? ChunkDisposition.Overflow : ChunkDisposition.Append;
    }

    /// <summary>
    /// Whether a session holding <paramref name="receivedBytes"/> may be completed. Exact
    /// equality: a transfer that delivered fewer bytes than declared is unfinished, and one
    /// that delivered more was refused piece by piece and cannot be here.
    /// </summary>
    public static bool IsComplete(long receivedBytes, long declaredSizeBytes) =>
        receivedBytes == declaredSizeBytes && declaredSizeBytes > 0;

    /// <summary>When a session touched at <paramref name="now"/> stops being resumable.</summary>
    public static DateTimeOffset ExpiryFrom(DateTimeOffset now) => now + IdleLifetime;
}
