// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it, vi } from 'vitest';
import { clusterLabelFor, syncLiveMarkers, type DrawnMarker } from './liveMarkerSync.ts';
import type { TrackedCaver } from './trackedCavers.ts';

function fakeViewer() {
  return {
    addLiveMarker: vi.fn(() => null),
    moveLiveMarker: vi.fn(() => null),
    removeLiveMarker: vi.fn(() => true),
  };
}

const marker = (station: string, label: DrawnMarker['label'] = 'Ana', color = '#3ab5b5'): DrawnMarker => ({
  station,
  label,
  color,
});

describe('syncLiveMarkers', () => {
  it('adds, moves and removes only what differs, and returns what is now drawn', () => {
    const viewer = fakeViewer();
    const drawn = new Map([
      ['a', marker('p.1')],
      ['b', marker('p.2', 'Bogdan')],
      ['c', marker('p.3', 'Cora')],
    ]);
    const wanted = new Map([
      ['a', marker('p.1')],
      ['b', marker('p.4', 'Bogdan')],
      ['d', marker('p.5', 'Dan')],
    ]);
    const next = syncLiveMarkers(viewer, drawn, wanted);

    expect(viewer.addLiveMarker).toHaveBeenCalledExactlyOnceWith('d', 'p.5', { label: 'Dan', color: '#3ab5b5' });
    expect(viewer.moveLiveMarker).toHaveBeenCalledExactlyOnceWith('b', 'p.4', { label: 'Bogdan', color: '#3ab5b5' });
    expect(viewer.removeLiveMarker).toHaveBeenCalledExactlyOnceWith('c');
    expect([...next.keys()]).toEqual(['a', 'b', 'd']);
    expect(next).not.toBe(wanted);
  });

  it('moves a marker whose label or colour changed in place', () => {
    const viewer = fakeViewer();
    syncLiveMarkers(
      viewer,
      new Map([['a', marker('p.1')], ['b', marker('p.1', 'Bogdan')]]),
      new Map([['a', marker('p.1', 'Ana (out)')], ['b', marker('p.1', 'Bogdan', '#9a9a9a')]]),
    );
    expect(viewer.moveLiveMarker).toHaveBeenCalledTimes(2);
    expect(viewer.addLiveMarker).not.toHaveBeenCalled();
  });

  it('compares a label of several lines by its lines, not by identity', () => {
    const viewer = fakeViewer();
    syncLiveMarkers(
      viewer,
      new Map([['a', marker('p.1', ['Team', 'Ana'])]]),
      new Map([['a', marker('p.1', ['Team', 'Ana'])]]),
    );
    expect(viewer.moveLiveMarker).not.toHaveBeenCalled();
    syncLiveMarkers(
      viewer,
      new Map([['a', marker('p.1', ['Team', 'Ana'])]]),
      new Map([['a', marker('p.1', ['Team', 'Ana', 'Bogdan'])]]),
    );
    expect(viewer.moveLiveMarker).toHaveBeenCalledOnce();
  });

  it('gives a duration on moves only, and only when one is asked for', () => {
    const viewer = fakeViewer();
    syncLiveMarkers(
      viewer,
      new Map([['a', marker('p.1')]]),
      new Map([['a', marker('p.2')], ['b', marker('p.3', 'Bogdan')]]),
      { duration: 0 },
    );
    expect(viewer.moveLiveMarker).toHaveBeenCalledWith('a', 'p.2', { label: 'Ana', color: '#3ab5b5', duration: 0 });
    expect(viewer.addLiveMarker).toHaveBeenCalledWith('b', 'p.3', { label: 'Bogdan', color: '#3ab5b5' });
  });
});

const caver = (overrides: Partial<TrackedCaver>): TrackedCaver => ({
  caverId: 'caver-1',
  name: 'Ana',
  teamId: 'team-a',
  teamTitle: 'Team A',
  position: { kind: 'station', station: 'p.1' },
  lastRecordedAt: null,
  positionAt: null,
  enteredAt: null,
  out: false,
  ...overrides,
});

describe('clusterLabelFor', () => {
  const lineOf = (member: TrackedCaver) => (member.out ? `${member.name} (out)` : member.name);

  it('heads one team with its title and lists whoever is underground first', () => {
    const members = [
      caver({ caverId: 'a', name: 'Ana', out: true }),
      caver({ caverId: 'b', name: 'Bogdan' }),
    ];
    expect(clusterLabelFor(members, lineOf)).toEqual(['Team A', 'Bogdan', 'Ana (out)']);
  });

  it('gives no heading to two teams, even ones titled alike', () => {
    const members = [
      caver({ caverId: 'a', name: 'Ana' }),
      caver({ caverId: 'b', name: 'Bogdan', teamId: 'team-of-another-trip' }),
    ];
    expect(clusterLabelFor(members, lineOf)).toEqual(['Ana', 'Bogdan']);
  });

  it('answers null for nobody', () => {
    expect(clusterLabelFor([], lineOf)).toBeNull();
  });
});
