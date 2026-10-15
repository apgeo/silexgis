// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, render, screen, waitFor } from '@testing-library/react';
import { App } from 'antd';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { SurveyModelInfo } from '../../api/hooks.ts';
import DankezCaveViewPane from './DankezCaveViewPane.tsx';

const MODEL = {
  id: 'model-1',
  name: 'P8_Master',
  format: 'survex3d',
  modelUrl: 'http://files.local/model?token=secret',
} as unknown as SurveyModelInfo;

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

function show() {
  vi.stubGlobal('fetch', vi.fn(async () => new Response(new Uint8Array([1, 2, 3, 4]))));
  render(
    <App>
      <DankezCaveViewPane model={MODEL} height="300px" />
    </App>,
  );
  const frame = (screen.getByTestId('dankez-caveview-frame') as HTMLIFrameElement).contentWindow!;
  const post = vi.spyOn(frame, 'postMessage').mockImplementation(() => {});
  /** A message as the viewer in the frame would send it. */
  const say = (data: unknown, origin = window.location.origin) =>
    act(() => {
      window.dispatchEvent(new MessageEvent('message', { data, origin, source: frame }));
    });
  return { post, say };
}

describe('DankezCaveViewPane', () => {
  it('sends the file it read to the viewer that says it is ready, and shows what comes back', async () => {
    const { post, say } = show();
    expect(screen.getByTestId('dankez-caveview-frame').getAttribute('src')).not.toContain('secret');

    say({ type: 'lochviewer:ready', protocol: 1 });
    await waitFor(() => expect(post).toHaveBeenCalled());
    const [message, origin] = post.mock.calls[0] as unknown as [{ type: string; name: string; data: ArrayBuffer }, string];
    expect(message.type).toBe('lochviewer:open');
    // The extension is what picks the viewer's parser.
    expect(message.name).toBe('P8_Master.3d');
    expect(message.data.byteLength).toBe(4);
    expect(origin).toBe(window.location.origin);

    say({ type: 'lochviewer:loaded', name: 'P8_Master.3d', stations: 393, segments: 389 });
    expect(screen.getByTestId('dankez-caveview-status')).toHaveTextContent('Stations loaded: 393');

    say({ type: 'lochviewer:station', name: 'p8.12', x: 1, y: 2, z: 3 });
    expect(screen.getByTestId('dankez-caveview-station')).toHaveTextContent('Station clicked: p8.12');
  });

  it('sends nothing to a page of another origin speaking from the frame', async () => {
    const { post, say } = show();

    say({ type: 'lochviewer:ready', protocol: 1 }, 'https://elsewhere.example');
    // Give the file read every chance to land before concluding nothing was sent.
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 20));
    });

    expect(post).not.toHaveBeenCalled();
  });

  it('shows the viewer’s own error', () => {
    const { say } = show();

    say({ type: 'lochviewer:error', message: 'Chyba pri načítaní: broken' });

    expect(screen.getByTestId('dankez-caveview-status')).toHaveTextContent('Chyba pri načítaní: broken');
  });
});
