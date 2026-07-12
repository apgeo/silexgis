// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { Cv2Namespace } from '../../caveview/loadCaveView.ts';
import CaveViewPanel from './CaveViewPanel.tsx';

// A fake CV2 global stands in for the vendored bundle: it records event
// listeners so tests can replay the viewer's events against the wrapper.
type Listener = (event: unknown) => void;
const listeners = new Map<string, Listener[]>();

class FakeViewer {
  addEventListener(type: string, listener: Listener) {
    listeners.set(type, [...(listeners.get(type) ?? []), listener]);
  }
  removeEventListener() {}
}

class FakeUi {
  loadCave() {}
  dispose() {}
}

function emit(type: string, event: unknown) {
  for (const listener of listeners.get(type) ?? []) {
    listener(event);
  }
}

beforeEach(() => {
  listeners.clear();
  window.CV2 = {
    CaveViewer: FakeViewer,
    CaveViewUI: FakeUi,
  } as unknown as Cv2Namespace;
  vi.stubGlobal('fetch', vi.fn(async () => new Response(new Blob(['survey']))));
});

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  window.CV2 = undefined;
});

describe('CaveViewPanel', () => {
  it('reports entrance label clicks through onEntrancePick', async () => {
    const onPick = vi.fn();
    render(<CaveViewPanel fileUrl="http://files.local/survey" fileName="demo.lox" onEntrancePick={onPick} />);

    await waitFor(() => expect(listeners.get('entrance')?.length ?? 0).toBeGreaterThan(0));
    emit('entrance', { type: 'entrance', displayName: 'Main entrance' });

    expect(onPick).toHaveBeenCalledWith('Main entrance');
  });

  it('ignores entrance events without a display name and leaves loading on newCave', async () => {
    const onPick = vi.fn();
    render(<CaveViewPanel fileUrl="http://files.local/survey" fileName="demo.lox" onEntrancePick={onPick} />);

    await waitFor(() => expect(listeners.get('entrance')?.length ?? 0).toBeGreaterThan(0));
    emit('entrance', { type: 'entrance' });
    expect(onPick).not.toHaveBeenCalled();

    expect(screen.getByTestId('caveview-loading')).toBeInTheDocument();
    emit('newCave', {});
    await waitFor(() => expect(screen.queryByTestId('caveview-loading')).not.toBeInTheDocument());
  });
});
