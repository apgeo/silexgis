// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  FLYBY_BUNDLE_FPS,
  FLYBY_BUNDLE_MAX_CAVES,
  FLYBY_BUNDLE_MAX_FRAMES,
  flybyBundleFrames,
  flybyBundleOmissions,
  flybyBundleReliefMarks,
  flybyBundleRequest,
  flybyBundleShows,
  flybyBundleTexts,
  flybyBundleUnderground,
  type FlybyBundleAsk,
} from './flybyBlenderBundle.ts';
import { FLYBY_VERSION, type FlybyDocument, type FlybySection } from './flybyDocument.ts';
import { flybyLookPlan, type FlybyLookBase } from './flybyLook.ts';
import { flybyFrameSchedule } from './flybyMovie.ts';
import type { FlybyResolvedSubject } from './flybySubjects.ts';
import { compileFlyby, flybyCameraAt } from './flybyTimeline.ts';

// What a flyby is turned into when it is handed over as a bundle for Blender: the camera at every
// frame, the look at the first and what changes of it afterwards, the texts on the clip's own
// clock — and the list of what the bundle does not carry. Everything here is invented: two boxes
// for caves, flat ground.

const cave: FlybyResolvedSubject = { centre: { lon: 25, lat: 45.5, height: 900 }, radius: 200, halfHeight: 100, coarse: false };
const other: FlybyResolvedSubject = { centre: { lon: 25.05, lat: 45.52, height: 1100 }, radius: 300, halfHeight: 60, coarse: false };
const flat = () => 0;

const SECTIONS: FlybySection[] = [
  { id: 'in', kind: 'approach', subject: 0, seconds: 4, to: { range: 2 } },
  { id: 'round', kind: 'orbit', subject: 0, seconds: 6, to: { headingBy: 180 } },
];

function flyby(overrides: Partial<FlybyDocument> = {}): FlybyDocument {
  return {
    version: FLYBY_VERSION,
    name: 'Round one cave',
    speed: 1,
    loop: false,
    subjects: [
      { kind: 'cave', caveId: 'a' },
      { kind: 'cave', caveId: 'b' },
    ],
    start: { subject: 0, pose: { heading: 0, pitch: -30, range: 5, lift: 0, clearance: null } },
    sections: SECTIONS,
    ...overrides,
  };
}

/** A viewer with walls and lines on and nothing else; `on` switches more layers on. */
function viewer(on: string[] = [], extra: Partial<FlybyLookBase> = {}): FlybyLookBase {
  const layers = Object.fromEntries(
    ['walls', 'centerlines', 'entrances', 'surface-features', 'scans', 'photos', ...on].map((key) => [
      key,
      { visible: key === 'walls' || key === 'centerlines' || on.includes(key), opacity: 1 },
    ]),
  );
  return { backgroundOpacity: 1, surface: 'overlay', layers, ...extra };
}

function ask(document: FlybyDocument, overrides: Partial<FlybyBundleAsk> = {}): FlybyBundleAsk {
  const timeline = compileFlyby(document, [cave, other]);
  return {
    name: document.name,
    timeline,
    schedule: flybyFrameSchedule(timeline.length, FLYBY_BUNDLE_FPS),
    size: { width: 1280, height: 720 },
    quality: 'medium',
    groundHeight: flat,
    base: viewer(),
    flybyRelief: true,
    placement: { absolute: false, offsetM: 0 },
    routeCaves: ['a', 'b'],
    cavesInView: ['a', 'c', 'd'],
    surveyTop: (caveId) => (caveId === 'a' ? 985 : undefined),
    groundAt: (caveId) => (caveId === 'c' ? 640 : undefined),
    depthBands: [
      { fromMeters: 0, color: '#d9480f' },
      { fromMeters: 50, color: '#8c2f6b' },
    ],
    reliefWords: (factor) => `Relief ×${factor}`,
    ...overrides,
  };
}

