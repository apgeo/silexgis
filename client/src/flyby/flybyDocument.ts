// SPDX-License-Identifier: AGPL-3.0-or-later

// A flyby, written down: what the camera of the 3D scene does, around what, and for how long.
//
// Plain data on purpose. The same document is played in the view, scrubbed back and forth by a
// slider, saved in the browser, written to a file and read back, and later handed to whatever makes
// a movie of it — so everything it can ask for is a named kind below rather than a function, and
// nothing in it is an engine object.
//
// It names no place unless its author pinned one. The camera is described around a *subject* — a
// cave, an area feature, whatever the view was showing — as a direction, a tilt and a distance
// counted in the subject's own size, and the subject is found again each time the flyby is played,
// from what the server sends the person playing it. A flyby of a cave is therefore a few numbers
// and an id: it discloses nothing its reader could not already see, and it still works after the
// cave has been resurveyed and its extent has changed.
//
// Every moment of a flyby is a function of its time and of nothing else. Nothing here is an action
// that cannot be taken back, so a player can be asked for any moment in any order.

export const FLYBY_VERSION = 1;

/** What the camera is about. */
export type FlybySubject =
  /** A cave: its survey as drawn, or where the server says it is when nothing of it is drawn. */
  | { kind: 'cave'; caveId: string }
  /** A feature with an extent on the ground: a massif, a karst area, a hill. */
  | { kind: 'feature'; featureId: string }
  /** Whatever the 3D view is centred on at the moment the flyby is prepared. */
  | { kind: 'view' }
  /** An imported file — a GPX track, a line in a KML —, by its id: what it draws, found when played. */
  | { kind: 'file'; geofileId: string }
  /**
   * A place its author put into the flyby: a point pinned on the ground, or the point a camera view
   * they captured was looking at, which is where `height` comes from — a view can look at a cave
   * under the ground. The one subject that holds coordinates.
   */
  | { kind: 'place'; longitude: number; latitude: number; radiusM: number; height?: number }
  /**
   * The caves a saved filter selects, at most `limit` of them, in the order the filter gives. Only
   * the filter's id and the limit are written down: the caves are asked for when the flyby is
   * prepared, under the rights of whoever plays it, so a flyby kept in a file names none of them
   * and follows the filter when its author refines it. Taken as a whole, it is all of its caves
   * together; a section marked `each` is flown once for every one of them.
   */
  | { kind: 'filter'; savedFilterId: string; limit: number };

/** How many caves a subject drawn from a saved filter may stand for, and how many it is given at first. */
export const FLYBY_FILTER_LIMIT = { min: 1, max: 40, initial: 12 } as const;

/** One of the document's subjects by position in its list, or all of them taken as one. */
export type FlybySubjectRef = number | 'all';

/**
 * Which point of a subject the camera looks at: its middle, its highest point or its lowest. For a
 * cave the last two are places on the survey itself — usually an entrance, and the deepest station
 * — which is what makes a descent follow the cave rather than sink through the middle of the box
 * around it. A subject that has no such places of its own answers with the top and the bottom of
 * its middle.
 */
export type FlybyAim = 'centre' | 'top' | 'bottom';

export const FLYBY_AIMS: readonly FlybyAim[] = ['centre', 'top', 'bottom'];

/**
 * Where the camera is, around a subject. A value left out stays as the move before left it, which
 * is what lets a section say only what it changes.
 */
export interface FlybyPose {
  /** The point of the subject looked at; the middle unless a move before said otherwise. */
  aim?: FlybyAim;
  /** Compass direction the camera faces, degrees. */
  heading?: number;
  /** Turns the heading by this much from where it was, degrees; positive is clockwise from above. */
  headingBy?: number;
  /** Angle below the horizon, degrees: -90 looks straight down. */
  pitch?: number;
  /** Distance from the point looked at, in radii of the subject. */
  range?: number;
  /**
   * Distance from the point looked at in metres, in place of `range`: for a camera whose distance
   * has nothing to do with the size of what it looks at — following a track from a few hundred
   * metres behind, or starting a flight at the scale of a country. A section that gives a `range`
   * takes this away again.
   */
  rangeM?: number;
  /**
   * Raises the point looked at, counted in halves of the subject's height: from its middle, 1 is
   * level with its top and -1 with its bottom.
   */
  lift?: number;
  /**
   * Keeps the eye at least this many metres above the ground, tilting down to keep looking at the
   * same point; null lifts the limit, which going underground needs.
   */
  clearance?: number | null;
}

/** A pose with nothing left out: what the opening of a flyby has to be. It looks at the middle unless it says. */
export type FlybyFullPose = Required<Omit<FlybyPose, 'headingBy' | 'aim' | 'rangeM'>> & Pick<FlybyPose, 'aim' | 'rangeM'>;

/**
 * What a section is, as its author thinks of it. Every kind is a move of the camera to a pose; the
 * kind is what the section is called, which defaults it is given, and — for a hop — the arc it is
 * flown in.
 */
export type FlybySectionKind =
  | 'approach'
  | 'orbit'
  | 'hop'
  | 'dive'
  | 'rise'
  | 'hold'
  | 'tilt'
  | 'keyframe'
  | 'descent'
  | 'follow'
  | 'fromFar';

