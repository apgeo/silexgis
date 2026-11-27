// SPDX-License-Identifier: AGPL-3.0-or-later
import { ApiError, api } from '../../api/client.ts';
import { shouldResume } from './uploadPlan.ts';

/** Where one upload is aimed. Every part is optional and they combine. */
export interface UploadTarget {
  /** The shelf to file into, or none — an unfiled upload is a real, ordinary destination. */
  cabinetId?: string;
  /** The folder path the browser reported, mirrored as cabinets under `cabinetId`. */
  relativePath?: string;
  attachEntityType?: string;
  attachEntityId?: string;
  attachRole?: string;
  /** The drop this file belongs to, so everything that arrived together can be found again. */
  batchId?: string;
  cavingGroupId?: string;
  /** Expand a dropped archive on the server rather than storing it as one file. */
  expandArchive?: boolean;
}

/** What the transport made of one file. */
export interface UploadResult {
  fileId: string;
  documentId: string;
}

/** The upload was refused because this content is already here and the caller can see it. */
export class DuplicateUploadError extends Error {
  constructor() {
    super('This content is already stored.');
    this.name = 'DuplicateUploadError';
  }
}

/** How the transport reports progress and asks the one question it cannot answer itself. */
export interface UploadOptions {
  /** Called with 0–1 as the file moves. A single-request upload only ever reports 1. */
  onProgress?: (fraction: number) => void;
  /**
   * Asked when the server says this content is already stored and the caller may see it.
   * Answering true re-sends with the duplicate accepted; false leaves it skipped.
   *
   * The question lives here rather than in the queue because only the transport knows the
   * refusal happened — and only the person at the screen can answer it.
   */
  onDuplicate?: (documentId: string | undefined, fileName: string) => Promise<boolean>;
  signal?: AbortSignal;
  /**
   * A resumable session an earlier attempt at this file left open. The transport asks the server
   * where that file ends and sends only the rest; a session that has expired is replaced by a
   * fresh one.
   */
  resumeSession?: string;
  /**
   * Told the session this file is travelling in as soon as it is open, and told `undefined` once
   * it is gone (completed, abandoned or expired). Whoever keeps the queue holds on to it, so a
   * retry after a failure continues the transfer instead of sending the file from its first byte.
   */
  onSession?: (sessionId: string | undefined) => void;
}

/** The limits the server publishes, as the transport needs them. */
export interface UploadLimits {
  chunkBytes: number;
  resumableThresholdBytes: number;
}

/**
 * Sends one file, choosing between a single request and a resumable session.
 *
 * The choice is the server's to advise: it publishes the threshold and the piece size, so a
 * client build cannot disagree with the installation it is talking to.
 */
export async function uploadOne(
  file: File,
  target: UploadTarget,
  limits: UploadLimits,
  options: UploadOptions = {},
): Promise<UploadResult> {
  return shouldResume(file.size, limits.resumableThresholdBytes)
    ? uploadResumable(file, target, limits, options)
    : uploadSingle(file, target, options);
}

/** One request, for a file small enough that re-sending it after a failure costs little. */
async function uploadSingle(
  file: File,
  target: UploadTarget,
  options: UploadOptions,
  allowDuplicate = false,
): Promise<UploadResult> {
  const form = new FormData();
  form.append('file', file, file.name);

  const { data, error, response } = await api.POST('/api/v1/files', {
    params: { query: { ...queryOf(target), allowDuplicate } },
    body: form as never,
    bodySerializer: (b: unknown) => b as FormData,
    signal: options.signal,
  });

  if (error || !data) {
    // The one refusal a person can answer. Asked once: a "yes" re-sends with the duplicate
    // accepted, and that second attempt is not asked again whatever it says.
    if (response.status === 409 && codeOf(error) === 'file.duplicate' && !allowDuplicate) {
      const proceed = await options.onDuplicate?.(duplicateIdOf(error), file.name);
      if (proceed) {
        return uploadSingle(file, target, options, true);
      }
      throw new DuplicateUploadError();
    }

    throw problemError(response.status, error);
  }

  options.onProgress?.(1);
  return { fileId: data.id, documentId: data.documentId };
}

/**
 * How long to wait before sending a piece again after the connection dropped or the server said
 * it was briefly unavailable. Three tries spread over about half a minute ride out a bad patch of
 * signal; a link that stays down longer fails the file, and the session is kept so a retry
 * continues where it stopped.
 */
const PieceRetryDelaysMs = [2_000, 8_000, 20_000] as const;

/** Answers that say "not now" rather than "no": a gateway or the server catching its breath. */
const TransientStatuses = new Set([502, 503, 504]);

/**
 * How many conflicts in a row, without a byte of progress between them, before the transport
 * stops believing the server's offset. Each conflict names where the file ends, so one is normal
 * and a second means another attempt is racing this one; more is a loop, not a recovery.
 */
