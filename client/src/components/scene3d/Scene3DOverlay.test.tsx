// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { Scene3DPickPayload } from '../../scene3d/selection3d.ts';
import type { Scene3DPosition, Scene3DScreenPosition } from '../../scene3d/scene3dEngine.ts';
import Scene3DOverlay, { type Scene3DOverlayProps } from './Scene3DOverlay.tsx';

afterEach(cleanup);

/**
 * A surface with a size, because jsdom gives every element zero of everything and the placement
 * arithmetic then has no room to put anything in — every box would land at the origin and every
 * assertion about where it went would pass for the wrong reason.
 */
function withMeasuredElements(surface = { width: 1000, height: 600 }, box = { width: 200, height: 40 }) {
  vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockReturnValue(surface.width);
  vi.spyOn(HTMLElement.prototype, 'clientHeight', 'get').mockReturnValue(surface.height);
  vi.spyOn(HTMLElement.prototype, 'offsetWidth', 'get').mockReturnValue(box.width);
  vi.spyOn(HTMLElement.prototype, 'offsetHeight', 'get').mockReturnValue(box.height);
}

/** A scene that draws when it is told to, so a test can say "and then a frame was drawn". */
function fakeScene(project: (position: Scene3DPosition) => Scene3DScreenPosition | undefined) {
  const listeners = new Set<() => void>();
  return {
    project,
    subscribeFrames: (listener: () => void) => {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    drawFrame: () => act(() => listeners.forEach((listener) => listener())),
    frameListenerCount: () => listeners.size,
  };
}

function renderOverlay(overrides: Partial<Scene3DOverlayProps> = {}) {
  const scene = fakeScene(() => ({ x: 500, y: 300 }));
  const props: Scene3DOverlayProps = {
    project: scene.project,
    subscribeFrames: scene.subscribeFrames,
    onDismissSelection: vi.fn(),
    ...overrides,
  };
  render(<Scene3DOverlay {...props} />);
  return { props, scene };
}

const entrance: Scene3DPickPayload = {
  kind: 'entrance',
  entranceId: 'e1',
  caveId: 'c1',
  label: 'Intrarea Mică — Peștera Demo',
  anchor: { longitude: 25, latitude: 45, height: 0 },
};

describe('the hover tooltip', () => {
  it('names what the pointer is resting on, at the pointer', () => {
    withMeasuredElements();
    renderOverlay({ hovered: entrance, hoveredAt: { x: 400, y: 200 } });

    const tooltip = screen.getByTestId('scene3d-hover-tooltip');
    expect(tooltip).toHaveTextContent('Intrarea Mică — Peștera Demo');
    // Beside the pointer and level with it, which is where the flat map puts the same thing.
    expect(tooltip.style.transform).toBe('translate(414px, 180px)');
  });

  it('is announced rather than made focusable', () => {
    withMeasuredElements();
    renderOverlay({ hovered: entrance, hoveredAt: { x: 400, y: 200 } });

    // It describes what a POINTER is resting on, a state no keyboard can enter, and anything able
    // to take focus here would also take pointer events and sit under the very pointer driving it.
    // What a viewer without a pointer needs is to be told; what they act on is the callout.
    const tooltip = screen.getByTestId('scene3d-hover-tooltip');
    expect(tooltip).toHaveAttribute('role', 'status');
    expect(tooltip).toHaveAttribute('aria-live', 'polite');
    expect(tooltip).not.toHaveAttribute('tabindex');
  });

  it('says nothing at all when the pointer is over nothing', () => {
    withMeasuredElements();
    renderOverlay();

    expect(screen.queryByTestId('scene3d-hover-tooltip')).toBeNull();
  });

  it('is not pushed off the controls, because it cannot take a press from them', () => {
    withMeasuredElements({ width: 412, height: 915 }, { width: 200, height: 24 });
    render(
      <Scene3DOverlay
        hovered={entrance}
        hoveredAt={{ x: 150, y: 20 }}
        project={() => undefined}
        subscribeFrames={() => () => {}}
        onDismissSelection={vi.fn()}
        controlsArea={() => ({
          leftPixels: 320,
          topPixels: 8,
          widthPixels: 84,
          heightPixels: 40,
        })}
      />,
    );

    // It is drawn under the pointer driving it, so it can only be over a button while the pointer
    // is over that button, and it lets presses through to whatever is beneath it in any case.
    // Moving it would make it jump about under a pointer that had not moved.
    expect(screen.getByTestId('scene3d-hover-tooltip').style.transform).toBe('translate(164px, 8px)');
  });

  it('describes a cluster by its count, which is all a patch of ground has', () => {
    withMeasuredElements();
    renderOverlay({
      hovered: { kind: 'cluster', lon: 25, lat: 45, count: 7, zoom: 9 },
      hoveredAt: { x: 100, y: 100 },
    });

    expect(screen.getByTestId('scene3d-hover-tooltip')).toHaveTextContent('7 entrances');
  });
});

describe('the selected-feature callout', () => {
  it('names what was picked and what kind of thing it is', () => {
    withMeasuredElements();
    renderOverlay({ selected: entrance });

    const callout = screen.getByTestId('scene3d-callout');
    expect(callout).toHaveTextContent('Intrarea Mică — Peștera Demo');
    expect(callout).toHaveTextContent('Cave entrances');
  });

  it('names an unnamed pick by its kind rather than showing an empty box', () => {
    withMeasuredElements();
    renderOverlay({ selected: { kind: 'feature', featureId: 'f1', anchor: entrance.anchor } });

    expect(screen.getByTestId('scene3d-callout')).toHaveTextContent('Surface features');
  });

  it('can be reached and dismissed from the keyboard', () => {
    withMeasuredElements();
    const { props } = renderOverlay({ selected: entrance });
    const callout = screen.getByTestId('scene3d-callout');

    // In the tab order, so the thing a viewer just picked can be read without a pointer. Not
    // focused automatically: a pick can arrive from the flat map or from another window, and
    // pulling focus out from under whoever made it would be worse than a press of Tab.
    expect(callout).toHaveAttribute('tabindex', '0');
    callout.focus();
    expect(document.activeElement).toBe(callout);

    fireEvent.keyDown(callout, { key: 'Escape' });
    expect(props.onDismissSelection).toHaveBeenCalled();
  });

  it('closes from its own button', () => {
    withMeasuredElements();
    const { props } = renderOverlay({ selected: entrance });

    fireEvent.click(screen.getByTestId('scene3d-callout-close'));
    expect(props.onDismissSelection).toHaveBeenCalled();
  });

  it('follows the thing it is about as the camera moves', () => {
    withMeasuredElements();
    let pixel: Scene3DScreenPosition | undefined = { x: 500, y: 300 };
    const scene = fakeScene(() => pixel);
    render(
      <Scene3DOverlay
        selected={entrance}
        project={scene.project}
        subscribeFrames={scene.subscribeFrames}
        onDismissSelection={vi.fn()}
      />,
    );
    const callout = screen.getByTestId('scene3d-callout');
    expect(callout.style.transform).toBe('translate(514px, 280px)');

    // The camera turns; the cave is now somewhere else on the screen. Without this the callout
    // would name the right cave and point at whatever had drifted under it.
    pixel = { x: 200, y: 100 };
    scene.drawFrame();
    expect(callout.style.transform).toBe('translate(214px, 80px)');
  });

  it('is placed before the first frame is ever drawn', () => {
    withMeasuredElements();
    const scene = fakeScene(() => ({ x: 500, y: 300 }));
    render(
      <Scene3DOverlay
        selected={entrance}
        project={scene.project}
        subscribeFrames={scene.subscribeFrames}
        onDismissSelection={vi.fn()}
      />,
    );

    // A still scene draws no frames at all, so a callout that waited for one would sit in the
    // corner over an idle view — which is the ordinary case, not an edge one.
    expect(screen.getByTestId('scene3d-callout').style.visibility).toBe('visible');
  });

  it('is put somewhere else rather than over the scene\'s own controls', () => {
    withMeasuredElements({ width: 412, height: 915 }, { width: 288, height: 56 });
    const scene = fakeScene(() => ({ x: 60, y: 30 }));
    render(
      <Scene3DOverlay
        selected={entrance}
        project={scene.project}
        subscribeFrames={scene.subscribeFrames}
        onDismissSelection={vi.fn()}
        // The two trigger buttons in the top-right corner of a phone-sized view.
        controlsArea={() => ({
          leftPixels: 320,
          topPixels: 8,
          widthPixels: 84,
          heightPixels: 40,
        })}
      />,
    );

    // Level with its anchor it would span the corner both buttons stand in, leaving a viewer
    // unable to read the label, unable to reach its close button, and — on a phone, where those
    // buttons are the only way to the camera positions and every layer control — pressing
    // something they can see and getting nothing.
    expect(screen.getByTestId('scene3d-callout').style.transform).toBe('translate(74px, 48px)');
  });

  it('hides, rather than pointing at the edge, when its anchor leaves the view', () => {
    withMeasuredElements();
    let pixel: Scene3DScreenPosition | undefined = { x: 500, y: 300 };
    const scene = fakeScene(() => pixel);
    render(
      <Scene3DOverlay
        selected={entrance}
        project={scene.project}
        subscribeFrames={scene.subscribeFrames}
        onDismissSelection={vi.fn()}
      />,
    );
    const callout = screen.getByTestId('scene3d-callout');

    pixel = undefined; // behind the camera
    scene.drawFrame();
    expect(callout.style.visibility).toBe('hidden');

    // Still in the document rather than torn down: an anchor swings off the edge and back several
    // times during one drag, and rebuilding it each time would also throw away the focus in it.
    pixel = { x: 400, y: 200 };
    scene.drawFrame();
    expect(callout.style.visibility).toBe('visible');
  });
});

describe('what the overlay costs the scene', () => {
  it('lets go of the scene when it goes away', () => {
    withMeasuredElements();
    const scene = fakeScene(() => ({ x: 1, y: 1 }));
    const { unmount } = render(
      <Scene3DOverlay
        selected={entrance}
        project={scene.project}
        subscribeFrames={scene.subscribeFrames}
        onDismissSelection={vi.fn()}
      />,
    );
    expect(scene.frameListenerCount()).toBe(1);

    unmount();

    // A frame listener left behind runs for the life of the scene, on every frame, against
    // elements that no longer exist.
    expect(scene.frameListenerCount()).toBe(0);
  });

  it('takes exactly one subscription however many frames are drawn', () => {
    withMeasuredElements();
    const scene = fakeScene(() => ({ x: 100, y: 100 }));
    render(
      <Scene3DOverlay
        selected={entrance}
        project={scene.project}
        subscribeFrames={scene.subscribeFrames}
        onDismissSelection={vi.fn()}
      />,
    );

    scene.drawFrame();
    scene.drawFrame();

    expect(scene.frameListenerCount()).toBe(1);
  });
});