export const FLYBY_SECTION_KINDS: readonly FlybySectionKind[] = [
  'approach',
  'orbit',
  'hop',
  'dive',
  'rise',
  'hold',
  'tilt',
  /** To a camera view its author captured. */
  'keyframe',
  /**
   * Down a cave's survey from its highest point to its deepest, a little behind and above the point
   * followed; a cave whose survey gives no such way is dived into instead, to its `to` pose.
   */
  'descent',
  /**
   * Along an imported file's track, behind and above the point followed, looking ahead; a file with
   * no line in it is flown to as a plain move.
   */
  'follow',
  /**
   * In from far away — the scale of a country — looking straight down while far, and tilting to
   * its pose only once near.
   */
  'fromFar',
];

/**
 * What a section writes over the picture about its subject, for as long as it lasts: the name, or
 * the name with its length and depth. Read from the server when the flyby is prepared, never
 * written into the document, so a flyby kept in a file names its caves by id alone.
 */
export type FlybySay = 'name' | 'nameAndSize';

export const FLYBY_SAYINGS: readonly FlybySay[] = ['name', 'nameAndSize'];

/** One shot: the camera moves from where the section before left it to `to`. */
export interface FlybySection {
  /** Stable within the document, so a timed change can be tied to a section that is later moved. */
  id: string;
  kind: FlybySectionKind;
  subject: FlybySubjectRef;
  /** How long the section lasts at a speed of 1, in seconds. */
  seconds: number;
  /** This section's own speed: 2 plays it in half the time. Left out, 1. */
  speed?: number;
  to: FlybyPose;
  /** The camera comes to a stop at the end of this section instead of flying on into the next. */
  rest?: boolean;
  /** Writes the subject's name — and, if asked, its size — along the bottom while the section lasts. */
  say?: FlybySay;
  /**
   * Flown once for each cave its subject — a saved filter's caves — stands for. Consecutive
   * sections marked so, about the same subject, are one run: the whole run is flown for the first
   * cave, then the whole run for the next, so a tour of "hop over, circle, stop" is written once.
   */
  each?: boolean;
}

export type FlybyPresetId =
  | 'aroundArea'
  | 'aroundCave'
  | 'aroundSeveral'
  | 'tour'
  | 'planToProfile'
  | 'myViews'
  | 'descent'
  | 'alongLine';

export const FLYBY_PRESETS: readonly FlybyPresetId[] = [
  'aroundCave',
  'tour',
  'aroundSeveral',
  'aroundArea',
  'planToProfile',
  'descent',
  'alongLine',
  'myViews',
];

// ---- how the scene looks, and what is written over it ----

/** A colour, written `#rrggbb`: the one form a file can carry that every reader agrees on. */
export type FlybyColour = string;

/** How the ground is drawn over a cave: on top of it, or cut away above it. */
export type FlybySurface = 'overlay' | 'cutaway';

export const FLYBY_SURFACES: readonly FlybySurface[] = ['overlay', 'cutaway'];

/**
 * The layers a flyby can show, hide and fade, by keys that mean the same thing in a file opened on
 * another day: the scene's own layers by name, and the ones an installation lists by the kind of
 * list and the id in it.
 */
export const FLYBY_NAMED_LAYERS = ['centerlines', 'entrances', 'surface-features', 'walls', 'scans', 'photos'] as const;
export type FlybyNamedLayer = (typeof FLYBY_NAMED_LAYERS)[number];

/** A tile overlay of the catalogue, a georeferenced map, an imported file, a layer drawn from a saved filter. */
export const FLYBY_LAYER_KINDS = ['overlay', 'raster', 'file', 'filter'] as const;
export type FlybyListedLayerKind = (typeof FLYBY_LAYER_KINDS)[number];

/** `centerlines`, `walls`… or `overlay:12`, `raster:<id>`, `file:<id>`, `filter:<savedFilterId>`. */
export type FlybyLayerKey = string;

const LAYER_ID_PATTERN = /^[A-Za-z0-9_-]{1,64}$/;

export function isFlybyLayerKey(value: unknown): value is FlybyLayerKey {
  if (typeof value !== 'string') {
    return false;
  }
  if ((FLYBY_NAMED_LAYERS as readonly string[]).includes(value)) {
    return true;
  }
  const colon = value.indexOf(':');
  const kind = value.slice(0, colon);
  const id = value.slice(colon + 1);
  if (colon < 0 || !(FLYBY_LAYER_KINDS as readonly string[]).includes(kind) || !LAYER_ID_PATTERN.test(id)) {
    return false;
  }
  // A catalogue entry is numbered; the others are named by the ids their own lists give them.
  return kind !== 'overlay' || /^\d+$/.test(id);
}

/** The key of a layer an installation lists. */
export function flybyLayerKey(kind: FlybyListedLayerKind, id: string | number): FlybyLayerKey {
  return `${kind}:${id}`;
}

const COLOUR_PATTERN = /^#[0-9a-f]{6}$/;

export function isFlybyColour(value: unknown): value is FlybyColour {
  return typeof value === 'string' && COLOUR_PATTERN.test(value);
}

/** How a layer is shown: on or off, and how strongly, 0–1. A value left out is the viewer's own. */
export interface FlybyLayerLook {
  visible?: boolean;
  opacity?: number;
}

/**
 * How the scene looks when a flyby starts. Every member is optional, and one left out is whatever
 * the viewer has: a flyby that names only a background leaves every layer as the viewer set it.
 */
