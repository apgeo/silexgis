// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { flybyRouteCaves, flybyWallsQuestion, type FlybyWallsAsk } from './flybyWalls.ts';

describe('walls on a flyby\'s route, asked about once', () => {
  const walls = (sizes: Record<string, number>): FlybyWallsAsk & { asked: string[][] } => {
    const asked: string[][] = [];
    return {
      asked,
      pending: (caveIds) => {
        asked.push([...caveIds]);
        return caveIds.flatMap((caveId) => (sizes[caveId] !== undefined ? [{ caveId, bytes: sizes[caveId] }] : []));
      },
      agree: () => undefined,
    };
  };

  it('gathers every cave on the route whose walls wait for a yes into one question, with their size', () => {
    const route = {
      subjects: [
        { kind: 'cave' as const, caveId: 'a' },
        { kind: 'view' as const },
        { kind: 'cave' as const, caveId: 'b' },
        { kind: 'cave' as const, caveId: 'a' },
      ],
    };
    expect(flybyRouteCaves(route)).toEqual(['a', 'b']);
    const view = walls({ a: 60_000_000, b: 80_000_000 });
    expect(flybyWallsQuestion(route, view)).toEqual({ caveIds: ['a', 'b'], bytes: 140_000_000 });
    expect(view.asked).toEqual([['a', 'b']]);
  });

  it('asks nothing when no walls on the route wait for a yes', () => {
    expect(flybyWallsQuestion({ subjects: [{ kind: 'cave', caveId: 'a' }] }, walls({}))).toBeUndefined();
  });
});
