// SPDX-License-Identifier: AGPL-3.0-or-later
import { beforeEach, describe, expect, it } from 'vitest';
import { requestReveal3d, takePendingReveal3d } from './pendingReveal3d.ts';

const cave = { targetType: 'feature', targetId: 'cave-1', label: 'Coiba Mare' };
const other = { targetType: 'feature', targetId: 'cave-2' };

beforeEach(() => {
  // The slot is module state, so a test that leaves a request behind would hand it to the next.
  takePendingReveal3d();
});

describe('pendingReveal3d', () => {
  it('hands a request over once, and then has nothing', () => {
    requestReveal3d(cave);

    expect(takePendingReveal3d()).toEqual(cave);
    // A scene remounted later must not fly back to a cave the viewer asked for minutes ago.
    expect(takePendingReveal3d()).toBeUndefined();
  });

  it('has nothing to hand over when nothing was asked', () => {
    expect(takePendingReveal3d()).toBeUndefined();
  });

  it('keeps only the latest request when two are made before a scene takes one', () => {
    requestReveal3d(cave);
    requestReveal3d(other);

    // The viewer changed their mind; the scene opens on the second place and never visits the
    // first.
    expect(takePendingReveal3d()).toEqual(other);
    expect(takePendingReveal3d()).toBeUndefined();
  });
});