export interface FlybyLook {
  /** The background map, by its id in the installation's catalogue. */
  background?: number;
  /** How strongly the background is drawn, 0–1: below 1 the ground shows the caves under it. */
  backgroundOpacity?: number;
  layers?: Record<FlybyLayerKey, FlybyLayerLook>;
  /** The caves' walls mixed with one colour, by `strength` 0–1; their shading is kept. */
  walls?: { colour: FlybyColour; strength: number };
  /** The survey lines in one colour instead of their colours by depth. */
  lines?: FlybyColour;
  surface?: FlybySurface;
  /** What is drawn above the horizon; left out, nothing — the scene draws no sky of its own. */
  sky?: FlybyColour;
  /**
   * The lens: how wide the picture is, in degrees across its wider side. Narrow flattens distance —
   * a mountain stands like a wall behind the cave —, wide takes in a valley from close by.
   */
  lens?: number;
}

/** The narrowest and the widest lens, in degrees across the wider side of the picture; the scene's own is 60. */
export const FLYBY_LENS_RANGE = { min: 10, max: 100 } as const;

/** What a change changes, and what to. */
export type FlybyChangeWhat =
  /** Another background: cut, or crossed into over the change's length. */
  | { kind: 'background'; background: number }
  | { kind: 'backgroundOpacity'; opacity: number }
  /** A layer shown or hidden: one shown rises from nothing, one hidden fades away and is then put off. */
  | { kind: 'layer'; layer: FlybyLayerKey; visible: boolean }
  | { kind: 'layerOpacity'; layer: FlybyLayerKey; opacity: number }
  /** The walls' colour; a strength of 0 gives them back their own. */
  | { kind: 'walls'; colour: FlybyColour; strength: number }
  /** The survey lines in one colour, or (null) by depth again. Always a cut: the lines are rebuilt. */
  | { kind: 'lines'; colour: FlybyColour | null }
  /** Always a cut. */
  | { kind: 'surface'; surface: FlybySurface }
  /** The sky's colour, or (null) none. */
  | { kind: 'sky'; colour: FlybyColour | null }
  /** The lens, widened or narrowed: a zoom without moving the camera. */
  | { kind: 'lens'; degrees: number };

export type FlybyChangeKind = FlybyChangeWhat['kind'];

export const FLYBY_CHANGE_KINDS: readonly FlybyChangeKind[] = [
  'background',
  'backgroundOpacity',
  'layer',
  'layerOpacity',
  'walls',
  'lines',
  'surface',
  'sky',
  'lens',
];

/** The kinds that happen at one moment whatever length they are given. */
export const FLYBY_CUT_CHANGES: readonly FlybyChangeKind[] = ['lines', 'surface'];

/**
 * A change of the scene's look, tied to a section: it starts `at` seconds into the section and
 * lasts `seconds`, both counted at a speed of 1 and played at the section's speed — so a section
 * made slower carries its changes with it, stretched as it is.
 */
export type FlybyChange = FlybyChangeWhat & {
  /** Stable within the document, so a change can be named, edited and removed. */
  id: string;
  /** The section it belongs to, by its id. */
  section: string;
  at: number;
  /** 0 is a cut. */
  seconds: number;
};

/** A card over the whole picture: a title at the start, an end card at the end. */
export interface FlybyCard {
  text: string;
  subtitle?: string;
  /** Real seconds, whatever the speed: a card is there to be read. */
  seconds: number;
}

/** A line of text along the bottom of the picture during part of a section. */
export interface FlybyCaption {
  section: string;
  text: string;
  /** Seconds into the section and how long, counted like a change's. */
  at: number;
  seconds: number;
}

export interface FlybyTexts {
  title?: FlybyCard;
  captions?: FlybyCaption[];
  end?: FlybyCard;
  /** Seconds the picture rises out of black at the start; left out, none. */
  fadeIn?: number;
  /** Seconds it sinks to black at the end. */
  fadeOut?: number;
  /** A north arrow in a corner of the picture, turning as the camera turns. */
  northArrow?: boolean;
  /** A line of text in a corner of the picture for the whole flyby: a club's name, a date. */
  corner?: string;
}

export interface FlybyDocument {
  version: typeof FLYBY_VERSION;
  name: string;
  /** The preset the sections were built from, for the panel to show; the sections are what is played. */
  preset?: FlybyPresetId;
  /** The speed of the whole flyby: 2 plays all of it in half the time. */
  speed: number;
  /** Plays again from the start when it reaches the end. */
  loop: boolean;
  subjects: FlybySubject[];
  /** Where the camera is at the very start. */
  start: { subject: FlybySubjectRef; pose: FlybyFullPose };
  sections: FlybySection[];
  /** How the scene looks at the start; left out, as the viewer has it. */
  look?: FlybyLook;
  /** Timed changes of the look. */
  changes?: FlybyChange[];
  texts?: FlybyTexts;
}

export const FLYBY_SPEED_RANGE = { min: 0.25, max: 4 } as const;
export const FLYBY_SECONDS_RANGE = { min: 0.5, max: 600 } as const;
/** A camera that looks at its subject from closer than this is inside the point it looks at. */
export const FLYBY_MIN_RANGE = 0.02;
export const FLYBY_MAX_RANGE = 5000;
/** The nearest and the furthest a distance in metres may be: a metre, and the width of a large country. */
export const FLYBY_RANGE_M = { min: 1, max: 2_000_000 } as const;
export const FLYBY_MAX_SUBJECTS = 40;
export const FLYBY_MAX_SECTIONS = 200;
export const FLYBY_MAX_CHANGES = 400;
export const FLYBY_MAX_LAYERS = 60;
/** The longest any change or card lasts, in seconds. */
export const FLYBY_MAX_CHANGE_SECONDS = 600;
export const FLYBY_MAX_TEXT = 200;
/** A lift beyond this many half-heights looks at nothing of the subject at all. */
export const FLYBY_LIFT_LIMIT = 10;
/** Metres: a camera held further above the ground than this is not following the ground any more. */
export const FLYBY_MAX_CLEARANCE = 5000;
/** Ten turns, in degrees: more is a spin, not a flight. A heading is unwrapped, so it is held to the same. */
export const FLYBY_MAX_TURN_DEGREES = 3600;

