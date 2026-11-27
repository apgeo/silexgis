// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { App } from 'antd';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../../i18n';
import type { FlybyRender } from '../../../api/hooks/flybyRenders.ts';
import FlybyServerRender from './FlybyServerRender.tsx';

// Rendering a flyby on the server, as the movie panel shows it: offered only while a render node is
// there, and the reader's latest render followed, stopped and downloaded. The server is a few
// answers written down here; what it does with a render is its own tests' to say.

const state = {
  available: true,
  renders: [] as FlybyRender[],
  cancelled: [] as string[],
  downloaded: [] as string[],
};
vi.mock('../../../api/hooks/flybyRenders.ts', async (original) => ({
  ...(await original<typeof import('../../../api/hooks/flybyRenders.ts')>()),
  useFlybyRenderAvailability: () => ({ data: { available: state.available } }),
  useFlybyRenders: () => ({ data: state.renders }),
  useCancelFlybyRender: () => ({ mutate: (id: string) => state.cancelled.push(id), isPending: false }),
}));
vi.mock('../../../api/download.ts', async (original) => ({
  ...(await original<typeof import('../../../api/download.ts')>()),
  downloadFile: async (url: string) => {
    state.downloaded.push(url);
  },
}));

function aRender(extra: Partial<FlybyRender> = {}): FlybyRender {
  return {
    id: 'r1',
    name: 'An invented flyby',
    plan: 'draft',
    container: 'mp4',
    status: 'queued',
    attempts: 0,
    phase: null,
    message: null,
    done: null,
    total: null,
    secondsLeft: null,
    error: null,
    reason: null,
    cancelRequested: false,
    device: null,
    renderSeconds: null,
    files: [],
    createdAt: '2026-11-26T10:00:00Z',
    completedAt: null,
    expiresAt: null,
    ...extra,
  };
}

function show(onStart = vi.fn(), canStart = true) {
  render(
    <App>
      <FlybyServerRender canStart={canStart} starting={false} onStart={onStart} />
    </App>,
  );
  return onStart;
}

beforeEach(() => {
  state.available = true;
  state.renders = [];
  state.cancelled = [];
  state.downloaded = [];
});

afterEach(() => {
  cleanup();
});