const MaxConflictsWithoutProgress = 3;

/**
 * A session, then the file a piece at a time, then completion.
 *
 * Everything that can refuse the upload is decided when the session opens, before a byte
 * moves — which is the whole value of the mechanism: the failure it exists to prevent is a
 * large transfer over a bad link that is refused at the end, or that has to start over.
 */
async function uploadResumable(
  file: File,
  target: UploadTarget,
  limits: UploadLimits,
  options: UploadOptions,
): Promise<UploadResult> {
  const kept = options.resumeSession ? await sessionStatus(options.resumeSession, options) : undefined;
  if (options.resumeSession && !kept) {
    options.onSession?.(undefined);
  }

  const session = kept ?? (await openSession(file, target, options));
  const chunkBytes = session.chunkBytes || limits.chunkBytes;
  await sendPieces(file, session.id, chunkBytes, session.receivedBytes, options);
  return completeResumable(session.id, file.name, options);
}

interface SessionState {
  id: string;
  chunkBytes: number;
  receivedBytes: number;
}

async function openSession(file: File, target: UploadTarget, options: UploadOptions): Promise<SessionState> {
  const opened = await api.POST('/api/v1/files/uploads', {
    body: {
      fileName: file.name,
      sizeBytes: file.size,
      cabinetId: target.cabinetId ?? null,
      relativePath: target.relativePath ?? null,
      attachEntityType: target.attachEntityType ?? null,
      attachEntityId: target.attachEntityId ?? null,
      attachRole: target.attachRole ?? null,
      batchId: target.batchId ?? null,
      cavingGroupId: target.cavingGroupId ?? null,
    } as never,
    signal: options.signal,
  });

  if (opened.error || !opened.data) {
    throw problemError(opened.response.status, opened.error);
  }

  options.onSession?.(opened.data.id);
  return opened.data;
}

/**
 * Where a kept session's file ends, or undefined when the session is gone. Gone means expired, or
 * already completed by an attempt whose answer never arrived — a fresh session then either
 * stores the file or meets the duplicate check, which names the document it became.
 */
async function sessionStatus(id: string, options: UploadOptions): Promise<SessionState | undefined> {
  const status = await api.GET('/api/v1/files/uploads/{id}', {
    params: { path: { id } },
    signal: options.signal,
  });

  if (status.data) {
    return status.data;
  }
  if (status.response.status === 404) {
    return undefined;
  }
  throw problemError(status.response.status, status.error);
}

/**
 * Sends the file from `start` to its end.
 *
 * A piece whose answer was lost is simply sent again: the server accepts a piece lying wholly
 * inside what it already holds as a harmless replay, and answers any other mismatch with a
 * conflict naming the offset it expects — so neither case needs a separate question first.
 * A piece the server failed to write (a failing or full disk) is not resent: that is a fault on
 * the server's side, and the session stays for a retry once it is put right.
 */
async function sendPieces(
  file: File,
  session: string,
  chunkBytes: number,
  start: number,
  options: UploadOptions,
): Promise<void> {
  let received = start;
  let retries = 0;
  let conflicts = 0;

  while (received < file.size) {
    const end = Math.min(file.size, received + chunkBytes);
    let sent;
    try {
      sent = await api.PUT('/api/v1/files/uploads/{id}', {
        params: { path: { id: session }, query: { offset: received } },
        body: file.slice(received, end) as never,
        bodySerializer: (b: unknown) => b as Blob,
        headers: { 'Content-Type': 'application/octet-stream' },
        signal: options.signal,
      });
    } catch (error) {
      // The connection dropped. Nothing the server said is known, so the same piece goes again.
      if (options.signal?.aborted || retries >= PieceRetryDelaysMs.length) {
        throw error;
      }
      await pause(PieceRetryDelaysMs[retries++]!, options.signal);
      continue;
    }

    if (sent.data) {
      if (sent.data.receivedBytes > received) {
        conflicts = 0;
      }
      retries = 0;
      received = sent.data.receivedBytes;
      options.onProgress?.(received / file.size);
      continue;
    }

    const status = sent.response.status;
    if (status === 409 && isOutOfOrder(sent.error) && conflicts < MaxConflictsWithoutProgress) {
      // The server's idea of where the file ends is not ours — a lost answer, or a second
      // attempt racing this one. It says where it is; carrying on from there is the recovery.
      conflicts += 1;
      const expected = expectedOffsetOf(sent.error) ?? (await sessionStatus(session, options))?.receivedBytes;
      if (expected === undefined) {
        throw new ApiError(404, 'upload.session_not_found');
      }
      received = expected;
      continue;
    }

    if (TransientStatuses.has(status) && retries < PieceRetryDelaysMs.length) {
      await pause(PieceRetryDelaysMs[retries++]!, options.signal);
      continue;
    }

    throw problemError(status, sent.error);
  }
}

