// SPDX-License-Identifier: AGPL-3.0-or-later
import { ApiError, api } from '../../api/client.ts';
import { chunkRanges, shouldResume } from './uploadPlan.ts';

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
  onDuplicate?: (documentId: string | undefined) => Promise<boolean>;
  signal?: AbortSignal;
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
      const proceed = await options.onDuplicate?.(duplicateIdOf(error));
      if (proceed) {
        return uploadSingle(file, target, options, true);
      }
      throw new DuplicateUploadError();
    }

    throw new ApiError(response.status, codeOf(error));
  }

  options.onProgress?.(1);
  return { fileId: data.id, documentId: data.documentId };
}

/**
 * A session, then the file a piece at a time, then completion.
 *
 * Everything that can refuse the upload is decided when the session opens, before a byte
 * moves — which is the whole value of the mechanism: the failure it exists to prevent is a
 * large transfer over a bad link that is refused at the end.
 */
async function uploadResumable(
  file: File,
  target: UploadTarget,
  limits: UploadLimits,
  options: UploadOptions,
): Promise<UploadResult> {
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
    throw new ApiError(opened.response.status, codeOf(opened.error));
  }

  const session = opened.data.id;
  const chunkBytes = opened.data.chunkBytes || limits.chunkBytes;
  let received = opened.data.receivedBytes;

  for (const range of chunkRanges(file.size, chunkBytes, received)) {
    const sent = await api.PUT('/api/v1/files/uploads/{id}', {
      params: { path: { id: session }, query: { offset: range.start } },
      body: file.slice(range.start, range.end) as never,
      bodySerializer: (b: unknown) => b as Blob,
      headers: { 'Content-Type': 'application/octet-stream' },
      signal: options.signal,
    });

    if (sent.error || !sent.data) {
      // A conflict means the server's idea of where the file ends is not ours — a lost
      // response, or a second attempt racing this one. Asking it and continuing from its
      // answer is cheaper than starting over, which is the point of the whole mechanism.
      if (sent.response.status === 409) {
        const status = await api.GET('/api/v1/files/uploads/{id}', {
          params: { path: { id: session } },
          signal: options.signal,
        });
        if (status.data && status.data.receivedBytes > received) {
          received = status.data.receivedBytes;
          return finishResumable(file, session, received, chunkBytes, options);
        }
      }

      throw new ApiError(sent.response.status, codeOf(sent.error));
    }

    received = sent.data.receivedBytes;
    options.onProgress?.(received / file.size);
  }

  return completeResumable(session, options);
}

/** Sends whatever is left after the server told us where it had actually got to. */
async function finishResumable(
  file: File,
  session: string,
  received: number,
  chunkBytes: number,
  options: UploadOptions,
): Promise<UploadResult> {
  let sentSoFar = received;
  for (const range of chunkRanges(file.size, chunkBytes, sentSoFar)) {
    const sent = await api.PUT('/api/v1/files/uploads/{id}', {
      params: { path: { id: session }, query: { offset: range.start } },
      body: file.slice(range.start, range.end) as never,
      bodySerializer: (b: unknown) => b as Blob,
      headers: { 'Content-Type': 'application/octet-stream' },
      signal: options.signal,
    });

    if (sent.error || !sent.data) {
      throw new ApiError(sent.response.status, codeOf(sent.error));
    }

    sentSoFar = sent.data.receivedBytes;
    options.onProgress?.(sentSoFar / file.size);
  }

  return completeResumable(session, options);
}

async function completeResumable(
  session: string,
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
      const proceed = await options.onDuplicate?.(duplicateIdOf(error));
      if (proceed) {
        return completeResumable(session, options, true);
      }

      await api.DELETE('/api/v1/files/uploads/{id}', { params: { path: { id: session } } });
      throw new DuplicateUploadError();
    }

    throw new ApiError(response.status, codeOf(error));
  }

  options.onProgress?.(1);
  return { fileId: data.id, documentId: data.documentId };
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
 * The document the refusal named, pulled out of the detail sentence.
 *
 * The server states it in prose rather than as a field because the sentence is what a person
 * reads; a client that wants to link to it recovers the id from the same string rather than
 * the response carrying it twice.
 */
function duplicateIdOf(error: unknown): string | undefined {
  const detail =
    typeof error === 'object' && error !== null && 'detail' in error
      ? (error as { detail?: string }).detail
      : undefined;
  return detail?.match(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i)?.[0];
}
