// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { Scene3DCameraState } from '../scene3d/scene3dEngine.ts';
import { FLYBY_VERSION, flybyProblems, normalizeFlybyDocument, type FlybyDocument } from './flybyDocument.ts';
import { expandFlyby } from './flybyExpand.ts';
import { cavesOfFilterDocument, featuresOfTypesDocument } from './flybyFilters.ts';
import { FlybyFilterEmpty, prepareFlyby, type FlybyPrepareStage } from './flybyPrepare.ts';
import { buildPreset, DEFAULT_FLYBY_PRESET_OPTIONS, presetFits, presetFor } from './flybyPresets.ts';
import type { FlybyWorld } from './flybySubjects.ts';
import { flybySectionAt } from './flybyTimeline.ts';

const FILTER = '0b0d6f8e-6b1c-4c1a-9a35-2f7f1c1e0a01';

/** A tour of a filter's caves: hop and circle each, a caption and a change on the circle, and a rise to all. */
function filterTour(): FlybyDocument {
  return {
    version: FLYBY_VERSION,
    name: '',
    speed: 1,
    loop: false,
    subjects: [{ kind: 'filter', savedFilterId: FILTER, limit: 12 }],
    start: { subject: 0, pose: { heading: 0, pitch: -40, range: 2, lift: 0, clearance: 100 } },
    sections: [
      { id: 'hop-1', kind: 'hop', subject: 0, seconds: 4, to: { range: 2.8 }, each: true },
      { id: 'orbit-2', kind: 'orbit', subject: 0, seconds: 6, to: { headingBy: 360 }, each: true },
      { id: 'rise-3', kind: 'rise', subject: 0, seconds: 5, to: { range: 2.2 }, rest: true },
    ],
    changes: [{ id: 'sky-1', kind: 'sky', colour: '#112233', section: 'orbit-2', at: 1, seconds: 1 }],
    texts: { captions: [{ section: 'orbit-2', text: 'Round', at: 0, seconds: 2 }] },
  };
}

describe('a flyby about the caves a saved filter keeps', () => {
  it('is read back from a file with the filter and the number, and nothing else about the caves', () => {
    const read = normalizeFlybyDocument(JSON.parse(JSON.stringify(filterTour())));
    expect(read?.subjects).toEqual([{ kind: 'filter', savedFilterId: FILTER, limit: 12 }]);
    expect(read?.sections.map((section) => section.each ?? false)).toEqual([true, true, false]);
  });

  it('refuses a number of caves no flyby can visit, and "for each" over a subject that is not a filter', () => {
    const tooMany = { ...filterTour(), subjects: [{ kind: 'filter' as const, savedFilterId: FILTER, limit: 41 }] };
    expect(flybyProblems(tooMany)).toContainEqual({ code: 'badSubject', subject: 0 });
    const notAFilter: FlybyDocument = { ...filterTour(), subjects: [{ kind: 'cave', caveId: 'a' }] };
    expect(flybyProblems(notAFilter)).toContainEqual({ code: 'badEach', section: 'hop-1' });
    expect(flybyProblems(filterTour())).toEqual([]);
  });

  it('writes the run of sections out once per cave, changes and captions with it, and keeps the rest', () => {
    const expanded = expandFlyby(filterTour(), new Map([[0, ['c1', 'c2', 'c3']]]));
    const { document } = expanded;
    expect(document.subjects).toEqual([
      { kind: 'filter', savedFilterId: FILTER, limit: 12 },
      { kind: 'cave', caveId: 'c1' },
      { kind: 'cave', caveId: 'c2' },
      { kind: 'cave', caveId: 'c3' },
    ]);
    expect(document.sections.map((section) => `${section.id}@${String(section.subject)}`)).toEqual([
      'hop-1~1@1',
      'orbit-2~1@1',
      'hop-1~2@2',
      'orbit-2~2@2',
      'hop-1~3@3',
      'orbit-2~3@3',
      'rise-3@0',
    ]);
    expect(document.sections.some((section) => section.each)).toBe(false);
    expect(expanded.sectionOf).toEqual([0, 1, 0, 1, 0, 1, 2]);
    expect(expanded.groups.get(0)).toEqual([1, 2, 3]);
    expect(document.changes?.map((change) => `${change.id}@${change.section}`)).toEqual([
      'sky-1~1@orbit-2~1',
      'sky-1~2@orbit-2~2',
      'sky-1~3@orbit-2~3',
    ]);
    expect(document.texts?.captions?.map((caption) => caption.section)).toEqual(['orbit-2~1', 'orbit-2~2', 'orbit-2~3']);
    // The copy is a document that plays: every id is its own, every change is in its section.
    expect(flybyProblems(document)).toEqual([]);
  });

  it('leaves a document without a filter exactly as it was', () => {
    const plain: FlybyDocument = { ...filterTour(), subjects: [{ kind: 'cave', caveId: 'a' }], sections: [filterTour().sections[2]] };
    expect(expandFlyby(plain, new Map()).document).toBe(plain);
  });

  it('is toured by the tour preset, written once for all of its caves', () => {
    const subjects = [{ kind: 'filter' as const, savedFilterId: FILTER, limit: 5 }];
    expect(presetFor(subjects)).toBe('tour');
    expect(presetFits('tour', subjects)).toBe(true);
    expect(presetFits('descent', subjects)).toBe(false);
    const built = buildPreset('tour', subjects, { ...DEFAULT_FLYBY_PRESET_OPTIONS, pauseSeconds: 2, pauseSay: 'name' });
    expect(built.sections.map((section) => `${section.kind}${section.each ? '*' : ''}`)).toEqual(['hop*', 'orbit*', 'hold*', 'rise']);
    expect(flybyProblems(built)).toEqual([]);
  });
});

