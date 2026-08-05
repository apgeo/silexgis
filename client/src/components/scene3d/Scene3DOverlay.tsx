// SPDX-License-Identifier: AGPL-3.0-or-later
import { useCallback, useEffect, useLayoutEffect, useRef } from 'react';
import { CloseOutlined } from '@ant-design/icons';
import { Button, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { placeOverlay, type OverlayRect } from '../../scene3d/overlayPlacement.ts';
import type { Scene3DPickPayload } from '../../scene3d/selection3d.ts';
import type { Scene3DPosition, Scene3DScreenPosition } from '../../scene3d/scene3dEngine.ts';
import './Scene3DOverlay.css';

// The two pieces of chrome that name what is under the pointer and what was picked, drawn as
// ordinary elements over the scene rather than by the renderer.
//
// This is how the selection in three dimensions is made to feel like the selection on the flat
// map, which draws its own tooltip as a div in an overlay and not into the canvas. Doing it the
// other way — as textured geometry in the scene — would mean reimplementing text layout, theming,
// translation and every accessibility affordance inside a renderer that has none of them, and the
// result would still not be selectable, focusable or readable by a screen reader.
//
// The component never touches the engine. It is handed a function that projects a position and a
// function that says when a frame is about to be drawn, and that is the whole of its contact with
// the scene — which is what keeps the renderer behind its one module.

/** How far from the anchor the chrome sits, matching the flat map's twelve-pixel tooltip gap. */
const ANCHOR_OFFSET_PIXELS = 14;

/**
 * How far outside the view an anchor may drift before its chrome is taken down. A little slack
 * keeps a label that is half off the edge from flickering as the camera moves; much more and the
 * label would be pinned to the border pointing at something nobody can see.
 */
const OFF_SCREEN_MARGIN_PIXELS = 48;

export interface Scene3DOverlayProps {
  /** What the pointer is resting on, or undefined when it rests on nothing nameable. */
  hovered?: Scene3DPickPayload;
  /** Where the pointer is, in surface pixels — the tooltip follows the pointer, as the map's does. */
  hoveredAt?: Scene3DScreenPosition;
  /** What was picked in this scene, or undefined when nothing was or it was dismissed. */
  selected?: Scene3DPickPayload;
  /** Where a position on the globe lands on the drawing surface right now. */
  project(position: Scene3DPosition): Scene3DScreenPosition | undefined;
  /** Subscribes to drawn frames; the returned function unsubscribes. */
  subscribeFrames(listener: () => void): () => void;
  /** Takes the callout down, from its own close button or from Escape. */
  onDismissSelection(): void;
  /**
   * Where the scene's own controls stand, in surface pixels, so the callout is not laid over them.
   *
   * Asked for on every placement rather than handed over as a value: the controls change size and
   * corner between a mouse and a finger, and the answer has to be the one that is true now.
   */
  controlsArea?(): OverlayRect | undefined;
}

export default function Scene3DOverlay({
  hovered,
  hoveredAt,
  selected,
  project,
  subscribeFrames,
  onDismissSelection,
  controlsArea,
}: Scene3DOverlayProps) {
  const { t } = useTranslation();
  const rootRef = useRef<HTMLDivElement>(null);
  const tooltipRef = useRef<HTMLDivElement>(null);
  const calloutRef = useRef<HTMLDivElement>(null);

  const hoverLabel = hovered ? describe(hovered, t) : undefined;
  const selectedTitle = selected ? describe(selected, t) : undefined;
  const selectedKind = selected ? kindName(selected, t) : undefined;
  const selectedAnchor = anchorOf(selected);

  /**
   * Puts both pieces where they belong, without going through React.
   *
   * This runs on every drawn frame, which during a drag or a flight is every frame the display
   * shows. Asking React to re-render at that rate to move two absolutely positioned boxes would
   * cost more than the scene does, so the position is written straight onto the elements and only
   * the *content* — which changes when the pointer moves onto something else, not when the camera
   * moves — is state.
   */
  const reposition = useCallback(() => {
    const root = rootRef.current;
    if (!root) {
      return;
    }
    const surface = { widthPixels: root.clientWidth, heightPixels: root.clientHeight };

    const tooltip = tooltipRef.current;
    if (tooltip) {
      // The tooltip follows the pointer rather than the thing under it, which is what the flat map
      // does: a pointer resting on a long survey line has no single place on it to point at, and a
      // label that jumped to the middle of a cave while the pointer was at its end would read as
      // naming something else.
      place(tooltip, hoveredAt, surface);
    }

    const callout = calloutRef.current;
    if (callout) {
      // The callout follows the thing, not the pointer: it outlives the click that opened it, and
      // it has to stay on the cave while the viewer turns the camera to look at it.
      //
      // It is also the only one of the two kept off the controls. It persists, it takes pointer
      // events, and it is wide; the tooltip is none of those — it is drawn under the pointer that
      // is driving it, so it can only be over a control while the pointer is over that control,
      // and it lets presses through to whatever is beneath it in any case.
      place(callout, selectedAnchor ? project(selectedAnchor) : undefined, surface, controlsArea?.());
    }
  }, [hoveredAt, project, selectedAnchor, controlsArea]);

  // Placed once as soon as it exists, before the browser paints, and then again on every drawn
  // frame. Both are needed and neither is enough: a still scene draws no frames at all, so waiting
  // for one would leave the chrome in the corner over an idle view; and a single placement would
  // leave it there the moment the camera moved.
  useLayoutEffect(reposition, [reposition, hoverLabel, selectedTitle, selectedKind]);

  useEffect(() => subscribeFrames(reposition), [subscribeFrames, reposition]);

  // Escape closes the callout while the keyboard is inside it, the way every dismissible surface
  // in this application closes. Bound to the callout rather than to the window so it cannot
  // swallow the key from a dialog opened over the scene.
  const onKeyDown = useCallback(
    (event: React.KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.stopPropagation();
        onDismissSelection();
      }
    },
    [onDismissSelection],
  );

  return (
    <div className="scene3d-overlay" ref={rootRef} data-testid="scene3d-overlay">
      {hoverLabel && (
        /* Announced rather than focusable, and deliberately so: it describes what a POINTER is
           resting on, which is a state no keyboard can enter, and anything able to take focus here
           would also have to take pointer events — which would put it under the pointer that is
           driving it and make the scene unclickable wherever it appeared. What a viewer who is not
           using a pointer needs is to be told; what they act on is the callout below. */
        <div
          className="scene3d-overlay-tooltip"
          ref={tooltipRef}
          role="status"
          aria-live="polite"
          data-testid="scene3d-hover-tooltip"
        >
          {hoverLabel}
        </div>
      )}
      {selected && (
        <div
          className="scene3d-overlay-callout"
          ref={calloutRef}
          // In the tab order, so the thing a viewer just picked can be read and dismissed from the
          // keyboard. Not focused automatically: a pick can arrive from the other view or from
          // another window, and pulling focus out from under whoever made it would be worse than
          // making them press Tab.
          tabIndex={0}
          role="group"
          aria-label={selectedTitle}
          onKeyDown={onKeyDown}
          data-testid="scene3d-callout"
        >
          <div className="scene3d-overlay-callout-body">
            <Typography.Text strong ellipsis={{ tooltip: selectedTitle }}>
              {selectedTitle}
            </Typography.Text>
            {selectedKind && (
              <Typography.Text type="secondary" className="scene3d-overlay-callout-kind">
                {selectedKind}
              </Typography.Text>
            )}
          </div>
          <Button
            type="text"
            size="small"
            icon={<CloseOutlined />}
            aria-label={t('scene3d.closeCallout')}
            onClick={onDismissSelection}
            data-testid="scene3d-callout-close"
          />
        </div>
      )}
    </div>
  );
}

