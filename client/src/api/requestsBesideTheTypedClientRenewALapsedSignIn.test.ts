// SPDX-License-Identifier: AGPL-3.0-or-later
import type { User } from 'oidc-client-ts';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

// Every fetch leaves through this one stand-in for the network, put in place before any module
// that might keep the `fetch` it found at load.
const network = vi.hoisted(() => {
  const state = {
    answer: (_request: Request): Promise<Response> => Promise.reject(new Error('no answer set')),
  };
  globalThis.fetch = ((input: RequestInfo | URL, init?: RequestInit) =>
    state.answer(
      input instanceof Request
        ? input
        : new Request(new URL(String(input), globalThis.location.origin), init),
    )) as typeof fetch;
  return state;
});

vi.mock('../auth/renewSignIn.ts', () => ({
  renewSignIn: vi.fn(),
  lastSignInRenewal: vi.fn(() => null),
  signInAgain: vi.fn(() => Promise.resolve()),
  onSignInRenewed: vi.fn(() => () => {}),
}));

import { userManager } from '../auth/auth.tsx';
import { renewSignIn } from '../auth/renewSignIn.ts';
import { useSignInLapsed } from '../auth/signInLapsed.ts';
import { makeCrsLookup } from '../caveview/loadCaveView.ts';
import { installationDefinition } from '../map/projections.ts';
import { ApiError } from './client.ts';
import { DownloadError, downloadFile, downloadFilePost } from './download.ts';
import { uploadWithProgress } from './uploadWithProgress.ts';

/**
 * <b>Requests the typed client cannot make are renewed past a lapsed sign-in all the same.</b>
 * A file download, an upload that reports its progress and a coordinate-system lookup each build
 * their own request, and so each once skipped the transport's renewal: a tab that slept past its
 * token's life had every such request refused as coming from nobody, while the screens around them
 * recovered. These pin that each goes through the same single rule — renewed once and asked again
 * with the new bearer; a sign-in that cannot be renewed is announced and the refusal handed back.
 */
