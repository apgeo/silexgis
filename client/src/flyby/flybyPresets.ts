// SPDX-License-Identifier: AGPL-3.0-or-later
import {
  FLYBY_VERSION,
  type FlybyDocument,
  type FlybyFullPose,
  type FlybyPresetId,
  type FlybySection,
  type FlybySubject,
  type FlybySubjectRef,
} from './flybyDocument.ts';

// The ready-made flybys: a list of subjects and a handful of choices in, a document out.
//
// A preset is only a way of writing the sections. What it returns is an ordinary document that can
// be edited section by section afterwards, and nothing remembers which preset it came from except
// a label for the panel — so a preset can be improved later without changing a flyby somebody has
// already saved.

export interface FlybyPresetOptions {
  /** How many times the camera goes round each subject it circles. */
  turns: number;
  /** Which way it goes round, seen from above. */
  clockwise: boolean;
  /** Whether the camera goes below the ground to a cave's lowest point and back. */
  underground: boolean;
  /** Seconds one full circle takes at a speed of 1. */
  secondsPerTurn: number;
}

export const DEFAULT_FLYBY_PRESET_OPTIONS: FlybyPresetOptions = {
  turns: 1,
  clockwise: true,
  underground: false,
  secondsPerTurn: 24,
};

export const FLYBY_TURNS_RANGE = { min: 0.25, max: 5 } as const;
export const FLYBY_SECONDS_PER_TURN_RANGE = { min: 6, max: 180 } as const;

/** Metres the eye is kept above the ground while circling a cave or hopping between two. */
const CAVE_CLEARANCE = 60;
/** The same for a mountain, circled from further off and so held higher over its ridges. */
const AREA_CLEARANCE = 150;
/**
 * How far below the horizon a flyby opens. The scene draws no sky — above the horizon is a black
 * void — and a camera tilted less than about half its field of view shows a band of it along the
 * top of the picture. From here down the horizon stays above the frame.
 */
const OPENING_PITCH = -26;

/**
 * The sky a flyby built by a preset opens with. The scene draws none of its own, and a camera
 * circling low over a ridge shows a band of black above the horizon that reads as a hole in the
 * picture; a pale, slightly hazy blue reads as distance instead, and is quiet enough not to pull
 * the eye off the ground under it.
 */
export const FLYBY_PRESET_SKY = '#b4c8dc';

/**
 * A camera view its author captured: the point the middle of the picture showed, how far the eye
 * stood from it and which way it looked, and how many seconds the flight into it takes.
 */
export interface FlybyCapturedView {
  target: { longitude: number; latitude: number; height: number };
  /** Metres from the eye to the point looked at. */
  distance: number;
  heading: number;
  pitch: number;
  /** The eye was under the ground: the flight to it may go there too. */
  underground: boolean;
  /** Seconds the flight into this view takes, at a speed of 1; nothing for the first. */
  seconds: number;
}

/** Seconds a newly captured view is flown into. */
export const FLYBY_VIEW_SECONDS = 6;
/** Metres the eye is kept above the ground between two views captured above it. */
const VIEW_CLEARANCE = 10;
/** Seconds the camera holds a single view, which is all a flyby of one view can do. */
const SINGLE_VIEW_SECONDS = 3;

/** The turn from one heading to another the short way round, in degrees, between -180 and 180. */
function shortTurn(from: number, to: number): number {
  const turn = (((to - from) % 360) + 540) % 360 - 180;
  return turn === -180 ? 180 : turn;
}

/** The pose a view is seen from, around the place it looked at. */
function viewPose(view: FlybyCapturedView, heading: number): FlybyFullPose {
  return {
    aim: 'centre',
    heading,
    pitch: view.pitch,
    range: 1,
    lift: 0,
    clearance: view.underground ? null : VIEW_CLEARANCE,
  };
}