describe('the questions a flyby puts to the filter routes', () => {
  it('asks a saved filter for its caves alone, in its own order', () => {
    const asked = cavesOfFilterDocument({
      version: 1,
      scope: [
        { world: 'document', where: null },
        {
          world: 'feature',
          where: { node: 'allOf', of: [{ node: 'condition', field: 'region', op: 'contains', values: [{ type: 'text', value: 'Apuseni' }] }] },
        },
      ],
      sort: 'title',
      descending: false,
    });
    expect(asked).toEqual({
      version: 1,
      scope: [
        {
          world: 'feature',
          where: {
            node: 'allOf',
            of: [
              { node: 'condition', field: 'region', op: 'contains', values: [{ type: 'text', value: 'Apuseni' }] },
              { node: 'condition', field: 'kind', op: 'equals', values: [{ type: 'id', value: 'cave' }] },
            ],
          },
        },
      ],
      sort: 'title',
      descending: false,
    });
    expect(cavesOfFilterDocument({ version: 1, scope: [{ world: 'trip', where: null }], sort: 'updated', descending: true })).toBeUndefined();
  });

  it('writes feature types as the type condition the feature list writes', () => {
    expect(featuresOfTypesDocument([7]).scope).toEqual([
      { world: 'feature', where: { node: 'condition', field: 'typeId', op: 'equals', values: [{ type: 'id', value: '7' }] } },
    ]);
    expect(featuresOfTypesDocument([9, 3, 9]).scope[0].where).toEqual({
      node: 'condition',
      field: 'typeId',
      op: 'in',
      values: [
        { type: 'id', value: '3' },
        { type: 'id', value: '9' },
      ],
    });
  });
});

describe('getting a tour of a filter\'s caves ready', () => {
  const caveAt = (caveId: string) => {
    const east = 25 + Number(caveId.slice(1)) * 0.01;
    return { caveId, bounds: [east, 45.5, east + 0.002, 45.502] as [number, number, number, number], top: 990, bottom: 900 };
  };
  const world = (caves: string[] | undefined): FlybyWorld & { asked: [string, number][] } => {
    const asked: [string, number][] = [];
    return {
      asked,
      caveInView: (caveId) => caveAt(caveId),
      caveFacts: async () => undefined,
      featureBounds: async () => undefined,
      groundHeight: () => 1000,
      viewTarget: () => undefined,
      filterCaves: async (savedFilterId, limit) => {
        asked.push([savedFilterId, limit]);
        return caves;
      },
    };
  };
  const stage: FlybyPrepareStage = {
    setCamera: (_camera: Scene3DCameraState) => undefined,
    refreshData: () => undefined,
    groundHeight: () => 1000,
    settle: async () => true,
  };

  it('asks for the caves when it is prepared, for the reader, and flies to each in the order given', async () => {
    const found = world(['c2', 'c1']);
    const prepared = await prepareFlyby(filterTour(), found, stage, { loadRoute: false });
    expect(found.asked).toEqual([[FILTER, 12]]);
    expect(prepared.filterCaves.get(0)).toEqual(['c2', 'c1']);
    // Two sections flown for each of two caves, and the rise: five spans, 25 seconds.
    expect(prepared.timeline.spans.map((span) => span.id)).toEqual(['hop-1~1', 'orbit-2~1', 'hop-1~2', 'orbit-2~2', 'rise-3']);
    expect(prepared.timeline.length).toBe(25);
    // The opening looks at all of them together: the filter subject stands for its caves as one.
    expect(prepared.resolved[0]?.centre.lon).toBeCloseTo((25.02 + 25.012) / 2, 2);
    // And the camera circles the first cave the filter gave first.
    expect(flybySectionAt(prepared.timeline, 5)).toBe(1);
    expect(prepared.expanded.document.sections[1].subject).toBe(1);
    expect(prepared.expanded.document.subjects[1]).toEqual({ kind: 'cave', caveId: 'c2' });
  });

  it('says so when the filter keeps no cave for this reader, or cannot be run for them', async () => {
    await expect(prepareFlyby(filterTour(), world([]), stage, { loadRoute: false })).rejects.toBeInstanceOf(FlybyFilterEmpty);
    await expect(prepareFlyby(filterTour(), world(undefined), stage, { loadRoute: false })).rejects.toBeInstanceOf(FlybyFilterEmpty);
  });
});
