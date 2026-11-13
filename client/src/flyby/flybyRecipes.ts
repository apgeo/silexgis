// SPDX-License-Identifier: AGPL-3.0-or-later
import {
  flybyChangeMember,
  flybyLayerKey,
  flybyLength,
  flybySectionStarts,
  sectionLength,
  type FlybyChange,
  type FlybyChangeWhat,
  type FlybyColour,
  type FlybyDocument,
  type FlybyLayerKey,
  type FlybyLook,
} from './flybyDocument.ts';

// Ready-made sets of timed changes, written into a flyby in one press: a recipe is only a way of
// writing changes, and what it writes is an ordinary list a person can then edit change by change.
//
// Each recipe first takes out the changes it is about to write again — cycling the backgrounds a
// second time replaces the first cycle rather than clashing with it — and leaves every other
// change as it was. Times are worked out on the flyby's clock and written back as offsets into
// sections, so a recipe's changes move with their sections like any other.

/** What a recipe hands back: the flyby's new opening look and its new list of changes. */
export interface FlybyRecipeResult {
  look: FlybyLook;
  changes: FlybyChange[];
}

type Timed = FlybyChangeWhat & { t: number; seconds: number };

/** The section a moment of the flyby falls in, and how far into it, both counted at a speed of 1. */
function placeAt(document: FlybyDocument, t: number, seconds: number): Pick<FlybyChange, 'section' | 'at' | 'seconds'> | undefined {
  const starts = flybySectionStarts(document);
  for (let index = document.sections.length - 1; index >= 0; index -= 1) {
    const section = document.sections[index];
    const start = starts.get(section.id) ?? 0;
    if (t >= start - 1e-9 || index === 0) {
      const rate = document.speed * (section.speed ?? 1);
      const at = Math.min(section.seconds, Math.max(0, (t - start) * rate));
      return { section: section.id, at: round(at), seconds: round(seconds * rate) };
    }
  }
  return undefined;
}

function round(value: number): number {
  return Math.round(value * 100) / 100;
}

/** Ids not yet used in the document, `kind-1`, `kind-2`… */
function idMaker(changes: readonly FlybyChange[]): (kind: string) => string {
  const taken = new Set(changes.map((change) => change.id));
  let count = 0;
  return (kind) => {
    let id: string;
    do {
      count += 1;
      id = `${kind}-${count}`;
    } while (taken.has(id));
    taken.add(id);
    return id;
  };
}

/** The document's changes without those that steer any of `members`, with `added` placed and appended. */
function replacing(document: FlybyDocument, members: ReadonlySet<string>, added: readonly Timed[]): FlybyChange[] {
  const kept = (document.changes ?? []).filter((change) => !members.has(flybyChangeMember(change)));
  const id = idMaker(kept);
  const placed: FlybyChange[] = [];
  for (const { t, seconds, ...what } of added) {
    const place = placeAt(document, t, seconds);
    if (place) {
      placed.push({ ...what, id: id(what.kind), ...place } as FlybyChange);
    }
  }
  return [...kept, ...placed];
}

export interface CycleBackgroundsOptions {
  /** In the order shown, by catalogue id; the first opens the flyby. */
  backgrounds: number[];
  /** How long each is shown, in real seconds. */
  secondsEach: number;
  /** Cross-fades from one to the next over this many seconds; 0 cuts. */
  fadeSeconds: number;
}

/** Each background in turn, the list starting again from the first until the flyby ends. */
export function cycleBackgrounds(document: FlybyDocument, options: CycleBackgroundsOptions): FlybyRecipeResult {
  const look = { ...document.look };
  if (options.backgrounds.length === 0 || !(options.secondsEach > 0)) {
    return { look, changes: document.changes ?? [] };
  }
  look.background = options.backgrounds[0];
  const length = flybyLength(document);
  const fade = Math.min(options.fadeSeconds, options.secondsEach / 2);
  const added: Timed[] = [];
  if (options.backgrounds.length > 1) {
    for (let index = 1; index * options.secondsEach + fade < length; index += 1) {
      const background = options.backgrounds[index % options.backgrounds.length];
      added.push({ kind: 'background', background, t: index * options.secondsEach, seconds: fade });
    }
  }
  return { look, changes: replacing(document, new Set(['background']), added) };
}

/**
 * The background fades to `share` of its strength and back during each circle, so the caves under
 * the ground are seen while the camera goes round them.
 */
export function seeThroughGround(document: FlybyDocument, options: { share: number }): FlybyRecipeResult {
  const look = { ...document.look };
  const back = look.backgroundOpacity ?? 1;
  const starts = flybySectionStarts(document);
  const added: Timed[] = [];
  for (const section of document.sections) {
    if (section.kind !== 'orbit') {
      continue;
    }
    const start = starts.get(section.id) ?? 0;
    const length = sectionLength(document, section);
    added.push({ kind: 'backgroundOpacity', opacity: options.share, t: start, seconds: length * 0.25 });
    added.push({ kind: 'backgroundOpacity', opacity: back, t: start + length * 0.7, seconds: length * 0.25 });
  }
  return { look, changes: replacing(document, new Set(['backgroundOpacity']), added) };
}