describe('rendering a flyby on the server', () => {
  it('is not offered while no render node has called', () => {
    state.available = false;
    show();
    expect(screen.queryByTestId('flyby-render-server')).not.toBeInTheDocument();
  });

  it('asks for the plan and the container chosen, a draft in mp4 unless changed', () => {
    const onStart = show();
    fireEvent.click(screen.getByTestId('flyby-render-start'));
    expect(onStart).toHaveBeenLastCalledWith('draft', 'mp4');

    fireEvent.click(screen.getByText('Full'));
    fireEvent.click(screen.getByTestId('flyby-render-start'));
    expect(onStart).toHaveBeenLastCalledWith('full', 'mp4');
  });

  it('says a queued render waits for a node, and stops it on asking', () => {
    state.renders = [aRender()];
    show();
    expect(screen.getByTestId('flyby-render-state')).toHaveTextContent('“An invented flyby” is waiting for a render node.');
    // One render at a time: another is not asked for while this one is going.
    expect(screen.getByTestId('flyby-render-start')).toBeDisabled();
    fireEvent.click(screen.getByTestId('flyby-render-cancel'));
    expect(state.cancelled).toEqual(['r1']);
  });

  it('follows a running render by its frames and its time left, and keeps it shown when the node goes quiet', () => {
    state.renders = [aRender({ status: 'running', phase: 'rendering', done: 37, total: 240, secondsLeft: 489 })];
    state.available = false;
    show();
    const running = screen.getByTestId('flyby-render-state');
    expect(running).toHaveAttribute('data-status', 'running');
    expect(running).toHaveTextContent('Rendering · frame 37 of 240 · about 8:09 left');
    expect(screen.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '15');
    // Nothing new is offered without a node, but the running render can still be stopped.
    expect(screen.queryByTestId('flyby-render-start')).not.toBeInTheDocument();
    expect(screen.getByTestId('flyby-render-cancel')).toBeEnabled();
  });

  it('says a stop was asked for until the node hears it', () => {
    state.renders = [aRender({ status: 'running', phase: 'uploading', cancelRequested: true })];
    show();
    expect(screen.getByTestId('flyby-render-cancel')).toHaveTextContent('Stopping…');
    expect(screen.getByTestId('flyby-render-cancel')).toBeDisabled();
    expect(screen.getByTestId('flyby-render-state')).toHaveTextContent('Sending the film back');
  });

  it('offers the film of a finished render first, and its log beside it', () => {
    state.renders = [
      aRender({
        status: 'succeeded',
        files: [
          { name: '12.log', role: 'log', size: 10 },
          { name: '12.mp4', role: 'movie', size: 1000 },
        ],
        expiresAt: '2026-12-10T10:00:00Z',
      }),
    ];
    show();
    expect(screen.getByTestId('flyby-render-state')).toHaveTextContent('“An invented flyby” is rendered.');
    const buttons = screen.getAllByRole('button').filter((b) => b.getAttribute('data-testid')?.startsWith('flyby-render-file-'));
    expect(buttons.map((b) => b.getAttribute('data-testid'))).toEqual(['flyby-render-file-movie', 'flyby-render-file-log']);
    fireEvent.click(screen.getByTestId('flyby-render-file-movie'));
    expect(state.downloaded).toEqual(['/api/v1/flybys/blender-renders/r1/files/12.mp4']);
  });

  it('says before anything is asked for that nothing under location protection goes to a node', () => {
    show();
    expect(screen.getByTestId('flyby-render-protected-notice')).toHaveTextContent(
      'Caves, entrances and features under location protection are never sent to a render node',
    );
  });

  it('keeps a finished film and its download while no render node is calling, and offers no new render', () => {
    state.available = false;
    state.renders = [
      aRender({ status: 'succeeded', files: [{ name: '12.mp4', role: 'movie', size: 1000 }], expiresAt: '2026-12-10T10:00:00Z' }),
    ];
    show();
    expect(screen.getByTestId('flyby-render-file-movie')).toBeInTheDocument();
    expect(screen.getByTestId('flyby-render-state')).toHaveTextContent('The film is kept here until');
    expect(screen.queryByTestId('flyby-render-start')).not.toBeInTheDocument();
    expect(screen.getByTestId('flyby-render-no-node')).toBeInTheDocument();
  });

  it("says why a render failed in its own words, the node's words marked as the node's, and that a stopped one was stopped", () => {
    state.renders = [aRender({ status: 'failed', error: 'scriptError: the player failed', reason: 'scriptError' })];
    show();
    expect(screen.getByTestId('flyby-render-state')).toHaveTextContent('The render failed.');
    expect(screen.getByTestId('flyby-render-reason')).toHaveTextContent('Blender stopped with an error while it rendered.');
    expect(screen.getByTestId('flyby-render-node-said')).toHaveTextContent('The render node said: the player failed');
    cleanup();

    // This server's own reason is said whole, with no words of a node's.
    state.renders = [
      aRender({ status: 'failed', error: 'timeLimit: the render ran past the 24 hours this server allows one render', reason: 'timeLimit' }),
    ];
    show();
    expect(screen.getByTestId('flyby-render-reason')).toHaveTextContent('The render ran longer than this server allows one render.');
    expect(screen.queryByTestId('flyby-render-node-said')).not.toBeInTheDocument();
    cleanup();

    // A node's long words are shortened.
    state.renders = [aRender({ status: 'failed', error: `error: ${'x'.repeat(1000)}`, reason: 'error' })];
    show();
    expect(screen.getByTestId('flyby-render-node-said').textContent!.length).toBeLessThan(400);
    cleanup();

    state.renders = [aRender({ status: 'cancelled', error: 'cancelled: stopped by the person who asked for it' })];
    show();
    expect(screen.getByTestId('flyby-render-state')).toHaveTextContent('The render was stopped.');
  });
});