/** Why a document cannot be played, in a form the interface can put into words. */
export type FlybyProblem =
  | { code: 'noSubjects' }
  /** A subject drawn from a saved filter asks for no caves, or for more than a flyby can visit. */
  | { code: 'badSubject'; subject: number }
  /** A section is to be flown for each of its subject's caves, and its subject is not a saved filter's. */
  | { code: 'badEach'; section: string }
  | { code: 'noSections' }
  | { code: 'unknownSubject'; section?: string }
  | { code: 'badSeconds'; section: string }
  | { code: 'badSpeed'; section?: string }
  | { code: 'badRange'; section?: string }
  | { code: 'badPitch'; section?: string }
  /** A lift, a clearance or a turn outside what it may be. */
  | { code: 'badPose'; section?: string }
  | { code: 'duplicateSection'; section: string }
  /** The opening look has a value outside what it may be. */
  | { code: 'badLook' }
  /** A change names a section the flyby does not have. */
  | { code: 'unknownChangeSection'; change: string; section: string }
  /** A change starts outside its section, lasts a negative time, or asks for a value outside its range. */
  | { code: 'badChange'; change: string; section: string }
  | { code: 'duplicateChange'; change: string; section: string }
  /** Two changes steer the same thing at the same time. */
  | { code: 'overlappingChanges'; change: string; section: string; other: string }
  /** A caption names a section the flyby does not have, or a card or caption has no time to be seen. */
  | { code: 'badText'; section?: string };

function subjectExists(document: FlybyDocument, ref: FlybySubjectRef): boolean {
  return ref === 'all' || (Number.isInteger(ref) && ref >= 0 && ref < document.subjects.length);
}

function poseProblems(pose: FlybyPose, section: string | undefined): FlybyProblem[] {
  const problems: FlybyProblem[] = [];
  if (
    (pose.range !== undefined && !(pose.range >= FLYBY_MIN_RANGE && pose.range <= FLYBY_MAX_RANGE)) ||
    (pose.rangeM !== undefined && !(pose.rangeM >= FLYBY_RANGE_M.min && pose.rangeM <= FLYBY_RANGE_M.max))
  ) {
    problems.push({ code: 'badRange', section });
  }
  if (pose.pitch !== undefined && !(pose.pitch >= -90 && pose.pitch <= 90)) {
    problems.push({ code: 'badPitch', section });
  }
  if (
    (pose.lift !== undefined && !(Math.abs(pose.lift) <= FLYBY_LIFT_LIMIT)) ||
    (pose.clearance !== undefined &&
      pose.clearance !== null &&
      !(pose.clearance >= 0 && pose.clearance <= FLYBY_MAX_CLEARANCE)) ||
    (pose.headingBy !== undefined && !(Math.abs(pose.headingBy) <= FLYBY_MAX_TURN_DEGREES)) ||
    (pose.heading !== undefined && !(Math.abs(pose.heading) <= FLYBY_MAX_TURN_DEGREES))
  ) {
    problems.push({ code: 'badPose', section });
  }
  return problems;
}

/** Every reason the document cannot be played; empty when it can. */
export function flybyProblems(document: FlybyDocument): FlybyProblem[] {
  const problems: FlybyProblem[] = [];
  if (document.subjects.length === 0) {
    problems.push({ code: 'noSubjects' });
  }
  document.subjects.forEach((subject, index) => {
    if (
      subject.kind === 'filter' &&
      !(Number.isInteger(subject.limit) && subject.limit >= FLYBY_FILTER_LIMIT.min && subject.limit <= FLYBY_FILTER_LIMIT.max)
    ) {
      problems.push({ code: 'badSubject', subject: index });
    }
  });
  if (document.sections.length === 0) {
    problems.push({ code: 'noSections' });
  }
  if (!(document.speed >= FLYBY_SPEED_RANGE.min && document.speed <= FLYBY_SPEED_RANGE.max)) {
    problems.push({ code: 'badSpeed' });
  }
  if (!subjectExists(document, document.start.subject)) {
    problems.push({ code: 'unknownSubject' });
  }
  problems.push(...poseProblems(document.start.pose, undefined));
  const seen = new Set<string>();
  for (const section of document.sections) {
    if (seen.has(section.id)) {
      problems.push({ code: 'duplicateSection', section: section.id });
    }
    seen.add(section.id);
    if (!subjectExists(document, section.subject)) {
      problems.push({ code: 'unknownSubject', section: section.id });
    } else if (section.each && (section.subject === 'all' || document.subjects[section.subject]?.kind !== 'filter')) {
      problems.push({ code: 'badEach', section: section.id });
    }
    if (!(section.seconds >= FLYBY_SECONDS_RANGE.min && section.seconds <= FLYBY_SECONDS_RANGE.max)) {
      problems.push({ code: 'badSeconds', section: section.id });
    }
    const speed = section.speed ?? 1;
    if (!(speed >= FLYBY_SPEED_RANGE.min && speed <= FLYBY_SPEED_RANGE.max)) {
      problems.push({ code: 'badSpeed', section: section.id });
    }
    problems.push(...poseProblems(section.to, section.id));
  }
  if (document.look && !lookIsSound(document.look)) {
    problems.push({ code: 'badLook' });
  }
  problems.push(...changeProblems(document), ...textProblems(document));
  return problems;
}