/** The subject a captured view is kept as: the place it looked at, as far off as the eye stood. */
export function viewSubject(view: FlybyCapturedView): FlybySubject {
  return {
    kind: 'place',
    longitude: view.target.longitude,
    latitude: view.target.latitude,
    height: view.target.height,
    radiusM: view.distance,
  };
}

/**
 * Through the views in order, one leg each, flown as one curve and coming to rest on the last.
 * Headings are written unwrapped, each the short way round from the one before, so a view facing
 * 350° after one facing 10° is a turn of twenty degrees and not of three hundred and forty.
 */
function myViews(views: readonly FlybyCapturedView[]): Built {
  const id = numbered();
  let heading = views[0].heading;
  const sections: FlybySection[] = views.slice(1).map((view, index) => {
    heading += shortTurn(heading, view.heading);
    const pose = viewPose(view, heading);
    return {
      id: id('keyframe'),
      kind: 'keyframe',
      subject: index + 1,
      seconds: view.seconds,
      to: { aim: pose.aim, heading: pose.heading, pitch: pose.pitch, range: pose.range, lift: pose.lift, clearance: pose.clearance },
    };
  });
  if (sections.length === 0) {
    sections.push({ id: id('hold'), kind: 'hold', subject: 0, seconds: SINGLE_VIEW_SECONDS, to: {} });
  }
  return { start: { subject: 0, pose: viewPose(views[0], views[0].heading) }, sections };
}

/**
 * The views a flyby made of captured views was made from, read back out of it so they can be
 * edited again; undefined for any other flyby, or one whose sections were changed so far that it
 * is no longer a flight through its views.
 */
export function viewsOfDocument(document: FlybyDocument): FlybyCapturedView[] | undefined {
  if (document.preset !== 'myViews' || document.subjects.length === 0) {
    return undefined;
  }
  const places = document.subjects.map((subject) =>
    subject.kind === 'place' && subject.height !== undefined ? subject : undefined,
  );
  const legs = document.sections.filter((section) => section.kind === 'keyframe');
  const poses = [document.start.pose, ...legs.map((section) => section.to)];
  if (
    places.some((place) => !place) ||
    legs.length !== places.length - 1 ||
    legs.some((section, index) => section.subject !== index + 1) ||
    document.start.subject !== 0
  ) {
    return undefined;
  }
  return places.map((place, index) => ({
    target: { longitude: place!.longitude, latitude: place!.latitude, height: place!.height! },
    distance: place!.radiusM,
    heading: ((poses[index].heading ?? 0) % 360 + 360) % 360,
    pitch: poses[index].pitch ?? -30,
    underground: poses[index].clearance === null,
    seconds: index === 0 ? FLYBY_VIEW_SECONDS : legs[index - 1].seconds,
  }));
}

/** Whether a preset can be built from these subjects at all. */
export function presetFits(preset: FlybyPresetId, subjects: readonly FlybySubject[]): boolean {
  switch (preset) {
    case 'myViews':
      // Built from the views captured, not from what the flyby is about.
      return true;
    case 'aroundCave':
    case 'aroundArea':
    case 'planToProfile':
      return subjects.length >= 1;
    case 'aroundSeveral':
    case 'tour':
      return subjects.length >= 2;
  }
}

/** The preset a set of subjects is most likely wanted for. */
export function presetFor(subjects: readonly FlybySubject[]): FlybyPresetId {
  if (subjects.length >= 2) {
    return 'tour';
  }
  return subjects[0]?.kind === 'cave' ? 'aroundCave' : 'aroundArea';
}

/** Section ids for one document: short, stable, and in order. */
function numbered(): (kind: string) => string {
  let count = 0;
  return (kind) => {
    count += 1;
    return `${kind}-${count}`;
  };
}

interface Built {
  start: { subject: FlybySubjectRef; pose: FlybyFullPose };
  sections: FlybySection[];
}

