// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  FLYBY_BUNDLE_FPS,
  FLYBY_BUNDLE_MAX_CAVES,
  FLYBY_BUNDLE_MAX_FRAMES,
  flybyBundleFrames,
  flybyBundleOmissions,
  flybyBundleOpeningLook,
  flybyBundleRequest,
  flybyBundleTexts,
  type FlybyBundleAsk,
} from './flybyBlenderBundle.ts';
import { FLYBY_VERSION, type FlybyDocument, type FlybySection } from './flybyDocument.ts';
import { flybyLookPlan, type FlybyLookBase } from './flybyLook.ts';
import { flybyFrameSchedule } from './flybyMovie.ts';
import type { FlybyResolvedSubject } from './flybySubjects.ts';
import { compileFlyby, flybyCameraAt } from './flybyTimeline.ts';

// What a flyby is turned into when it is handed over as a bundle for Blender: the camera at every
// frame, the look at the first, the texts on the clip's own clock — and the list of what the
// bundle does not carry. Everything here is invented: two boxes for caves, flat ground.

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

  it('is a change still to come when the clip starts, and not one that had ended by then', () => {
    const document = flyby({
      changes: [{ id: 'sky', kind: 'sky', colour: '#223344', section: 'in', at: 1, seconds: 2 }],
    });
    const plan = flybyLookPlan(document);
    expect(flybyBundleOmissions(plan, viewer(), { from: 0, to: 10 })).toContain('timedChanges');
    // From the fifth second on, the sky is already the new colour: the first frame's look has it.
    expect(flybyBundleOmissions(plan, viewer(), { from: 5, to: 10 })).toEqual(['ground']);
    expect(flybyBundleOpeningLook(plan, viewer(), 5).sky).toBe('#223344');
    // And a stretch that stops before it starts never sees it.
    expect(flybyBundleOmissions(plan, viewer(), { from: 0, to: 0.5 })).toEqual(['ground']);
  });

  it('is a relief that changes, the tint on the walls, the opening in the ground and a faded layer', () => {
    const document = flyby({
      look: { relief: 1.5, walls: { colour: '#ff0000', strength: 0.5 }, surface: 'cutaway', layers: { walls: { opacity: 0.4 } } },
      changes: [{ id: 'grow', kind: 'relief', factor: 3, section: 'round', at: 0, seconds: 3 }],
    });
    expect(flybyBundleOmissions(flybyLookPlan(document), viewer(), schedule)).toEqual([
      'ground',
      'reliefChanges',
      'wallColours',
      'surfaceOpening',
      'layerOpacity',
    ]);
    // The bundle is drawn at the relief of its first frame throughout.
    expect(flybyBundleRequest(ask(document)).request.relief).toBe(1.5);
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