describe('a request beside the typed client, refused because the sign-in ran out', () => {
  let token: string | null;
  const sent: Request[] = [];
  let answers: Response[];

  beforeEach(() => {
    token = 'expired-token';
    sent.length = 0;
    answers = [];
    vi.spyOn(userManager, 'getUser').mockImplementation(
      async () => (token ? ({ access_token: token } as User) : null),
    );
    network.answer = async (request: Request) => {
      sent.push(request);
      return answers.shift() ?? new Response(null, { status: 500 });
    };
    URL.createObjectURL = vi.fn(() => 'blob:file');
    URL.revokeObjectURL = vi.fn();
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.mocked(renewSignIn).mockReset();
    useSignInLapsed.setState({ lapsed: false });
  });

  const refused = () =>
    new Response(JSON.stringify({ status: 401 }), {
      status: 401,
      headers: { 'Content-Type': 'application/problem+json' },
    });
  const renews = () =>
    vi.mocked(renewSignIn).mockImplementation(async () => {
      token = 'renewed-token';
      return 'renewed';
    });

  it('downloads a file after renewing the sign-in once', async () => {
    renews();
    answers = [refused(), new Response('bytes', { status: 200 })];

    await downloadFile('/api/v1/caves/export.csv');

    expect(renewSignIn).toHaveBeenCalledTimes(1);
    expect(sent.map((r) => r.headers.get('Authorization'))).toEqual([
      'Bearer expired-token',
      'Bearer renewed-token',
    ]);
    expect(URL.createObjectURL).toHaveBeenCalledTimes(1);
  });

  it('asks an export again with the body it was first sent with', async () => {
    renews();
    answers = [refused(), new Response('bytes', { status: 200 })];

    await downloadFilePost('/api/v1/caves/export', { decisions: [{ caveId: 'c-1', include: true }] });

    expect(sent).toHaveLength(2);
    expect(sent[1].method).toBe('POST');
    expect(await sent[1].json()).toEqual({ decisions: [{ caveId: 'c-1', include: true }] });
  });

  it('announces a sign-in that cannot be renewed, and hands the download its refusal', async () => {
    vi.mocked(renewSignIn).mockResolvedValue('lapsed');
    answers = [refused()];

    const failure = await downloadFile('/api/v1/caves/export.csv').catch((error: unknown) => error);

    expect(failure).toBeInstanceOf(DownloadError);
    expect(failure).toEqual(expect.objectContaining({ status: 401 }));
    expect(sent).toHaveLength(1);
    expect(useSignInLapsed.getState().lapsed).toBe(true);
  });

  it('reads a coordinate system for the map after renewing the sign-in once', async () => {
    renews();
    answers = [refused(), new Response('+proj=sterea', { status: 200 })];

    expect(await installationDefinition('EPSG:3844')).toBe('+proj=sterea');
    expect(sent[1].headers.get('Authorization')).toBe('Bearer renewed-token');
  });

  it('reads a coordinate system for the survey viewer after renewing the sign-in once', async () => {
    renews();
    answers = [refused(), new Response('+proj=sterea', { status: 200 })];

    expect(await makeCrsLookup()('3844')).toBe('+proj=sterea');
    expect(sent[1].headers.get('Authorization')).toBe('Bearer renewed-token');
  });

  it('leaves a request with no bearer alone', async () => {
    token = null;
    answers = [refused()];

    expect(await installationDefinition('EPSG:3844')).toBeNull();
    expect(renewSignIn).not.toHaveBeenCalled();
    expect(sent).toHaveLength(1);
  });

  describe('an upload that reports its progress', () => {
    /** A browser request answered from a list, in the order they are sent. */
    class FakeRequest {
      static made: FakeRequest[] = [];
      static statuses: number[] = [];
      headers: Record<string, string> = {};
      status = 0;
      responseText = '';
      upload: { onprogress?: unknown } = {};
      onload?: () => void;
      onerror?: () => void;
      ontimeout?: () => void;
      onabort?: () => void;
      sent?: unknown;

      constructor() {
        FakeRequest.made.push(this);
      }
      open() {}
      setRequestHeader(name: string, value: string) {
        this.headers[name] = value;
      }
      getResponseHeader() {
        return null;
      }
      abort() {
        this.onabort?.();
      }
      send(body: unknown) {
        this.sent = body;
        const status = FakeRequest.statuses.shift() ?? 500;
        queueMicrotask(() => {
          this.status = status;
          this.responseText = status === 201 ? JSON.stringify({ id: 'g-1' }) : JSON.stringify({ status });
          this.onload?.();
        });
      }
    }

    beforeEach(() => {
      FakeRequest.made = [];
      vi.stubGlobal('XMLHttpRequest', FakeRequest);
    });

    afterEach(() => {
      vi.unstubAllGlobals();
    });

    it('sends the same form again with the renewed bearer', async () => {
      renews();
      FakeRequest.statuses = [401, 201];
      const form = new FormData();

      const created = await uploadWithProgress<{ id: string }>('/api/v1/geofiles', form);

      expect(created).toEqual({ id: 'g-1' });
      expect(renewSignIn).toHaveBeenCalledTimes(1);
      expect(FakeRequest.made.map((r) => r.headers.Authorization)).toEqual([
        'Bearer expired-token',
        'Bearer renewed-token',
      ]);
      expect(FakeRequest.made[1].sent).toBe(form);
    });

    it('announces a sign-in that cannot be renewed, and rejects with the refusal', async () => {
      vi.mocked(renewSignIn).mockResolvedValue('lapsed');
      FakeRequest.statuses = [401];

      const upload = uploadWithProgress('/api/v1/geofiles', new FormData());

      await expect(upload).rejects.toBeInstanceOf(ApiError);
      await expect(upload).rejects.toEqual(expect.objectContaining({ status: 401 }));
      expect(FakeRequest.made).toHaveLength(1);
      expect(useSignInLapsed.getState().lapsed).toBe(true);
    });
  });
});