const isShare = (value: number) => value >= 0 && value <= 1;

function lookIsSound(look: FlybyLook): boolean {
  if (look.backgroundOpacity !== undefined && !isShare(look.backgroundOpacity)) {
    return false;
  }
  if (look.walls && !(isFlybyColour(look.walls.colour) && isShare(look.walls.strength))) {
    return false;
  }
  if ((look.lines !== undefined && !isFlybyColour(look.lines)) || (look.sky !== undefined && !isFlybyColour(look.sky))) {
    return false;
  }
  if (look.lens !== undefined && !isLens(look.lens)) {
    return false;
  }
  return Object.entries(look.layers ?? {}).every(
    ([key, layer]) => isFlybyLayerKey(key) && (layer.opacity === undefined || isShare(layer.opacity)),
  );
}

/** Whether what a change asks for is within range. */
function changeIsSound(change: FlybyChange): boolean {
  switch (change.kind) {
    case 'background':
      return Number.isInteger(change.background);
    case 'backgroundOpacity':
      return isShare(change.opacity);
    case 'layer':
      return isFlybyLayerKey(change.layer);
    case 'layerOpacity':
      return isFlybyLayerKey(change.layer) && isShare(change.opacity);
    case 'walls':
      return isFlybyColour(change.colour) && isShare(change.strength);
    case 'lines':
    case 'sky':
      return change.colour === null || isFlybyColour(change.colour);
    case 'surface':
      return FLYBY_SURFACES.includes(change.surface);
    case 'lens':
      return isLens(change.degrees);
  }
}

function isLens(degrees: number): boolean {
  return degrees >= FLYBY_LENS_RANGE.min && degrees <= FLYBY_LENS_RANGE.max;
}

/**
 * What a change steers. Two changes of one member must not overlap in time, because the look at a
 * moment would then depend on which of them was applied last — which is the one thing a moment of
 * a flyby must never depend on. A layer's being shown and its strength are two members: a layer
 * can fade in while its strength is being changed by another change.
 */
export function flybyChangeMember(change: FlybyChangeWhat): string {
  switch (change.kind) {
    case 'layer':
      return `layer:${change.layer}`;
    case 'layerOpacity':
      return `layerOpacity:${change.layer}`;
    default:
      return change.kind;
  }
}

/** Where each section starts on the flyby's clock, in real seconds, by section id. */
export function flybySectionStarts(document: Pick<FlybyDocument, 'speed' | 'sections'>): Map<string, number> {
  const starts = new Map<string, number>();
  let t = 0;
  for (const section of document.sections) {
    if (!starts.has(section.id)) {
      starts.set(section.id, t);
    }
    t += sectionLength(document, section);
  }
  return starts;
}

/**
 * When something tied to a section happens on the flyby's clock: its offset and length are
 * counted at a speed of 1 and played at the section's speed, as the section itself is. Undefined
 * when the section is not in the document.
 */
export function flybyTimeIn(
  document: Pick<FlybyDocument, 'speed' | 'sections'>,
  starts: ReadonlyMap<string, number>,
  item: { section: string; at: number; seconds: number },
): { start: number; end: number } | undefined {
  const section = document.sections.find((candidate) => candidate.id === item.section);
  const sectionStart = starts.get(item.section);
  if (!section || sectionStart === undefined) {
    return undefined;
  }
  const rate = document.speed * (section.speed ?? 1);
  const start = sectionStart + item.at / rate;
  return { start, end: start + item.seconds / rate };
}

/** A change's length on the flyby's clock is nothing for the kinds that are always cuts. */
export function flybyChangeSeconds(change: Pick<FlybyChange, 'kind' | 'seconds'>): number {
  return FLYBY_CUT_CHANGES.includes(change.kind) ? 0 : change.seconds;
}

function changeProblems(document: FlybyDocument): FlybyProblem[] {
  const problems: FlybyProblem[] = [];
  const starts = flybySectionStarts(document);
  const seen = new Set<string>();
  const timed: { change: FlybyChange; member: string; start: number; end: number }[] = [];
  for (const change of document.changes ?? []) {
    const section = document.sections.find((candidate) => candidate.id === change.section);
    if (seen.has(change.id)) {
      problems.push({ code: 'duplicateChange', change: change.id, section: change.section });
    }
    seen.add(change.id);
    if (!section) {
      problems.push({ code: 'unknownChangeSection', change: change.id, section: change.section });
      continue;
    }
    if (
      !changeIsSound(change) ||
      !(change.at >= 0 && change.at <= section.seconds) ||
      !(change.seconds >= 0 && change.seconds <= FLYBY_MAX_CHANGE_SECONDS)
    ) {
      problems.push({ code: 'badChange', change: change.id, section: change.section });
      continue;
    }
    const time = flybyTimeIn(document, starts, { ...change, seconds: flybyChangeSeconds(change) });
    if (time) {
      timed.push({ change, member: flybyChangeMember(change), ...time });
    }
  }
  // Ordered by start; a change overlaps the one before it of the same member when it starts
  // before that one has ended — or at the same moment, which for two cuts is the same thing.
  timed.sort((a, b) => a.start - b.start);
  const last = new Map<string, (typeof timed)[number]>();
  for (const entry of timed) {
    const before = last.get(entry.member);
    if (before && (entry.start < before.end - 1e-9 || Math.abs(entry.start - before.start) < 1e-9)) {
      problems.push({
        code: 'overlappingChanges',
        change: entry.change.id,
        section: entry.change.section,
        other: before.change.id,
      });
    }
    if (!before || entry.end > before.end) {
      last.set(entry.member, entry);
    }
  }
  return problems;
}

