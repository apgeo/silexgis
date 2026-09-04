// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  partFromLeg,
  partFromStation,
  partFromSurvey,
  pathOf,
  sectionForRef,
  shortNameOf,
} from './modelParts.ts';

/** A survey-tree node as the vendored viewer hands one over. */
function node(path: string | null, name?: string) {
  return {
    getPath: () => path,
    name,
  };
}

describe('pathOf', () => {
  it('prefers the full path, because a bare station name is not unique in a real survey', () => {
    // Two chambers each having a station called `1` is the ordinary case. Storing the bare name
    // would resolve to whichever the viewer found first.
    expect(pathOf(node('pestera.sala-mare.1'))).toBe('pestera.sala-mare.1');
  });

  it('falls back to the name when there is no path to have', () => {
    expect(pathOf(node(null, 'entrance'))).toBe('entrance');
  });

  it('will not name a node that says nothing', () => {
    expect(pathOf(node(null))).toBeNull();
    expect(pathOf(node(''))).toBeNull();
    expect(pathOf({})).toBeNull();
    expect(pathOf(null)).toBeNull();
    expect(pathOf('a station')).toBeNull();
  });
});

describe('shortNameOf', () => {
  it('is the last segment, or the whole thing when there is only one', () => {
    expect(shortNameOf('pestera.sala-mare.12')).toBe('12');
    expect(shortNameOf('12')).toBe('12');
  });
});

describe('partFromStation', () => {
  it('anchors to the station the viewer names', () => {
    expect(partFromStation(node('pestera.galerie.7'))).toEqual({
      anchorKind: 'modelStation',
      anchor: { station: 'pestera.galerie.7' },
      label: 'pestera.galerie.7',
    });
  });

  it('refuses a station it cannot name', () => {
    expect(partFromStation(node(null))).toBeNull();
  });
});

describe('partFromSurvey', () => {
  it('anchors to the survey the viewer names', () => {
    expect(partFromSurvey(node('pestera.galerie'))).toEqual({
      anchorKind: 'modelSurvey',
      anchor: { survey: 'pestera.galerie' },
      label: 'pestera.galerie',
    });
  });
});

describe('partFromLeg', () => {
  it('stores a leg as the run between the two stations it connects', () => {
    // A leg's own identity is an index into geometry that was just built, and means nothing after
    // a re-import. Which two stations it runs between does not change, and is already storable.
    const leg = { start: () => node('p.g.6'), end: () => node('p.g.7') };
    expect(partFromLeg(leg)).toEqual({
      anchorKind: 'modelStationRange',
      anchor: { fromStation: 'p.g.6', toStation: 'p.g.7' },
      label: '6 → 7',
    });
  });

  it('refuses a splay, whose far end was never a station', () => {
    // The case that must not be half-recorded: an anchor naming one end and inventing the other
    // would read as exact while pointing at nothing.
    const splay = { start: () => node('p.g.6'), end: () => node(null) };
    expect(partFromLeg(splay)).toBeNull();
  });

  it('refuses a leg that goes nowhere, or one it cannot read at all', () => {
    expect(partFromLeg({ start: () => node('p.g.6'), end: () => node('p.g.6') })).toBeNull();
    expect(partFromLeg({})).toBeNull();
    expect(partFromLeg(null)).toBeNull();
  });
});

describe('sectionForRef', () => {
  const MODEL = 'model-1';
  const ref = (anchorKind: string, anchor: unknown, targetId = MODEL) => ({
    targetType: 'surveyModel',
    targetId,
    anchorKind,
    anchor,
  });

  it('answers a station and a survey by the field that names one', () => {
    expect(sectionForRef(ref('modelStation', { station: 'p.g.7' }), MODEL)).toBe('p.g.7');
    expect(sectionForRef(ref('modelSurvey', { survey: 'p.g' }), MODEL)).toBe('p.g');
  });

  it('answers a run of stations with the station it starts from', () => {
    // Reduced rather than refused: the viewer shows one section, and the start is where somebody
    // following "the passage from 6 to 7" wants to be standing. Refusing reads as a dead link.
    expect(
      sectionForRef(ref('modelStationRange', { fromStation: 'p.g.6', toStation: 'p.g.7' }), MODEL),
    ).toBe('p.g.6');
  });

  it('refuses a run of surveys, which has no start to reduce to', () => {
    expect(
      sectionForRef(ref('modelSurveyRange', { fromSurvey: 'p.a', toSurvey: 'p.b' }), MODEL),
    ).toBeNull();
  });

  it('refuses another cave’s model, and anything but a model', () => {
    // The rule that stops a viewer jumping to a station name that happens to exist in whatever
    // cave it has open.
    expect(sectionForRef(ref('modelStation', { station: 'p.g.7' }, 'model-2'), MODEL)).toBeNull();
    expect(
      sectionForRef({ targetType: 'document', targetId: MODEL, anchorKind: 'modelStation', anchor: { station: 'x' } }, MODEL),
    ).toBeNull();
    expect(sectionForRef(ref('modelStation', { station: 'p.g.7' }), undefined)).toBeNull();
  });

  it('refuses a payload that does not carry the field its kind needs', () => {
    expect(sectionForRef(ref('modelStation', {}), MODEL)).toBeNull();
    expect(sectionForRef(ref('modelStation', { station: '' }), MODEL)).toBeNull();
    expect(sectionForRef(ref('modelStation', null), MODEL)).toBeNull();
    expect(sectionForRef(ref('whole', null), MODEL)).toBeNull();
  });
});
