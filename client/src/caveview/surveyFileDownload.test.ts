// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  downloadSurveyFile,
  forgetSurveyFiles,
  SURVEY_FILE_STALL_MS,
  SurveyFileStalledError,
  type SurveyFileProgress,
} from './surveyFileDownload.ts';

/** A body the test feeds a chunk at a time, and the signal the download was asked with. */
function controlledFetch() {
  const asked: { url: string; signal: AbortSignal | undefined }[] = [];
  const bodies: ReadableStreamDefaultController<Uint8Array>[] = [];
  const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
    asked.push({ url, signal: init?.signal ?? undefined });
    const body = new ReadableStream<Uint8Array>({ start: (controller) => void bodies.push(controller) });
    init?.signal?.addEventListener('abort', () => bodies.at(-1)?.error(init.signal!.reason));
    return new Response(body, { headers: { 'content-length': '6' } });
  });
  vi.stubGlobal('fetch', fetchMock);
  return { fetchMock, asked, bodies };
}

const bytes = (text: string) => new TextEncoder().encode(text);
const flush = () => new Promise((resolve) => setTimeout(resolve, 0));

beforeEach(() => forgetSurveyFiles());
afterEach(() => {
  forgetSurveyFiles();
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe('downloadSurveyFile', () => {
  it('reads a file once for every viewer that asks for it, whatever the signature, and keeps it', async () => {
    const { fetchMock, bodies } = controlledFetch();
    const first = downloadSurveyFile('/api/files/f1/content?token=a');
    const second = downloadSurveyFile('/api/files/f1/content?token=b');
    await flush();
    bodies[0].enqueue(bytes('survey'));
    bodies[0].close();
    expect(await (await first).text()).toBe('survey');
    expect(await (await second).text()).toBe('survey');
    // Asked again once it has arrived: the same bytes, not another read.
    expect(await (await downloadSurveyFile('/api/files/f1/content?token=c')).text()).toBe('survey');
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('says how much has arrived, to a caller that joins half-way as well', async () => {
    const { bodies } = controlledFetch();
    const seen: SurveyFileProgress[] = [];
    const first = downloadSurveyFile('/api/files/f1/content', { onProgress: (progress) => seen.push(progress) });
    await flush();
    bodies[0].enqueue(bytes('sur'));
    await flush();
    const late: SurveyFileProgress[] = [];
    const second = downloadSurveyFile('/api/files/f1/content', { onProgress: (progress) => late.push(progress) });
    bodies[0].enqueue(bytes('vey'));
    bodies[0].close();
    await Promise.all([first, second]);
    expect(seen.at(-1)).toEqual({ received: 6, total: 6 });
    expect(late[0]).toEqual({ received: 3, total: 6 });
    expect(late.at(-1)).toEqual({ received: 6, total: 6 });
  });

  it('goes on for the viewers still waiting when one stops, and is called off when none is left', async () => {
    const { asked, bodies } = controlledFetch();
    const leaving = new AbortController();
    const staying = new AbortController();
    const left = downloadSurveyFile('/api/files/f1/content', { signal: leaving.signal });
    const stayed = downloadSurveyFile('/api/files/f1/content', { signal: staying.signal });
    await flush();
    leaving.abort();
    await expect(left).rejects.toBeDefined();
    expect(asked[0].signal!.aborted).toBe(false);
    staying.abort();
    await expect(stayed).rejects.toBeDefined();
    expect(asked[0].signal!.aborted).toBe(true);
    // Nothing of a download called off is kept: asked again, the file is read again.
    const again = downloadSurveyFile('/api/files/f1/content');
    await flush();
    expect(asked).toHaveLength(2);
    bodies[1].enqueue(bytes('survey'));
    bodies[1].close();
    expect(await (await again).text()).toBe('survey');
  });

  it('reads a file again after a failed read', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response('', { status: 503 })));
    await expect(downloadSurveyFile('/api/files/f1/content')).rejects.toThrow('503');
    vi.stubGlobal('fetch', vi.fn(async () => new Response('survey')));
    expect(await (await downloadSurveyFile('/api/files/f1/content')).text()).toBe('survey');
  });

  it('gives up a download that stops arriving, and says that is what happened', async () => {
    vi.useFakeTimers();
    const { asked, bodies } = controlledFetch();
    const waiting = downloadSurveyFile('/api/files/f1/content');
    const failed = expect(waiting).rejects.toBeInstanceOf(SurveyFileStalledError);
    await vi.advanceTimersByTimeAsync(SURVEY_FILE_STALL_MS - 1000);
    // A chunk arriving starts the wait again.
    bodies[0].enqueue(bytes('sur'));
    await vi.advanceTimersByTimeAsync(SURVEY_FILE_STALL_MS - 1000);
    expect(asked[0].signal!.aborted).toBe(false);
    await vi.advanceTimersByTimeAsync(2000);
    await failed;
    expect(asked[0].signal!.aborted).toBe(true);
  });

  it('does not set the length of a compressed answer against the file it unpacks to', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response('survey', { headers: { 'content-length': '3', 'content-encoding': 'gzip' } })),
    );
    const seen: SurveyFileProgress[] = [];
    await downloadSurveyFile('/api/files/f1/content', { onProgress: (progress) => seen.push(progress) });
    expect(seen.at(-1)).toEqual({ received: 6, total: null });
  });

  it('keeps the two files last asked for', async () => {
    const fetchMock = vi.fn(async (url: string) => new Response(url));
    vi.stubGlobal('fetch', fetchMock);
    for (const name of ['a', 'b', 'c']) await downloadSurveyFile(`/api/files/${name}/content`);
    await flush();
    await downloadSurveyFile('/api/files/c/content');
    await downloadSurveyFile('/api/files/b/content');
    expect(fetchMock).toHaveBeenCalledTimes(3);
    await downloadSurveyFile('/api/files/a/content');
    expect(fetchMock).toHaveBeenCalledTimes(4);
  });
});