function cardIsSound(card: FlybyCard | undefined): boolean {
  return !card || (card.seconds > 0 && card.seconds <= FLYBY_MAX_CHANGE_SECONDS);
}

function textProblems(document: FlybyDocument): FlybyProblem[] {
  const texts = document.texts;
  if (!texts) {
    return [];
  }
  const problems: FlybyProblem[] = [];
  if (
    !cardIsSound(texts.title) ||
    !cardIsSound(texts.end) ||
    !((texts.fadeIn ?? 0) >= 0 && (texts.fadeIn ?? 0) <= FLYBY_MAX_CHANGE_SECONDS) ||
    !((texts.fadeOut ?? 0) >= 0 && (texts.fadeOut ?? 0) <= FLYBY_MAX_CHANGE_SECONDS)
  ) {
    problems.push({ code: 'badText' });
  }
  for (const caption of texts.captions ?? []) {
    const section = document.sections.find((candidate) => candidate.id === caption.section);
    if (
      !section ||
      !(caption.at >= 0 && caption.at <= section.seconds) ||
      !(caption.seconds > 0 && caption.seconds <= FLYBY_MAX_CHANGE_SECONDS)
    ) {
      problems.push({ code: 'badText', section: caption.section });
    }
  }
  return problems;
}

/** How long a section really lasts, in seconds, at the document's and its own speed. */
export function sectionLength(document: Pick<FlybyDocument, 'speed'>, section: FlybySection): number {
  return section.seconds / (document.speed * (section.speed ?? 1));
}

/** How long the whole flyby lasts, in seconds. */
export function flybyLength(document: FlybyDocument): number {
  return document.sections.reduce((total, section) => total + sectionLength(document, section), 0);
}

/** True when the document holds a place: a pinned subject is the only thing in it with coordinates. */
export function flybyHoldsPlace(document: FlybyDocument): boolean {
  return document.subjects.some((subject) => subject.kind === 'place');
}

// ---- reading a document that came from outside the running program ----

function finite(value: unknown): number | undefined {
  return typeof value === 'number' && Number.isFinite(value) ? value : undefined;
}

function text(value: unknown, limit: number): string | undefined {
  return typeof value === 'string' && value.length > 0 && value.length <= limit ? value : undefined;
}

function readSubject(value: unknown): FlybySubject | null {
  if (typeof value !== 'object' || value === null) {
    return null;
  }
  const bag = value as Record<string, unknown>;
  switch (bag.kind) {
    case 'cave': {
      const caveId = text(bag.caveId, 100);
      return caveId ? { kind: 'cave', caveId } : null;
    }
    case 'feature': {
      const featureId = text(bag.featureId, 100);
      return featureId ? { kind: 'feature', featureId } : null;
    }
    case 'view':
      return { kind: 'view' };
    case 'filter': {
      const savedFilterId = text(bag.savedFilterId, 100);
      const limit = finite(bag.limit);
      return savedFilterId && limit !== undefined ? { kind: 'filter', savedFilterId, limit } : null;
    }
    case 'file': {
      const geofileId = text(bag.geofileId, 100);
      return geofileId ? { kind: 'file', geofileId } : null;
    }
    case 'place': {
      const longitude = finite(bag.longitude);
      const latitude = finite(bag.latitude);
      const radiusM = finite(bag.radiusM);
      const height = bag.height === undefined ? undefined : finite(bag.height);
      if (
        longitude === undefined ||
        latitude === undefined ||
        radiusM === undefined ||
        Math.abs(longitude) > 180 ||
        Math.abs(latitude) > 90 ||
        !(radiusM > 0) ||
        (bag.height !== undefined && height === undefined)
      ) {
        return null;
      }
      return { kind: 'place', longitude, latitude, radiusM, ...(height !== undefined ? { height } : {}) };
    }
    default:
      return null;
  }
}

function readSubjectRef(value: unknown): FlybySubjectRef | null {
  if (value === 'all') {
    return 'all';
  }
  const index = finite(value);
  return index !== undefined && Number.isInteger(index) && index >= 0 ? index : null;
}

function readPose(value: unknown): FlybyPose | null {
  if (typeof value !== 'object' || value === null) {
    return null;
  }
  const bag = value as Record<string, unknown>;
  const pose: FlybyPose = {};
  for (const key of ['heading', 'headingBy', 'pitch', 'range', 'rangeM', 'lift'] as const) {
    if (bag[key] !== undefined) {
      const number = finite(bag[key]);
      if (number === undefined) {
        return null;
      }
      pose[key] = number;
    }
  }
  if (bag.aim !== undefined) {
    const aim = FLYBY_AIMS.find((candidate) => candidate === bag.aim);
    if (!aim) {
      return null;
    }
    pose.aim = aim;
  }
  if (bag.clearance === null) {
    pose.clearance = null;
  } else if (bag.clearance !== undefined) {
    const clearance = finite(bag.clearance);
    if (clearance === undefined) {
      return null;
    }
    pose.clearance = clearance;
  }
  return pose;
}