describe('the camera of every frame', () => {
  it('is the camera the player and the movie recorder place, frame for frame', () => {
    const timeline = compileFlyby(flyby(), [cave, other]);
    const schedule = flybyFrameSchedule(timeline.length, 25);
    const frames = flybyBundleFrames(timeline, schedule, flat, () => 60);

    expect(frames).toHaveLength(250);
    for (const index of [0, 1, 99, 100, 249]) {
      const camera = flybyCameraAt(timeline, index / 25, flat);
      const [longitude, latitude, height, heading, pitch, roll, focus, lens] = frames[index];
      expect(longitude).toBeCloseTo(camera.longitude, 9);
      expect(latitude).toBeCloseTo(camera.latitude, 9);
      expect(height).toBeCloseTo(camera.height, 3);
      expect(heading).toBeCloseTo(camera.heading, 6);
      expect(pitch).toBeCloseTo(camera.pitch, 6);
      expect(roll).toBe(0);
      expect(focus).toBeGreaterThan(0);
      expect(lens).toBe(60);
    }
    // The distance the path keeps to its subject: five radii at the start, two after the approach.
    expect(frames[0][6]).toBeCloseTo(1000, 1);
    expect(frames[249][6]).toBeCloseTo(400, 0);
  });

  it('is in true heights whatever the relief: the factor travels beside it', () => {
    const plain = flybyBundleRequest(ask(flyby()));
    const tall = flybyBundleRequest(ask(flyby({ look: { relief: 2 } })));
    expect(tall.request.relief).toBe(2);
    expect(plain.request.relief).toBe(1);
    expect(tall.request.frames).toEqual(plain.request.frames);
  });

  it('follows a lens that changes, frame by frame', () => {
    const document = flyby({
      look: { lens: 60 },
      changes: [{ id: 'zoom', kind: 'lens', degrees: 30, section: 'round', at: 0, seconds: 4 }],
    });
    const { request, omissions } = flybyBundleRequest(ask(document));
    const lenses = request.frames.map((frame) => frame[7]);
    expect(lenses[0]).toBe(60);
    expect(lenses[100]).toBe(60);
    expect(lenses[150]).toBeGreaterThan(30);
    expect(lenses[150]).toBeLessThan(60);
    expect(lenses[249]).toBe(30);
    // Carried, so not something the bundle leaves out.
    expect(omissions).toEqual(['ground']);
  });

  it('takes the viewer\'s own lens where the flyby names none', () => {
    const { request } = flybyBundleRequest(ask(flyby(), { base: viewer([], { lens: 42 }) }));
    expect(new Set(request.frames.map((frame) => frame[7]))).toEqual(new Set([42]));
  });
});