/** Moves one element to where it belongs, or hides it when it belongs nowhere on screen. */
function place(
  element: HTMLElement,
  screen: Scene3DScreenPosition | undefined,
  surface: { widthPixels: number; heightPixels: number },
  reserved?: OverlayRect,
): void {
  const placement = placeOverlay({
    screen,
    surface,
    element: { widthPixels: element.offsetWidth, heightPixels: element.offsetHeight },
    offsetPixels: ANCHOR_OFFSET_PIXELS,
    marginPixels: OFF_SCREEN_MARGIN_PIXELS,
    ...(reserved ? { reserved } : {}),
  });
  if (!placement.visible) {
    // Hidden rather than unmounted: an anchor swings off the edge of the view and back again
    // several times during one drag, and tearing the element down and rebuilding it each time
    // would also discard the keyboard focus inside it.
    element.style.visibility = 'hidden';
    return;
  }
  element.style.visibility = 'visible';
  // A transform rather than `left`/`top`: it moves the element without asking the browser to lay
  // the page out again, which matters when it happens on every frame of a drag.
  element.style.transform = `translate(${placement.left}px, ${placement.top}px)`;
  element.dataset.side = placement.side;
}

type Translate = (key: string, options?: Record<string, unknown>) => string;

/** Where on the globe a callout about this pick is pinned. */
function anchorOf(payload: Scene3DPickPayload | undefined): Scene3DPosition | undefined {
  if (!payload) {
    return undefined;
  }
  if (payload.anchor) {
    return payload.anchor;
  }
  // A cluster stands at a place by construction — it is a count over a patch of ground and the
  // patch is what it reports — so it has one even when nothing filled the field in.
  return payload.kind === 'cluster'
    ? { longitude: payload.lon, latitude: payload.lat, height: 0 }
    : undefined;
}

/**
 * What to call a pick, in the viewer's language.
 *
 * A cluster is described by its count because it has no name — that is exactly what the flat map
 * does with the same thing — and anything else falls back to what kind of thing it is, so an
 * unnamed feature is still called something rather than appearing as an empty box.
 */
function describe(payload: Scene3DPickPayload, t: Translate): string {
  if (payload.kind === 'cluster') {
    return t('map.clusterTitle', { count: payload.count });
  }
  return payload.label ?? kindName(payload, t);
}

/** The name of the overlay a pick came from, in the same words the layer controls use. */
function kindName(payload: Scene3DPickPayload, t: Translate): string {
  switch (payload.kind) {
    case 'entrance':
      return t('map.entrances');
    case 'feature':
      return t('map.surfaceFeatures');
    case 'centerline':
      return t('map.centerlines');
    case 'cluster':
      return t('map.entrances');
  }
}