function readSection(value: unknown): FlybySection | null {
  if (typeof value !== 'object' || value === null) {
    return null;
  }
  const bag = value as Record<string, unknown>;
  const id = text(bag.id, 64);
  const kind = FLYBY_SECTION_KINDS.find((candidate) => candidate === bag.kind);
  const subject = readSubjectRef(bag.subject);
  const seconds = finite(bag.seconds);
  const to = readPose(bag.to);
  if (!id || !kind || subject === null || seconds === undefined || !to) {
    return null;
  }
  const section: FlybySection = { id, kind, subject, seconds, to };
  const speed = finite(bag.speed);
  if (speed !== undefined) {
    section.speed = speed;
  }
  if (bag.rest === true) {
    section.rest = true;
  }
  if (bag.say !== undefined) {
    const say = FLYBY_SAYINGS.find((candidate) => candidate === bag.say);
    if (!say) {
      return null;
    }
    section.say = say;
  }
  if (bag.each === true) {
    section.each = true;
  }
  return section;
}

function bagOf(value: unknown): Record<string, unknown> | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as Record<string, unknown>) : null;
}

/** The text of a card or caption: trimmed of nothing, refused when empty or too long. */
function plainText(value: unknown): string | undefined {
  return text(value, FLYBY_MAX_TEXT);
}

/**
 * Reads the opening look, or null when something in it is not what it should be. Values are
 * checked for their form here and for their range by {@link flybyProblems}, so a look with an
 * opacity of 3 is reported as such rather than vanishing.
 */
function readLook(value: unknown): FlybyLook | null {
  const bag = bagOf(value);
  if (!bag) {
    return null;
  }
  const look: FlybyLook = {};
  if (bag.background !== undefined) {
    const background = finite(bag.background);
    if (background === undefined || !Number.isInteger(background)) {
      return null;
    }
    look.background = background;
  }
  if (bag.backgroundOpacity !== undefined) {
    const opacity = finite(bag.backgroundOpacity);
    if (opacity === undefined) {
      return null;
    }
    look.backgroundOpacity = opacity;
  }
  if (bag.layers !== undefined) {
    const layers = bagOf(bag.layers);
    if (!layers || Object.keys(layers).length > FLYBY_MAX_LAYERS) {
      return null;
    }
    look.layers = {};
    for (const [key, entry] of Object.entries(layers)) {
      const layer = bagOf(entry);
      if (!isFlybyLayerKey(key) || !layer) {
        return null;
      }
      const read: FlybyLayerLook = {};
      if (layer.visible !== undefined) {
        if (typeof layer.visible !== 'boolean') {
          return null;
        }
        read.visible = layer.visible;
      }
      if (layer.opacity !== undefined) {
        const opacity = finite(layer.opacity);
        if (opacity === undefined) {
          return null;
        }
        read.opacity = opacity;
      }
      look.layers[key] = read;
    }
  }
  if (bag.walls !== undefined) {
    const walls = bagOf(bag.walls);
    const strength = finite(walls?.strength);
    if (!walls || !isFlybyColour(walls.colour) || strength === undefined) {
      return null;
    }
    look.walls = { colour: walls.colour, strength };
  }
  for (const key of ['lines', 'sky'] as const) {
    if (bag[key] !== undefined) {
      if (!isFlybyColour(bag[key])) {
        return null;
      }
      look[key] = bag[key];
    }
  }
  if (bag.surface !== undefined) {
    const surface = FLYBY_SURFACES.find((candidate) => candidate === bag.surface);
    if (!surface) {
      return null;
    }
    look.surface = surface;
  }
  if (bag.lens !== undefined) {
    const lens = finite(bag.lens);
    if (lens === undefined) {
      return null;
    }
    look.lens = lens;
  }
  return look;
}

function readColourOrNull(value: unknown): FlybyColour | null | undefined {
  return value === null ? null : isFlybyColour(value) ? value : undefined;
}

function readChange(value: unknown): FlybyChange | null {
  const bag = bagOf(value);
  if (!bag) {
    return null;
  }
  const id = text(bag.id, 64);
  const section = text(bag.section, 64);
  const at = finite(bag.at);
  const seconds = finite(bag.seconds) ?? 0;
  if (!id || !section || at === undefined) {
    return null;
  }
  const timing = { id, section, at, seconds };
  switch (bag.kind) {
    case 'background': {
      const background = finite(bag.background);
      return background !== undefined && Number.isInteger(background) ? { kind: 'background', background, ...timing } : null;
    }
    case 'backgroundOpacity': {
      const opacity = finite(bag.opacity);
      return opacity !== undefined ? { kind: 'backgroundOpacity', opacity, ...timing } : null;
    }
    case 'layer':
      return isFlybyLayerKey(bag.layer) && typeof bag.visible === 'boolean'
        ? { kind: 'layer', layer: bag.layer, visible: bag.visible, ...timing }
        : null;
    case 'layerOpacity': {
      const opacity = finite(bag.opacity);
      return isFlybyLayerKey(bag.layer) && opacity !== undefined
        ? { kind: 'layerOpacity', layer: bag.layer, opacity, ...timing }
        : null;
    }
    case 'walls': {
      const strength = finite(bag.strength);
      return isFlybyColour(bag.colour) && strength !== undefined
        ? { kind: 'walls', colour: bag.colour, strength, ...timing }
        : null;
    }
    case 'lines':
    case 'sky': {
      const colour = readColourOrNull(bag.colour);
      return colour === undefined ? null : { kind: bag.kind, colour, ...timing };
    }
    case 'surface': {
      const surface = FLYBY_SURFACES.find((candidate) => candidate === bag.surface);
      return surface ? { kind: 'surface', surface, ...timing } : null;
    }
    case 'lens': {
      const degrees = finite(bag.degrees);
      return degrees !== undefined ? { kind: 'lens', degrees, ...timing } : null;
    }
    default:
      return null;
  }
}

