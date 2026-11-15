// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef } from 'react';
import {
  clearFlybyTexts,
  drawFlybyTexts,
  flybyTextsAt,
  sameFlybyTexts,
  type FlybyTextState,
} from '../../../flyby/flybyTexts.ts';
import type { FlybyMoment } from './useFlyby.ts';

export interface FlybyTextsOverlayProps {
  subscribeMoment(listener: (moment: FlybyMoment | null) => void): () => void;
}

/** Which parts of the texts are showing, as words, for a reader of the page and for a test. */
function shownParts(state: FlybyTextState | undefined): string {
  if (!state) {
    return '';
  }
  return [
    state.dim > 0 ? 'dim' : undefined,
    state.title ? 'title' : undefined,
    state.caption ? 'caption' : undefined,
    state.end ? 'end' : undefined,
    state.north !== undefined ? 'north' : undefined,
    state.corner ? 'corner' : undefined,
    state.relief ? 'relief' : undefined,
  ]
    .filter(Boolean)
    .join(' ');
}

/**
 * What a flyby writes over the scene, drawn onto a canvas laid over it.
 *
 * Drawn by the same function a movie of the flyby draws its frames with, so the screen and the file
 * say the same. Redrawn from the flyby's own report of each frame rather than from page state, which
 * is brought up to date only a few times a second; and only when what it shows has changed, which
 * for most of a flyby is never. It takes no pointer: the scene under it stays the scene.
 */
export default function FlybyTextsOverlay({ subscribeMoment }: FlybyTextsOverlayProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null);

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) {
      return;
    }
    let drawn: FlybyTextState | undefined;
    let last: FlybyMoment | null = null;
    const draw = (force: boolean) => {
      const context = canvas.getContext('2d');
      const state = last && last.status !== 'idle' ? flybyTextsAt(last.texts, last.t) : undefined;
      if (!force && sameFlybyTexts(state, drawn)) {
        return;
      }
      drawn = state;
      canvas.dataset.shown = shownParts(state);
      if (context) {
        // This canvas holds only the texts, so the moment before is wiped off it first.
        clearFlybyTexts(context, canvas.width, canvas.height);
        drawFlybyTexts(context, state ?? { dim: 0 }, canvas.width, canvas.height);
      }
    };
    // Sized in the screen's own pixels, so text is as sharp as the page around it.
    const resize = () => {
      const ratio = window.devicePixelRatio || 1;
      const width = Math.max(1, Math.round(canvas.clientWidth * ratio));
      const height = Math.max(1, Math.round(canvas.clientHeight * ratio));
      if (canvas.width !== width || canvas.height !== height) {
        canvas.width = width;
        canvas.height = height;
        draw(true);
      }
    };
    resize();
    const observer = typeof ResizeObserver === 'undefined' ? undefined : new ResizeObserver(resize);
    observer?.observe(canvas);
    const unsubscribe = subscribeMoment((moment) => {
      last = moment;
      draw(false);
    });
    return () => {
      unsubscribe();
      observer?.disconnect();
    };
  }, [subscribeMoment]);

  return <canvas ref={canvasRef} className="scene3d-flyby-texts" data-testid="flyby-texts" data-shown="" aria-hidden="true" />;
}
