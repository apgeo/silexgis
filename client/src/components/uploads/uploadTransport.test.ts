// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const api = vi.hoisted(() => ({
  POST: vi.fn(),
  PUT: vi.fn(),
  GET: vi.fn(),
  DELETE: vi.fn(),
}));

vi.mock('../../api/client.ts', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/client.ts')>()),
  api,
}));

import { ApiError } from '../../api/client.ts';
import { abandonSession, DuplicateUploadError, uploadOne } from './uploadTransport.ts';

const DocumentId = '11111111-2222-3333-4444-555555555555';
const SessionId = 'session-1';

function answer<T>(status: number, data: T) {
  return { data, error: undefined, response: { status } };
}

function refusal(status: number, body: Record<string, unknown> = {}) {
  return { data: undefined, error: body, response: { status } };
}

/** A file of `size` bytes whose content does not matter, only its length. */
function fileOf(size: number, name = 'scan.bin'): File {
  return new File([new Uint8Array(size)], name);
}

const limits = { chunkBytes: 10, resumableThresholdBytes: 20 };

/**
 * The server side of one resumable session, close enough to the real rules to show what the
 * transport sends: a piece wholly inside what is stored is a harmless replay, one that does not
 * start where the file ends is a conflict naming the offset to send from.
 */
class FakeSession {
  received = 0;
  completed = 0;
  readonly offsets: number[] = [];
  /** Lets a test make one particular PUT fail before the fake answers it. */
  failNextPut: ((offset: number) => unknown) | undefined;

  constructor() {
    api.POST.mockImplementation(async (path: string, init: { params?: { query?: Record<string, unknown> } }) => {
      if (path === '/api/v1/files/uploads') {
        return answer(201, { id: SessionId, chunkBytes: 10, receivedBytes: 0 });
      }
      if (path === '/api/v1/files/uploads/{id}/complete') {
        this.completed += 1;
        void init;
        return answer(201, { id: 'file-1', documentId: DocumentId });
      }
      throw new Error(`unexpected POST ${path}`);
    });
    api.PUT.mockImplementation(
      async (_path: string, init: { params: { query: { offset: number } }; body: Blob }) => {
        const offset = init.params.query.offset;
        this.offsets.push(offset);
        const failure = this.failNextPut;
        if (failure) {
          this.failNextPut = undefined;
          const failed = failure(offset);
          if (failed !== undefined) {
            return failed;
          }
        }
        if (offset + init.body.size <= this.received) {
          return answer(200, { id: SessionId, receivedBytes: this.received });
        }
        if (offset !== this.received) {
          return refusal(409, { code: 'upload.chunk_out_of_order', expectedOffset: this.received });
        }
        this.received = offset + init.body.size;
        return answer(200, { id: SessionId, receivedBytes: this.received });
      },
    );
    api.GET.mockImplementation(async () =>
      answer(200, { id: SessionId, chunkBytes: 10, receivedBytes: this.received }),
    );
    api.DELETE.mockResolvedValue(answer(204, undefined));
  }
}

beforeEach(() => {
  api.POST.mockReset();
  api.PUT.mockReset();
  api.GET.mockReset();
  api.DELETE.mockReset();
});

afterEach(() => {
  vi.useRealTimers();
});

describe('choosing how to send', () => {
  it('sends a file at or under the threshold in one request', async () => {
    api.POST.mockResolvedValue(answer(201, { id: 'file-1', documentId: DocumentId }));

    const result = await uploadOne(fileOf(20), { cabinetId: 'c1' }, limits);

    expect(result).toEqual({ fileId: 'file-1', documentId: DocumentId });
    expect(api.POST).toHaveBeenCalledTimes(1);
    expect(api.POST.mock.calls[0]![0]).toBe('/api/v1/files');
    expect(api.POST.mock.calls[0]![1].params.query).toMatchObject({ cabinetId: 'c1', allowDuplicate: false });
  });

  it('sends a larger file a piece at a time and completes it', async () => {
    const server = new FakeSession();
    const progress: number[] = [];

    const result = await uploadOne(fileOf(25), {}, limits, { onProgress: (f) => progress.push(f) });

    expect(result.documentId).toBe(DocumentId);
    expect(server.offsets).toEqual([0, 10, 20]);
    expect(server.completed).toBe(1);
    expect(progress.at(-1)).toBe(1);
  });
});