function readCard(value: unknown): FlybyCard | null {
  const bag = bagOf(value);
  const textValue = plainText(bag?.text);
  const seconds = finite(bag?.seconds);
  if (!bag || !textValue || seconds === undefined) {
    return null;
  }
  const card: FlybyCard = { text: textValue, seconds };
  if (bag.subtitle !== undefined) {
    const subtitle = plainText(bag.subtitle);
    if (!subtitle) {
      return null;
    }
    card.subtitle = subtitle;
  }
  return card;
}

function readCaption(value: unknown): FlybyCaption | null {
  const bag = bagOf(value);
  const section = text(bag?.section, 64);
  const textValue = plainText(bag?.text);
  const at = finite(bag?.at);
  const seconds = finite(bag?.seconds);
  return section && textValue && at !== undefined && seconds !== undefined
    ? { section, text: textValue, at, seconds }
    : null;
}

function readTexts(value: unknown): FlybyTexts | null {
  const bag = bagOf(value);
  if (!bag) {
    return null;
  }
  const texts: FlybyTexts = {};
  for (const key of ['title', 'end'] as const) {
    if (bag[key] !== undefined) {
      const card = readCard(bag[key]);
      if (!card) {
        return null;
      }
      texts[key] = card;
    }
  }
  if (bag.captions !== undefined) {
    if (!Array.isArray(bag.captions) || bag.captions.length > FLYBY_MAX_SECTIONS) {
      return null;
    }
    const captions = bag.captions.map(readCaption);
    if (captions.some((caption) => caption === null)) {
      return null;
    }
    texts.captions = captions as FlybyCaption[];
  }
  for (const key of ['fadeIn', 'fadeOut'] as const) {
    if (bag[key] !== undefined) {
      const seconds = finite(bag[key]);
      if (seconds === undefined) {
        return null;
      }
      texts[key] = seconds;
    }
  }
  if (bag.northArrow !== undefined) {
    if (typeof bag.northArrow !== 'boolean') {
      return null;
    }
    if (bag.northArrow) {
      texts.northArrow = true;
    }
  }
  if (bag.corner !== undefined) {
    const corner = plainText(bag.corner);
    if (!corner) {
      return null;
    }
    texts.corner = corner;
  }
  return texts;
}

/**
 * Reads a flyby out of something stored — a file a person chose, an entry in the browser's storage
 * — or returns null when it is not one that can be played.
 *
 * Every field is checked rather than trusted, and a document that fails anywhere is refused whole:
 * half a flyby is not a shorter flyby, it is a camera that goes somewhere its author never meant.
 * What comes back holds only the fields this module knows, so nothing a stranger added to a file
 * travels any further than here.
 */
export function normalizeFlybyDocument(value: unknown): FlybyDocument | null {
  if (typeof value !== 'object' || value === null) {
    return null;
  }
  const bag = value as Record<string, unknown>;
  if (bag.version !== FLYBY_VERSION) {
    return null;
  }
  if (!Array.isArray(bag.subjects) || !Array.isArray(bag.sections)) {
    return null;
  }
  if (bag.subjects.length > FLYBY_MAX_SUBJECTS || bag.sections.length > FLYBY_MAX_SECTIONS) {
    return null;
  }
  const subjects = bag.subjects.map(readSubject);
  const sections = bag.sections.map(readSection);
  const start = typeof bag.start === 'object' && bag.start !== null ? (bag.start as Record<string, unknown>) : undefined;
  const startSubject = readSubjectRef(start?.subject);
  const startPose = readPose(start?.pose);
  if (
    subjects.some((subject) => subject === null) ||
    sections.some((section) => section === null) ||
    startSubject === null ||
    !startPose ||
    startPose.heading === undefined ||
    startPose.pitch === undefined ||
    startPose.range === undefined
  ) {
    return null;
  }
  const document: FlybyDocument = {
    version: FLYBY_VERSION,
    name: text(bag.name, 200) ?? '',
    speed: finite(bag.speed) ?? 1,
    loop: bag.loop === true,
    subjects: subjects as FlybySubject[],
    start: {
      subject: startSubject,
      pose: {
        heading: startPose.heading,
        pitch: startPose.pitch,
        range: startPose.range,
        lift: startPose.lift ?? 0,
        clearance: startPose.clearance === undefined ? null : startPose.clearance,
        ...(startPose.aim ? { aim: startPose.aim } : {}),
        ...(startPose.rangeM !== undefined ? { rangeM: startPose.rangeM } : {}),
      },
    },
    sections: sections as FlybySection[],
  };
  const preset = FLYBY_PRESETS.find((candidate) => candidate === bag.preset);
  if (preset) {
    document.preset = preset;
  }
  // What a flyby does to the scene's look and says over it. Each is optional, so a flyby written
  // before they existed opens as it was; one that is there and cannot be read refuses the whole.
  if (bag.look !== undefined) {
    const look = readLook(bag.look);
    if (!look) {
      return null;
    }
    document.look = look;
  }
  if (bag.changes !== undefined) {
    if (!Array.isArray(bag.changes) || bag.changes.length > FLYBY_MAX_CHANGES) {
      return null;
    }
    const changes = bag.changes.map(readChange);
    if (changes.some((change) => change === null)) {
      return null;
    }
    document.changes = changes as FlybyChange[];
  }
  if (bag.texts !== undefined) {
    const texts = readTexts(bag.texts);
    if (!texts) {
      return null;
    }
    document.texts = texts;
  }
  return flybyProblems(document).length === 0 ? document : null;
}
