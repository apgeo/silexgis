// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import type { CaveViewer } from '../../caveview/loadCaveView.ts';
import type { CaveViewerFailure } from '../../caveview/viewerLifecycle.ts';

/**
 * The second viewer of a comparison when the other survey cannot be drawn. The viewer itself is a
 * three.js bundle with a drawing context; what matters here is what the host does with the answer
 * of its start: a failed second survey is named with its reason and can be asked for again, never
 * left as a blank pane.
 */

interface Started {
  onLoaded: (viewer: CaveViewer, cv2: { SHADING_SURVEY: number }) => void;
  onFailed: (reason: CaveViewerFailure) => void;
  stopped: boolean;
}
const started: Started[] = [];

vi.mock('../../caveview/viewerLifecycle.ts', async (original) => ({
  ...(await original<typeof import('../../caveview/viewerLifecycle.ts')>()),
  startCaveViewer: (options: Omit<Started, 'stopped'>) => {
    const start: Started = { onLoaded: options.onLoaded, onFailed: options.onFailed, stopped: false };
    started.push(start);
    return () => {
      start.stopped = true;
    };
  },
}));
vi.mock('../../caveview/loadCaveView.ts', () => ({
  caveViewerOptions: () => ({}),
  loadCaveView: () => new Promise(() => undefined),
}));

const { default: CompareViewerHost } = await import('./CompareViewerHost.tsx');

afterEach(() => {
  cleanup();
  started.length = 0;
});

function show(onViewer = vi.fn()) {
  render(
    <CompareViewerHost
      files={[{ url: 'https://files.example/other.3d', fileName: 'other.3d', label: 'other' }]}
      filesKey="other"
      toolbar={null}
      onViewer={onViewer}
      testId="compare-viewer"
    />,
  );
  return onViewer;
}

describe('the second viewer of a comparison', () => {
  it('names a refused survey, hides the empty pane, and builds the viewer again when asked', () => {
    const onViewer = show();
    expect(screen.getByTestId('compare-viewer-loading')).toBeInTheDocument();

    act(() => started[0].onFailed({ kind: 'refused' }));

    expect(screen.getByText(i18n.t('caveview.loadError'))).toBeInTheDocument();
    expect(screen.getByText(i18n.t('caveview.loadFailed.refused'))).toBeInTheDocument();
    expect(screen.getByTestId('compare-viewer')).toHaveStyle({ display: 'none' });
    expect(onViewer).not.toHaveBeenCalled();

    fireEvent.click(screen.getByTestId('compare-viewer-retry'));

    // The failed attempt is ended and another one started for the same survey.
    expect(started).toHaveLength(2);
    expect(started[0].stopped).toBe(true);
    expect(screen.queryByText(i18n.t('caveview.loadError'))).toBeNull();
    expect(screen.getByTestId('compare-viewer-loading')).toBeInTheDocument();

    const viewer = { resize: vi.fn() } as unknown as CaveViewer;
    act(() => started[1].onLoaded(viewer, { SHADING_SURVEY: 1 }));

    expect(onViewer).toHaveBeenLastCalledWith(viewer);
    expect(screen.queryByTestId('compare-viewer-loading')).toBeNull();
    expect(screen.getByTestId('compare-viewer')).not.toHaveStyle({ display: 'none' });
  });

  it('says what the server answered when it failed for another reason', () => {
    show();
    act(() => started[0].onFailed({ kind: 'server', status: 503 }));

    expect(screen.getByText(i18n.t('caveview.loadFailed.server', { status: 503 }))).toBeInTheDocument();
    expect(screen.getByTestId('compare-viewer-retry')).toBeInTheDocument();
  });
});
