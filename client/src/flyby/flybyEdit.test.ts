// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { flybyProblems, FLYBY_SECTION_KINDS, type FlybyDocument } from './flybyDocument.ts';
import {
  addSection,
  duplicateSection,
  FLYBY_ADDABLE_KINDS,
  moveSection,
  newSectionId,
  removeSection,
  sectionDefaults,
  updateSection,
} from './flybyEdit.ts';
import { buildPreset } from './flybyPresets.ts';
import type { FlybyResolvedSubject } from './flybySubjects.ts';
import { compileFlyby, flybyCameraAt } from './flybyTimeline.ts';

function tour(): FlybyDocument {
  const document = buildPreset('tour', [
    { kind: 'cave', caveId: 'a' },
    { kind: 'cave', caveId: 'b' },
  ]);
  return {
    ...document,
    changes: [
      { id: 'c1', kind: 'backgroundOpacity', opacity: 0.5, section: 'orbit-2', at: 1, seconds: 2 },
      { id: 'c2', kind: 'backgroundOpacity', opacity: 1, section: 'orbit-4', at: 1, seconds: 2 },
    ],
    texts: { captions: [{ section: 'orbit-2', text: 'First', at: 0, seconds: 3 }] },
  };
}

const ids = (document: FlybyDocument) => document.sections.map((section) => section.id);

const subject = (lon: number): FlybyResolvedSubject => ({
  centre: { lon, lat: 45.5, height: 900 },
  radius: 200,
  halfHeight: 50,
  coarse: false,
});

describe('editing the sections of a flyby', () => {
  it('starts with the sections a tour is built from', () => {
    expect(ids(tour())).toEqual(['approach-1', 'orbit-2', 'hop-3', 'orbit-4', 'rise-5']);
  });

  it('adds a section of any kind with an id of its own, about the subject before it', () => {
    const added = addSection(tour(), 'tilt', 1);
    expect(ids(added)).toEqual(['approach-1', 'orbit-2', 'tilt-6', 'hop-3', 'orbit-4', 'rise-5']);
    expect(added.sections[2]).toEqual({ id: 'tilt-6', kind: 'tilt', subject: 0, ...sectionDefaults('tilt') });
    // At the end when no place is given; a hop goes on to the next subject.
    const hop = addSection(tour(), 'hop');
    expect(hop.sections.at(-1)).toMatchObject({ kind: 'hop', subject: 0 });
    expect(addSection(tour(), 'hop', 1).sections[2]).toMatchObject({ kind: 'hop', subject: 1 });
    expect(flybyProblems(added)).toEqual([]);
  });

  it('gives every addable kind sound defaults', () => {
    for (const kind of FLYBY_ADDABLE_KINDS) {
      expect(FLYBY_SECTION_KINDS).toContain(kind);
      expect(flybyProblems(addSection(tour(), kind))).toEqual([]);
    }
  });

  it('never reuses an id', () => {
    const document = tour();
    expect(newSectionId(document, 'orbit')).toBe('orbit-6');
    const twice = addSection(addSection(document, 'orbit'), 'orbit');
    expect(new Set(ids(twice)).size).toBe(twice.sections.length);
  });

  it('removes a section with its changes and its caption, and nothing else', () => {
    const removed = removeSection(tour(), 'orbit-2');
    expect(ids(removed)).toEqual(['approach-1', 'hop-3', 'orbit-4', 'rise-5']);
    expect(removed.changes?.map((change) => change.id)).toEqual(['c2']);
    expect(removed.texts?.captions).toEqual([]);
    expect(flybyProblems(removed)).toEqual([]);
  });

  it('duplicates a section straight after it, its changes staying with the original', () => {
    const copied = duplicateSection(tour(), 'orbit-2');
    expect(ids(copied)).toEqual(['approach-1', 'orbit-2', 'orbit-6', 'hop-3', 'orbit-4', 'rise-5']);
    expect(copied.sections[2]).toEqual({ ...copied.sections[1], id: 'orbit-6' });
    expect(copied.sections[2].to).not.toBe(copied.sections[1].to);
    expect(copied.changes).toEqual(tour().changes);
  });

  it('moves a section up and down, its changes moving with it', () => {
    const down = moveSection(tour(), 'orbit-2', 1);
    expect(ids(down)).toEqual(['approach-1', 'hop-3', 'orbit-2', 'orbit-4', 'rise-5']);
    expect(moveSection(down, 'orbit-2', -2).sections.map((section) => section.id)[0]).toBe('orbit-2');
    // Past either end it stops there, and nothing changes.
    expect(moveSection(tour(), 'approach-1', -1)).toEqual(tour());
    expect(moveSection(tour(), 'rise-5', 3)).toEqual(tour());
    expect(down.changes).toEqual(tour().changes);
  });

  it('changes what a section does, and a member taken out is carried over from before', () => {
    const changed = updateSection(tour(), 'orbit-2', {
      subject: 1,
      rest: true,
      to: { headingBy: -720, pitch: -50, range: 4, aim: 'top', lift: 0.5, clearance: null },
    });
    expect(changed.sections[1]).toEqual({
      id: 'orbit-2',
      kind: 'orbit',
      subject: 1,
      seconds: 24,
      rest: true,
      to: { headingBy: -720, pitch: -50, range: 4, aim: 'top', lift: 0.5, clearance: null },
    });
    const cleared = updateSection(changed, 'orbit-2', { rest: false, speed: 1, to: { pitch: undefined, aim: undefined } });
    expect(cleared.sections[1]).toEqual({
      id: 'orbit-2',
      kind: 'orbit',
      subject: 1,
      seconds: 24,
      to: { headingBy: -720, range: 4, lift: 0.5, clearance: null },
    });
    // A turn is a heading to face or an amount to turn by, never both.
    const facing = updateSection(changed, 'orbit-2', { to: { heading: 90 } });
    expect(facing.sections[1].to.headingBy).toBeUndefined();
    expect(updateSection(facing, 'orbit-2', { to: { headingBy: 45 } }).sections[1].to.heading).toBeUndefined();
  });

  it('flies what was edited: a camera told to look from further away is further away', () => {
    const document = buildPreset('aroundCave', [{ kind: 'cave', caveId: 'a' }]);
    const resolved = [subject(25)];
    const before = compileFlyby(document, resolved);
    const after = compileFlyby(updateSection(document, 'orbit-2', { to: { range: 6 } }), resolved);
    const distance = (timeline: typeof before, t: number) => {
      const camera = flybyCameraAt(timeline, t, () => 0);
      return Math.hypot(
        (camera.longitude - 25) * 78_000,
        (camera.latitude - 45.5) * 111_000,
        camera.height - 900,
      );
    };
    expect(distance(before, before.length)).toBeLessThan(700);
    expect(distance(after, after.length)).toBeGreaterThan(1100);
  });

  it('reports a pose outside its bounds against its section', () => {
    expect(flybyProblems(updateSection(tour(), 'orbit-2', { to: { lift: 40 } }))).toEqual([{ code: 'badPose', section: 'orbit-2' }]);
    expect(flybyProblems(updateSection(tour(), 'orbit-2', { to: { clearance: -5 } }))).toEqual([{ code: 'badPose', section: 'orbit-2' }]);
    expect(flybyProblems(updateSection(tour(), 'orbit-2', { to: { headingBy: 99_999 } }))).toEqual([{ code: 'badPose', section: 'orbit-2' }]);
  });
});
