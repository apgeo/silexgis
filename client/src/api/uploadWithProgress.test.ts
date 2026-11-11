// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

vi.mock('../auth/auth.tsx', () => ({
  userManager: { getUser: () => Promise.resolve({ access_token: 'token-1' }) },
}));
const { uploadWithProgress } = await import('./uploadWithProgress.ts');
const { ApiError } = await import('./client.ts');

/** A stand-in for the browser's request object that the test answers by hand. */
class FakeRequest {
  static last: FakeRequest;
  headers: Record<string, string> = {};
  method = '';
  url = '';
  status = 0;
  responseText = '';
  responseHeaders: Record<string, string> = {};
  upload: { onprogress?: (e: { lengthComputable: boolean; loaded: number; total: number }) => void } = {};
  onload?: () => void;
  onerror?: () => void;
  ontimeout?: () => void;
  onabort?: () => void;
  sent?: unknown;

  constructor() {
    FakeRequest.last = this;
  }
  open(method: string, url: string) {
    this.method = method;
    this.url = url;
  }
  setRequestHeader(name: string, value: string) {
    this.headers[name] = value;
  }
  getResponseHeader(name: string) {
    return this.responseHeaders[name] ?? null;
  }
  send(body: unknown) {
    this.sent = body;
  }
  abort() {
    this.onabort?.();
  }
  answer(status: number, body: string) {
    this.status = status;
    this.responseText = body;
    this.onload?.();
  }
}

/** Starts an upload and waits until its request has been sent. */
async function started(options: Parameters<typeof uploadWithProgress>[2] = {}) {
  const result = uploadWithProgress<{ id: string }>('/api/v1/geofiles', new FormData(), options);
  await vi.waitFor(() => expect(FakeRequest.last?.sent).toBeDefined());
  return { result, request: FakeRequest.last };
}

beforeEach(() => {
  FakeRequest.last = undefined as unknown as FakeRequest;
  vi.stubGlobal('XMLHttpRequest', FakeRequest);
});
afterEach(() => vi.unstubAllGlobals());

describe('uploadWithProgress', () => {
  it("sends the account's bearer and reports each step of the body going up", async () => {
    const seen: number[] = [];
    const { result, request } = await started({ onProgress: (p) => seen.push(p.loaded) });
    expect(request.method).toBe('POST');
    expect(request.headers.Authorization).toBe('Bearer token-1');

    request.upload.onprogress?.({ lengthComputable: true, loaded: 10, total: 20 });
    request.upload.onprogress?.({ lengthComputable: true, loaded: 20, total: 20 });
    request.answer(201, '{"id":"g-1"}');

    await expect(result).resolves.toEqual({ id: 'g-1' });
    expect(seen).toEqual([10, 20]);
  });

  it("throws a refusal with the problem's code and detail", async () => {
    const { result, request } = await started();
    request.answer(400, '{"code":"geofile.file_empty","detail":"The uploaded file is empty."}');
    const error = await result.catch((e: unknown) => e);
    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ status: 400, code: 'geofile.file_empty', detail: 'The uploaded file is empty.' });
  });

  it("reads a proxy's HTML page as a refusal with no problem", async () => {
    const { result, request } = await started();
    request.answer(413, '<html>Request Entity Too Large</html>');
    await expect(result).rejects.toMatchObject({ status: 413, code: undefined });
  });

  it('throws status 0 when no answer came at all', async () => {
    const { result, request } = await started();
    request.onerror?.();
    await expect(result).rejects.toMatchObject({ status: 0 });
  });

  it('stops when asked to', async () => {
    const controller = new AbortController();
    const { result } = await started({ signal: controller.signal });
    controller.abort();
    await expect(result).rejects.toMatchObject({ name: 'AbortError' });
  });
});
