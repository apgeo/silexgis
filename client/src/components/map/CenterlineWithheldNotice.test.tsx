// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { CenterlineLoadState } from '../../map/centerlineLayer.ts';

/**
 * The map-corner notice about survey lines held back at this zoom: when it shows, what pressing it
 * does, and what it says to a screen reader — once per appearance, not once per pan.
 */

// A stand-in for the overlay's load-state store: the real one is fed by map moves.
let state: CenterlineLoadState = { withheldCount: 0, detail: false };
const listeners = new Set<() => void>();

function publish(next: CenterlineLoadState) {
  act(() => {
    state = next;
    for (const listener of listeners) {
      listener();
    }
  });
}

vi.mock('../../map/centerlineLayer.ts', () => ({
  getCenterlineLoadState: () => state,
  subscribeCenterlineLoadState: (listener: () => void) => {
    listeners.add(listener);
    return () => listeners.delete(listener);
  },
}));

const { default: CenterlineWithheldNotice } = await import('./CenterlineWithheldNotice.tsx');
const { CENTERLINE_STATUS_ID, focusCenterlineStatus } = await import('./centerlineStatus.ts');

interface Shown {
  centerlinesVisible?: boolean;
  panelHidden?: boolean;
  chromeHidden?: boolean;
  onOpenPanel?: () => void;
}

function notice(props: Shown = {}) {
  return (
    <CenterlineWithheldNotice
      centerlinesVisible={props.centerlinesVisible ?? true}
      panelHidden={props.panelHidden ?? true}
      chromeHidden={props.chromeHidden ?? false}
      onOpenPanel={props.onOpenPanel ?? vi.fn()}
    />
  );
}

const live = () => screen.getByTestId('map-centerline-notice-live');
const sentence = (count: number) =>
  `${count} survey line(s) hidden at this zoom — zoom in or open the layer panel`;

beforeEach(() => {
  state = { withheldCount: 0, detail: false };
  listeners.clear();
});

afterEach(cleanup);

describe('the withheld survey-lines notice on the map', () => {
  it('says how many lines are held back, and opens the layer panel when pressed', () => {
    const onOpenPanel = vi.fn();
    state = { withheldCount: 3, detail: false };
    render(notice({ onOpenPanel }));

    const button = screen.getByRole('button', { name: sentence(3) });
    fireEvent.click(button);
    expect(onOpenPanel).toHaveBeenCalledTimes(1);
  });

  it('shows only while the lines are on, withheld, the panel closed and the chrome shown', () => {
    // The shown case first, so each absence below is the one condition changing and not a notice
    // that never renders at all.
    state = { withheldCount: 3, detail: false };
    const { rerender } = render(notice());
    expect(screen.getByTestId('map-centerline-notice')).toBeTruthy();

    // The panel is open: its own alert says the same, a second copy on the map is noise.
    rerender(notice({ panelHidden: false }));
    expect(screen.queryByTestId('map-centerline-notice')).toBeNull();

    // A clean map was asked for.
    rerender(notice({ chromeHidden: true }));
    expect(screen.queryByTestId('map-centerline-notice')).toBeNull();

    // The overlay is off.
    rerender(notice({ centerlinesVisible: false }));
    expect(screen.queryByTestId('map-centerline-notice')).toBeNull();

    rerender(notice());
    expect(screen.getByTestId('map-centerline-notice')).toBeTruthy();

    // Nothing is withheld any more: zoomed in far enough.
    publish({ withheldCount: 0, detail: true });
    expect(screen.queryByTestId('map-centerline-notice')).toBeNull();
  });

  it('is announced politely when it appears, and not again as the count follows the map', () => {
    render(notice());
    // The live region is there before it has anything to say, so its first words are read out.
    expect(live()).toHaveAttribute('aria-live', 'polite');
    expect(live()).toHaveAttribute('role', 'status');
    expect(live().textContent).toBe('');

    publish({ withheldCount: 3, detail: false });
    expect(live().textContent).toBe(sentence(3));

    // A pan republishes, usually with another count: the button follows, the announcement does not.
    publish({ withheldCount: 7, detail: false });
    expect(screen.getByRole('button', { name: sentence(7) })).toBeTruthy();
    expect(live().textContent).toBe(sentence(3));
    publish({ withheldCount: 7, detail: false });
    expect(live().textContent).toBe(sentence(3));

    // Gone, then back: a new appearance, said once more with the count it came back with.
    publish({ withheldCount: 0, detail: true });
    expect(live().textContent).toBe('');
    publish({ withheldCount: 5, detail: false });
    expect(live().textContent).toBe(sentence(5));
  });

  it('is announced in Romanian when the interface is', async () => {
    const { default: i18n } = await import('../../i18n');
    await act(async () => {
      await i18n.changeLanguage('ro');
    });
    try {
      state = { withheldCount: 2, detail: false };
      render(notice());
      expect(
        screen.getByRole('button', {
          name: '2 poligonale ascunse la această scară — apropie harta sau deschide panoul de straturi',
        }),
      ).toBeTruthy();
    } finally {
      await act(async () => {
        await i18n.changeLanguage('en');
      });
    }
  });
});

describe('moving focus to the layer panel block', () => {
  let frames: FrameRequestCallback[] = [];

  beforeEach(() => {
    frames = [];
    vi.spyOn(window, 'requestAnimationFrame').mockImplementation((callback) => {
      frames.push(callback);
      return frames.length;
    });
    vi.spyOn(window, 'cancelAnimationFrame').mockImplementation(() => {});
  });

  afterEach(() => {
    vi.restoreAllMocks();
    document.body.innerHTML = '';
  });

  const runFrame = () => {
    const pending = frames;
    frames = [];
    for (const callback of pending) {
      callback(0);
    }
  };

  function block(width: number) {
    const element = document.createElement('div');
    element.id = CENTERLINE_STATUS_ID;
    element.tabIndex = -1;
    element.getBoundingClientRect = () => ({ width, height: 10 }) as DOMRect;
    document.body.appendChild(element);
    return element;
  }

  it('waits for the panel to have width before it takes focus there', () => {
    // A collapsed pane still holds the block, at zero width: focus moved then would land on
    // something the reader cannot see.
    const element = block(0);
    focusCenterlineStatus();
    runFrame();
    expect(document.activeElement).not.toBe(element);

    element.getBoundingClientRect = () => ({ width: 240, height: 10 }) as DOMRect;
    runFrame();
    expect(document.activeElement).toBe(element);
  });

  it('gives up after its bound, and when cancelled', () => {
    const element = block(0);
    focusCenterlineStatus(3);
    runFrame();
    runFrame();
    runFrame();
    expect(frames).toHaveLength(0);
    expect(document.activeElement).not.toBe(element);

    const cancel = focusCenterlineStatus();
    cancel();
    element.getBoundingClientRect = () => ({ width: 240, height: 10 }) as DOMRect;
    runFrame();
    expect(document.activeElement).not.toBe(element);
  });
});