async function completeResumable(
  session: string,
  fileName: string,
  options: UploadOptions,
  allowDuplicate = false,
): Promise<UploadResult> {
  const { data, error, response } = await api.POST('/api/v1/files/uploads/{id}/complete', {
    params: { path: { id: session }, query: { allowDuplicate } },
    signal: options.signal,
  });

  if (error || !data) {
    if (response.status === 409 && codeOf(error) === 'file.duplicate' && !allowDuplicate) {
      // The session survives this refusal precisely so the answer can be "store it anyway"
      // without the bytes having to travel again.
      const proceed = await options.onDuplicate?.(duplicateIdOf(error), fileName);
      if (proceed) {
        return completeResumable(session, fileName, options, true);
      }

      await api.DELETE('/api/v1/files/uploads/{id}', { params: { path: { id: session } } });
      options.onSession?.(undefined);
      throw new DuplicateUploadError();
    }

    // A completed session answers "not found" when asked to complete again, so this is either an
    // expired session or one an earlier attempt already turned into a document. Either way it is
    // of no further use to a retry.
    if (response.status === 404) {
      options.onSession?.(undefined);
    }
    throw problemError(response.status, error);
  }

  options.onSession?.(undefined);

  // The route also answers bytes a phone staged for its sync upload, which become no document.
  // This client never opens such a transfer, so a staged answer means the session was not ours.
  if (!('documentId' in data)) {
    throw new ApiError(response.status);
  }

  options.onProgress?.(1);
  return { fileId: data.id, documentId: data.documentId };
}

/**
 * Gives up a session whose file will not be sent after all, so its partial bytes do not wait on
 * the server for the expiry sweep. Best effort: a session already gone, or a request that fails,
 * leaves nothing for the person to act on.
 */
export async function abandonSession(sessionId: string): Promise<void> {
  try {
    await api.DELETE('/api/v1/files/uploads/{id}', { params: { path: { id: sessionId } } });
  } catch {
    // The sweep removes it when it expires.
  }
}

/** Waits, unless the person cancels first. */
function pause(ms: number, signal: AbortSignal | undefined): Promise<void> {
  return new Promise((resolve, reject) => {
    if (signal?.aborted) {
      reject(signal.reason);
      return;
    }
    const timer = setTimeout(() => {
      signal?.removeEventListener('abort', onAbort);
      resolve();
    }, ms);
    const onAbort = () => {
      clearTimeout(timer);
      reject(signal?.reason);
    };
    signal?.addEventListener('abort', onAbort, { once: true });
  });
}

/** Only what was actually chosen: an explicit null would file into "no cabinet" needlessly. */
function queryOf(target: UploadTarget): Record<string, string | boolean | undefined> {
  return {
    cabinetId: target.cabinetId,
    // A bare file name adds nothing — the uploaded part already carries it — and sending it
    // would make every ordinary upload look like a folder drop of depth zero.
    relativePath: target.relativePath?.includes('/') ? target.relativePath : undefined,
    attachEntityType: target.attachEntityType,
    attachEntityId: target.attachEntityId,
    attachRole: target.attachRole,
    batchId: target.batchId,
    cavingGroupId: target.cavingGroupId,
    expandArchive: target.expandArchive,
  };
}

function codeOf(error: unknown): string | undefined {
  return typeof error === 'object' && error !== null && 'code' in error
    ? ((error as { code?: string }).code ?? undefined)
    : undefined;
}

/**
 * A refusal as an {@link ApiError} that keeps the server's own words and members, so a screen
 * can word a refusal from its facts (how much room was missing, which limit) instead of only
 * from its code.
 */
function problemError(status: number, error: unknown): ApiError {
  const problem = typeof error === 'object' && error !== null ? (error as Record<string, unknown>) : undefined;
  const detail = typeof problem?.detail === 'string' ? problem.detail : undefined;
  return new ApiError(status, codeOf(error), detail, problem);
}

/** A conflict about where the file ends, the one conflict a piece can recover from by itself. */
function isOutOfOrder(error: unknown): boolean {
  const code = codeOf(error);
  return code === undefined || code === 'upload.chunk_out_of_order';
}

/** The offset a conflict says the file ends at, when the server named one. */
function expectedOffsetOf(error: unknown): number | undefined {
  const value = typeof error === 'object' && error !== null ? (error as { expectedOffset?: unknown }).expectedOffset : undefined;
  return typeof value === 'number' && Number.isFinite(value) && value >= 0 ? value : undefined;
}

/**
 * The document a duplicate refusal named. The server sends it as a member of its own, because
 * the sentence beside it is for a person and may be reworded or translated.
 */
function duplicateIdOf(error: unknown): string | undefined {
  return problemError(409, error).member('duplicateOfDocumentId');
}