describe('the scene a bundle is laid out about', () => {
  it('is the flyby\'s first subject, and the caves are placed as the view draws them', () => {
    const { request } = flybyBundleRequest(ask(flyby(), { placement: { absolute: true, offsetM: 37.5 } }));
    expect(request.origin).toEqual({ longitude: 25, latitude: 45.5, heightM: 900 });
    expect(request.placement).toEqual({ absolute: true, offsetM: 37.5 });
    expect(request.output).toEqual({ width: 1280, height: 720, fps: 25, quality: 'medium' });
  });

  it('asks walls for the flyby\'s own caves and lines alone for the others in view', () => {
    const { request } = flybyBundleRequest(ask(flyby()));
    expect(request.caves).toEqual([
      { caveId: 'a', walls: true, surveyTopM: 985, groundM: null },
      { caveId: 'b', walls: true, surveyTopM: null, groundM: null },
      { caveId: 'c', walls: false, surveyTopM: null, groundM: 640 },
      { caveId: 'd', walls: false, surveyTopM: null, groundM: null },
    ]);
  });

  it('leaves the other caves out where the look hides the lines they would be drawn by', () => {
    const base = viewer();
    base.layers.centerlines = { visible: false, opacity: 1 };
    const { request } = flybyBundleRequest(ask(flyby(), { base }));
    expect(request.caves.map((entry) => entry.caveId)).toEqual(['a', 'b']);
    expect(request.look?.lines).toBe(false);
  });

  it('asks no walls where the look hides them, and none of a cave whose walls the viewer declined', () => {
    const base = viewer();
    base.layers.walls = { visible: false, opacity: 1 };
    const hidden = flybyBundleRequest(ask(flyby(), { base }));
    expect(hidden.request.caves.every((entry) => !entry.walls)).toBe(true);
    expect(hidden.omissions).not.toContain('wallsNotDownloaded');

    const declined = flybyBundleRequest(ask(flyby(), { wallsDeclined: ['b'] }));
    expect(declined.request.caves.slice(0, 2).map((entry) => entry.walls)).toEqual([true, false]);
    expect(declined.omissions).toContain('wallsNotDownloaded');
    expect(declined.request.leftOut).toContain('wallsNotDownloaded');
  });

  it('names no more caves than a bundle takes, and says so', () => {
    const many = Array.from({ length: FLYBY_BUNDLE_MAX_CAVES + 20 }, (_, i) => `in-view-${i}`);
    const { request, omissions } = flybyBundleRequest(ask(flyby(), { cavesInView: many }));
    expect(request.caves).toHaveLength(FLYBY_BUNDLE_MAX_CAVES);
    // The flyby's own first: they are what it is about.
    expect(request.caves.slice(0, 2).map((entry) => entry.caveId)).toEqual(['a', 'b']);
    expect(omissions).toContain('moreCavesInView');
  });

  it('sends the lines\' colours by depth, or the one colour the flyby gives them', () => {
    const byDepth = flybyBundleRequest(ask(flyby())).request.look;
    expect(byDepth).toEqual({
      lines: true,
      sky: null,
      lineColour: null,
      depthBands: [
        { fromM: 0, colour: '#d9480f' },
        { fromM: 50, colour: '#8c2f6b' },
      ],
    });
    const oneColour = flybyBundleRequest(ask(flyby({ look: { lines: '#00ff88', sky: '#102030' } }))).request.look;
    expect(oneColour).toMatchObject({ lineColour: '#00ff88', sky: '#102030' });
  });
});

