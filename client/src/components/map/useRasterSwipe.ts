// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import { setRasterSwipeActive, setRasterSwipeFraction } from '../../map/rasterSwipe.ts';

/** Raster swipe-compare: the armed flag, the handle's position and the drag that moves it. */
export function useRasterSwipe(visibleRasterIds: string[]) {
  // Raster swipe-compare: ephemeral, auto-disarms when the last raster goes away.
  const [swipeActive, setSwipeActive] = useState(false);
  const [swipePos, setSwipePos] = useState(0.5);
  useEffect(() => {
    if (visibleRasterIds.length === 0) {
      setSwipeActive(false);
    }
  }, [visibleRasterIds]);
  useEffect(() => {
    setRasterSwipeActive(swipeActive);
    return () => setRasterSwipeActive(false);
  }, [swipeActive]);

  const onSwipeHandleDown = (event: React.PointerEvent<HTMLDivElement>) => {
    const wrap = event.currentTarget.parentElement!.getBoundingClientRect();
    const handle = event.currentTarget;
    handle.setPointerCapture(event.pointerId);
    const move = (ev: PointerEvent) => {
      const f = Math.min(1, Math.max(0, (ev.clientX - wrap.left) / wrap.width));
      setSwipePos(f);
      setRasterSwipeFraction(f);
    };
    const up = () => {
      handle.removeEventListener('pointermove', move);
      handle.removeEventListener('pointerup', up);
    };
    handle.addEventListener('pointermove', move);
    handle.addEventListener('pointerup', up);
  };

  return { swipeActive, setSwipeActive, swipePos, onSwipeHandleDown };
}
