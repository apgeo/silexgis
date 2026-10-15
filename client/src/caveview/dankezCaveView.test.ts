// SPDX-License-Identifier: AGPL-3.0-or-later
import { existsSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it, vi } from 'vitest';
import { DANKEZ_CAVEVIEW_PAGE, readViewerEvent, sendModel } from './dankezCaveView.ts';

const publicDir = join(dirname(fileURLToPath(import.meta.url)), '..', '..', 'public');
const ORIGIN = 'https://gis.example';

function from(source: unknown, data: unknown, origin = ORIGIN) {
  return new MessageEvent('message', { data, origin, source: source as MessageEventSource });
}

describe('dankez-caveview messages', () => {
  const viewer = { postMessage: vi.fn() } as unknown as Window;

  it('believes a message only from the viewer window and the viewer origin', () => {
    const loaded = { type: 'lochviewer:loaded', name: 'a.3d', stations: 3, segments: 2 };

    expect(readViewerEvent(from(viewer, loaded), viewer, ORIGIN)).toEqual(loaded);
    expect(readViewerEvent(from({}, loaded), viewer, ORIGIN)).toBeNull();
    expect(readViewerEvent(from(viewer, loaded, 'https://elsewhere.example'), viewer, ORIGIN)).toBeNull();
    expect(readViewerEvent(from(viewer, loaded), null, ORIGIN)).toBeNull();
  });

  it('refuses a message that is not the shape its type promises', () => {
    expect(readViewerEvent(from(viewer, { type: 'lochviewer:station', name: 7 }), viewer, ORIGIN)).toBeNull();
    expect(readViewerEvent(from(viewer, { type: 'something:else' }), viewer, ORIGIN)).toBeNull();
    expect(readViewerEvent(from(viewer, 'lochviewer:ready'), viewer, ORIGIN)).toBeNull();
  });

  it('transfers a copy of the bytes, so the next viewer can be sent them too', () => {
    const post = vi.fn();
    const bytes = new Uint8Array([1, 2, 3]).buffer;

    sendModel({ postMessage: post } as unknown as Window, ORIGIN, 'Cave.lox', bytes);

    const [message, origin, transfer] = post.mock.calls[0];
    expect(origin).toBe(ORIGIN);
    expect(message).toMatchObject({ type: 'lochviewer:open', name: 'Cave.lox' });
    expect(transfer).toEqual([message.data]);
    expect(message.data).not.toBe(bytes);
    expect(bytes.byteLength).toBe(3);
  });

  it('points at a page that is actually vendored', () => {
    expect(existsSync(join(publicDir, DANKEZ_CAVEVIEW_PAGE)), `${DANKEZ_CAVEVIEW_PAGE} is vendored`).toBe(true);
  });
});