describe('what a bundle leaves out', () => {
  const schedule = { from: 0, to: 10 };

  it('is always the ground, and nothing else for a flyby that shows only caves', () => {
    expect(flybyBundleOmissions(flybyLookPlan(flyby()), viewer(), schedule)).toEqual(['ground']);
  });

  it('is every layer shown that is not walls or lines, the viewer\'s own and the flyby\'s', () => {
    const document = flyby({ look: { layers: { photos: { visible: true }, 'raster:old-map': { visible: true } } } });
    const omissions = flybyBundleOmissions(flybyLookPlan(document), viewer(['entrances', 'scans']), schedule, [
      'file:track-1',
      'filter:f1',
    ]);
    expect(omissions).toEqual(['ground', 'entrances', 'surfaceFeatures', 'scans', 'photos', 'mapLayers', 'tracks']);
  });

  it('is not a layer the flyby hides at its first frame, whatever the viewer has', () => {
    const document = flyby({ look: { layers: { entrances: { visible: false } } } });
    expect(flybyBundleOmissions(flybyLookPlan(document), viewer(['entrances']), schedule)).toEqual(['ground']);
  });

  it('is not a change of the look: the relief, the colours, the layers a bundle draws and the sky are carried', () => {
    const document = flyby({
      look: { relief: 1.5, walls: { colour: '#ff0000', strength: 0.5 }, layers: { walls: { opacity: 0.4 } } },
      changes: [
        { id: 'grow', kind: 'relief', factor: 3, section: 'round', at: 0, seconds: 3 },
        { id: 'sky', kind: 'sky', colour: '#223344', section: 'in', at: 1, seconds: 2 },
        { id: 'lit', kind: 'cave', subject: 0, colour: '#ffcc00', strength: 0.8, section: 'in', at: 2, seconds: 1 },
        { id: 'one', kind: 'lines', colour: '#00ff88', section: 'round', at: 1, seconds: 0 },
        { id: 'off', kind: 'layer', layer: 'centerlines', visible: false, section: 'round', at: 4, seconds: 1 },
        { id: 'half', kind: 'layerOpacity', layer: 'walls', opacity: 1, section: 'round', at: 0, seconds: 2 },
        { id: 'map', kind: 'background', background: 7, section: 'in', at: 0, seconds: 1 },
        { id: 'thin', kind: 'backgroundOpacity', opacity: 0.3, section: 'in', at: 0, seconds: 1 },
      ],
    });
    // The background is the ground's, and the ground is said once.
    expect(flybyBundleOmissions(flybyLookPlan(document), viewer(), schedule)).toEqual(['ground']);
    const { request, omissions } = flybyBundleRequest(ask(document));
    expect(omissions).toEqual(['ground']);
    expect(request.leftOut).toEqual(['ground']);
    expect(Object.entries(request.changes ?? {}).filter(([, said]) => said !== null).map(([member]) => member)).toEqual([
      'relief',
      'sky',
      'walls',
      'lines',
      'caves',
    ]);
  });

  it('is the opening in the ground and a layer of another kind, also where either comes only later in the clip', () => {
    const document = flyby({
      changes: [
        { id: 'cut', kind: 'surface', surface: 'cutaway', section: 'round', at: 1, seconds: 0 },
        { id: 'pins', kind: 'layer', layer: 'entrances', visible: true, section: 'round', at: 2, seconds: 1 },
      ],
    });
    const plan = flybyLookPlan(document);
    expect(flybyBundleOmissions(plan, viewer(), schedule)).toEqual(['ground', 'surfaceOpening', 'entrances']);
    // A stretch that stops before either never sees it.
    expect(flybyBundleOmissions(plan, viewer(), { from: 0, to: 4 })).toEqual(['ground']);
    // And one that starts after both has them from its first frame.
    expect(flybyBundleOmissions(plan, viewer(), { from: 8, to: 10 })).toEqual(['ground', 'surfaceOpening', 'entrances']);
  });
});

describe('what of a cave a bundle draws', () => {
  it('is whatever the flyby shows at some moment of the clip, not only at its first', () => {
    const base = viewer();
    base.layers.walls = { visible: false, opacity: 1 };
    const document = flyby({
      changes: [{ id: 'on', kind: 'layer', layer: 'walls', visible: true, section: 'round', at: 0, seconds: 2 }],
    });
    const plan = flybyLookPlan(document);
    expect(flybyBundleShows(plan, base, { from: 0, to: 10 })).toEqual({ walls: true, lines: true });
    expect(flybyBundleShows(plan, base, { from: 0, to: 3 })).toEqual({ walls: false, lines: true });

    // Walls that come later are asked for, and the bundle is told when they come.
    const { request, refusal } = flybyBundleRequest(ask(document, { base }));
    expect(refusal).toBeNull();
    expect(request.caves.slice(0, 2).map((entry) => entry.walls)).toEqual([true, true]);
    expect(request.changes?.walls?.shown).toEqual([{ frame: 100, shown: true, fadeFrames: 50 }]);
  });

  it('is nothing where walls and lines are both hidden from the first frame to the last', () => {
    const base = viewer();
    base.layers.walls = { visible: false, opacity: 1 };
    base.layers.centerlines = { visible: false, opacity: 1 };
    expect(flybyBundleRequest(ask(flyby(), { base })).refusal).toEqual({ kind: 'nothingShown' });
    const later = flyby({
      changes: [{ id: 'on', kind: 'layer', layer: 'centerlines', visible: true, section: 'round', at: 5, seconds: 0 }],
    });
    expect(flybyBundleRequest(ask(later, { base })).refusal).toBeNull();
  });
});

