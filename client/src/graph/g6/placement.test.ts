import { describe, expect, it } from 'vitest';
import type { Point } from '../renderer.ts';
import { MIN_GAP, RING_RADIUS, placeNewNodes } from './placement.ts';

const at = (x: number, y: number): Point => ({ x, y });
const dist = (a: Point, b: Point): number => Math.hypot(a.x - b.x, a.y - b.y);

describe('placeNewNodes', () => {
  it('puts children of the middle node on a ring round it', () => {
    const positions = new Map([['s', at(400, 300)]]);
    const out = placeNewNodes({
      added: ['a', 'b', 'c', 'd'],
      edges: ['a', 'b', 'c', 'd'].map((k) => ({ source: 's', target: k })),
      positions,
      anchorKey: 's',
      center: at(400, 300),
    });
    expect(out.size).toBe(4);
    for (const p of out.values()) {
      expect(dist(p, at(400, 300))).toBeGreaterThanOrEqual(RING_RADIUS - 1);
    }
  });

  it('never returns or moves a node that already has a position', () => {
    const positions = new Map([['s', at(0, 0)], ['a', at(50, 50)]]);
    const out = placeNewNodes({
      added: ['a', 'b'],
      edges: [{ source: 's', target: 'a' }, { source: 'a', target: 'b' }],
      positions,
      anchorKey: 'a',
      center: at(0, 0),
    });
    expect([...out.keys()]).toEqual(['b']);
    expect(positions.get('a')).toEqual(at(50, 50));
  });

  it('fans new nodes away from the rest of the picture', () => {
    // s is the heart, a sits to its right; a's children should land further right than a.
    const positions = new Map([['s', at(0, 0)], ['a', at(200, 0)]]);
    const out = placeNewNodes({
      added: ['x', 'y'],
      edges: [{ source: 'a', target: 'x' }, { source: 'a', target: 'y' }],
      positions,
      anchorKey: 'a',
      center: at(0, 0),
    });
    for (const p of out.values()) {
      expect(p.x).toBeGreaterThan(200);
    }
  });

  it('chains through a new node to a newer one', () => {
    const out = placeNewNodes({
      added: ['b', 'c'],
      edges: [{ source: 's', target: 'b' }, { source: 'b', target: 'c' }],
      positions: new Map([['s', at(0, 0)]]),
      center: at(0, 0),
    });
    expect(out.has('b') && out.has('c')).toBe(true);
    expect(dist(out.get('c')!, out.get('b')!)).toBeGreaterThan(MIN_GAP);
  });

  it('keeps new nodes clear of one another and of placed nodes', () => {
    const positions = new Map([['s', at(0, 0)]]);
    const keys = Array.from({ length: 20 }, (_, i) => `n${i}`);
    const out = placeNewNodes({
      added: keys,
      edges: keys.map((k) => ({ source: 's', target: k })),
      positions,
      anchorKey: 's',
      center: at(0, 0),
    });
    const all = [at(0, 0), ...out.values()];
    for (let i = 0; i < all.length; i += 1) {
      for (let j = i + 1; j < all.length; j += 1) {
        expect(dist(all[i], all[j])).toBeGreaterThan(MIN_GAP * 0.8);
      }
    }
  });

  it('places an island that no placed node leads to', () => {
    const out = placeNewNodes({
      added: ['lonely'],
      edges: [],
      positions: new Map([['s', at(0, 0)]]),
      center: at(0, 0),
    });
    expect(out.get('lonely')).toBeDefined();
  });

  it('is deterministic', () => {
    const run = () =>
      placeNewNodes({
        added: ['a', 'b', 'c'],
        edges: ['a', 'b', 'c'].map((k) => ({ source: 's', target: k })),
        positions: new Map([['s', at(10, 10)]]),
        anchorKey: 's',
        center: at(10, 10),
      });
    expect([...run()]).toEqual([...run()]);
  });
});
