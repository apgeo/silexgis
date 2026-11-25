// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, render, screen, waitFor } from '@testing-library/react';
import { App } from 'antd';
import { afterEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import type { SurveyModelInfo } from '../../api/hooks.ts';
import DankezCaveViewPane from './DankezCaveViewPane.tsx';
import { afterRejectionsAreReported } from '../../testEventLoop.ts';

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

function show(answer: () => Promise<Response> = async () => new Response(new Uint8Array([1, 2, 3, 4]))) {
  const fetchMock = vi.fn(answer);
  vi.stubGlobal('fetch', fetchMock);
  const view = render(
    <App>
      <DankezCaveViewPane model={MODEL} height="300px" />
    </App>,
  );
  /** The same pane handed another model, or the same one at another address. */
  const showModel = (model: SurveyModelInfo) =>
    view.rerender(
      <App>
        <DankezCaveViewPane model={model} height="300px" />
      </App>,
    );
  const frame = (screen.getByTestId('dankez-caveview-frame') as HTMLIFrameElement).contentWindow!;
  const post = vi.spyOn(frame, 'postMessage').mockImplementation(() => {});
  /** A message as the viewer in the frame would send it. */
  const say = (data: unknown, origin = window.location.origin) =>
    act(() => {
      window.dispatchEvent(new MessageEvent('message', { data, origin, source: frame }));
    });
  return { post, say, fetchMock, showModel, frame };
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
    // The witness is the frame saying the same from this origin: every answer waits on the one
    // file read and is sent in the order it was asked for, so once the genuine one is sent, one
    // for the stranger would have been sent before it.
    say({ type: 'lochviewer:ready', protocol: 1 });
    await waitFor(() => expect(post).toHaveBeenCalled());

    expect(post).toHaveBeenCalledTimes(1);
    expect(post.mock.calls[0][1]).toBe(window.location.origin);
  });

  it('shows the viewer’s own error', () => {
    const { say } = show();

    say({ type: 'lochviewer:error', message: 'Chyba pri načítaní: broken' });

    expect(screen.getByTestId('dankez-caveview-status')).toHaveTextContent('Chyba pri načítaní: broken');
  });

  it('keeps what it shows when the address of the same file is signed again', async () => {
    const { post, say, fetchMock, showModel } = show();
    say({ type: 'lochviewer:ready', protocol: 1 });
    await waitFor(() => expect(post).toHaveBeenCalledTimes(1));
    say({ type: 'lochviewer:loaded', name: 'P8_Master.3d', stations: 393, segments: 389 });
    say({ type: 'lochviewer:station', name: 'p8.12', x: 1, y: 2, z: 3 });

    // Whatever lists the survey signs its address again every few minutes; the file is the same.
    showModel({ ...MODEL, modelUrl: 'http://files.local/model?token=resigned' } as SurveyModelInfo);
    // A read starts as the pane takes the new address, so by now it would have been asked for;
    // the viewer asking again is answered from the read already in hand.
    say({ type: 'lochviewer:ready', protocol: 1 });
    await waitFor(() => expect(post).toHaveBeenCalledTimes(2));

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId('dankez-caveview-status')).toHaveTextContent('Stations loaded: 393');
    expect(screen.getByTestId('dankez-caveview-station')).toHaveTextContent('Station clicked: p8.12');

    // Another file is another read.
    showModel({ ...MODEL, modelUrl: 'http://files.local/other-model?token=x' } as SurveyModelInfo);
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));
    expect(fetchMock).toHaveBeenLastCalledWith('http://files.local/other-model?token=x');
  });

  it('keeps what it shows when the language is switched', async () => {
    const { post, say, fetchMock } = show();
    say({ type: 'lochviewer:ready', protocol: 1 });
    await waitFor(() => expect(post).toHaveBeenCalledTimes(1));
    say({ type: 'lochviewer:loaded', name: 'P8_Master.3d', stations: 393, segments: 389 });

    try {
      await act(async () => {
        await i18n.changeLanguage('ro');
      });
      expect(fetchMock).toHaveBeenCalledTimes(1);
      // Said in the new language, and still the count the viewer reported, not "sending" again.
      expect(screen.getByTestId('dankez-caveview-status')).toHaveTextContent(
        i18n.t('caveview.dankezCaveView.loaded', { count: 393 }),
      );
    } finally {
      await act(async () => {
        await i18n.changeLanguage('en');
      });
    }
  });

  it('sends nothing to another window of this origin that claims to be the viewer', async () => {
    const { post } = show();
    // Another same-origin window or frame — not the pane's frame, nor a window it opened.
    const stranger = document.createElement('iframe');
    document.body.appendChild(stranger);
    const strangerPost = vi.spyOn(stranger.contentWindow!, 'postMessage').mockImplementation(() => {});
    try {
      act(() => {
        window.dispatchEvent(new MessageEvent('message', {
          data: { type: 'lochviewer:ready', protocol: 1 },
          origin: window.location.origin,
          source: stranger.contentWindow,
        }));
      });
      // The pane's own frame saying the same is the witness: answers wait on the one file read
      // and go out in the order they were asked for, so the stranger's would have gone first.
      act(() => {
        window.dispatchEvent(new MessageEvent('message', {
          data: { type: 'lochviewer:ready', protocol: 1 },
          origin: window.location.origin,
          source: screen.getByTestId<HTMLIFrameElement>('dankez-caveview-frame').contentWindow,
        }));
      });
      await waitFor(() => expect(post).toHaveBeenCalled());
      expect(post).toHaveBeenCalledTimes(1);
      expect(strangerPost).not.toHaveBeenCalled();
    } finally {
      stranger.remove();
    }
  });

  it('answers a viewer that is ready before the file has arrived, once it arrives', async () => {
    let arrive: (response: Response) => void = () => {};
    const { post, say } = show(() => new Promise<Response>((resolve) => {
      arrive = resolve;
    }));

    say({ type: 'lochviewer:ready', protocol: 1 });
    // The file has not arrived, so there is nothing yet to send; what matters is that the
    // readiness is not forgotten by the time it does.
    expect(post).not.toHaveBeenCalled();

    await act(async () => {
      arrive(new Response(new Uint8Array([1, 2, 3, 4])));
    });
    await waitFor(() => expect(post).toHaveBeenCalledTimes(1));
  });

  it('sends nothing after a failed read, and says why the read failed', async () => {
    const { post, say } = show(async () => new Response('', { status: 403 }));
    await waitFor(() =>
      expect(screen.getByTestId('dankez-caveview-status')).toHaveTextContent(
        i18n.t('caveview.loadFailed.refused'),
      ),
    );

    say({ type: 'lochviewer:ready', protocol: 1 });
    // The answer waits on a read that has already failed, so all that is left of it is promise
    // callbacks; they have all run once the event loop has had a turn.
    await act(afterRejectionsAreReported);
    expect(post).not.toHaveBeenCalled();
  });

  it('reads the file again from its newest address when asked to try again, and sends it', async () => {
    let refuse = true;
    const { post, say, fetchMock, showModel } = show(async () =>
      refuse ? new Response('', { status: 403 }) : new Response(new Uint8Array([1, 2, 3, 4])),
    );
    const tryAgain = await screen.findByRole('button', { name: i18n.t('caveview.tryAgain') });

    // The address was signed again meanwhile — while the pane was showing the failure — and the
    // file is readable from it.
    refuse = false;
    showModel({ ...MODEL, modelUrl: 'http://files.local/model?token=renewed' } as SurveyModelInfo);
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));
    expect(String((fetchMock.mock.calls[1] as unknown[])[0])).toContain('token=renewed');
    await waitFor(() =>
      expect(screen.getByTestId('dankez-caveview-status')).toHaveTextContent(
        i18n.t('caveview.dankezCaveView.sending'),
      ),
    );
    expect(screen.queryByRole('button', { name: i18n.t('caveview.tryAgain') })).toBeNull();

    say({ type: 'lochviewer:ready', protocol: 1 });
    await waitFor(() => expect(post).toHaveBeenCalledTimes(1));
    expect(tryAgain).not.toBeInTheDocument();
  });

  it('offers to try again after a dropped connection, and reads again when pressed', async () => {
    let drop = true;
    const { post, say, fetchMock } = show(async () => {
      if (drop) {
        throw new TypeError('Failed to fetch');
      }
      return new Response(new Uint8Array([1, 2, 3, 4]));
    });
    const tryAgain = await screen.findByRole('button', { name: i18n.t('caveview.tryAgain') });
    expect(screen.getByTestId('dankez-caveview-status')).toHaveTextContent(i18n.t('caveview.loadFailed.network'));

    drop = false;
    act(() => tryAgain.click());
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));
    say({ type: 'lochviewer:ready', protocol: 1 });
    await waitFor(() => expect(post).toHaveBeenCalledTimes(1));
  });
});