describe('the daylight of a bundle', () => {
  it('knows the camera is underground where its eye is below the scene\'s ground at its place', () => {
    const hill = (longitude: number) => (longitude < 25 ? 1000 : 400);
    expect(
      flybyBundleUnderground(
        [
          [24.9, 45.5, 900, 0, 0, 0, 10, 60],
          [24.9, 45.5, 1000.5, 0, 0, 0, 10, 60],
          [25.1, 45.5, 900, 0, 0, 0, 10, 60],
          [25.1, 45.5, 399.9, 0, 0, 0, 10, 60],
        ],
        hill,
      ),
    ).toEqual([true, false, false, true]);
  });

  it('is not spoken of where the camera stays above the ground', () => {
    expect(flybyBundleRequest(ask(flyby())).request).not.toHaveProperty('changes');
  });

  it('is told as keys where the camera goes under it, though the flyby changes nothing of the look', () => {
    // The whole flight under a ground higher than the camera ever is: dark from the first frame.
    const buried = flybyBundleRequest(ask(flyby(), { groundHeight: () => 9000 })).request;
    expect(buried.changes).toEqual({
      relief: null,
      sky: null,
      walls: null,
      lines: null,
      caves: null,
      daylight: [{ frame: 0, value: 0, easing: null }],
    });
    // The ground is asked where each frame's eye stands, with the heights the frames carry.
    const frames = buried.frames;
    const halfway = (frames[0][2] + frames[249][2]) / 2;
    const sinking = flybyBundleRequest(ask(flyby(), { groundHeight: () => halfway })).request;
    const under = flybyBundleUnderground(sinking.frames, () => halfway);
    const first = under.indexOf(true);
    expect(first).toBeGreaterThan(0);
    expect(sinking.changes?.daylight?.slice(0, 3)).toEqual([
      { frame: 0, value: 1, easing: null },
      { frame: first, value: 1, easing: null },
      { frame: first + 25, value: 0, easing: 'smooth' },
    ]);
  });
});

describe('the language of a bundle\'s notes', () => {
  it('is the reader\'s, where the view says it, and is left out where it does not', () => {
    expect(flybyBundleRequest(ask(flyby(), { language: 'ro' })).request.language).toBe('ro');
    expect(flybyBundleRequest(ask(flyby())).request).not.toHaveProperty('language');
  });
});

describe('the look a clip opens with and what changes of it', () => {
  it('sends no changes at all for a flyby that changes nothing', () => {
    expect(flybyBundleRequest(ask(flyby())).request).not.toHaveProperty('changes');
    // A lens is carried by the frames, and is no change of the scene.
    const zoomed = flyby({ changes: [{ id: 'zoom', kind: 'lens', degrees: 30, section: 'round', at: 0, seconds: 4 }] });
    expect(flybyBundleRequest(ask(zoomed)).request).not.toHaveProperty('changes');
  });

  it('opens at the relief of the first frame and tells the keys the rest is read from', () => {
    const document = flyby({
      look: { relief: 1.5 },
      changes: [{ id: 'grow', kind: 'relief', factor: 3, section: 'round', at: 0, seconds: 3 }],
    });
    const { request } = flybyBundleRequest(ask(document));
    expect(request.relief).toBe(1.5);
    expect(request.changes?.relief).toEqual([
      { frame: 0, value: 1.5, easing: null },
      { frame: 100, value: 1.5, easing: null },
      { frame: 175, value: 3, easing: 'smooth' },
    ]);
    // The camera's heights stay true: the server multiplies them by what the keys give.
    expect(request.frames).toEqual(flybyBundleRequest(ask(flyby())).request.frames);
  });

  it('names a cave coloured apart only where the request names the cave', () => {
    const document = flyby({
      changes: [{ id: 'lit', kind: 'cave', subject: 1, colour: '#ffcc00', strength: 0.8, section: 'in', at: 2, seconds: 1 }],
    });
    const { request } = flybyBundleRequest(ask(document));
    expect(request.changes?.caves?.map((entry) => entry.caveId)).toEqual(['b']);
    expect(request.changes?.caves?.[0].walls).toEqual([
      { frame: 0, colour: '#000000', strength: 0, easing: null },
      { frame: 50, colour: '#ffcc00', strength: 0, easing: 'constant' },
      { frame: 75, colour: '#ffcc00', strength: 0.8, easing: 'smooth' },
    ]);
  });

  it('opens a stretch with the look the flyby has reached by then', () => {
    const document = flyby({
      look: { sky: '#000000' },
      changes: [{ id: 'sky', kind: 'sky', colour: '#223344', section: 'in', at: 1, seconds: 2 }],
    });
    const whole = flybyBundleRequest(ask(document)).request;
    // Black is no sky, and is said as none.
    expect(whole.look?.sky).toBeNull();
    expect(whole.changes?.sky?.[0]).toMatchObject({ frame: 0, colour: '#000000' });
    expect(whole.changes?.sky?.at(-1)).toMatchObject({ frame: 75, colour: '#223344' });
    // From the fifth second on, the sky is already the new colour, and nothing changes any more.
    const timeline = compileFlyby(document, [cave, other]);
    const late = flybyBundleRequest(ask(document, { schedule: flybyFrameSchedule(timeline.length, 25, { from: 5, to: 10 }) })).request;
    expect(late.look?.sky).toBe('#223344');
    expect(late).not.toHaveProperty('changes');
  });
});

