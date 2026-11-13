// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useLayoutEffect, useRef } from 'react';
import { frameArea, placeFrame, type FlybyFrameRect } from '../../../flyby/flybyFrame.ts';

/** The class the view wears while its scene is boxed to a movie's frame. */
export const FRAMED_CLASS = 'scene3d-wrap-framed';

const FRAME_PROPERTIES = ['--flyby-frame-left', '--flyby-frame-top', '--flyby-frame-width', '--flyby-frame-height'];

/** Where an element stands in the view, measured from the view's top left. */
function rectIn(view: DOMRect, element: Element): FlybyFrameRect {
  const rect = element.getBoundingClientRect();
  return { left: rect.left - view.left, top: rect.top - view.top, width: rect.width, height: rect.height };
}

/**
 * Boxes the scene to the shape of a movie's frame while `frame` is given: the scene's element is
 * made the largest box of exactly that shape that fits beside the controls `covering` names, centred
 * there, and the rest of the view is left dark — so what is seen is what the file will hold. The
 * scene follows its element's size by itself.
 *
 * Held where it is while `frozen`: the scene must not change size under a movie being made, which
 * a panel growing by a line, or the window being resized, would otherwise do.
 */
export function useFlybyFrameBox(
  wrap: () => HTMLElement | null,
  frame: { width: number; height: number } | null,
  covering: () => (Element | null)[],
  frozen: boolean,
): void {
  const frozenRef = useRef(frozen);
  frozenRef.current = frozen;
  const coveringRef = useRef(covering);
  coveringRef.current = covering;
  const fitRef = useRef<(() => void) | null>(null);
  const width = frame?.width;
  const height = frame?.height;

  useLayoutEffect(() => {
    const element = wrap();
    if (!element || width === undefined || height === undefined) {
      return;
    }
    const fit = () => {
      if (frozenRef.current) {
        return;
      }
      const view = element.getBoundingClientRect();
      const covered = coveringRef.current().flatMap((cover) => (cover ? [rectIn(view, cover)] : []));
      const placed = placeFrame({ width, height }, frameArea({ width: view.width, height: view.height }, covered));
      if (!placed) {
        // Not even the smallest box of the shape fits: the scene is left as it was, and a movie
        // made from it is refused for its shape rather than drawn stretched.
        element.classList.remove(FRAMED_CLASS);
        return;
      }
      element.style.setProperty('--flyby-frame-left', `${placed.left}px`);
      element.style.setProperty('--flyby-frame-top', `${placed.top}px`);
      element.style.setProperty('--flyby-frame-width', `${placed.width}px`);
      element.style.setProperty('--flyby-frame-height', `${placed.height}px`);
      element.classList.add(FRAMED_CLASS);
    };
    fitRef.current = fit;
    fit();
    const observer = typeof ResizeObserver === 'undefined' ? undefined : new ResizeObserver(() => fit());
    observer?.observe(element);
    window.addEventListener('resize', fit);
    return () => {
      fitRef.current = null;
      observer?.disconnect();
      window.removeEventListener('resize', fit);
      element.classList.remove(FRAMED_CLASS);
      for (const name of FRAME_PROPERTIES) {
        element.style.removeProperty(name);
      }
    };
  }, [wrap, width, height]);

  // The controls beside the box come and go, and change size — a panel mounted once its code has
  // arrived, a sheet that grows by a line — and the box follows them. Looked at after every render
  // of whoever holds the box, and between renders through their own sizes.
  const watched = useRef<{ observer: ResizeObserver | undefined; elements: Element[] }>({ observer: undefined, elements: [] });
  useEffect(() => {
    fitRef.current?.();
    const elements = coveringRef.current().filter((element): element is Element => element !== null);
    const held = watched.current;
    if (elements.length === held.elements.length && elements.every((element, index) => element === held.elements[index])) {
      return;
    }
    held.observer?.disconnect();
    held.elements = elements;
    held.observer = typeof ResizeObserver === 'undefined' ? undefined : new ResizeObserver(() => fitRef.current?.());
    for (const element of elements) {
      held.observer?.observe(element);
    }
  });
  useEffect(
    () => () => {
      watched.current.observer?.disconnect();
    },
    [],
  );
}