/** Every cave in one colour from the start of a section: walls tinted, survey lines in the colour. */
export function colourCaves(
  document: FlybyDocument,
  options: { colour: FlybyColour; strength: number; section: string },
): FlybyRecipeResult {
  const look = { ...document.look };
  const t = flybySectionStarts(document).get(options.section);
  if (t === undefined) {
    return { look, changes: document.changes ?? [] };
  }
  const added: Timed[] = [
    { kind: 'walls', colour: options.colour, strength: options.strength, t, seconds: 2 },
    { kind: 'lines', colour: options.colour, t, seconds: 0 },
  ];
  return { look, changes: replacing(document, new Set(['walls', 'lines']), added) };
}

/** A georeferenced map fades in over the background during a section, and out again before it ends. */
export function oldMapNewMap(document: FlybyDocument, options: { raster: string; section: string }): FlybyRecipeResult {
  const key = flybyLayerKey('raster', options.raster);
  const look: FlybyLook = { ...document.look, layers: { ...document.look?.layers, [key]: { visible: false } } };
  const section = document.sections.find((candidate) => candidate.id === options.section);
  const start = flybySectionStarts(document).get(options.section);
  if (!section || start === undefined) {
    return { look: { ...document.look }, changes: document.changes ?? [] };
  }
  const length = sectionLength(document, section);
  const added: Timed[] = [
    { kind: 'layer', layer: key, visible: true, t: start + length * 0.15, seconds: length * 0.2 },
    { kind: 'layer', layer: key, visible: false, t: start + length * 0.65, seconds: length * 0.2 },
  ];
  return { look, changes: replacing(document, new Set([`layer:${key}`]), added) };
}

/**
 * Layers of features shown one after another — the viewer's layers from saved filters, and the
 * flyby's own by kind or by filter, by their keys: each fades in, stays its time and fades out as
 * the next fades in. All of them are off when the flyby opens.
 */
export function featuresInTurn(
  document: FlybyDocument,
  options: { layers: FlybyLayerKey[]; secondsEach: number },
): FlybyRecipeResult {
  const keys = [...options.layers];
  const layers = { ...document.look?.layers };
  for (const key of keys) {
    layers[key] = { visible: false };
  }
  const look: FlybyLook = { ...document.look, layers };
  const length = flybyLength(document);
  const fade = Math.min(1, options.secondsEach / 4);
  const added: Timed[] = [];
  keys.forEach((layer, index) => {
    const t = index * options.secondsEach;
    if (t + fade > length) {
      return;
    }
    added.push({ kind: 'layer', layer, visible: true, t, seconds: fade });
    const off = t + options.secondsEach - fade;
    // The last one stays to the end.
    if (index < keys.length - 1 && off + fade <= length) {
      added.push({ kind: 'layer', layer, visible: false, t: off, seconds: fade });
    }
  });
  return { look, changes: replacing(document, new Set(keys.map((key) => `layer:${key}`)), added) };
}

export interface LightTheVisitedOptions {
  /** The cave being visited. */
  lit: FlybyColour;
  /** Every other cave. */
  dim: FlybyColour;
  /** How much of `dim` is mixed into the others' walls, 0–1. */
  dimStrength: number;
}

/** How much of the lit colour is mixed into the walls of the cave being visited. */
const LIT_STRENGTH = 0.85;
/** Seconds a cave takes to light up, and to dim again; less in a stop too short for both. */
const LIGHT_SECONDS = 1.5;

/**
 * The cave being visited lit, the others dimmed: every cave opens in the dim colour — walls and
 * survey lines — and each stop of the tour lights its own cave as the camera heads for it and
 * dims it again as the camera leaves. A stop is a run of sections about the same cave, or about a
 * saved filter's caves flown for each of them, and so lights each of those in turn.
 */
export function lightTheVisited(document: FlybyDocument, options: LightTheVisitedOptions): FlybyRecipeResult {
  const look: FlybyLook = { ...document.look, walls: { colour: options.dim, strength: options.dimStrength }, lines: options.dim };
  const stopAbout = (index: number): number | undefined => {
    const section = document.sections[index];
    const subject = typeof section.subject === 'number' ? document.subjects[section.subject] : undefined;
    return subject?.kind === 'cave' || (subject?.kind === 'filter' && section.each) ? (section.subject as number) : undefined;
  };
  const stops: { subject: number; first: number; last: number }[] = [];
  document.sections.forEach((section, index) => {
    const subject = stopAbout(index);
    const last = stops[stops.length - 1];
    if (subject === undefined) {
      return;
    }
    // A run of sections flown for each of a filter's caves is one stop however it is cut.
    if (last && last.last === index - 1 && last.subject === subject && Boolean(document.sections[last.first].each) === Boolean(section.each)) {
      last.last = index;
    } else {
      stops.push({ subject, first: index, last: index });
    }
  });
  const members = new Set(['walls', 'lines', ...(document.changes ?? []).filter((change) => change.kind === 'cave').map(flybyChangeMember)]);
  const kept = (document.changes ?? []).filter((change) => !members.has(flybyChangeMember(change)));
  const id = idMaker(kept);
  const added: FlybyChange[] = stops.flatMap(({ subject, first, last }) => {
    const from = document.sections[first];
    const to = document.sections[last];
    const fade = round(Math.min(LIGHT_SECONDS, from.seconds / 2, to.seconds / 2));
    return [
      { kind: 'cave', subject, colour: options.lit, strength: LIT_STRENGTH, id: id('cave'), section: from.id, at: 0, seconds: fade },
      { kind: 'cave', subject, colour: options.lit, strength: 0, id: id('cave'), section: to.id, at: round(to.seconds - fade), seconds: fade },
    ];
  });
  return { look, changes: [...kept, ...added] };
}