function orbitSeconds(options: FlybyPresetOptions): number {
  return options.turns * options.secondsPerTurn;
}

/**
 * In at the top of the cave, down to its lowest place turning round it on the way, and back out.
 *
 * The camera looks at places on the survey — its highest point, then its deepest — and not at the
 * middle of the box around it: a cave's deepest passage is rarely under its middle, and a camera
 * sunk through the middle looks at rock. It looks down at each from close by, so the passages
 * leading there are in the picture rather than behind it.
 */
function dive(subject: FlybySubjectRef, sign: number, id: (kind: string) => string, back: FlybyFullPose): FlybySection[] {
  return [
    {
      id: id('dive'),
      kind: 'dive',
      subject,
      seconds: 5,
      to: { aim: 'top', headingBy: sign * 70, pitch: -28, range: 0.7, lift: 0, clearance: null },
    },
    {
      id: id('dive'),
      kind: 'dive',
      subject,
      seconds: 9,
      to: { aim: 'bottom', headingBy: sign * 200, pitch: -32, range: 0.5 },
    },
    {
      id: id('rise'),
      kind: 'rise',
      subject,
      seconds: 6,
      to: {
        aim: 'centre',
        headingBy: sign * 90,
        pitch: back.pitch,
        range: back.range,
        lift: back.lift,
        clearance: back.clearance,
      },
    },
  ];
}

function aroundCave(options: FlybyPresetOptions): Built {
  const sign = options.clockwise ? 1 : -1;
  const id = numbered();
  const circling: FlybyFullPose = { heading: 0, pitch: -32, range: 2.8, lift: 0, clearance: CAVE_CLEARANCE };
  return {
    start: { subject: 0, pose: { heading: 0, pitch: OPENING_PITCH, range: 7, lift: 0.5, clearance: CAVE_CLEARANCE } },
    sections: [
      { id: id('approach'), kind: 'approach', subject: 0, seconds: 6, to: { headingBy: sign * 60, pitch: circling.pitch, range: circling.range, lift: 0 } },
      { id: id('orbit'), kind: 'orbit', subject: 0, seconds: orbitSeconds(options), to: { headingBy: sign * 360 * options.turns } },
      ...(options.underground ? dive(0, sign, id, circling) : []),
    ],
  };
}

function aroundArea(options: FlybyPresetOptions): Built {
  const sign = options.clockwise ? 1 : -1;
  const id = numbered();
  return {
    start: { subject: 0, pose: { heading: 0, pitch: OPENING_PITCH, range: 4.5, lift: 0.4, clearance: AREA_CLEARANCE } },
    sections: [
      { id: id('approach'), kind: 'approach', subject: 0, seconds: 6, to: { headingBy: sign * 40, pitch: -30, range: 2.6, lift: 0.2 } },
      { id: id('orbit'), kind: 'orbit', subject: 0, seconds: orbitSeconds(options), to: { headingBy: sign * 360 * options.turns } },
    ],
  };
}

function aroundSeveral(options: FlybyPresetOptions): Built {
  const sign = options.clockwise ? 1 : -1;
  const id = numbered();
  return {
    start: { subject: 'all', pose: { heading: 0, pitch: OPENING_PITCH, range: 4, lift: 0.4, clearance: AREA_CLEARANCE } },
    sections: [
      { id: id('approach'), kind: 'approach', subject: 'all', seconds: 6, to: { headingBy: sign * 40, pitch: -30, range: 2.2, lift: 0 } },
      { id: id('orbit'), kind: 'orbit', subject: 'all', seconds: orbitSeconds(options), to: { headingBy: sign * 360 * options.turns } },
    ],
  };
}