describe('a piece that goes wrong', () => {
  it('continues from the offset a conflict names instead of giving up', async () => {
    const server = new FakeSession();
    // Another attempt at the same session got further while this piece was on its way.
    server.failNextPut = (offset) => {
      if (offset === 0) {
        server.failNextPut = (next) => {
          if (next === 10) {
            server.received = 15;
          }
          return undefined;
        };
      }
      return undefined;
    };

    const result = await uploadOne(fileOf(30), {}, limits);

    expect(result.documentId).toBe(DocumentId);
    expect(server.offsets).toEqual([0, 10, 15, 25]);
    expect(server.received).toBe(30);
  });

  it('retries a piece the connection dropped, after a pause, and does not start over', async () => {
    vi.useFakeTimers();
    const server = new FakeSession();
    server.failNextPut = () => {
      server.failNextPut = (offset) => {
        if (offset === 10) {
          throw new TypeError('Failed to fetch');
        }
        return undefined;
      };
      return undefined;
    };

    const pending = uploadOne(fileOf(30), {}, limits);
    await vi.runAllTimersAsync();
    const result = await pending;

    expect(result.documentId).toBe(DocumentId);
    expect(server.offsets).toEqual([0, 10, 10, 20]);
    expect(api.POST.mock.calls.filter(([path]) => path === '/api/v1/files/uploads')).toHaveLength(1);
  });

  it('gives up on a connection that stays down, keeping the session for a later retry', async () => {
    vi.useFakeTimers();
    new FakeSession();
    api.PUT.mockRejectedValue(new TypeError('Failed to fetch'));
    const sessions: (string | undefined)[] = [];

    const pending = uploadOne(fileOf(30), {}, limits, { onSession: (id) => sessions.push(id) });
    const outcome = expect(pending).rejects.toBeInstanceOf(TypeError);
    await vi.runAllTimersAsync();
    await outcome;

    expect(sessions).toEqual([SessionId]);
    expect(api.PUT.mock.calls.length).toBeGreaterThan(1);
    expect(api.PUT.mock.calls.length).toBeLessThan(6);
  });

  it('does not resend a piece the server could not write, and says so with its status', async () => {
    const server = new FakeSession();
    server.failNextPut = () => refusal(500, { title: 'An error occurred' });

    const failure = await uploadOne(fileOf(30), {}, limits).catch((e: unknown) => e);

    expect(failure).toBeInstanceOf(ApiError);
    expect((failure as ApiError).status).toBe(500);
    expect(server.offsets).toEqual([0]);
  });

  it('carries the refusal code of a piece sent without its length', async () => {
    const server = new FakeSession();
    server.failNextPut = () => refusal(411, { code: 'upload.length_required' });

    const failure = await uploadOne(fileOf(30), {}, limits).catch((e: unknown) => e);

    expect((failure as ApiError).status).toBe(411);
    expect((failure as ApiError).code).toBe('upload.length_required');
  });

  it('stops at once when the person cancels, sending nothing more', async () => {
    const server = new FakeSession();
    const controller = new AbortController();
    server.failNextPut = () => {
      controller.abort();
      throw new DOMException('The operation was aborted.', 'AbortError');
    };

    await expect(uploadOne(fileOf(30), {}, limits, { signal: controller.signal })).rejects.toThrow();

    expect(server.offsets).toEqual([0]);
    expect(server.completed).toBe(0);
  });
});

describe('resuming a session kept from an earlier attempt', () => {
  it('asks the server where the file ends and sends only the rest', async () => {
    const server = new FakeSession();
    server.received = 20;

    const result = await uploadOne(fileOf(30), {}, limits, { resumeSession: SessionId });

    expect(result.documentId).toBe(DocumentId);
    expect(api.POST.mock.calls.filter(([path]) => path === '/api/v1/files/uploads')).toHaveLength(0);
    expect(server.offsets).toEqual([20]);
  });

  it('opens a fresh session when the kept one has expired', async () => {
    const server = new FakeSession();
    api.GET.mockResolvedValue(refusal(404, { code: 'upload.session_not_found' }));
    const sessions: (string | undefined)[] = [];

    await uploadOne(fileOf(30), {}, limits, {
      resumeSession: 'expired',
      onSession: (id) => sessions.push(id),
    });

    expect(server.offsets).toEqual([0, 10, 20]);
    expect(sessions).toEqual([undefined, SessionId, undefined]);
  });

  it('reports the session gone once it has been completed, so a retry does not reuse it', async () => {
    new FakeSession();
    const sessions: (string | undefined)[] = [];

    await uploadOne(fileOf(30), {}, limits, { onSession: (id) => sessions.push(id) });

    expect(sessions).toEqual([SessionId, undefined]);
  });

  it('names a completion whose session has already ended rather than calling it a plain failure', async () => {
    new FakeSession();
    api.POST.mockImplementation(async (path: string) =>
      path === '/api/v1/files/uploads'
        ? answer(201, { id: SessionId, chunkBytes: 10, receivedBytes: 0 })
        : refusal(404, { code: 'upload.session_not_found' }),
    );

    const failure = await uploadOne(fileOf(30), {}, limits).catch((e: unknown) => e);

    expect((failure as ApiError).code).toBe('upload.session_not_found');
  });
});