describe('the texts on the clip\'s own clock', () => {
  const document = flyby({
    texts: {
      title: { text: 'A title', subtitle: 'under it', seconds: 3 },
      end: { text: 'The end', seconds: 2 },
      captions: [
        { section: 'in', text: 'Coming in', at: 1, seconds: 2 },
        { section: 'round', text: 'Going round', at: 1, seconds: 3 },
      ],
      fadeIn: 1,
      fadeOut: 1.5,
      northArrow: true,
      corner: 'Somebody, 2026',
    },
  });
  const timeline = compileFlyby(document, [cave, other]);

  it('are the flyby\'s own for the whole of it', () => {
    const texts = flybyBundleTexts(timeline.texts, flybyFrameSchedule(timeline.length, 25));
    expect(texts).toEqual({
      title: { text: 'A title', subtitle: 'under it', seconds: 3 },
      end: { text: 'The end', subtitle: null, seconds: 2 },
      captions: [
        { text: 'Coming in', startS: 1, endS: 3 },
        { text: 'Going round', startS: 5, endS: 8 },
      ],
      corner: 'Somebody, 2026',
      cornerCaptions: [],
      fadeInS: 1,
      fadeOutS: 1.5,
      northArrow: true,
    });
  });

  it('move with a stretch: what is over is gone, what is under way starts at once', () => {
    const texts = flybyBundleTexts(timeline.texts, flybyFrameSchedule(timeline.length, 25, { from: 2, to: 7 }));
    // A second of the title is left; the first caption is half over; the second is cut by the end.
    expect(texts.title).toEqual({ text: 'A title', subtitle: 'under it', seconds: 1 });
    expect(texts.captions).toEqual([
      { text: 'Coming in', startS: 0, endS: 1 },
      { text: 'Going round', startS: 3, endS: 5 },
    ]);
    // The picture has risen out of black by then, and the stretch stops before it sinks back.
    expect(texts.fadeInS).toBe(0);
    expect(texts.fadeOutS).toBe(0);
    // The end card belongs to the last two seconds, which the stretch does not reach.
    expect(texts.end).toBeNull();
  });

  it('keep the end card through the frames held at the end', () => {
    const texts = flybyBundleTexts(timeline.texts, flybyFrameSchedule(timeline.length, 25, { from: 6, to: 10 }, 1));
    // Four seconds flown and one held: the card comes two seconds before the flight ends and stays.
    expect(texts.end).toEqual({ text: 'The end', subtitle: null, seconds: 3 });
    expect(texts.title).toBeNull();
    expect(texts.fadeOutS).toBe(1.5);
  });

  it('say the relief of each stretch of a ramp to the tenth, and nothing while it is true', () => {
    // 1 for a second, up to 1.2 over the next, held: frames at 10 a second.
    const reliefAt = (frame: number) => (frame < 10 ? 1 : frame >= 20 ? 1.2 : 1 + (0.2 * (frame - 10)) / 10);
    const marks = flybyBundleReliefMarks({ count: 30, fps: 10 }, reliefAt, (factor) => `×${factor}`);
    // 1.04 is still "1"; from 1.06 it reads 1.1, from 1.16 it reads 1.2.
    expect(marks).toEqual([
      { text: '×1.1', startS: 1.3, endS: 1.8 },
      { text: '×1.2', startS: 1.8, endS: 3 },
    ]);
    const document = flyby({ changes: [{ id: 'grow', kind: 'relief', factor: 2, section: 'round', at: 0, seconds: 2 }] });
    const said = flybyBundleRequest(ask(document)).request.texts?.cornerCaptions ?? [];
    expect(said[0]).toMatchObject({ text: 'Relief ×1.1' });
    expect(said.at(-1)).toMatchObject({ text: 'Relief ×2', endS: 10 });
    expect(said).toHaveLength(10);
    // One after the other, with no gap and no overlap.
    said.slice(1).forEach((mark, index) => expect(mark.startS).toBe(said[index].endS));
  });

  it('say in a corner that the relief is stretched, in the reader\'s words, while it is', () => {
    const stretched = flybyBundleRequest(ask(flyby({ look: { relief: 2.5 } }))).request.texts;
    expect(stretched?.cornerCaptions).toEqual([{ text: 'Relief ×2.5', startS: 0, endS: 10 }]);
    expect(flybyBundleRequest(ask(flyby())).request.texts?.cornerCaptions).toEqual([]);
    // Not where the flyby asked for no mark.
    const unmarked = flybyBundleRequest(ask(flyby({ look: { relief: 2.5 }, texts: { reliefMark: false } }))).request.texts;
    expect(unmarked?.cornerCaptions).toEqual([]);
    // And the viewer's own factor, where the view lets no flyby stretch the relief.
    const viewers = flybyBundleRequest(ask(flyby({ look: { relief: 2.5 } }), { flybyRelief: false, base: viewer([], { relief: 3 }) }));
    expect(viewers.request.relief).toBe(3);
    expect(viewers.request.texts?.cornerCaptions).toEqual([{ text: 'Relief ×3', startS: 0, endS: 10 }]);
  });
});

