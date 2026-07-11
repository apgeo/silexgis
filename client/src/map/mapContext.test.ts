// SPDX-License-Identifier: AGPL-3.0-or-later
import VectorLayer from 'ol/layer/Vector';
import VectorSource from 'ol/source/Vector';
import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  applyOverlayOrder,
  applyPendingOverlayOrder,
  getOverlayGroup,
  getOverlayOrder,
  setDesiredOverlayOrder,
} from './mapContext.ts';

function addLayer(id: string): VectorLayer {
  const layer = new VectorLayer({ source: new VectorSource() });
  layer.set('id', id);
  getOverlayGroup().getLayers().push(layer);
  return layer;
}

afterEach(() => {
  getOverlayGroup().getLayers().clear();
  // Drain any pending saved-view order so tests stay independent.
  vi.useFakeTimers();
  vi.advanceTimersByTime(10_000);
  applyPendingOverlayOrder();
  vi.useRealTimers();
});

describe('overlay group stacking', () => {
  it('renumbers child zIndex from collection position on add and remove', () => {
    const a = addLayer('a');
    const b = addLayer('b');
    const c = addLayer('c');
    expect([a.getZIndex(), b.getZIndex(), c.getZIndex()]).toEqual([1, 2, 3]);

    getOverlayGroup().getLayers().remove(b);
    expect([a.getZIndex(), c.getZIndex()]).toEqual([1, 2]);
  });

  it('applies a bottom→top order to the listed layers and keeps others in place', () => {
    addLayer('a');
    addLayer('b');
    addLayer('c');
    addLayer('d');

    applyOverlayOrder(['c', 'a']); // swap a and c, leave b and d alone
    expect(getOverlayOrder()).toEqual(['c', 'b', 'a', 'd']);
  });

  it('ignores ids that do not exist', () => {
    addLayer('a');
    addLayer('b');
    applyOverlayOrder(['ghost', 'b', 'a']);
    expect(getOverlayOrder()).toEqual(['b', 'a']);
  });

  it('holds a desired order until the missing layer appears', () => {
    addLayer('a');
    addLayer('b');
    setDesiredOverlayOrder(['missing', 'b', 'a']);
    expect(getOverlayOrder()).toEqual(['b', 'a']);

    addLayer('missing');
    applyPendingOverlayOrder();
    expect(getOverlayOrder()).toEqual(['missing', 'b', 'a']);
  });

  it('drops a desired order after its TTL expires', () => {
    vi.useFakeTimers();
    addLayer('a');
    addLayer('b');
    setDesiredOverlayOrder(['missing', 'b', 'a']);

    vi.advanceTimersByTime(6000);
    addLayer('missing');
    applyPendingOverlayOrder();
    // The a/b part applied immediately; expired, so the late layer keeps its
    // natural (appended) position instead of moving to the bottom.
    expect(getOverlayOrder()).toEqual(['b', 'a', 'missing']);
    vi.useRealTimers();
  });
});