describe('content that is already stored', () => {
  const duplicate = refusal(409, {
    code: 'file.duplicate',
    detail: 'This content is already stored.',
    duplicateOfDocumentId: DocumentId,
  });

  it('names the stored document from the refusal member, not from its sentence', async () => {
    api.POST.mockResolvedValueOnce(duplicate);
    const asked: (string | undefined)[] = [];

    await expect(
      uploadOne(fileOf(5), {}, limits, {
        onDuplicate: async (id) => {
          asked.push(id);
          return false;
        },
      }),
    ).rejects.toBeInstanceOf(DuplicateUploadError);

    expect(asked).toEqual([DocumentId]);
    expect(api.POST).toHaveBeenCalledTimes(1);
  });

  it('re-sends a single upload with the duplicate accepted when the answer is yes, asking once', async () => {
    api.POST.mockResolvedValueOnce(duplicate).mockResolvedValueOnce(
      answer(201, { id: 'file-2', documentId: 'doc-2' }),
    );
    const onDuplicate = vi.fn().mockResolvedValue(true);

    const result = await uploadOne(fileOf(5), {}, limits, { onDuplicate });

    expect(result.documentId).toBe('doc-2');
    expect(onDuplicate).toHaveBeenCalledTimes(1);
    expect(api.POST.mock.calls[1]![1].params.query.allowDuplicate).toBe(true);
  });

  it('completes a resumable upload again with the duplicate accepted, without resending bytes', async () => {
    const server = new FakeSession();
    let completions = 0;
    const original = api.POST.getMockImplementation()!;
    api.POST.mockImplementation(async (path: string, init: { params?: { query?: Record<string, unknown> } }) => {
      if (path === '/api/v1/files/uploads/{id}/complete' && completions++ === 0) {
        return duplicate;
      }
      return original(path, init);
    });

    const result = await uploadOne(fileOf(30), {}, limits, { onDuplicate: async () => true });

    expect(result.documentId).toBe(DocumentId);
    expect(server.offsets).toEqual([0, 10, 20]);
    expect(api.POST.mock.calls.at(-1)![1].params.query.allowDuplicate).toBe(true);
  });

  it('abandons the session when a resumable duplicate is declined', async () => {
    new FakeSession();
    const original = api.POST.getMockImplementation()!;
    api.POST.mockImplementation(async (path: string, init: { params?: { query?: Record<string, unknown> } }) =>
      path === '/api/v1/files/uploads/{id}/complete' ? duplicate : original(path, init),
    );
    const sessions: (string | undefined)[] = [];

    await expect(
      uploadOne(fileOf(30), {}, limits, {
        onDuplicate: async () => false,
        onSession: (id) => sessions.push(id),
      }),
    ).rejects.toBeInstanceOf(DuplicateUploadError);

    expect(api.DELETE).toHaveBeenCalledWith('/api/v1/files/uploads/{id}', {
      params: { path: { id: SessionId } },
    });
    expect(sessions).toEqual([SessionId, undefined]);
  });
});

describe('refusals keep what the server said', () => {
  it('keeps the members of a refusal so a screen can word it', async () => {
    api.POST.mockResolvedValue(
      refusal(409, { code: 'file.quota_exceeded', detail: 'There is not enough room left.', limit: 'group' }),
    );

    const failure = (await uploadOne(fileOf(5), {}, limits).catch((e: unknown) => e)) as ApiError;

    expect(failure.code).toBe('file.quota_exceeded');
    expect(failure.detail).toBe('There is not enough room left.');
    expect(failure.member('limit')).toBe('group');
  });
});

describe('abandoning a kept session', () => {
  it('asks the server to drop it and swallows a failure nobody can act on', async () => {
    api.DELETE.mockRejectedValueOnce(new TypeError('Failed to fetch'));

    await expect(abandonSession(SessionId)).resolves.toBeUndefined();

    expect(api.DELETE).toHaveBeenCalledWith('/api/v1/files/uploads/{id}', {
      params: { path: { id: SessionId } },
    });
  });
});

describe('cancelling during a pause between retries', () => {
  it('stops waiting at once', async () => {
    vi.useFakeTimers();
    new FakeSession();
    api.PUT.mockRejectedValue(new TypeError('Failed to fetch'));
    const controller = new AbortController();

    const pending = uploadOne(fileOf(30), {}, limits, { signal: controller.signal });
    const outcome = expect(pending).rejects.toBeDefined();
    await vi.advanceTimersByTimeAsync(100);
    controller.abort();
    await outcome;

    expect(api.PUT).toHaveBeenCalledTimes(1);
  });
});