describe('a flyby a bundle cannot be made of', () => {
  it('is one with more frames than a bundle holds, and no frame is sampled for it', () => {
    const long = flyby({ sections: [{ id: 'long', kind: 'orbit', subject: 0, seconds: 600, to: { headingBy: 360 } }, ...SECTIONS] });
    const second = flyby({
      sections: [
        { id: 'long', kind: 'orbit', subject: 0, seconds: 600, to: { headingBy: 360 } },
        { id: 'longer', kind: 'orbit', subject: 0, seconds: 300, to: { headingBy: 360 } },
      ],
    });
    expect(flybyBundleRequest(ask(long)).refusal).toBeNull();
    const { refusal, request } = flybyBundleRequest(ask(second));
    expect(refusal).toEqual({ kind: 'tooLong', frames: 22_500, max: FLYBY_BUNDLE_MAX_FRAMES });
    expect(request.frames).toEqual([]);
  });

  it('is one about no cave, and one whose look shows neither walls nor lines', () => {
    expect(flybyBundleRequest(ask(flyby(), { routeCaves: [] })).refusal).toEqual({ kind: 'noCaves' });
    const base = viewer();
    base.layers.walls = { visible: false, opacity: 1 };
    base.layers.centerlines = { visible: false, opacity: 1 };
    expect(flybyBundleRequest(ask(flyby(), { base })).refusal).toEqual({ kind: 'nothingShown' });
  });
});