function tour(subjects: readonly FlybySubject[], options: FlybyPresetOptions): Built {
  const sign = options.clockwise ? 1 : -1;
  const id = numbered();
  const circling: FlybyFullPose = { heading: 0, pitch: -32, range: 2.8, lift: 0, clearance: CAVE_CLEARANCE };
  const sections: FlybySection[] = [];
  subjects.forEach((_, index) => {
    sections.push(
      index === 0
        ? { id: id('approach'), kind: 'approach', subject: 0, seconds: 6, to: { headingBy: sign * 50, pitch: circling.pitch, range: circling.range, lift: 0 } }
        : { id: id('hop'), kind: 'hop', subject: index, seconds: 8, to: { headingBy: sign * 50, pitch: circling.pitch, range: circling.range, lift: 0, clearance: CAVE_CLEARANCE } },
    );
    sections.push({
      id: id('orbit'),
      kind: 'orbit',
      subject: index,
      seconds: orbitSeconds(options),
      to: { headingBy: sign * 360 * options.turns },
    });
    if (options.underground) {
      sections.push(...dive(index, sign, id, circling));
    }
  });
  // And back out to all of them at once, which is the picture a tour is remembered by.
  sections.push({
    id: id('rise'),
    kind: 'rise',
    subject: 'all',
    seconds: 8,
    to: { headingBy: sign * 60, pitch: -40, range: 2.2, lift: 0, clearance: AREA_CLEARANCE },
    rest: true,
  });
  return {
    start: { subject: 0, pose: { heading: 0, pitch: OPENING_PITCH, range: 7, lift: 0.5, clearance: CAVE_CLEARANCE } },
    sections,
  };
}

function planToProfile(options: FlybyPresetOptions): Built {
  const sign = options.clockwise ? 1 : -1;
  const id = numbered();
  return {
    // Straight down is the survey's plan. A hair short of it, because exactly overhead every
    // heading shows the same picture and the turn that follows would start with a spin.
    start: { subject: 0, pose: { heading: 0, pitch: -89.5, range: 2.6, lift: 0, clearance: CAVE_CLEARANCE } },
    sections: [
      { id: id('hold'), kind: 'hold', subject: 0, seconds: 3, to: {} },
      // Down to the side view: the profile, seen from the south.
      { id: id('tilt'), kind: 'tilt', subject: 0, seconds: 7, to: { pitch: -8, range: 2.4 } },
      { id: id('orbit'), kind: 'orbit', subject: 0, seconds: orbitSeconds(options), to: { headingBy: sign * 360 * options.turns } },
    ],
  };
}

/**
 * A document for these subjects, built the preset's way. A flyby through captured views is built
 * from `views` instead, and its subjects are the places those views looked at.
 */
export function buildPreset(
  preset: FlybyPresetId,
  subjects: readonly FlybySubject[],
  options: FlybyPresetOptions = DEFAULT_FLYBY_PRESET_OPTIONS,
  name = '',
  views: readonly FlybyCapturedView[] = [],
): FlybyDocument {
  if (preset === 'myViews') {
    return {
      version: FLYBY_VERSION,
      name,
      preset,
      speed: 1,
      loop: false,
      subjects: views.map(viewSubject),
      ...(views.length > 0
        ? myViews(views)
        : { start: { subject: 0, pose: { heading: 0, pitch: OPENING_PITCH, range: 1, lift: 0, clearance: null } }, sections: [] }),
      look: { sky: FLYBY_PRESET_SKY },
    };
  }
  const built = (() => {
    switch (preset) {
      case 'aroundCave':
        return aroundCave(options);
      case 'aroundArea':
        return aroundArea(options);
      case 'aroundSeveral':
        return aroundSeveral(options);
      case 'tour':
        return tour(subjects, options);
      case 'planToProfile':
        return planToProfile(options);
    }
  })();
  return {
    version: FLYBY_VERSION,
    name,
    preset,
    speed: 1,
    loop: false,
    // The presets that are about one subject take the first; the rest stay listed, so switching to
    // a tour afterwards finds them.
    subjects: [...subjects],
    ...built,
    look: { sky: FLYBY_PRESET_SKY },
  };
}
