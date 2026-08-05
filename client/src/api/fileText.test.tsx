// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { renderHook, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { maxInlineTextBytes, useFileText, type FileInfo } from './hooks.ts';

/** Every fetch the hook made, with the part of the file it asked for. */
const asked: (string | undefined)[] = [];

const file: FileInfo = {
  id: 'f9',
  documentId: 'd9',
  originalName: 'season.log',
  mimeType: 'text/plain',
  // A season of survey notes: far more than anything would put on a screen at once.
  sizeBytes: 40 * 1024 * 1024,
  sha256: 'x',
  kind: 'document',
  versionNumber: 1,
  documentDate: null,
  createdAt: '2026-01-01T00:00:00Z',
  contentUrl: '/api/v1/files/f9/content?token=t',
  thumbnailUrl: null,
  mayDownloadOriginal: true,
};

/** A provider with a cache of its own, built once so a re-render does not empty it. */
function wrapper() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
}

/** Answers with the given bytes, recording which part of the file was asked for. */
function serve(bytes: Uint8Array) {
  vi.stubGlobal('fetch', (_url: string, init?: RequestInit) => {
    asked.push((init?.headers as Record<string, string> | undefined)?.Range);
    return Promise.resolve({
      ok: true,
      arrayBuffer: () =>
        Promise.resolve(bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength)),
    } as Response);
  });
}

/** Filler up to a byte count, then the given text — so a character can be placed on the cut. */
function bytes(filler: number, tail: string): Uint8Array {
  const rest = new TextEncoder().encode(tail);
  const all = new Uint8Array(filler + rest.length).fill(0x61); // 'a'
  all.set(rest, filler);
  return all;
}

beforeEach(() => {
  asked.length = 0;
});

afterEach(() => vi.unstubAllGlobals());

describe('useFileText', () => {
  it('asks for only the part it is going to show', async () => {
    // The cut has to be made before the bytes cross the network, not after: fetching forty
    // megabytes over a phone connection in order to display half of one is the whole file spent
    // to show a fragment of it, and the reader is the one paying for it.
    serve(bytes(maxInlineTextBytes, ''));
    const { result } = renderHook(() => useFileText(file), { wrapper: wrapper() });
    await waitFor(() => expect(result.current.data).toBeTruthy());

    expect(asked).toEqual([`bytes=0-${maxInlineTextBytes - 1}`]);
    expect(result.current.data!.text.length).toBe(maxInlineTextBytes);
    // What was cut is a fact about the file, not about the length of the answer — a partial
    // answer is exactly as long as a whole one when the file happens to be that long.
    expect(result.current.data!.truncated).toBe(true);
  });

  it('cuts a server that sent everything anyway, and never splits a character across the cut', async () => {
    // A two-byte character straddling the boundary: the first of its bytes is the last byte
    // kept. Shown rather than dropped it becomes a replacement mark, which in Romanian text is
    // a corruption sitting directly above a notice saying the text was truncated.
    serve(bytes(maxInlineTextBytes - 1, 'șiruri lungi de text'));
    const split = renderHook(() => useFileText(file), { wrapper: wrapper() });
    await waitFor(() => expect(split.result.current.data).toBeTruthy());

    expect(split.result.current.data!.text).not.toContain('�');
    expect(split.result.current.data!.text.length).toBe(maxInlineTextBytes - 1);

    // And the other half of the same rule: a character that fits inside the cut is kept whole,
    // so this is dropping an incomplete character rather than trimming the tail for its own sake.
    serve(bytes(maxInlineTextBytes - 2, 'ș'));
    const whole = renderHook(() => useFileText({ ...file, id: 'f10' }), { wrapper: wrapper() });
    await waitFor(() => expect(whole.result.current.data).toBeTruthy());

    expect(whole.result.current.data!.text.endsWith('ș')).toBe(true);
  });

  it('does not claim a file was truncated when the whole of it is on the screen', async () => {
    serve(bytes(12, 'notes'));
    const { result } = renderHook(
      () => useFileText({ ...file, id: 'f11', sizeBytes: 17 }),
      { wrapper: wrapper() },
    );
    await waitFor(() => expect(result.current.data).toBeTruthy());

    expect(result.current.data!.text).toBe('aaaaaaaaaaaanotes');
    expect(result.current.data!.truncated).toBe(false);
  });
});
